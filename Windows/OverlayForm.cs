using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PrivacyScreen.Windows;

/// 물리 픽셀 좌표로 배치되는 클릭 통과 오버레이.
/// UpdateLayeredWindow(per-pixel 알파)로 진짜 반투명 렌더링을 한다.
/// 그리는 색은 항상 검정(RGB 0)이라 프리멀티플라이드/스트레이트 알파가 동일 → 별도 변환 불필요.
internal sealed class OverlayForm : Form
{
    private const int GwlExStyle = -20;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int UlwAlpha = 0x02;

    private readonly Rectangle _bounds; // 물리 픽셀
    private readonly List<Rectangle> _clipRects = [];
    private readonly List<Rectangle> _fillRects = [];
    private float _alpha;
    private bool _frontalFocusEnabled;
    private float _frontalFocusWidth;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
        IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

    public OverlayForm(Rectangle bounds, float alpha, bool frontalFocusEnabled, float frontalFocusWidth)
    {
        _bounds = bounds;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        _alpha = Math.Clamp(alpha, 0.3f, 1.0f);
        _frontalFocusEnabled = frontalFocusEnabled;
        _frontalFocusWidth = Math.Clamp(frontalFocusWidth, 0.01f, 1.0f);
    }

    public Rectangle PhysicalBounds => _bounds;

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
        Win32.PositionTopMost(Handle, _bounds); // 물리 픽셀로 직접 배치
        Render();
    }

    // 레이어드 창은 UpdateLayeredWindow 로만 그린다(WM_PAINT 배경 지우기 무시).
    protected override void OnPaintBackground(PaintEventArgs e) { }

    public void SetAppearance(float alpha, bool frontalFocusEnabled, float frontalFocusWidth)
    {
        _alpha = Math.Clamp(alpha, 0.3f, 1.0f);
        _frontalFocusEnabled = frontalFocusEnabled;
        _frontalFocusWidth = Math.Clamp(frontalFocusWidth, 0.01f, 1.0f);
        Render();
    }

    public void SetGeometry(IReadOnlyList<Rectangle> clip, IReadOnlyList<Rectangle> fill)
    {
        // 변화가 없으면 다시 그리지 않는다(불필요한 전체화면 비트맵 생성 방지).
        if (SameRects(_clipRects, clip) && SameRects(_fillRects, fill))
        {
            return;
        }

        _clipRects.Clear();
        _clipRects.AddRange(clip);
        _fillRects.Clear();
        _fillRects.AddRange(fill);
        Render();
    }

    private static bool SameRects(List<Rectangle> a, IReadOnlyList<Rectangle> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private void Render()
    {
        if (!IsHandleCreated || _bounds.Width <= 0 || _bounds.Height <= 0)
        {
            return;
        }

        using var bitmap = new Bitmap(_bounds.Width, _bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CompositingMode = CompositingMode.SourceOver;
            g.Clear(Color.Transparent);
            DrawContent(g);
        }

        PushLayered(bitmap);
    }

    private void DrawContent(Graphics g)
    {
        if (_clipRects.Count == 0 || _fillRects.Count == 0)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var clipPath = new GraphicsPath();
        foreach (var clipRect in _clipRects)
        {
            clipPath.AddRectangle(clipRect);
        }

        g.SetClip(clipPath);

        using var fillPath = new GraphicsPath();
        var fillBounds = Rectangle.Empty;
        var hasBounds = false;
        foreach (var fillRect in _fillRects)
        {
            using var rounded = CreateRoundedRectPath(fillRect, 10f);
            fillPath.AddPath(rounded, false);
            fillBounds = hasBounds ? Rectangle.Union(fillBounds, fillRect) : fillRect;
            hasBounds = true;
        }

        // 기본 덮개(어둡기 슬라이더 값)를 항상 칠하고, 세로선 필터가 켜져 있으면 그 위에 슬랫을 얹는다.
        using (var baseBrush = new SolidBrush(Color.FromArgb((int)(_alpha * 255), 0, 0, 0)))
        {
            g.FillPath(baseBrush, fillPath);
        }

        if (_frontalFocusEnabled && hasBounds)
        {
            DrawVerticalSlats(g, fillPath, fillBounds);
        }
    }

    private void DrawVerticalSlats(Graphics g, GraphicsPath path, Rectangle bounds)
    {
        var pitch = Math.Max(2.0f, 1.0f + (_frontalFocusWidth * 12.0f)); // 슬랫 간격(최소 2px)
        var state = g.Save();
        g.SetClip(path, CombineMode.Intersect);
        g.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(Color.FromArgb(255, 0, 0, 0), 1f); // 불투명 슬랫
        for (var x = (float)bounds.Left; x < bounds.Right; x += pitch)
        {
            g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
        }

        g.Restore(state);
    }

    private void PushLayered(Bitmap bitmap)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var hBitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;
        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memDc, hBitmap);

            var size = new Size(_bounds.Width, _bounds.Height);
            var source = new Point(0, 0);
            var dest = new Point(_bounds.Left, _bounds.Top);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };

            UpdateLayeredWindow(Handle, screenDc, ref dest, ref size, memDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            if (hBitmap != IntPtr.Zero)
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
            }

            DeleteDC(memDc);
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
