using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Dashboard;

public sealed class TrayMenuForm : Form
{
    private const int CsDropShadow = 0x00020000;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;
    private const int CornerRadius = 14;
    private const int MenuWidth = 190;
    private const int OuterPadding = 5;
    private const int TextHorizontalPadding = 14;
    private const int ItemHeight = 38;
    private const int SeparatorHeight = 11;

    private readonly List<TrayMenuItem> _items;
    private readonly Font _menuFont;
    private int _hoverIndex = -1;
    private Bitmap? _menuBuffer;
    private bool _bufferDirty = true;

    public TrayMenuForm(IEnumerable<TrayMenuItem> items)
    {
        _items = items.ToList();
        _menuFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.White;
        DoubleBuffered = true;
        Font = _menuFont;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        var height = OuterPadding * 2 + _items.Sum(item => item.IsSeparator ? SeparatorHeight : ItemHeight);
        Size = new Size(MenuWidth, height);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CsDropShadow;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryEnableDwmCorners();
    }

    public void ShowNear(Point point)
    {
        var screen = Screen.FromPoint(point).WorkingArea;
        var x = Math.Min(point.X, screen.Right - Width - 8);
        var y = Math.Min(point.Y, screen.Bottom - Height - 8);
        x = Math.Max(screen.Left + 8, x);
        y = Math.Max(screen.Top + 8, y);
        Location = new Point(x, y);
        Show();
        Activate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menuFont.Dispose();
            _menuBuffer?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        using var region = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, CornerRadius, CornerRadius);
        Region = Region.FromHrgn(region.Handle);
        _bufferDirty = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var nextHover = HitTest(e.Location);
        if (nextHover == _hoverIndex)
        {
            return;
        }

        _hoverIndex = nextHover;
        _bufferDirty = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            _bufferDirty = true;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var index = HitTest(e.Location);
        if (index < 0 || index >= _items.Count || !_items[index].Enabled || _items[index].IsSeparator)
        {
            return;
        }

        var action = _items[index].Action;
        Close();
        action?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // 使用双缓冲优化渲染
        if (_menuBuffer == null || _bufferDirty)
        {
            _menuBuffer?.Dispose();
            _menuBuffer = new Bitmap(Width, Height);

            using var g = Graphics.FromImage(_menuBuffer);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            RenderMenu(g);
            _bufferDirty = false;
        }

        e.Graphics.DrawImageUnscaled(_menuBuffer, 0, 0);
    }

    private void RenderMenu(Graphics g)
    {
        using (var background = new SolidBrush(Color.White))
        {
            g.FillRectangle(background, 0, 0, Width, Height);
        }

        var y = OuterPadding;
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.IsSeparator)
            {
                DrawSeparator(g, y);
                y += SeparatorHeight;
                continue;
            }

            DrawItem(g, item, i, new Rectangle(OuterPadding, y, Width - OuterPadding * 2, ItemHeight));
            y += ItemHeight;
        }
    }

    private void DrawItem(Graphics g, TrayMenuItem item, int index, Rectangle bounds)
    {
        var itemBackColor = Color.White;
        if (index == _hoverIndex && item.Enabled)
        {
            itemBackColor = Color.FromArgb(245, 246, 248);
            using var hoverBrush = new SolidBrush(itemBackColor);
            using var path = RoundedRect(bounds, 9);
            g.FillPath(hoverBrush, path);
        }

        var textColor = item.Enabled ? Color.FromArgb(24, 24, 27) : Color.FromArgb(161, 161, 170);
        var textRect = new Rectangle(
            bounds.Left + TextHorizontalPadding,
            bounds.Top,
            bounds.Width - TextHorizontalPadding * 2,
            bounds.Height);
        TextRenderer.DrawText(
            g,
            item.Text,
            Font,
            textRect,
            textColor,
            itemBackColor,
            TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix
                | TextFormatFlags.PreserveGraphicsClipping);
    }

    private void DrawSeparator(Graphics g, int y)
    {
        using var pen = new Pen(Color.FromArgb(229, 232, 236));
        g.DrawLine(pen, 0, y + SeparatorHeight / 2, Width, y + SeparatorHeight / 2);
    }

    private int HitTest(Point point)
    {
        if (point.X < OuterPadding || point.X >= Width - OuterPadding)
        {
            return -1;
        }

        var y = OuterPadding;
        for (var i = 0; i < _items.Count; i++)
        {
            var height = _items[i].IsSeparator ? SeparatorHeight : ItemHeight;
            if (point.Y >= y && point.Y < y + height)
            {
                return _items[i].IsSeparator ? -1 : i;
            }

            y += height;
        }

        return -1;
    }

    private void TryEnableDwmCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var preference = DwmCornerRound;
        _ = DwmSetWindowAttribute(Handle, DwmWindowCornerPreference, ref preference, sizeof(int));
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private sealed class SafeRegionHandle : IDisposable
    {
        public SafeRegionHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                DeleteObject(Handle);
            }
        }
    }

    private static SafeRegionHandle CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height)
    {
        return new SafeRegionHandle(CreateRoundRectRgnNative(left, top, right, bottom, width, height));
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgnNative(int left, int top, int right, int bottom, int width, int height);
}

public sealed record TrayMenuItem(string Text, Action? Action = null, bool Enabled = true, bool IsSeparator = false)
{
    public static TrayMenuItem Separator() => new(string.Empty, IsSeparator: true);
}
