using System;

namespace Pokey;

/// <summary>
/// A stream of 4-bit samples driven straight at one channel's volume nibble.
///
/// <b>This is the other way to make a POKEY make a noise, and it is the opposite of
/// everything <see cref="PokeyChip.Tick"/> does.</b> Normally the chip is the
/// instrument: a divider sets a rate, a poly gates the toggle, and the CPU only has to
/// write a register when something changes. Here the chip is a loudspeaker. The
/// channel goes into volume-only mode, which cuts the divider and the poly out
/// entirely, and something upstream rewrites the volume nibble at the sample rate. The
/// sound is whatever that upstream thing computed; the chip contributes nothing but a
/// 4-bit DAC.
///
/// <b>This is how S.A.M. talked.</b> Software Automatic Mouth (Mark Barton, 1982) was
/// a formant synthesiser running on the 6502; on an Atari it had nowhere to put its
/// output except this nibble, because volume-only POKEY is the machine's only sample
/// path. The same trick is the C64's <c>$D418</c> volume-register digi. So a POKEY
/// speaking is a POKEY switched off, and that is worth being able to hear next to a
/// POKEY doing its actual job -- which is why this lives in the library beside
/// <see cref="StepEnvelope"/> rather than in a front end.
///
/// <b>Structurally it is StepEnvelope.</b> Both are "walk a table on a timer and
/// rewrite registers, cutting the audio block at the boundaries so the result does not
/// depend on the buffer size". The only differences are the rate (thousands of writes a
/// second rather than a couple of hundred) and that a step here is one byte rather than
/// four. That similarity is the real answer to whether arcade sound and speech
/// synthesis are the same kind of thing: the driving loop is identical, and all the
/// distance is in how much the thing being driven does for you.
/// </summary>
public sealed class SampleVoice
{
    /// <summary>The samples, one 4-bit value per entry, 0-15. Values above 15 are
    /// masked when written, not rejected -- the nibble is the hardware's, and clipping
    /// quietly is what the register does.</summary>
    public byte[] Samples { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// How often the nibble is rewritten.
    ///
    /// <b>Not the chip's clock and not the output rate</b> -- it is the rate of the
    /// interrupt doing the writing, and on a 1MHz 6502 that ceiling is low. Somewhere
    /// between 4kHz and 11kHz is the honest range for the period, and the difference is
    /// audible: at 4kHz everything above 2kHz is gone, which takes the third formant
    /// and most of the consonants with it.
    /// </summary>
    public double SampleRateHz { get; set; } = 8000.0;

    public int Channel { get; set; }

    public bool Looping { get; set; }

    public bool Playing { get; private set; }

    /// <summary>Index of the sample currently being held, for a playhead.</summary>
    public int Position { get; private set; }

    private double _samplesToNextWrite;

    public double Seconds => Samples.Length / Math.Max(1.0, SampleRateHz);

    /// <summary>
    /// Starts playback, putting the channel into volume-only mode.
    ///
    /// The distortion field is cleared as well as the mode bit being set. It has no
    /// effect in volume-only mode, so leaving whatever was there would do no harm --
    /// but it would leave the channel strip reading a distortion that is not being
    /// consulted, which is precisely the "two representations of one register" problem
    /// the poly page was fixed for.
    /// </summary>
    public void Trigger(PokeyChip chip)
    {
        if (Samples.Length == 0) { Playing = false; return; }

        Position = 0;
        _samplesToNextWrite = 0.0;
        Playing = true;
        Write(chip);
    }

    /// <summary>
    /// Stops, and hands the channel back silent and out of volume-only mode.
    ///
    /// Leaving the mode bit set would be the subtler bug of the two: the channel would
    /// hold its last nibble as a constant level, which is inaudible on its own because
    /// <see cref="PokeyChip.Render"/>'s DC blocker removes it, while still taking its
    /// share of the four-channel headroom in <see cref="PokeyChip.Mix"/>. Every other
    /// channel would quietly get quieter and nothing on screen would say why.
    /// </summary>
    public void Stop(PokeyChip chip)
    {
        Playing = false;
        chip.SetAudc(Channel, 0);
    }

    private void Write(PokeyChip chip)
    {
        int value = Position >= 0 && Position < Samples.Length ? Samples[Position] : 0;
        chip.SetAudc(Channel, PokeyChip.AudcVolumeOnly | (value & PokeyChip.AudcVolumeMask));
    }

    /// <summary>
    /// Renders audio while stepping through the samples in time with it.
    ///
    /// Same shape as <see cref="StepEnvelope.Render"/> and for the same reason: the
    /// chip runs at over a megahertz and this at a few thousand hertz, so a block of a
    /// few hundred output samples spans several writes. Writing once per block would
    /// quantise the playback to the audio buffer size and make the sound depend on it,
    /// which is the kind of error that sounds close enough and cannot be chased later.
    ///
    /// Renders the chip flat when not playing, so a caller can always just call this.
    ///
    /// <b>One limit, shared with StepEnvelope:</b> a sample rate above the output rate
    /// would want less than one output sample per write, and the floor of one sample
    /// per slice means it would run slow instead. Nothing near the period's 4-11kHz
    /// comes close, so it is stated rather than guarded.
    /// </summary>
    public void Render(PokeyChip chip, float[] buffer, int offset, int count, double sampleRate)
    {
        double outputPerSample = sampleRate / Math.Max(1.0, SampleRateHz);
        int done = 0;

        while (done < count)
        {
            if (!Playing)
            {
                chip.Render(buffer, offset + done, count - done, sampleRate);
                return;
            }

            if (_samplesToNextWrite <= 0.0)
            {
                Position++;
                if (Position >= Samples.Length)
                {
                    if (Looping) Position = 0;
                    else { Stop(chip); continue; }
                }

                Write(chip);
                _samplesToNextWrite += outputPerSample;
            }

            int slice = Math.Min(count - done, Math.Max(1, (int)_samplesToNextWrite));
            chip.Render(buffer, offset + done, slice, sampleRate);
            _samplesToNextWrite -= slice;
            done += slice;
        }
    }
}
