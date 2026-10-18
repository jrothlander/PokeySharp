using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Audio;
using Speech;

namespace Demo1;

/// <summary>
/// Everything a game needs to make it talk. It is about thirty lines.
///
/// <b>The whole pipeline, in three calls.</b> <see cref="LetterToSound.Translate"/> turns
/// English into phonemes, <see cref="SpeechSource.Speak"/> turns phonemes into a buffer of
/// levels, and <see cref="Hold"/> below turns those levels into PCM a
/// <see cref="SoundEffect"/> will play. Nothing else is required, and no file is loaded
/// from disk — the words are synthesised at load from their spelling.
///
/// <b>The levels are the interesting number.</b> <c>Speak</c> takes a <c>maxLevel</c>
/// because the synthesiser deliberately knows nothing about what it is being played
/// through. Pass 15 and you get the 1982 sound: four bits, which is all a POKEY's volume
/// nibble could hold, and how S.A.M. actually spoke on an Atari. Pass 32767 and you get
/// the same synthesiser with the quantisation taken off. That single argument is the only
/// difference between the two, which is worth hearing.
/// </summary>
public sealed class Voice : IDisposable
{
    /// <summary>What MonoGame will play. The synthesiser's own rate is far lower — see
    /// <see cref="Hold"/> for why the two are not the same number.</summary>
    private const int OutputRate = 44100;

    private readonly List<SoundEffect> _built = new();
    private readonly Dictionary<(int Line, int Setting), SoundEffect> _cache = new();

    /// <summary>Turn the audio off without unwiring the calls.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Says an English sentence.
    ///
    /// The phonemes are derived rather than written by hand, which is what a game would
    /// do — you want to write "EXTRA LIFE" in your source, not <c>EH1 K S T R AH0 L AY1
    /// F</c>. <paramref name="guessed"/> reports the words that came from the rules rather
    /// than the exception dictionary; they are not necessarily wrong, but they are the
    /// ones worth listening to before shipping.
    /// </summary>
    public static SoundEffect Say(string english, VoiceSetting setting, out string phonemes,
                                  out List<string> guessed, string melody = null)
    {
        phonemes = LetterToSound.Translate(english, out guessed);
        return Speak(phonemes, english, setting, melody);
    }

    /// <summary>
    /// Says a phoneme string directly, for the words the rules get wrong. Both layers ship
    /// for the same reason S.A.M. shipped both.
    /// </summary>
    /// <param name="melody">
    /// An optional tune, in <see cref="Song"/>'s notation, one note per syllable.
    ///
    /// <b>A melody replaces the prosody rather than adding to it</b> — a sung syllable's
    /// pitch and length come from its note, not from where the stress fell or how far the
    /// sentence had declined. That makes it the way to fix a line the contour cannot say
    /// on its own: "Shall we play a game?" is a question, and hand-written notes give it
    /// the rise that <see cref="Intonation"/> alone does not.
    /// </param>
    public static SoundEffect Speak(string phonemes, string english, VoiceSetting setting,
                                    string melody = null)
    {
        // A question rises and a statement falls. Punctuation is the signal -- English
        // also marks questions syntactically, but a rising contour is wrong for many of
        // those anyway ("where are you" falls). Ignored when there is a melody, which
        // decides every syllable's pitch itself.
        var intonation = SpeechSource.IntonationOf(english);

        byte[] levels = SpeechSource.Speak(phonemes, setting.DacRateHz, setting.MaxLevel,
                                           out _, setting.Profile, setting.Speed, intonation,
                                           melody);

        return levels.Length == 0
            ? null
            : new SoundEffect(Hold(levels, setting), OutputRate, AudioChannels.Mono);
    }

    /// <summary>
    /// Turns the synthesiser's levels into 16-bit PCM at the output rate.
    ///
    /// <b>Sample-and-hold, not interpolation, and that is not laziness.</b> The levels
    /// arrive at the rate the DAC was written at — 8kHz, or 4kHz for a really period
    /// result — and a real POKEY holds each nibble until the interrupt writes the next
    /// one. Repeating each sample reproduces exactly that staircase, aliasing included,
    /// which is a large part of why the 1982 voice sounds the way it does. Smoothing
    /// between them would quietly fix a defect that is the whole character of the thing.
    ///
    /// The levels are unsigned, 0 to <c>MaxLevel</c>, so they are centred here: a buffer
    /// left unipolar plays as a loud click followed by the words at half amplitude.
    /// </summary>
    private static byte[] Hold(byte[] levels, VoiceSetting setting)
    {
        int count = (int)(levels.Length * (OutputRate / setting.DacRateHz));
        var pcm = new byte[count * 2];
        float centre = setting.MaxLevel / 2f;

        for (int i = 0; i < count; i++)
        {
            int source = (int)(i * setting.DacRateHz / OutputRate);
            if (source >= levels.Length) source = levels.Length - 1;

            float value = (levels[source] - centre) / centre;
            short sample = (short)(value * setting.Amplitude * short.MaxValue);

            // 16-bit PCM, little-endian: low byte first. Backwards gives noise that is
            // almost your voice, which is a horrible thing to debug.
            pcm[i * 2] = (byte)(sample & 0xff);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }

        return pcm;
    }

    /// <summary>
    /// The demo's own player: builds a line the first time it is asked for and keeps it.
    ///
    /// <b>A game would not do this.</b> With a fixed script you build every line once in
    /// <c>LoadContent</c>, the way <see cref="Sounds"/> does. The cache is here only
    /// because this demo multiplies a dozen lines by several voice settings, and building
    /// sixty utterances at startup to hear two of them would be silly.
    /// </summary>
    public SoundEffect Get(int line, int setting)
    {
        if (_cache.TryGetValue((line, setting), out var cached)) return cached;

        var effect = Speak(VoiceLibrary.All[line].Phonemes, VoiceLibrary.All[line].English,
                           VoiceSetting.All[setting], VoiceLibrary.All[line].Melody);
        _cache[(line, setting)] = effect;
        if (effect != null) _built.Add(effect);
        return effect;
    }

    /// <summary>
    /// Plays one line, and never throws.
    ///
    /// <b>Audio is never worth a crash.</b> Sound is the one subsystem a game can lose
    /// entirely and still be completely playable, so every failure ends as silence.
    /// </summary>
    public void Play(int line, int setting)
    {
        if (!Enabled) return;

        try { Get(line, setting)?.Play(); }
        catch (Exception) { /* device lost mid-session; carry on silently */ }
    }

    public void Dispose()
    {
        foreach (var effect in _built) effect.Dispose();
        _built.Clear();
        _cache.Clear();
    }
}
