using System;
using System.Collections.Generic;
using System.IO;
using Speech;

namespace VoiceDump;

/// <summary>
/// Renders an utterance to a 16-bit mono WAV.
///
/// <b>Rendered at the DAC's own rate and level, not at CD quality.</b> The point of the
/// file is to be measured against a reference, so it has to carry the same losses the
/// real output does -- the interrupt rate, and the quantisation to POKEY's nibble.
/// A clean 44.1kHz float render would measure better than the thing it is standing in
/// for, which would make every comparison optimistic.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("voicedump <out.wav> \"<phonemes>\" [rateHz] [voice] [speed] [\"melody\"]");
            Console.WriteLine("  voice: " + string.Join(" | ", Names()));
            return 1;
        }

        string path = args[0];
        string phonemes = args[1];
        double rate = args.Length > 2 ? double.Parse(args[2]) : 8000.0;
        var voice = args.Length > 3 ? Find(args[3]) : VoiceProfile.Default;
        double? speed = args.Length > 4 ? double.Parse(args[4]) : null;
        string melody = args.Length > 5 ? args[5] : null;

        // Four bits, because that is what it plays through. See the class summary.
        const int Levels = 15;

        var samples = SpeechSource.Speak(phonemes, rate, Levels, out List<string> unknown,
                                        voice, speed, SpeechSource.IntonationOf(phonemes), melody);

        if (unknown.Count > 0)
            Console.WriteLine($"not recognised: {string.Join(" ", unknown)}");

        if (samples.Length == 0) { Console.WriteLine("nothing to render"); return 1; }

        WriteWav(path, samples, rate);
        Console.WriteLine($"{path}  {samples.Length} samples  {samples.Length / rate:0.000} s  "
                          + $"{rate:0} Hz  voice \"{voice.Name}\"  pitch {voice.PitchHz:0} Hz  "
                          + $"speed {speed ?? voice.Speed:0.00}");
        return 0;
    }

    /// <summary>Matches a voice by case-insensitive prefix, so "low" finds "Low and
    /// deliberate" without anyone quoting a whole name at a shell.</summary>
    private static VoiceProfile Find(string name)
    {
        foreach (var profile in VoiceProfile.All)
            if (profile.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return profile;

        Console.WriteLine($"no voice matching \"{name}\"; using {VoiceProfile.Default.Name}");
        return VoiceProfile.Default;
    }

    private static IEnumerable<string> Names()
    {
        foreach (var profile in VoiceProfile.All) yield return profile.Name;
    }

    /// <summary>
    /// Writes the nibble stream out as 16-bit PCM.
    ///
    /// The unipolar 0-15 values are centred on the way out, because a WAV with a
    /// half-scale DC offset measures as having enormous energy at 0Hz and skews every
    /// spectral statistic taken from it. That the chip itself removes the same offset
    /// with a capacitor is the same fact, one layer down.
    /// </summary>
    private static void WriteWav(string path, byte[] samples, double rate)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        int dataBytes = samples.Length * 2;

        writer.Write(new[] { 'R', 'I', 'F', 'F' });
        writer.Write(36 + dataBytes);
        writer.Write(new[] { 'W', 'A', 'V', 'E' });
        writer.Write(new[] { 'f', 'm', 't', ' ' });
        writer.Write(16);
        writer.Write((short)1);                       // PCM
        writer.Write((short)1);                       // mono
        writer.Write((int)rate);
        writer.Write((int)rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(new[] { 'd', 'a', 't', 'a' });
        writer.Write(dataBytes);

        foreach (byte sample in samples)
        {
            double centred = (sample - 7.5) / 7.5;    // 0..15 -> -1..1
            writer.Write((short)Math.Clamp(centred * 32000.0, short.MinValue, short.MaxValue));
        }
    }
}
