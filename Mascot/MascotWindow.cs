using System.Runtime.InteropServices;

namespace HelloWorldBot.Mascot;

/// <summary>
/// The window the robot is drawn in: borderless, always on top and a per-pixel-alpha "layered" window sized
/// to just the robot. Fully transparent pixels let mouse clicks fall through to whatever is underneath, so
/// only the robot itself can be clicked. It is shown without being activated and never takes focus.
/// </summary>
internal sealed class MascotWindow : Form
{
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int MA_NOACTIVATE = 3;
    private const int DragThresholdPixels = 4;
    private bool _pressed;
    private bool _dragging;
    private Point _pressedAt;
    private const int ULW_ALPHA = 2;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    public MascotWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Size = new Size(16, 16);
    }

    /// <summary>Raised when the character is clicked (pressed and released without moving).</summary>
    public event EventHandler? Clicked;

    /// <summary>Raised when the character is double-clicked.</summary>
    public event EventHandler? DoubleClicked;

    /// <summary>Raised once the mouse has moved far enough with the button down: the character is picked up. Screen position.</summary>
    public event Action<Point>? DragStarted;

    /// <summary>Raised while the picked-up character is moved. Screen position of the mouse.</summary>
    public event Action<Point>? DragMoved;

    /// <summary>Raised when the mouse button is released after a drag. Screen position of the mouse.</summary>
    public event Action<Point>? DragEnded;

    /// <summary>Where the mouse pointer is, in screen pixels. Replaceable so tests can drive a drag without moving the real mouse.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<Point> CursorProvider { get; set; } = () => Cursor.Position;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return parameters;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = MA_NOACTIVATE; // clicking the robot must never pull focus away from the user's window
            return;
        }

        base.WndProc(ref m);
        switch (m.Msg)
        {
            case WM_LBUTTONDOWN:
                _pressed = true;
                _dragging = false;
                _pressedAt = CursorProvider();
                Capture = true; // keep getting mouse messages while the pointer leaves the small window
                break;
            case WM_MOUSEMOVE when _pressed:
                var position = CursorProvider();
                if (!_dragging && Math.Abs(position.X - _pressedAt.X) + Math.Abs(position.Y - _pressedAt.Y) >= DragThresholdPixels)
                {
                    _dragging = true;
                    DragStarted?.Invoke(position);
                }

                if (_dragging) DragMoved?.Invoke(position);
                break;
            case WM_LBUTTONUP when _pressed:
                var wasDragging = _dragging;
                _pressed = false;
                _dragging = false;
                Capture = false;
                if (wasDragging) DragEnded?.Invoke(CursorProvider());
                else Clicked?.Invoke(this, EventArgs.Empty);
                break;
            case WM_LBUTTONDBLCLK:
                DoubleClicked?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>Shows <paramref name="frame"/> (premultiplied ARGB) with its top-left corner at <paramref name="topLeft"/>.</summary>
    public void SetFrame(Bitmap frame, Point topLeft)
    {
        if (!IsHandleCreated) CreateHandle();

        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = frame.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = SelectObject(memoryDc, bitmapHandle);
        try
        {
            var size = new SIZE { cx = frame.Width, cy = frame.Height };
            var source = new POINT();
            var destination = new POINT { x = topLeft.X, y = topLeft.Y };
            var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memoryDc, oldBitmap);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Lifts the window back above other always-on-top windows without activating it.</summary>
    public void KeepOnTop()
    {
        if (IsHandleCreated && Visible) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
