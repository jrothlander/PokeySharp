using System;
using System.IO;
using System.Threading;

// System.Windows.Forms is spelled out rather than imported, for the reason SoundPage gives:
// its Keys enum collides with XNA's in any file that handles input.

namespace Demo1;

/// <summary>
/// The common Open dialog, owned by the game window — the one piece of WinForms the demo
/// uses, shared by the pages that open files.
///
/// <b>Shared rather than written twice because it took two tries to get right.</b> The
/// sounds page had it first; the song page wanted the same, and the fixes below are exactly
/// the kind a second copy would lose.
/// </summary>
internal static class FileDialog
{
    /// <summary>Wraps the game window so a WinForms dialog can be owned by it.</summary>
    private sealed class Owner : System.Windows.Forms.IWin32Window
    {
        public Owner(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }

    /// <summary>
    /// Asks for a file. Returns its path, or null if nothing was chosen or the dialog could
    /// not open — in which case <paramref name="failure"/> says why, for the caller to show.
    ///
    /// <b>On the calling thread, which <c>Program.Main</c> marks STA for this.</b> The
    /// dialog needs a single-threaded apartment, and an <i>owned</i> modal dialog also
    /// needs its owner window to belong to the calling thread. Doing this on a worker
    /// thread satisfies the first and breaks the second — which was a real bug: the dialog
    /// was shown owned by a window on another thread, WinForms refused, and the failure
    /// went into a silent catch, so pressing O did nothing at all.
    ///
    /// The fallback exists because that silence is the thing to avoid twice. If the thread
    /// ever is not STA, an unowned dialog on a worker is worse than this one but far better
    /// than none.
    ///
    /// <b>The game loop stops while it is open</b>, so the caller must tell
    /// <see cref="Game1"/> — see <see cref="IPage.ClearBlocked"/> — or MonoGame will read
    /// the gap as the game running slowly and try to catch up.
    /// </summary>
    /// <param name="owner">The game window. <b>Not optional in practice:</b> an unowned
    /// dialog can open behind the game, and while it is up the game's thread is blocked,
    /// so Windows paints the "not responding" ghost over it — pressing O looks exactly
    /// like a hang.</param>
    /// <param name="what">What the files are, for the filter: "Sound banks".</param>
    public static string Open(IntPtr owner, string title, string what, string extension,
                              string directory, out string failure)
    {
        string picked = null;
        failure = null;

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return Ask(owner, title, what, extension, directory, out failure);

        string threadFailure = null;
        var thread = new Thread(() => picked = Ask(IntPtr.Zero, title, what, extension, directory, out threadFailure));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure = threadFailure;
        return picked;
    }

    /// <summary>Shows the dialog. A zero <paramref name="owner"/> means unowned.</summary>
    private static string Ask(IntPtr owner, string title, string what, string extension,
                              string directory, out string failure)
    {
        failure = null;

        try
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = title,
                Filter = $"{what} (*{extension})|*{extension}|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(directory) ? directory : AppContext.BaseDirectory,
            };

            var result = owner == IntPtr.Zero
                ? dialog.ShowDialog()
                : dialog.ShowDialog(new Owner(owner));

            return result == System.Windows.Forms.DialogResult.OK ? dialog.FileName : null;
        }
        catch (Exception e)
        {
            // Never a crash: every file found on disk is still reachable without the
            // dialog, so a missing one is a lost convenience rather than a lost feature.
            // But the reason goes back to the caller to be shown.
            failure = e.Message;
            return null;
        }
    }
}
