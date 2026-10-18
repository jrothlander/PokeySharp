using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RimWalker.Core;

/// <summary>
/// Draws glowing line segments -- the only drawing primitive in the game.
///
/// Tempest had no sprites and no raster at all. Its picture was a list of
/// segment endpoints handed to a deflection amplifier, so the honest primitive
/// here is a line, not a textured quad. Each segment is drawn as a stretched 1x1
/// white texel, three times: a wide dim pass, a medium pass, and a thin bright
/// core, summed with additive blending. That is a fair imitation of what a
/// phosphor actually does -- the beam saturates the centre of the stroke and
/// bleeds outward -- and it is why bright overlapping lines bloom to white the
/// way the cabinet's did.
///
/// There is no render target and no integer scaling. See <see cref="Layout"/>
/// for why.
/// </summary>
public sealed class VectorRenderer : IDisposable
{
    /// <summary>Width of the bright core stroke, in virtual units.</summary>
    public const float DefaultThickness = 2.6f;

    private readonly Texture2D _texel;
    private SpriteBatch _batch;

    /// <summary>Beam intensity multiplier, 0..1. The arcade dimmed the whole
    /// display during attract mode and flashed it on a hit; one global knob is
    /// enough to reproduce both.</summary>
    public float Intensity { get; set; } = 1f;

    public VectorRenderer(GraphicsDevice device)
    {
        _texel = new Texture2D(device, 1, 1);
        _texel.SetData(new[] { Color.White });
    }

    /// <summary>Call once per frame, inside an already-begun additive batch.</summary>
    public void Begin(SpriteBatch batch) => _batch = batch;

    /// <summary>The three glow passes: (width multiplier, brightness).</summary>
    private static readonly (float Width, float Alpha)[] Passes =
    {
        (5.0f, 0.10f),
        (2.2f, 0.22f),
        (1.0f, 0.85f),
    };

    public void Line(Vector2 a, Vector2 b, Color colour, float thickness = DefaultThickness)
    {
        var delta = b - a;
        float length = delta.Length();

        // A zero-length segment is a legitimate thing to ask for -- a dot, which
        // is what a vector display shows when the beam is parked. Give it enough
        // length to render as one.
        if (length < 0.001f)
        {
            length = thickness;
            delta = new Vector2(thickness, 0f);
            a -= new Vector2(thickness / 2f, 0f);
        }

        float angle = MathF.Atan2(delta.Y, delta.X);
        foreach (var (widthScale, alpha) in Passes)
        {
            float width = thickness * widthScale;
            _batch.Draw(
                _texel,
                a,
                null,
                colour * (alpha * Intensity),
                angle,
                new Vector2(0f, 0.5f),
                new Vector2(length, width),
                SpriteEffects.None,
                0f);
        }
    }

    /// <summary>Connects <paramref name="points"/> in order; closes the loop when asked.</summary>
    public void Polyline(Vector2[] points, bool closed, Color colour, float thickness = DefaultThickness)
    {
        for (int i = 0; i + 1 < points.Length; i++)
            Line(points[i], points[i + 1], colour, thickness);
        if (closed && points.Length > 2)
            Line(points[^1], points[0], colour, thickness);
    }

    /// <summary>A regular n-gon, used for explosion rings and the level-select ring.</summary>
    public void Circle(Vector2 centre, float radius, Color colour, int segments = 16,
                       float thickness = DefaultThickness)
    {
        var previous = centre + new Vector2(radius, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = MathF.Tau * i / segments;
            var next = centre + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            Line(previous, next, colour, thickness);
            previous = next;
        }
    }

    public void Dispose() => _texel.Dispose();
}
