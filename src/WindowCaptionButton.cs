using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace MihomoDashboard;

public enum WindowCaptionButtonKind
{
    Minimize,
    Maximize,
    Restore,
    Close
}

public sealed class WindowCaptionButton : Control
{
    private bool _hovered;
    private bool _pressed;
    private WindowCaptionButtonKind _kind;

    public WindowCaptionButton(WindowCaptionButtonKind kind)
    {
        _kind = kind;
        DoubleBuffered = true;
        Margin = Padding.Empty;
        TabStop = false;
        Width = 46;
        Cursor = Cursors.Default;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public WindowCaptionButtonKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value)
            {
                return;
            }

            _kind = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(GetBackground());

        using var pen = new Pen(GetGlyphColor(), 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        var cx = Width / 2f;
        var cy = Height / 2f;

        switch (Kind)
        {
            case WindowCaptionButtonKind.Minimize:
                g.DrawLine(pen, cx - 5, cy + 3, cx + 5, cy + 3);
                break;
            case WindowCaptionButtonKind.Maximize:
                g.DrawRectangle(pen, cx - 5, cy - 5, 10, 10);
                break;
            case WindowCaptionButtonKind.Restore:
                g.DrawRectangle(pen, cx - 3, cy - 6, 9, 9);
                g.DrawRectangle(pen, cx - 6, cy - 3, 9, 9);
                break;
            case WindowCaptionButtonKind.Close:
                g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
                break;
        }
    }

    private Color GetBackground()
    {
        if (!_hovered)
        {
            return Color.FromArgb(244, 244, 245);
        }

        if (Kind == WindowCaptionButtonKind.Close)
        {
            return _pressed ? Color.FromArgb(196, 43, 28) : Color.FromArgb(232, 17, 35);
        }

        return _pressed ? Color.FromArgb(214, 214, 218) : Color.FromArgb(229, 229, 232);
    }

    private Color GetGlyphColor()
    {
        return _hovered && Kind == WindowCaptionButtonKind.Close
            ? Color.White
            : Color.FromArgb(24, 24, 27);
    }
}
