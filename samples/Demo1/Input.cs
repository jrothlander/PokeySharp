using Microsoft.Xna.Framework.Input;

namespace Demo1;

/// <summary>
/// A frame of input, already edge-detected.
///
/// <b>The edge is the whole point.</b> <c>IsKeyDown</c> is true on every frame a key is
/// held, so a page triggering a sound off it directly fires sixty times a second — sixty
/// overlapping copies, which is both an unpleasant stutter and a fast way to exhaust the
/// voices MonoGame will play at once. A sound effect wants the transition, not the state,
/// and doing that once here means no page can forget to.
/// </summary>
public sealed class Input
{
    private KeyboardState _keys, _previousKeys;
    private MouseState _mouse, _previousMouse;

    public void Update()
    {
        _previousKeys = _keys;
        _previousMouse = _mouse;
        _keys = Keyboard.GetState();
        _mouse = Mouse.GetState();
    }

    /// <summary>True only on the frame a key goes down.</summary>
    public bool Pressed(Keys key) => _keys.IsKeyDown(key) && _previousKeys.IsKeyUp(key);

    /// <summary>True only on the frame the left button goes down.</summary>
    public bool Clicked => _mouse.LeftButton == ButtonState.Pressed
                           && _previousMouse.LeftButton == ButtonState.Released;

    public int MouseX => _mouse.X;
    public int MouseY => _mouse.Y;
}
