using System;
using System.Collections.Generic;
using Godot;

namespace SandlotSlugfest.UI;

/// <summary>
/// Screens register their clickable rectangles here while drawing, then ask the map to resolve
/// a click or a hover. Keeps every screen mouse-operable without each one hand-rolling its own
/// hit testing, and keeps the hit boxes automatically in step with what was actually drawn.
/// </summary>
public sealed class ClickMap
{
    private readonly List<(Rect2 Rect, Action Click, Action Hover)> _items = new();

    /// <summary>Call at the top of _Draw, before registering this frame's regions.</summary>
    public void Begin() => _items.Clear();

    public void Add(Rect2 rect, Action onClick, Action onHover = null) =>
        _items.Add((rect, onClick, onHover));

    /// <summary>Runs the action under the point. Later entries win, matching draw order.</summary>
    public bool Click(Vector2 point)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (!_items[i].Rect.HasPoint(point)) continue;
            _items[i].Click?.Invoke();
            return true;
        }
        return false;
    }

    public bool Hover(Vector2 point)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (!_items[i].Rect.HasPoint(point)) continue;
            if (_items[i].Hover == null) return false;
            _items[i].Hover.Invoke();
            return true;
        }
        return false;
    }
}
