using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RimWalker.Core;

namespace Demo1;

/// <summary>
/// The drawing surface: a palette, a stroke font, and the handful of primitives every
/// page here needs.
///
/// <b>Every glyph is line segments</b>, drawn by RimWalker's vector font. The repo ships
/// no asset files, so a SpriteFont would mean a content build, a <c>.spritefont</c> and a
/// font licence to think about. Strokes cost none of that, and they suit a demo about a
/// chip from 1979.
/// </summary>
public sealed class Screen : IDisposable
{
    public const int Width = 1280, Height = 800;

    // --- Palette ----------------------------------------------------------
    // Amber on near-black, the way a vector cabinet's overlay looks, with one cyan for
    // measured values so numbers separate from prose at a glance.
    public static readonly Color Background = new(8, 9, 12);
    public static readonly Color Heading = new(255, 176, 32);
    public static readonly Color Number = new(89, 216, 230);
    public static readonly Color Body = new(186, 190, 198);
    public static readonly Color Chosen = new(255, 255, 255);
    public static readonly Color RuleColour = new(38, 42, 50);
    public static readonly Color Live = new(126, 232, 148);

    /// <summary>Lifted once the strokes got thinner: a hairline needs more contrast than a
    /// fat one to carry the same weight, so the value that read as "quiet" before read as
    /// "barely there" afterwards.</summary>
    public static readonly Color Dim = new(122, 128, 138);

    private readonly VectorRenderer _beam;
    private readonly VectorFont _font;

    public SpriteBatch Batch { get; }

    public Screen(GraphicsDevice device)
    {
        Batch = new SpriteBatch(device);
        _beam = new VectorRenderer(device);
        _font = new VectorFont();
    }

    /// <summary>
    /// How wide the bright core of a stroke should be for text of a given size.
    ///
    /// <b>The reason this exists at all.</b> <see cref="VectorRenderer"/> draws every
    /// stroke three times — a wide dim pass, a medium one, and a bright core — which is
    /// what makes it look like a phosphor rather than a line. But those passes are
    /// multiples of the thickness, and the thickness was a constant tuned for RimWalker's
    /// large HUD text. Used unchanged for a twelve-pixel list row it puts a thirteen-pixel
    /// halo on an eight-pixel glyph, and every counter — the hole in an O, the bowl of a
    /// P — fills in solid.
    ///
    /// Tying it to the size keeps the glow proportional, so small text stays open and
    /// large text keeps the bloom. The ratio is below what the title alone would suggest,
    /// deliberately: the glow spreads by a fixed multiple of the core, so small type needs
    /// a relatively lighter stroke to survive it, not merely a smaller one. The floor
    /// stops the thinnest labels from disappearing.
    /// </summary>
    public static float Weight(float size) => MathF.Max(0.9f, size * 0.09f);

    public void Begin()
    {
        // Additive, not the default alpha blending, because the renderer's three passes
        // are meant to sum. Composited with alpha instead, each pass paints over the last
        // and the core never reaches full brightness -- the text comes out both haloed and
        // dim. RimWalkerGame.Draw begins its batch the same way, for the same reason.
        Batch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
                    DepthStencilState.None, RasterizerState.CullNone);
        _beam.Begin(Batch);
    }

    public void End() => Batch.End();

    public void Text(string text, float x, float y, float size, Color colour)
    {
        _font.Draw(_beam, text, new Vector2(x, y), size, colour, Weight(size));
        if (text.Contains('#')) Sharps(text, x, y, size, colour);
    }

    public void TextRight(string text, float rightX, float y, float size, Color colour)
        => Text(text, rightX - Measure(text, size), y, size, colour);

    /// <summary>
    /// Draws the <c>#</c> the font does not have, over the blank it leaves.
    ///
    /// <b>Here rather than in the font</b>, because that is RimWalker's, linked, and its
    /// glyph set is the game's — adding one there changes the font explainer page in the
    /// game to suit a demo. Note names are the only thing that needs it, and the font is
    /// fixed-pitch, so the blank is exactly where the glyph would have been.
    /// </summary>
    private void Sharps(string text, float x, float y, float size, Color colour)
    {
        float unit = size / VectorFont.GridHeight;
        float weight = Weight(size);

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '#') continue;

            float left = x + i * VectorFont.GridAdvance * unit;
            Vector2 At(float gx, float gy) => new(left + gx * unit, y + gy * unit);

            _beam.Line(At(1.7f, 0.4f), At(1.1f, 5.6f), colour, weight);
            _beam.Line(At(3.3f, 0.4f), At(2.7f, 5.6f), colour, weight);
            _beam.Line(At(0.2f, 2.1f), At(4f, 2.1f), colour, weight);
            _beam.Line(At(0f, 3.9f), At(3.8f, 3.9f), colour, weight);
        }
    }

    public float Measure(string text, float size) => _font.Measure(text, size);

    public void Line(Vector2 a, Vector2 b, Color colour, float thickness)
        => _beam.Line(a, b, colour, thickness);

    public void Rule(float x, float y, float width) => Rule(x, y, width, RuleColour);

    public void Rule(float x, float y, float width, Color colour)
        => _beam.Line(new Vector2(x, y), new Vector2(x + width, y), colour, 1.4f);

    /// <summary>
    /// Breaks a sentence into lines that fit.
    ///
    /// The vector font is fixed-pitch, so this could count characters — but it measures
    /// instead, because that stays correct if the font ever stops being fixed-pitch, and
    /// costs nothing at twenty words.
    /// </summary>
    public IEnumerable<string> Wrap(string text, float width, float size)
    {
        var line = new StringBuilder();

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Measure(candidate, size) > width)
            {
                yield return line.ToString();
                line.Clear().Append(word);
            }
            else
            {
                line.Clear().Append(candidate);
            }
        }

        if (line.Length > 0) yield return line.ToString();
    }

    /// <summary>The beam's texel is unmanaged memory and nothing else will free it.</summary>
    public void Dispose()
    {
        _beam.Dispose();
        Batch.Dispose();
    }
}
