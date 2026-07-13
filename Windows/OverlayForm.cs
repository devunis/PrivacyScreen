using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PrivacyScreen.Windows;

internal sealed class OverlayForm : Form
{
    private const int GwlExStyle = -20;
    private readonly List<Rectangle> _clipRects = [];
    private readonly List<Rectangle> _fillRects = [];
    private float _alpha;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public OverlayForm(Rectangle bounds, float alpha)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
        _alpha = alpha;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle = Win32.AddClickThroughStyles(cp.ExStyle);
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var exStyle = GetWindowLong(Handle, GwlExStyle);
        SetWindowLong(Handle, GwlExStyle, Win32.AddClickThroughStyles(exStyle));
    }

    public void SetAlpha(float value)
    {
        _alpha = Math.Clamp(value, 0.3f, 1.0f);
        Invalidate();
    }

    public void SetGeometry(IEnumerable<Rectangle> clip, IEnumerable<Rectangle> fill)
    {
        _clipRects.Clear();
        _fillRects.Clear();
        _clipRects.AddRange(clip);
        _fillRects.AddRange(fill);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_clipRects.Count == 0 || _fillRects.Count == 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var clipPath = new GraphicsPath();
        foreach (var clipRect in _clipRects)
        {
            clipPath.AddRectangle(clipRect);
        }

        e.Graphics.SetClip(clipPath);
        using var brush = new SolidBrush(Color.FromArgb((int)(_alpha * 255), 0, 0, 0));
        foreach (var fillRect in _fillRects)
        {
            using var path = CreateRoundedRectPath(fillRect, 10f);
            e.Graphics.FillPath(brush, path);
        }
    }

    private static GraphicsPath CreateRoundedRectPath(Rectangle rect, float radius)
    {
        var diameter = radius * 2f;
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return path;
        }

        if (diameter > rect.Width)
        {
            diameter = rect.Width;
        }

        if (diameter > rect.Height)
        {
            diameter = rect.Height;
        }

        var arc = new RectangleF(rect.X, rect.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
