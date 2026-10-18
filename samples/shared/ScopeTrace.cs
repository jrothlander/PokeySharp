using System;

namespace PokeyLab;

/// <summary>
/// A ring of the most recent output samples, handed out <b>trigger-aligned</b> so a
/// steady tone stands still on screen.
///
/// <b>The triggering is the whole difference between a scope and a smear.</b> The
/// audio block rate has no relationship to the frame rate, so drawing the newest N
/// samples every frame shows a waveform sliding sideways at whatever the beat
/// frequency happens to be -- unreadable, and worse, it looks like the chip is doing
/// something it is not. A real scope solves this by starting the sweep at a
/// consistent point in the wave, and so does this: it scans back for a rising
/// zero crossing and draws from there.
///
/// When nothing crosses -- silence, or a channel sitting in volume-only mode -- it
/// free-runs from the newest sample rather than showing nothing, because a flat line
/// is the correct picture of a flat signal.
/// </summary>
public sealed class ScopeTrace
{
    /// <summary>Samples kept. Several windows' worth, so the trigger search has
    /// somewhere to look without ever reaching data the writer is overwriting.</summary>
    private const int Capacity = 8192;

    private readonly float[] _ring = new float[Capacity];
    private int _written;

    /// <summary>Copies a freshly rendered block in. Cheap enough to call every audio
    /// block; the ring is the only state.</summary>
    public void Push(float[] block, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _ring[_written % Capacity] = block[i];
            _written++;
        }
    }

    /// <summary>True once enough has been rendered to draw a full window.</summary>
    public bool Ready(int window) => _written >= window * 2;

    /// <summary>
    /// Fills <paramref name="destination"/> with one window ending at the trigger.
    ///
    /// The search runs backwards from the newest sample so the picture is always as
    /// current as possible, and stops well short of the write head so a block landing
    /// mid-copy cannot tear the trace.
    /// </summary>
    public void Fill(float[] destination)
    {
        int window = destination.Length;
        if (!Ready(window))
        {
            Array.Clear(destination);
            return;
        }

        // Newest sample, backed off by one window so there is always a full window
        // ahead of whatever we trigger on.
        int newest = _written - 1 - window;
        int limit = Math.Min(window * 4, Capacity - window - 2);
        int start = newest;

        for (int back = 1; back < limit; back++)
        {
            int i = newest - back;
            if (At(i) <= 0f && At(i + 1) > 0f) { start = i + 1; break; }
        }

        for (int i = 0; i < window; i++) destination[i] = At(start + i);
    }

    private float At(int absolute)
    {
        int index = absolute % Capacity;
        if (index < 0) index += Capacity;
        return _ring[index];
    }
}
