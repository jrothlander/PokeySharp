using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Demo1;

/// <summary>
/// The list down the left of both pages: categories, rows, and a selection you can move
/// with the keyboard or land on with the mouse.
///
/// <b>One component rather than one per page.</b> The sound browser and the voice demo
/// want exactly the same behaviour — arrows step, left and right jump a category, a click
/// selects and plays — and the fiddly part is not the drawing but keeping hit-testing and
/// rendering agreed about which row is where. Doing that twice is how a list ends up
/// playing the row above the one you clicked.
/// </summary>
public sealed class ListView
{
    public readonly record struct Row(string Category, string Label, string Right);

    private readonly record struct Line(string Text, string Right, int Item, float Y);

    private readonly IReadOnlyList<Row> _rows;
    private readonly List<Line> _lines = new();
    private readonly float _x, _width;

    public int Selected { get; private set; }
    public int Count => _rows.Count;

    private const float RowHeight = 21f, CategoryGap = 7f, RowSize = 14f;

    /// <summary>
    /// Works out where every line sits, once.
    ///
    /// Laying the list out at construction rather than while drawing is what lets a mouse
    /// click be resolved to a row: hit-testing and rendering read the same coordinates, so
    /// they cannot disagree.
    /// </summary>
    public ListView(IReadOnlyList<Row> rows, float x, float width, float top)
    {
        _rows = rows;
        _x = x;
        _width = width;

        float y = top;
        string category = null;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Category != category)
            {
                if (category != null) y += CategoryGap;
                _lines.Add(new Line(rows[i].Category, null, -1, y));
                y += RowHeight;
                category = rows[i].Category;
            }

            _lines.Add(new Line(rows[i].Label, rows[i].Right, i, y));
            y += RowHeight;
        }
    }

    /// <summary>Steps the selection. Returns whether it actually moved, so a caller can
    /// make a noise only when something happened.</summary>
    public bool Move(int delta)
    {
        int was = Selected;
        Selected = Math.Clamp(Selected + delta, 0, _rows.Count - 1);
        return Selected != was;
    }

    /// <summary>
    /// Jumps to the first row of the neighbouring category.
    ///
    /// Going back lands on the top of the current category first and only crosses into the
    /// previous one if already there — the way a list of sections usually behaves, and the
    /// difference between one press and eight when you overshoot.
    /// </summary>
    public bool MoveCategory(int delta)
    {
        string here = _rows[Selected].Category;

        if (delta > 0)
        {
            for (int i = Selected + 1; i < _rows.Count; i++)
                if (_rows[i].Category != here) { Selected = i; return true; }
            return false;
        }

        int start = Selected;
        while (start > 0 && _rows[start - 1].Category == here) start--;
        if (start < Selected) { Selected = start; return true; }

        for (int i = start - 1; i >= 0; i--)
        {
            if (_rows[i].Category == here) continue;

            string previous = _rows[i].Category;
            while (i > 0 && _rows[i - 1].Category == previous) i--;
            Selected = i;
            return true;
        }

        return false;
    }

    /// <summary>The row under a point, or -1. Category headings are not rows.</summary>
    public int HitTest(int x, int y)
    {
        if (x < _x || x > _x + _width) return -1;

        foreach (var line in _lines)
        {
            if (line.Item < 0) continue;
            if (y >= line.Y - 3 && y < line.Y + RowHeight - 3) return line.Item;
        }

        return -1;
    }

    public void Select(int item) => Selected = Math.Clamp(item, 0, _rows.Count - 1);

    /// <summary><paramref name="live"/> is the row currently making a noise, or -1.</summary>
    public void Draw(Screen screen, int live)
    {
        foreach (var line in _lines)
        {
            if (line.Item < 0)
            {
                screen.Text(line.Text, _x, line.Y, RowSize, Screen.Heading);
                continue;
            }

            bool chosen = line.Item == Selected;
            bool sounding = line.Item == live;
            Color colour = sounding ? Screen.Live : chosen ? Screen.Chosen : Screen.Body;

            // A marker rather than a filled bar behind the row: the font is strokes on
            // black, and a solid highlight behind thin lines makes them harder to read
            // rather than easier.
            if (chosen) screen.Text(">", _x, line.Y, RowSize, Screen.Heading);

            screen.Text(line.Text, _x + 26f, line.Y, RowSize, colour);

            if (line.Right != null)
                screen.TextRight(line.Right, _x + _width, line.Y, RowSize,
                                 chosen || sounding ? colour : Screen.Dim);
        }
    }
}
