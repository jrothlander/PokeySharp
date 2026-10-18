using System;
using Pokey;
using PokeyLab;

namespace PokeyLabForms;

/// <summary>Which register-writer currently owns the chip. See
/// <see cref="ChipSession.Driver"/> for why it is one at a time.</summary>
public enum ChipDriver { Envelope, Sample }

/// <summary>
/// The live chip, and everything that drives it.
///
/// <b>The chip runs continuously and you poke registers at it</b> -- the same design
/// PokeyLab has, and for the same reason: a note already sounding should slide when
/// you turn its divider, not restart. What differs here is who does the driving.
/// PokeyLab renders from its game loop, so the chip only ever sees one thread. This
/// app has no game loop, so rendering happens on <see cref="WaveOutStream"/>'s feeder
/// thread while the controls are on the UI thread -- two threads, one chip.
///
/// <b>Hence <see cref="Gate"/>, and the rule that goes with it: everything that reads
/// or writes the chip or the envelope takes it first.</b> That includes reads for
/// display, which cost nothing at thirty a second and would otherwise be one of those
/// races that is fine for months and then produces a value nobody can explain. The
/// lock is held for a block of rendering, which is a few hundred microseconds of work
/// for about twelve milliseconds of audio -- invisible from the UI thread, and it
/// leaves the audio thread waiting on a knob turn for less time than a single sample.
///
/// The one thing deliberately outside the gate is <see cref="Scope"/>; see
/// <see cref="Fill"/>.
/// </summary>
public sealed class ChipSession : IDisposable
{
    /// <summary>Output rate. Not the chip's rate -- the chip runs at its base clock,
    /// around 1.5-1.8MHz depending which machine is selected, and
    /// <see cref="PokeyChip.Render"/> resamples down.</summary>
    public const int SampleRate = 44100;

    /// <summary>Samples per streamed block. About 12ms, matching PokeyLab.</summary>
    private const int BlockSamples = 512;

    /// <summary>Buffers in the pool -- about 46ms in hand. One more than PokeyLab
    /// queues, because that app is refilled by a frame loop the framework guarantees
    /// will run, and this one by a thread competing with whatever else Windows is
    /// doing.</summary>
    private const int BufferCount = 4;

    public object Gate { get; } = new();

    public PokeyChip Chip { get; } = new();

    /// <summary>Owned here rather than by a panel because it has to advance in step
    /// with rendering, and rendering happens on this class's thread. A panel only gets
    /// to display it.</summary>
    public StepEnvelope Envelope { get; } = new();

    /// <summary>The 4-bit DAC path -- S.A.M.'s route. Owned here for the same reason
    /// the envelope is: it advances in step with rendering.</summary>
    public SampleVoice Voice { get; } = new();

    public ScopeTrace Scope { get; } = new();

    /// <summary>
    /// Which of the two register-writers is driving the chip.
    ///
    /// <b>Mutually exclusive, and that is a real constraint rather than a simplification
    /// to be tidied away later.</b> Both <see cref="StepEnvelope.Render"/> and
    /// <see cref="SampleVoice.Render"/> work by slicing the audio block at their own
    /// write boundaries and calling <see cref="PokeyChip.Render"/> for each slice, so
    /// they cannot both wrap the same block -- running one inside the other would make
    /// the inner one's timing depend on the outer one's slice lengths, which is exactly
    /// the buffer-size dependence both are written to avoid. Supporting both at once
    /// needs a driver that merges their event streams and renders to the nearer
    /// deadline; that is a real design, not a patch, and nothing yet wants it.
    ///
    /// <b>What is not affected:</b> the tone generators. A channel with a divider and a
    /// volume just sounds, with nobody writing to it, so an A/B of speech against the
    /// chip's own oscillators works under either driver -- which is the comparison this
    /// was built for.
    /// </summary>
    public ChipDriver Driver { get; private set; } = ChipDriver.Envelope;

    private WaveOutStream _stream;
    private readonly float[] _block = new float[BlockSamples];

    /// <summary>Why there is no sound, when there is no sound. Empty when all is
    /// well.</summary>
    public string AudioFault { get; private set; } = "";

    public bool AudioRunning => _stream != null && !_stream.Faulted;

    // --- Base clock --------------------------------------------------------

    /// <summary>
    /// The base clocks worth switching between.
    ///
    /// <b>One list, where PokeyLab has two.</b> Its register page offered the three
    /// real machines and its envelope page offered those plus RIMWALKER, because they
    /// were separate screens with separate state and each carried the clocks its own
    /// job needed. On one window that split would be a bug waiting to happen -- two
    /// selectors for one property of one chip -- so this is the union, and there is a
    /// single control for it.
    ///
    /// RIMWALKER is not a machine. It is the clock the game plays at, back-derived
    /// from tuning the shot against the reference captures, and it sits beside
    /// Tempest's real 1.512MHz precisely because the two disagree by a fifth and that
    /// gap is the project's open question.
    /// </summary>
    /// <remarks>
    /// <c>Symbol</c> is the constant's own name, carried so the envelope panel's
    /// "copy as C#" can emit <c>PokeyClocks.TempestHz</c> rather than a bare 1512000.
    /// A number would still work; a name says which machine it came from, which is the
    /// thing you want six months later.
    /// </remarks>
    public static readonly (string Label, double Hz, string Symbol)[] Clocks =
    {
        ("Tempest 1.512 MHz", PokeyClocks.TempestHz, nameof(PokeyClocks.TempestHz)),
        ("RimWalker 0.896 MHz", PokeyClocks.RimWalkerHz, nameof(PokeyClocks.RimWalkerHz)),
        ("Atari NTSC 1.79 MHz", PokeyClocks.Atari800NtscHz, nameof(PokeyClocks.Atari800NtscHz)),
        ("Atari PAL 1.77 MHz", PokeyClocks.Atari800PalHz, nameof(PokeyClocks.Atari800PalHz)),
    };

    // --- Mute --------------------------------------------------------------

    private readonly int[] _savedVolume = new int[PokeyChip.Channels];

    public bool Muted { get; private set; }

    public ChipSession()
    {
        Chip.BaseClockHz = Clocks[0].Hz;

        // Something audible on open, or the tool looks broken. One channel, pure tone,
        // mid volume, a divider in the middle of the useful range -- PokeyLab's chip
        // page opens the same way.
        Chip.SetAudf(0, 60);
        Chip.SetAudc(0, 0xa8);

        Envelope.Channel = 0;
        Envelope.TickHz = 220.0;
        Envelope.Looping = false;
    }

    public void Start()
    {
        try
        {
            _stream = new WaveOutStream(SampleRate, BlockSamples, BufferCount, Fill);
            _stream.Start();
            AudioFault = "";
        }
        catch (Exception error)
        {
            // No audio device is a reason to run silently, not a reason to fail. Every
            // register, every pitch and every poly train still works, which is most of
            // the tool -- they are read from pure accessors that do not depend on
            // anything having been rendered.
            //
            // The one thing that does stop is time: the chip is ticked from inside
            // Render, so with no audio it never advances and the scope stays empty.
            // That is worth saying out loud on the status bar rather than leaving
            // someone to wonder why the trace is flat.
            _stream = null;
            AudioFault = error.Message;
        }
    }

    /// <summary>
    /// Renders one block, on the feeder thread.
    ///
    /// Always through the envelope, never the chip directly: it renders the chip flat
    /// when nothing is playing, and when something is, it cuts the block at the tick
    /// boundaries so a sweep never quantises to the audio buffer size.
    /// </summary>
    private void Fill(byte[] pcm)
    {
        lock (Gate)
        {
            // Whichever driver owns the chip. Both render it flat when they are not
            // playing, so this is never a special case for silence -- the tone
            // generators sound through either.
            if (Driver == ChipDriver.Sample)
                Voice.Render(Chip, _block, 0, BlockSamples, SampleRate);
            else
                Envelope.Render(Chip, _block, 0, BlockSamples, SampleRate);
        }

        // Outside the gate on purpose. The ring is written here and read by the UI
        // thread when it paints, and it is built to tolerate exactly that: Fill backs
        // off a full window from the write head, so a block landing mid-copy cannot
        // tear the trace. Taking the gate would serialise painting against rendering
        // for no benefit -- and the ring's own guarantee, not the lock, is what makes
        // the picture whole.
        Scope.Push(_block, BlockSamples);

        for (int i = 0; i < BlockSamples; i++)
        {
            short sample = (short)(Math.Clamp(_block[i], -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)(sample & 0xff);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }
    }

    // --- Driver handover ---------------------------------------------------
    //
    // Starting either voice stops the other and takes the chip. Done here rather than
    // in the two panels so the handover is one piece of code: a panel that only stopped
    // its rival's playback without taking the driver, or took it without stopping the
    // rival, would leave a voice reading as playing while silent -- and the panels
    // would disagree about which.

    // Both guard on the rival actually playing, and that guard is load-bearing rather
    // than an optimisation. Stopping either voice silences its channel -- StepEnvelope
    // clears the volume nibble, SampleVoice clears the whole AUDC byte to get out of
    // volume-only mode -- so stopping one that was never playing wipes whatever the
    // user had dialled into that channel by hand. Pressing Speak silenced the tone on
    // channel 1, which is exactly the tone the A/B wants left running underneath.

    public void PlayEnvelope()
    {
        lock (Gate)
        {
            if (Voice.Playing) Voice.Stop(Chip);
            Driver = ChipDriver.Envelope;
            Envelope.Trigger(Chip);
        }
    }

    public void PlaySample()
    {
        lock (Gate)
        {
            if (Envelope.Playing) Envelope.Stop(Chip);
            Driver = ChipDriver.Sample;
            Voice.Trigger(Chip);
        }
    }

    // --- Writes ------------------------------------------------------------
    //
    // The handful of register writes that are more than a Set: they either span two
    // fields of one byte, or they interact with mute. Everything simpler is done at
    // the call site inside a lock (session.Gate).

    /// <summary>Writes a volume without disturbing the distortion field or the
    /// volume-only bit, which share the byte.</summary>
    public void SetVolume(int channel, int volume)
    {
        lock (Gate)
        {
            // Turning a volume while muted brings everything back first, then applies
            // the change. Just clearing the flag and setting this one channel would
            // throw away the other three channels' saved levels -- the same
            // "state destroyed with no way back" the mute control had in PokeyLab's
            // first version, only smaller and harder to notice. Restoring first also
            // means a knob always makes a sound, which is what a live tool should do.
            if (Muted) Unmute();

            Chip.SetAudc(channel, (Chip.Audc(channel) & ~PokeyChip.AudcVolumeMask)
                                  | Math.Clamp(volume, 0, 15));
        }
    }

    /// <summary>Writes the distortion field, AUDC bits 5-7, leaving the volume nibble
    /// and the volume-only bit alone.</summary>
    public void SetDistortion(int channel, int code)
    {
        lock (Gate)
            Chip.SetAudc(channel, (Chip.Audc(channel) & 0x1f) | ((code & 7) << 5));
    }

    public void SetVolumeOnly(int channel, bool on)
    {
        lock (Gate)
        {
            int audc = Chip.Audc(channel);
            Chip.SetAudc(channel, on ? audc | PokeyChip.AudcVolumeOnly
                                     : audc & ~PokeyChip.AudcVolumeOnly);
        }
    }

    /// <summary>
    /// Silences every channel, or brings them all back.
    ///
    /// <b>The volumes have to be remembered, not just zeroed.</b> Volume lives in
    /// AUDC's low nibble, so silencing by clearing it destroys the only record of what
    /// each channel was set to -- PokeyLab's first version did exactly that, which made
    /// the control a one-way switch and meant getting sound back was four channels of
    /// dialling by hand.
    ///
    /// Muting with everything already silent does nothing, rather than saving a row of
    /// zeroes and making the next press restore silence.
    /// </summary>
    public void ToggleMute()
    {
        lock (Gate)
        {
            if (Muted) { Unmute(); return; }

            bool anythingAudible = false;
            for (int channel = 0; channel < PokeyChip.Channels; channel++)
            {
                _savedVolume[channel] = Chip.Volume(channel);
                anythingAudible |= _savedVolume[channel] > 0;
            }

            if (!anythingAudible) return;

            for (int channel = 0; channel < PokeyChip.Channels; channel++) WriteVolume(channel, 0);
            Muted = true;
        }
    }

    /// <summary>Restores the saved levels. Caller holds the gate.</summary>
    private void Unmute()
    {
        for (int channel = 0; channel < PokeyChip.Channels; channel++)
            WriteVolume(channel, _savedVolume[channel]);
        Muted = false;
    }

    /// <summary>The volume write itself, with no mute interaction -- what mute uses
    /// internally, where <see cref="SetVolume"/> would recurse.</summary>
    private void WriteVolume(int channel, int volume)
        => Chip.SetAudc(channel, (Chip.Audc(channel) & ~PokeyChip.AudcVolumeMask)
                                 | Math.Clamp(volume, 0, 15));

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
