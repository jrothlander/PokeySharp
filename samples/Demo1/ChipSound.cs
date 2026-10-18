using Microsoft.Xna.Framework.Audio;
using Pokey;

namespace Demo1;

/// <summary>
/// Binds a rendered POKEY voice to MonoGame.
///
/// <b>All that is left here is the binding.</b> The render, the gain correction and the
/// PCM packing moved to <see cref="SoundRender"/> in the Pokey library, where RimWalker
/// can reach them too -- this file and <c>RimWalker/Core/Sfx.cs</c> used to carry a copy
/// each. What stays is the one line that turns bytes into a <see cref="SoundEffect"/>,
/// because that line is the only part that knows which MonoGame this app links: Demo1
/// builds against WindowsDX and RimWalker against DesktopGL, so the two cannot share an
/// assembly that names the type.
/// </summary>
public static class ChipSound
{
    private const int SampleRate = 44100;

    public static SoundEffect Build(StepEnvelope.Step[] audf, StepEnvelope.Step[] audc,
                                    double tickHz, double baseClockHz, float amplitude)
    {
        var pcm = SoundRender.ToPcm16(Render(audf, audc, tickHz, baseClockHz, amplitude));
        return new SoundEffect(pcm, SampleRate, AudioChannels.Mono);
    }

    /// <summary>
    /// The signal before it is packed, so the demo can draw the waveform it is about to
    /// play. Nothing is left to do to these samples but scale them to a short, which is
    /// what makes the picture on screen the sound in the speaker rather than an
    /// approximation of it.
    /// </summary>
    public static float[] Render(StepEnvelope.Step[] audf, StepEnvelope.Step[] audc,
                                 double tickHz, double baseClockHz, float amplitude)
        => SoundRender.Render(audf, audc, tickHz, baseClockHz, amplitude, SampleRate);
}
