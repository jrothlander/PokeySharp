using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Demo1;

/// <summary>
/// Three demonstrations of making noise from nothing but numbers, and a tab bar between
/// them: <see cref="SoundPage"/> for the arcade chip, <see cref="VoicePage"/> for speech,
/// and <see cref="SongPage"/> for music the way an Atari 800 played it.
///
/// <b>Nothing here loads a file.</b> Not a sound, not a font, not a word of the speech,
/// not a note of the song. Every effect is a handful of integers rendered through an
/// emulated POKEY, every letter on screen is line segments, and every spoken phrase is
/// synthesised from its spelling. That is the whole argument of the demo, and it is why
/// the Content directory is empty.
/// </summary>
public class Game1 : Game
{
    private const float TabX = 40f, TabGap = 28f;

    /// <summary>A number key per page, in tab order.</summary>
    private static readonly Keys[] PageKeys = { Keys.D1, Keys.D2, Keys.D3 };

    private readonly GraphicsDeviceManager _graphics;
    private readonly Input _input = new();

    private Screen _screen;
    private Sounds _sounds;
    private IPage[] _pages;
    private int _page;

    // Where each tab ended up, so a click can be resolved to one. Worked out while
    // drawing, which is safe because a click is handled on the frame after the draw that
    // positioned them, and the bar never moves. Sized once the pages exist.
    private float[] _tabFrom;
    private float[] _tabTo;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Screen.Width,
            PreferredBackBufferHeight = Screen.Height,

            // Every glyph is diagonal strokes, and a stroke is a rotated quad. Without
            // multisampling their edges are staircases, which at this text size reads as
            // grubbiness rather than as jaggedness.
            PreferMultiSampling = true,
        };
        _graphics.PreparingDeviceSettings += (_, e) =>
            e.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = 8;

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "POKEY sound library";
    }

    protected override void LoadContent()
    {
        _screen = new Screen(GraphicsDevice);

        // Built here rather than in the constructor: SoundEffect needs an audio device, and
        // by LoadContent there is one.
        //
        // This is the interface's own bank -- one click -- and it is deliberately not the
        // set the sounds page browses. That one changes whenever a file is opened, and a
        // shell whose menu goes silent because of what you loaded would be a strange thing
        // to debug.
        _sounds = new Sounds(SoundLibrary.Ui);
        _pages = new IPage[]
        {
            new SoundPage(_sounds, Window.Handle),
            new VoicePage(_sounds),
            new SongPage(_sounds, Window.Handle),
        };
        _tabFrom = new float[_pages.Length];
        _tabTo = new float[_pages.Length];
    }

    protected override void Update(GameTime gameTime)
    {
        _input.Update();

        if (_input.Pressed(Keys.Escape)
            || GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
        {
            Exit();
        }

        // A number per page, and the tab labels are clickable. Both, because a demo is as
        // likely to be driven by someone reaching for the mouse as by someone who has read
        // the key hints.
        //
        // <b>Not Tab, which is what this used to be.</b> MonoGame's WindowsDX window is a
        // WinForms form, and Tab is a dialog navigation key there — it never reaches
        // Keyboard.GetState, so the shortcut silently did nothing. Numbering the tabs also
        // reads better with more than two pages, which Tab never would have.
        for (int i = 0; i < _pages.Length && i < PageKeys.Length; i++)
            if (_input.Pressed(PageKeys[i])) Show(i);
        if (_input.Clicked && _input.MouseY < 60f) ClickTab(_input.MouseX);

        _pages[_page].Update(_input, gameTime.ElapsedGameTime.TotalSeconds);

        // The file dialog blocks the loop for as long as it is open, so the next frame
        // arrives with an elapsed time of however long that was. Left alone, MonoGame reads
        // that as the game running slowly and calls Update repeatedly to catch up -- which
        // for a page that plays a sound on a keypress means a burst of them. Resetting the
        // clock says the gap was not the game's fault.
        if (_pages[_page].ClearBlocked()) ResetElapsedTime();

        base.Update(gameTime);
    }

    private void ClickTab(int x)
    {
        for (int i = 0; i < _pages.Length; i++)
            if (x >= _tabFrom[i] && x <= _tabTo[i]) { Show(i); return; }
    }

    /// <summary>Switches page, telling the old one it has been left — the song page pauses
    /// on that, because nothing feeds its stream while another page is showing.</summary>
    private void Show(int page)
    {
        if (page == _page) return;
        _pages[_page].Leave();
        _page = page;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Screen.Background);
        _screen.Begin();

        DrawTabs();
        _screen.Text(_pages[_page].Hint + "    ESC QUIT", TabX, 74f, 12f, Screen.Body);
        _screen.Rule(TabX, 100f, Screen.Width - 2f * TabX);

        _pages[_page].Draw(_screen);

        _screen.End();
        base.Draw(gameTime);
    }

    /// <summary>The tab bar doubles as the title, so the page you are on is named once
    /// rather than twice.</summary>
    private void DrawTabs()
    {
        float x = TabX;

        for (int i = 0; i < _pages.Length; i++)
        {
            // The number is part of the label rather than a separate hint, so the shortcut
            // is visible at the thing it acts on instead of in a line of key hints nobody
            // reads twice.
            string label = $"{i + 1} {_pages[i].Name}";
            float width = _screen.Measure(label, 24f);

            _screen.Text(label, x, 34f, 24f, i == _page ? Screen.Heading : Screen.Dim);

            // The current tab is underlined rather than boxed. A box on a stroke font
            // competes with the letters it is meant to be framing.
            if (i == _page) _screen.Rule(x, 58f, width, Screen.Heading);

            _tabFrom[i] = x;
            _tabTo[i] = x + width;
            x += width + TabGap;
        }
    }

    /// <summary>Where MonoGame expects load-time resources to be released. The sound
    /// buffers and the beam's texel are unmanaged memory and nothing else will free
    /// them.</summary>
    protected override void UnloadContent()
    {
        if (_pages != null)
            foreach (var page in _pages)
                (page as System.IDisposable)?.Dispose();

        _sounds?.Dispose();
        _screen?.Dispose();
        base.UnloadContent();
    }
}
