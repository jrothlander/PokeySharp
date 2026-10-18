using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace PokeyLabForms;

/// <summary>
/// A continuously streaming PCM output, on <c>winmm.dll</c>'s waveOut API.
///
/// <b>Why this exists at all.</b> PokeyLab streams through MonoGame's
/// <c>DynamicSoundEffectInstance</c>, which is the one part of that app with no
/// Windows Forms equivalent -- and it cannot simply be dropped, because a lab whose
/// chip only runs while a sound is being auditioned is a different and much less
/// useful tool. The chip has to run continuously so that turning a divider slides the
/// pitch of a note already sounding. So the streaming had to be rebuilt on something,
/// and waveOut is the something available without taking a dependency, which
/// RimWalker's ground rules ask us not to do casually.
///
/// <b>The shape is the same as PokeyLab's queue.</b> A small pool of buffers, each
/// handed to the driver; when one comes back, refill it and hand it over again. The
/// difference is where the refilling happens. There it is pumped from <c>Update</c> on
/// the game thread, which works because a game already has a frame loop. Here there is
/// no such loop -- a Forms timer would do it, but its resolution is a frame or two of
/// audio and it stops being serviced promptly whenever the UI is busy, which is
/// exactly when a knob is being turned. So the refilling runs on a dedicated thread
/// and the caller's fill callback is invoked there. See <see cref="ChipSession"/> for
/// the locking that follows from that.
/// </summary>
public sealed class WaveOutStream : IDisposable
{
    // --- winmm ------------------------------------------------------------

    private const int MmSysErrNoError = 0;
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint CallbackEvent = 0x00050000;
    private const uint WaveFormatPcm = 1;

    /// <summary>Set by the driver once it has finished with a buffer. The only signal
    /// this class needs: a buffer wearing it is ours to refill, and one without it is
    /// still being read and must not be touched.</summary>
    private const uint WhdrDone = 0x00000001;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr device, uint deviceId, ref WaveFormatEx format,
                                          IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr device, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr device);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr device);

    // --- State ------------------------------------------------------------

    private readonly int _blockBytes;
    private readonly int _bufferCount;
    private readonly Action<byte[]> _fill;
    private readonly int _headerSize = Marshal.SizeOf<WaveHdr>();

    private IntPtr _device;
    private readonly IntPtr[] _headers;
    private readonly byte[][] _blocks;
    private readonly GCHandle[] _pins;
    private readonly bool[] _prepared;

    /// <summary>Whether a buffer has ever been handed to the driver. Distinct from
    /// <see cref="WhdrDone"/>, which is only meaningful after the first write: a freshly
    /// prepared header has neither flag, and without this the feeder would wait forever
    /// for a completion that cannot arrive because nothing was ever queued.</summary>
    private readonly bool[] _queued;

    private AutoResetEvent _ready;
    private Thread _feeder;
    private volatile bool _stop;

    /// <summary>Set if the fill callback throws or the device fails mid-stream. The
    /// feeder stops rather than spinning on a broken device; the app goes quiet and
    /// keeps working, which is the same contract PokeyLab and RimWalker's Sfx both
    /// hold to.</summary>
    public volatile bool Faulted;

    public WaveOutStream(int sampleRate, int blockSamples, int bufferCount, Action<byte[]> fill)
    {
        _blockBytes = blockSamples * 2;   // mono, 16-bit
        _bufferCount = bufferCount;
        _fill = fill;

        _headers = new IntPtr[bufferCount];
        _blocks = new byte[bufferCount][];
        _pins = new GCHandle[bufferCount];
        _prepared = new bool[bufferCount];
        _queued = new bool[bufferCount];

        var format = new WaveFormatEx
        {
            wFormatTag = (ushort)WaveFormatPcm,
            nChannels = 1,
            nSamplesPerSec = (uint)sampleRate,
            nAvgBytesPerSec = (uint)(sampleRate * 2),
            nBlockAlign = 2,
            wBitsPerSample = 16,
            cbSize = 0,
        };

        _ready = new AutoResetEvent(false);

        int result = waveOutOpen(out _device, WaveMapper, ref format,
                                 _ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero,
                                 CallbackEvent);
        if (result != MmSysErrNoError)
        {
            _ready.Dispose();
            _ready = null;
            throw new InvalidOperationException($"waveOutOpen failed with MMSYSERR {result}.");
        }

        for (int i = 0; i < bufferCount; i++)
        {
            _blocks[i] = new byte[_blockBytes];

            // Pinned for the object's whole life. The driver reads this memory from
            // another thread with no idea the CLR exists, so a compacting collector
            // moving it out from under a queued buffer would be a use-after-move with
            // no exception to catch -- it would surface as a burst of noise, or a
            // crash somewhere unrelated.
            _pins[i] = GCHandle.Alloc(_blocks[i], GCHandleType.Pinned);

            var header = new WaveHdr
            {
                lpData = _pins[i].AddrOfPinnedObject(),
                dwBufferLength = (uint)_blockBytes,
            };

            _headers[i] = Marshal.AllocHGlobal(_headerSize);
            Marshal.StructureToPtr(header, _headers[i], false);

            if (waveOutPrepareHeader(_device, _headers[i], _headerSize) != MmSysErrNoError)
            {
                Dispose();
                throw new InvalidOperationException("waveOutPrepareHeader failed.");
            }
            _prepared[i] = true;
        }
    }

    public void Start()
    {
        _feeder = new Thread(Feed)
        {
            IsBackground = true,
            Name = "POKEY audio",

            // Above normal, not highest. A starved buffer is an audible click, and
            // this thread does almost nothing but wait -- but it also runs the chip
            // emulation inside the fill callback, so putting it at the top would let a
            // slow render on a loaded machine push the UI around instead of merely
            // sounding rough.
            Priority = ThreadPriority.AboveNormal,
        };
        _feeder.Start();
    }

    private void Feed()
    {
        while (!_stop)
        {
            bool wrote = false;

            for (int i = 0; i < _bufferCount && !_stop; i++)
            {
                if (_queued[i] && !IsDone(i)) continue;

                try
                {
                    _fill(_blocks[i]);
                }
                catch (Exception)
                {
                    Faulted = true;
                    return;
                }

                if (waveOutWrite(_device, _headers[i], _headerSize) != MmSysErrNoError)
                {
                    Faulted = true;
                    return;
                }

                _queued[i] = true;
                wrote = true;
            }

            // Only wait once nothing could be refilled, so a pass that fills several
            // buffers does not sleep between them.
            //
            // The timeout is a safety net rather than the mechanism. An AutoResetEvent
            // counts to one, so two buffers completing close together signal it twice
            // and it is only observed once -- harmless, because the loop above rescans
            // every buffer rather than trusting the signal to say how many are free.
            // The timeout only matters if a signal is missed entirely.
            if (!wrote) _ready.WaitOne(50);
        }
    }

    private bool IsDone(int index)
    {
        var header = Marshal.PtrToStructure<WaveHdr>(_headers[index]);
        return (header.dwFlags & WhdrDone) != 0;
    }

    public void Dispose()
    {
        _stop = true;
        _ready?.Set();

        // Bounded. A feeder blocked inside a misbehaving driver must not stop the
        // application from closing; the buffers below are freed either way, and the
        // thread is a background one so it cannot hold the process open.
        _feeder?.Join(500);
        _feeder = null;

        if (_device != IntPtr.Zero)
        {
            // Reset before unprepare, always: it marks every queued buffer done, and
            // unpreparing one the driver still holds fails and leaks it.
            waveOutReset(_device);

            for (int i = 0; i < _bufferCount; i++)
                if (_prepared[i])
                {
                    waveOutUnprepareHeader(_device, _headers[i], _headerSize);
                    _prepared[i] = false;
                }

            waveOutClose(_device);
            _device = IntPtr.Zero;
        }

        for (int i = 0; i < _bufferCount; i++)
        {
            if (_headers[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_headers[i]);
                _headers[i] = IntPtr.Zero;
            }
            if (_pins[i].IsAllocated) _pins[i].Free();
        }

        _ready?.Dispose();
        _ready = null;
    }
}
