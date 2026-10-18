using System.Drawing;

namespace PokeyLabForms;

/// <summary>
/// Colours for the three instrument displays -- the scope, the sweep graph and the
/// poly trains.
///
/// <b>Only those three.</b> Everything else on this form is an ordinary Windows
/// control wearing the system theme, which is the entire point of a Forms front end:
/// a checkbox should look like the operating system's checkbox. But a waveform on a
/// grey panel reads as a chart, and these are not charts -- they are what the chip is
/// doing, and they came off a vector monitor. Drawing them on black in the arcade's
/// own hues keeps that distinction visible and keeps this app's displays and
/// PokeyLab's recognisably the same picture.
///
/// Values are RimWalker's, taken from <c>Core/Palette.cs</c> and the semantic names in
/// <c>Core/RimWalkerSkin.cs</c> rather than picked again by eye.
/// </summary>
public static class LabColors
{
    public static readonly Color Background = Color.FromArgb(0, 0, 0);

    /// <summary>Yellow. The thing being measured: the trace, the sweep curve, the
    /// counter actually gating the selected channel.</summary>
    public static readonly Color Accent = Color.FromArgb(0xFF, 0xE0, 0x20);

    /// <summary>Cyan. Labels and secondary marks -- headings on a display, the
    /// sampling ticks under a poly train.</summary>
    public static readonly Color Cool = Color.FromArgb(0x30, 0xE0, 0xFF);

    /// <summary>Green. A measurement that holds.</summary>
    public static readonly Color Good = Color.FromArgb(0x30, 0xFF, 0x40);

    /// <summary>Red. A measurement that does not, and the two markers that mean
    /// "here": the playhead, and where a counter repeats.</summary>
    public static readonly Color Bad = Color.FromArgb(0xFF, 0x30, 0x20);

    public static readonly Color Ink = Color.FromArgb(0xFF, 0xFF, 0xFF);
    public static readonly Color InkFaint = Color.FromArgb(0x50, 0x50, 0x60);

    /// <summary>Axes and cell outlines.</summary>
    public static readonly Color Rule = Color.FromArgb(0x40, 0x40, 0x55);

    /// <summary>The scope's centre line -- present, but never competing with the
    /// trace over it.</summary>
    public static readonly Color SurfaceDim = Color.FromArgb(0x18, 0x18, 0x22);
}
