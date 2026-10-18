using System;

namespace Demo1;

/// <summary>
/// The entry point, written out longhand rather than as a top-level statement.
///
/// <b>Only so that <c>[STAThread]</c> has somewhere to live.</b> The sounds page opens a
/// common file dialog, which requires a single-threaded apartment, and an owned modal
/// dialog additionally requires that its owner window belong to the calling thread. Running
/// it on a worker thread satisfies the first and breaks the second — the dialog then either
/// refuses to show or appears behind the game with no way to say so. Marking the main
/// thread STA lets the dialog run where the window already is, which is what every WinForms
/// host does and what the MonoGame WindowsDX template does by default.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var game = new Game1();
        game.Run();
    }
}
