using System;
using System.Windows.Forms;

namespace PokeyLabForms;

/// <summary>
/// Entry point.
///
/// No <c>--selftest</c> here, deliberately. The emulation checks live in
/// <c>PokeyLab/SelfTest.cs</c> and run against the same <c>Pokey</c> library this
/// app references, so <c>dotnet run --project PokeyLab -- --selftest</c> already
/// covers everything worth asserting about the chip. Duplicating them would give
/// two copies to keep in step and no more coverage; what is unique to this project
/// is Windows Forms layout and a wave device, neither of which a headless test can
/// speak to.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Set explicitly rather than through the generated ApplicationConfiguration
        // helper: this project has ImplicitUsings off to match the rest of the
        // solution, and three plain calls are clearer than a source generator.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        Application.Run(new MainForm());
    }
}
