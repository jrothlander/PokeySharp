using System;
using Microsoft.Xna.Framework.Audio;
using Pokey;

namespace Demo1;

/// <summary>
/// Every sound the demo can make, built once at load from a handful of integers.
///
/// <b>Nothing is loaded from disk.</b> Each voice is a pair of short step chains rendered
/// through an emulated POKEY at startup — the same arrangement RimWalker uses in
/// <c>Core/Sfx.cs</c>, and the reason neither ships an audio file.
///
/// The recipes themselves live in <see cref="SoundLibrary"/>, deliberately apart from
/// this class: that table has no MonoGame in it, so it can be rendered and measured by a
/// headless test. This is the half that needs an audio device.
///
/// <b>Built once, in <c>LoadContent</c>.</b> Rendering a voice costs a few milliseconds
/// and allocates an unmanaged buffer; doing it per shot would both stutter and leak.
/// </summary>
public sealed class Sounds : IDisposable
{
    private readonly SoundEffect[] _effects;
    private readonly EnvelopeBank _bank;

    /// <summary>Turn the audio off without unwiring the calls. Handy when something else
    /// is being debugged and the noise is in the way.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Renders every recipe in a bank.
    ///
    /// <b>The bank's clock and tick, not the app's.</b> Every pitch scales with the clock,
    /// so a set designed against RimWalker's 32kHz divider and played at Tempest's 54kHz is
    /// in the wrong key — which is exactly the mistake that becomes possible once banks can
    /// come from different games.
    /// </summary>
    public Sounds(EnvelopeBank bank)
    {
        _bank = bank;
        _effects = new SoundEffect[bank.Recipes.Length];

        for (int i = 0; i < bank.Recipes.Length; i++)
        {
            _effects[i] = ChipSound.Build(
                audf: bank.Recipes[i].Audf,
                audc: bank.Recipes[i].Audc,
                tickHz: bank.TickHz,
                baseClockHz: bank.ClockHz,
                amplitude: bank.Recipes[i].Amplitude);
        }
    }

    /// <summary>Plays the recipe at <paramref name="index"/> in this bank.</summary>
    public void Play(int index)
    {
        if (index >= 0 && index < _effects.Length) Play(_effects[index]);
    }

    /// <summary>
    /// Plays one by name, which is how a game would reach for it.
    ///
    /// A real game would resolve the index once at load and hold it, rather than
    /// searching a table on every shot; this exists so the demo's own interface sounds
    /// can name what they want without hard-coding a row number that moves whenever the
    /// library gains an entry.
    /// </summary>
    public void Play(string name)
    {
        for (int i = 0; i < _bank.Recipes.Length; i++)
        {
            if (_bank.Recipes[i].Name == name) { Play(_effects[i]); return; }
        }
    }

    /// <summary>
    /// Plays one effect, and never throws.
    ///
    /// <b>Audio is never worth a crash.</b> Sound is the one subsystem a game can lose
    /// entirely and still be completely playable, so every failure ends as silence. The
    /// catch is for a device that worked at startup and has since gone — a headset
    /// unplugged mid-game, a driver reset — which throws here rather than at
    /// construction. RimWalker's <c>Sfx.Play</c> does exactly this, for the same reason.
    /// </summary>
    private void Play(SoundEffect effect)
    {
        if (!Enabled || effect == null) return;

        try { effect.Play(); }
        catch (Exception) { /* device lost mid-session; carry on silently */ }
    }

    /// <summary><c>SoundEffect</c> holds unmanaged audio-buffer memory. Build them once at
    /// load and dispose them together; building one per frame leaks until it hurts.</summary>
    public void Dispose()
    {
        foreach (var effect in _effects) effect?.Dispose();
        Array.Clear(_effects);
    }
}
