using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace RimWalker.Core;

/// <summary>
/// A stroke font: every glyph is a list of polylines drawn by the same beam that
/// draws the game.
///
/// Deliberately not a bitmap font and not a SpriteFont. Tempest's text came out
/// of the same vector generator as its tubes -- the characters are strokes, not
/// pixel cells -- so a glyph here is a handful of line segments on a 4-wide by
/// 6-tall grid, and scaling it is just scaling the coordinates. That is also why
/// text can be drawn at any size without going soft.
///
/// Only the characters the game needs exist; anything else draws as a blank.
/// </summary>
public sealed class VectorFont
{
    // Public because the font explainer page draws this grid, and a page drawing
    // a grid of its own choosing while claiming to explain this one would be a lie
    // the first time either number changed.

    /// <summary>Glyph design grid: 4 units wide, 6 tall, y increasing downward.</summary>
    public const float GridWidth = 4f;
    public const float GridHeight = 6f;
    /// <summary>Advance per character in grid units, including the 1-unit gap.</summary>
    public const float GridAdvance = 5f;

    private readonly Dictionary<char, Vector2[][]> _glyphs = new();

    public VectorFont()
    {
        foreach (var (ch, strokes) in Strokes)
            _glyphs[ch] = Parse(strokes);
    }

    /// <summary><paramref name="size"/> is the cap height in virtual units.</summary>
    public float Measure(string text, float size)
        => text.Length <= 0 ? 0f : (text.Length * GridAdvance - 1f) * (size / GridHeight);

    public float LineHeight(float size) => size;

    // --- Reading the font's own construction ------------------------------

    /// <summary>Every character the font can draw, in declaration order.</summary>
    public static IEnumerable<char> Characters
    {
        get { foreach (var (ch, _) in Strokes) yield return ch; }
    }

    public static int GlyphCount => Strokes.Length;

    /// <summary>The parsed polylines for a character, in grid coordinates.</summary>
    public bool TryGetStrokes(char ch, out Vector2[][] strokes)
        => _glyphs.TryGetValue(char.ToUpperInvariant(ch), out strokes);

    /// <summary>The literal <c>"x,y x,y ..."</c> strings a glyph was authored as,
    /// so the page prints the same text the source file holds.</summary>
    public static string[] StrokeSource(char ch)
    {
        char upper = char.ToUpperInvariant(ch);
        foreach (var (candidate, strokes) in Strokes)
            if (candidate == upper) return strokes;
        return System.Array.Empty<string>();
    }

    public void Draw(VectorRenderer beam, string text, Vector2 topLeft, float size, Color colour,
                     float thickness = VectorRenderer.DefaultThickness)
    {
        float unit = size / GridHeight;
        float x = topLeft.X;

        foreach (char raw in text)
        {
            char ch = char.ToUpperInvariant(raw);
            if (_glyphs.TryGetValue(ch, out var strokes))
            {
                foreach (var stroke in strokes)
                {
                    for (int i = 0; i + 1 < stroke.Length; i++)
                    {
                        var a = new Vector2(x + stroke[i].X * unit, topLeft.Y + stroke[i].Y * unit);
                        var b = new Vector2(x + stroke[i + 1].X * unit, topLeft.Y + stroke[i + 1].Y * unit);
                        beam.Line(a, b, colour, thickness);
                    }
                }
            }
            x += GridAdvance * unit;
        }
    }

    public void DrawCentred(VectorRenderer beam, string text, float centreX, float y, float size, Color colour,
                            float thickness = VectorRenderer.DefaultThickness)
        => Draw(beam, text, new Vector2(centreX - Measure(text, size) / 2f, y), size, colour, thickness);

    public void DrawRight(VectorRenderer beam, string text, float rightX, float y, float size, Color colour,
                          float thickness = VectorRenderer.DefaultThickness)
        => Draw(beam, text, new Vector2(rightX - Measure(text, size), y), size, colour, thickness);

    /// <summary>"x,y x,y ..." per stroke, on the 4x6 grid.</summary>
    private static Vector2[][] Parse(string[] strokes)
    {
        var result = new Vector2[strokes.Length][];
        for (int s = 0; s < strokes.Length; s++)
        {
            var pairs = strokes[s].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var points = new Vector2[pairs.Length];
            for (int p = 0; p < pairs.Length; p++)
            {
                var xy = pairs[p].Split(',');
                points[p] = new Vector2(
                    float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
            }
            result[s] = points;
        }
        return result;
    }

    private static readonly (char, string[])[] Strokes =
    {
        (' ', new string[0]),
        ('A', new[]{ "0,6 2,0 4,6", "0.7,4 3.3,4" }),
        ('B', new[]{ "0,0 0,6", "0,0 3,0 4,1 4,2 3,3 0,3", "0,3 3,3 4,4 4,5 3,6 0,6" }),
        ('C', new[]{ "4,1 3,0 1,0 0,1 0,5 1,6 3,6 4,5" }),
        ('D', new[]{ "0,0 0,6", "0,0 3,0 4,1 4,5 3,6 0,6" }),
        ('E', new[]{ "4,0 0,0 0,6 4,6", "0,3 3,3" }),
        ('F', new[]{ "4,0 0,0 0,6", "0,3 3,3" }),
        ('G', new[]{ "4,1 3,0 1,0 0,1 0,5 1,6 3,6 4,5 4,3 2,3" }),
        ('H', new[]{ "0,0 0,6", "4,0 4,6", "0,3 4,3" }),
        ('I', new[]{ "1,0 3,0", "2,0 2,6", "1,6 3,6" }),
        ('J', new[]{ "3,0 3,5 2,6 1,6 0,5" }),
        ('K', new[]{ "0,0 0,6", "4,0 0,3 4,6" }),
        ('L', new[]{ "0,0 0,6 4,6" }),
        ('M', new[]{ "0,6 0,0 2,3 4,0 4,6" }),
        ('N', new[]{ "0,6 0,0 4,6 4,0" }),
        ('O', new[]{ "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0" }),
        ('P', new[]{ "0,6 0,0 3,0 4,1 4,2 3,3 0,3" }),
        ('Q', new[]{ "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0", "2,4 4,6" }),
        ('R', new[]{ "0,6 0,0 3,0 4,1 4,2 3,3 0,3", "2,3 4,6" }),
        ('S', new[]{ "4,1 3,0 1,0 0,1 0,2 1,3 3,3 4,4 4,5 3,6 1,6 0,5" }),
        ('T', new[]{ "0,0 4,0", "2,0 2,6" }),
        ('U', new[]{ "0,0 0,5 1,6 3,6 4,5 4,0" }),
        ('V', new[]{ "0,0 2,6 4,0" }),
        ('W', new[]{ "0,0 1,6 2,3 3,6 4,0" }),
        ('X', new[]{ "0,0 4,6", "4,0 0,6" }),
        ('Y', new[]{ "0,0 2,3 4,0", "2,3 2,6" }),
        ('Z', new[]{ "0,0 4,0 0,6 4,6" }),
        ('0', new[]{ "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0", "0,5 4,1" }),
        ('1', new[]{ "1,1 2,0 2,6", "1,6 3,6" }),
        ('2', new[]{ "0,1 1,0 3,0 4,1 4,2 0,6 4,6" }),
        ('3', new[]{ "0,0 4,0 2,3 4,4 4,5 3,6 1,6 0,5" }),
        ('4', new[]{ "3,6 3,0 0,4 4,4" }),
        ('5', new[]{ "4,0 0,0 0,3 3,3 4,4 4,5 3,6 1,6 0,5" }),
        ('6', new[]{ "4,1 3,0 1,0 0,1 0,5 1,6 3,6 4,5 4,4 3,3 0,3" }),
        ('7', new[]{ "0,0 4,0 1,6" }),
        ('8', new[]{ "1,3 0,2 0,1 1,0 3,0 4,1 4,2 3,3 1,3", "1,3 0,4 0,5 1,6 3,6 4,5 4,4 3,3 1,3" }),
        ('9', new[]{ "0,5 1,6 3,6 4,5 4,1 3,0 1,0 0,1 0,2 1,3 4,3" }),
        ('-', new[]{ "0,3 4,3" }),
        ('.', new[]{ "2,5.4 2,6" }),
        (',', new[]{ "2,5 1,6.4" }),
        (':', new[]{ "2,1.6 2,2.2", "2,3.8 2,4.4" }),
        ('/', new[]{ "4,0 0,6" }),
        ('!', new[]{ "2,0 2,4", "2,5.4 2,6" }),
        ('?', new[]{ "0,1 1,0 3,0 4,1 4,2 2,3.5 2,4", "2,5.4 2,6" }),
        ('\'', new[]{ "2,0 2,2" }),
        ('(', new[]{ "3,0 1,2 1,4 3,6" }),
        (')', new[]{ "1,0 3,2 3,4 1,6" }),
        ('*', new[]{ "2,1 2,5", "0.3,2 3.7,4", "3.7,2 0.3,4" }),
        ('+', new[]{ "2,1 2,5", "0,3 4,3" }),
        ('<', new[]{ "4,0 0,3 4,6" }),
        ('>', new[]{ "0,0 4,3 0,6" }),
    };
}
