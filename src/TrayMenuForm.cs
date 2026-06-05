using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Dashboard;

public sealed class TrayMenuForm : Form
{
    private const int WsExLayered = 0x00080000;
    private const int UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int ShadowPadding = 14;
    private const int CornerRadius = 15;
    private const int MenuWidth = 220;
    private const int ContentPadding = 6;
    private const int ItemHeight = 38;
    private const int SeparatorHeight = 11;

    private readonly List<TrayMenuItem> _items;
    private readonly Font _menuFont;
    private int _hoverIndex = -1;

    public TrayMenuForm(IEnumerable<TrayMenuItem> items)
    {
        _items = items.ToList();
        _menuFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold, GraphicsUnit.Point);

        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        Font = _menuFont;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        var height = ShadowPadding * 2
            + ContentPadding * 2
            + _items.Sum(item => item.IsSeparator ? SeparatorHeight : ItemHeight);
        Size = new Size(MenuWidth + ShadowPadding * 2, height);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExLayered;
            return cp;
        }
    }

    public void ShowNear(Point point)
    {
        var screen = Screen.FromPoint(point).WorkingArea;
        var x = Math.Min(point.X, screen.Right - Width - 8);
        var y = Math.Min(point.Y, screen.Bottom - Height - 8);
        x = Math.Max(screen.Left + 8, x);
        y = Math.Max(screen.Top + 8, y);
        Location = new Point(x, y);
        _ = Handle;
        RenderLayeredMenu();
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
        }

        base.Dispose(disposing);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsHandleCreated)
        {
            RenderLayeredMenu();
        }
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
        RenderLayeredMenu();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        RenderLayeredMenu();
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
        if (DesignMode)
        {
            base.OnPaint(e);
        }
    }

    private void RenderLayeredMenu()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
        {
            return;
        }

        using var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var menuBounds = new Rectangle(ShadowPadding, ShadowPadding, MenuWidth, Height - ShadowPadding * 2);
        DrawShadow(g, menuBounds);

        using (var background = new SolidBrush(Color.White))
        using (var path = RoundedRect(menuBounds, CornerRadius))
        {
            g.FillPath(background, path);
        }

        using (var borderPen = new Pen(Color.FromArgb(236, 238, 241)))
        using (var borderPath = RoundedRect(Rectangle.Inflate(menuBounds, -1, -1), CornerRadius - 1))
        {
            g.DrawPath(borderPen, borderPath);
        }

        var y = ShadowPadding + ContentPadding;
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.IsSeparator)
            {
                DrawSeparator(g, y);
                y += SeparatorHeight;
                continue;
            }

            DrawItem(g, item, i, new Rectangle(
                ShadowPadding + ContentPadding,
                y,
                MenuWidth - ContentPadding * 2,
                ItemHeight));
            y += ItemHeight;
        }

        ApplyLayeredBitmap(bitmap);
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

        var textColor = item.Enabled ? Color.FromArgb(39, 39, 42) : Color.FromArgb(161, 161, 170);
        var textRect = new Rectangle(bounds.Left + 20, bounds.Top, bounds.Width - 40, bounds.Height);
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
        g.DrawLine(
            pen,
            ShadowPadding,
            y + SeparatorHeight / 2,
            ShadowPadding + MenuWidth,
            y + SeparatorHeight / 2);
    }

    private int HitTest(Point point)
    {
        var contentLeft = ShadowPadding + ContentPadding;
        var contentRight = ShadowPadding + MenuWidth - ContentPadding;
        if (point.X < contentLeft || point.X >= contentRight)
        {
            return -1;
        }

        var y = ShadowPadding + ContentPadding;
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

    private static void DrawShadow(Graphics g, Rectangle menuBounds)
    {
        var shadowBounds = menuBounds;
        shadowBounds.Offset(0, 2);

        var shadowLayers = new[]
        {
            (spread: 12, alpha: 2),
            (spread: 9, alpha: 4),
            (spread: 6, alpha: 5),
            (spread: 3, alpha: 5)
        };

        foreach (var layer in shadowLayers)
        {
            var bounds = Rectangle.Inflate(shadowBounds, layer.spread, layer.spread);
            using var brush = new SolidBrush(Color.FromArgb(layer.alpha, 24, 24, 27));
            using var path = RoundedRect(bounds, CornerRadius + layer.spread);
            g.FillPath(brush, path);
        }
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

    private void ApplyLayeredBitmap(Bitmap bitmap)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = SelectObject(memoryDc, bitmapHandle);

        try
        {
            var topLeft = new NativePoint(Left, Top);
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var source = new NativePoint(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };

            UpdateLayeredWindow(Handle, screenDc, ref topLeft, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            SelectObject(memoryDc, oldBitmap);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref NativePoint pptDst,
        ref NativeSize psize,
        IntPtr hdcSrc,
        ref NativePoint pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public NativeSize(int cx, int cy)
        {
            Cx = cx;
            Cy = cy;
        }

        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}

public sealed record TrayMenuItem(string Text, Action? Action = null, bool Enabled = true, bool IsSeparator = false)
{
    public static TrayMenuItem Separator() => new(string.Empty, IsSeparator: true);
}
