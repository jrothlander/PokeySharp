namespace Demo1;

/// <summary>One screen of the demo. <see cref="Game1"/> owns which one is showing and
/// draws the tab bar; everything below the bar is the page's own.</summary>
public interface IPage
{
    /// <summary>The tab label.</summary>
    string Name { get; }

    /// <summary>This page's key hints, shown under the tabs. Quit is Game1's, not a
    /// page's, so it is not listed here.</summary>
    string Hint { get; }

    void Update(Input input, double elapsedSeconds);

    void Draw(Screen screen);

    /// <summary>Called when another page is chosen. Only a page that keeps something
    /// running needs it — the song page stops feeding its stream, so it pauses rather than
    /// starving.</summary>
    void Leave() { }

    /// <summary>True, once, if the game loop was blocked since the last call — by a file
    /// dialog, which stops the loop for as long as it is open. <see cref="Game1"/> resets
    /// its clock on it, or MonoGame reads the gap as the game running slowly and runs
    /// Update several times to catch up. Only a page that opens files needs it.</summary>
    bool ClearBlocked() => false;
}
