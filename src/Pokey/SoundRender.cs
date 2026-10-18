using System;

namespace Pokey;

/// <summary>
/// Renders a voice offline, to samples and to PCM bytes.
///
/// <b>This is the layer that was missing, and both front ends had written it themselves.</b>
/// Demo1 had a <c>ChipSound</c> class and RimWalker had its own loop in <c>Core/Sfx.cs</c>,
/// each with its own copy of the gain correction and the tail fade -- two places to fix a
/// bug, and two places for one to drift. Everything a caller genuinely has to decide is a
/// parameter here; everything that follows from the chip is not.
///
/// <b>Deliberately no audio API.</b> It returns floats and bytes, and the caller spends one
/// line binding those to whatever it plays through. That is not purity for its own sake:
/// Demo1 links MonoGame's WindowsDX build and RimWalker links DesktopGL, so an adapter
/// holding a reference to either would drag two <c>SoundEffect</c> types into one build.
/// PCM is the only thing both agree on.
/// </summary>
public static class SoundRender
{
    /// <summary>
    /// Scales one emulated channel up to full range, and both halves of it are real.
    ///
    /// <see cref="PokeyChip.Mix"/> divides by all four channels' worth of headroom and only
    /// one is driven, which is 4. The chip's output is unipolar -- a real POKEY sources
    /// current one way -- so the DC blocker centres it and halves the swing, which is the
    /// other 2. Drop this and everything is eight times too quiet.
    ///
    /// It lives here rather than in a caller because it is a property of the chip, not a
    /// decision anybody makes. A game asking for a laser should never meet the number 8.
    /// </summary>
    private const float ChipGain = 2f * PokeyChip.Channels;

    /// <summary>How long the tail is faded, in seconds. The arcade cuts its sounds dead
    /// and fading the attack blunts the very thing that makes a blip read as a blip, so
    /// only the end is touched -- just enough to avoid a click.</summary>
    private const float TailSeconds = 0.01f;

    /// <summary>
    /// The finished signal, one float per sample in -1..1.
    ///
    /// Rendered as fast as the CPU likes rather than streamed: the chip has no idea it is
    /// not running in real time. <see cref="StepEnvelope.Render"/> cuts the block at the
    /// envelope's own tick boundaries, so a sweep never quantises to a buffer size.
    /// </summary>
    public static float[] Render(StepEnvelope.Step[] audf, StepEnvelope.Step[] audc,
                                 double tickHz, double baseClockHz, float amplitude,
                                 int sampleRate = 44100, int audctl = 0)
    {
        var chip = new PokeyChip { BaseClockHz = baseClockHz };
        chip.SetControl(audctl);

        var envelope = new StepEnvelope
        {
            AudfChain = audf,
            AudcChain = audc,
            TickHz = tickHz,
            Channel = 0,
        };

        int count = Math.Max(1, (int)(sampleRate * envelope.Seconds));
        var mixed = new float[count];

        envelope.Trigger(chip);
        envelope.Render(chip, mixed, 0, count, sampleRate);

        float tailSamples = sampleRate * TailSeconds;
        for (int i = 0; i < count; i++)
        {
            float tail = Math.Min(1f, (count - i) / tailSamples);
            mixed[i] = Math.Clamp(mixed[i] * ChipGain, -1f, 1f) * amplitude * tail;
        }

        return mixed;
    }

    /// <summary>
    /// Packs samples as 16-bit little-endian mono PCM -- the one format every audio API
    /// on the way out of here accepts.
    /// </summary>
    public static byte[] ToPcm16(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            // Little-endian: low byte first. Backwards gives noise that is almost your
            // sound, which is a horrible thing to debug.
            short sample = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)(sample & 0xff);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }
        return pcm;
    }

    /// <summary>Samples as signed 16-bit values, for a caller that wants shorts rather
    /// than bytes.</summary>
    public static short[] ToPcm16Samples(float[] samples)
    {
        var pcm = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            pcm[i] = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
        return pcm;
    }

    /// <summary>
    /// A complete WAV file: 16-bit mono PCM behind the standard 44-byte header.
    ///
    /// <b>A file format, not an audio API</b>, which is why it may live here: it is bytes
    /// in and bytes out, and it names nothing either front end links. It exists because
    /// every tool that wanted a sound on disk had written the header itself — the lasers
    /// tool, and the harness that checks the songs — and a header is exactly the kind of
    /// forty-four bytes that is right in one copy and subtly wrong in the next.
    /// </summary>
    public static byte[] ToWav(float[] samples, int sampleRate = 44100)
        => Wav(ToPcm16(samples), sampleRate);

    /// <summary>The same, from samples already packed to shorts — which is what a caller
    /// with its own renderer, like RimWalker's, has in hand.</summary>
    public static byte[] ToWav(short[] samples, int sampleRate)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            pcm[i * 2] = (byte)(samples[i] & 0xff);
            pcm[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xff);
        }
        return Wav(pcm, sampleRate);
    }

    private static byte[] Wav(byte[] pcm, int sampleRate)
    {
        const int Header = 44, Channels = 1, Bits = 16, BlockAlign = Channels * Bits / 8;

        var wav = new byte[Header + pcm.Length];
        int at = 0;

        void Text(string s) { foreach (char c in s) wav[at++] = (byte)c; }
        void Int(int v) { for (int i = 0; i < 4; i++) wav[at++] = (byte)(v >> (8 * i)); }
        void Short(int v) { wav[at++] = (byte)v; wav[at++] = (byte)(v >> 8); }

        // Every multi-byte field little-endian, the same as the samples. The two sizes
        // are the ones a player trusts: RIFF's counts everything after itself, data's
        // counts the samples alone.
        Text("RIFF"); Int(Header - 8 + pcm.Length); Text("WAVE");
        Text("fmt "); Int(16); Short(1); Short(Channels);
        Int(sampleRate); Int(sampleRate * BlockAlign); Short(BlockAlign); Short(Bits);
        Text("data"); Int(pcm.Length);

        Array.Copy(pcm, 0, wav, Header, pcm.Length);
        return wav;
    }
}
