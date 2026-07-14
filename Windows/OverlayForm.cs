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
    private bool _frontalFocusEnabled;
    private float _frontalFocusWidth;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public OverlayForm(Rectangle bounds, float alpha, bool frontalFocusEnabled, float frontalFocusWidth)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
        _alpha = Math.Clamp(alpha, 0.3f, 1.0f);
        _frontalFocusEnabled = frontalFocusEnabled;
        _frontalFocusWidth = Math.Clamp(frontalFocusWidth, 0.25f, 1.0f);
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

    public void SetAppearance(float alpha, bool frontalFocusEnabled, float frontalFocusWidth)
    {
        _alpha = Math.Clamp(alpha, 0.3f, 1.0f);
        _frontalFocusEnabled = frontalFocusEnabled;
        _frontalFocusWidth = Math.Clamp(frontalFocusWidth, 0.25f, 1.0f);
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
        foreach (var fillRect in _fillRects)
        {
            using var path = CreateRoundedRectPath(fillRect, 10f);
            if (_frontalFocusEnabled)
            {
                FillVerticalLouver(e.Graphics, path, fillRect);
            }
            else
            {
                using var brush = new SolidBrush(Color.FromArgb((int)(_alpha * 255), 0, 0, 0));
                e.Graphics.FillPath(brush, path);
            }
        }
    }

    private void FillVerticalLouver(Graphics graphics, GraphicsPath path, Rectangle fillRect)
    {
        var baseFillAlpha = Math.Max(0.06f, _alpha * 0.22f);
        var lineAlpha = Math.Min(1.0f, _alpha + 0.12f);
        var stepPixels = Math.Max(1, (int)Math.Round(1.0f + (_frontalFocusWidth * 7.0f)));
        var pitch = (float)stepPixels;
        const float lineWidth = 1.0f;
        using var baseBrush = new SolidBrush(Color.FromArgb((int)(baseFillAlpha * 255), 0, 0, 0));
        using var brush = new SolidBrush(Color.FromArgb((int)(lineAlpha * 255), 0, 0, 0));
        var state = graphics.Save();
        graphics.SetClip(path, CombineMode.Intersect);
        graphics.FillPath(baseBrush, path);
        var oldSmoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.None;
        for (var x = (float)fillRect.Left; x < fillRect.Right; x += pitch)
        {
            graphics.FillRectangle(brush, (float)Math.Floor(x), fillRect.Top, lineWidth, fillRect.Height);
        }

        graphics.SmoothingMode = oldSmoothing;
        graphics.Restore(state);
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
