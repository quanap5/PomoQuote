using System.Runtime.InteropServices;
using System.Text;
using PomoQuote.Widget;

namespace PomoQuote;

/// <summary>What the window that dialogs belong to says about the screen: the widget to keep clear of, and the screens' usable areas.</summary>
internal interface IDialogAnchor
{
    /// <summary>The widget's rectangle while it is on screen; null in the normal window and while the widget is hidden in the tray.</summary>
    Rectangle? WidgetOnScreen { get; }

    /// <summary>The usable area of every screen, the primary screen first.</summary>
    IReadOnlyList<Rectangle> DialogWorkAreas { get; }
}

/// <summary>
/// The one way the app opens a modal dialog or a message box, so that none can end up behind the always-on-top widget.
/// <para>Why it is needed: <see cref="Form.ShowDialog(IWin32Window)"/> gives a dialog its owner only after the dialog's window exists. Windows
/// then does not move it into the "always on top" group of its owner, so the widget stays above it and covers it. The dialog is therefore made
/// topmost itself whenever its owner is. (A message box gets its owner when it is created and needs only the position.)</para>
/// <para>It also places the window: beside the widget, inside the usable area of the widget's screen (see <see cref="DialogPlacement"/>),
/// again when a dialog grows, and a dialog larger than that area gets scroll bars. Nothing about the widget itself is changed.</para>
/// </summary>
internal static class DialogHost
{
    private sealed record Open(Form? Dialog, IDialogAnchor? Anchor)
    {
        public IntPtr Window { get; set; }
    }

    private static readonly List<Open> OpenWindows = []; // the app's one UI thread only; the last is the one in front

    /// <summary>The handle of the dialog or message box in front if it is an always-on-top one, else zero.</summary>
    public static IntPtr FrontTopMostWindow
    {
        get
        {
            for (var i = OpenWindows.Count - 1; i >= 0; i--)
            {
                var window = OpenWindows[i].Window;
                if (window != IntPtr.Zero && IsWindow(window)) return IsTopMost(window) ? window : IntPtr.Zero;
            }

            return IntPtr.Zero;
        }
    }

    /// <summary>Shows <paramref name="dialog"/> modally over <paramref name="owner"/>: the app's window or one of its open dialogs.</summary>
    public static DialogResult Show(Form dialog, Form owner)
    {
        var anchor = AnchorOf(owner);
        if (owner.TopMost) dialog.TopMost = true; // joins the owner's always-on-top group; as an owned window it then stays above it
        dialog.StartPosition = FormStartPosition.Manual;

        var entry = new Open(dialog, anchor);
        Point? placed = null;
        var busy = false;
        void Place()
        {
            if (busy) return;
            busy = true;
            try
            {
                var widget = anchor?.WidgetOnScreen;
                var overAnchor = ReferenceEquals(owner, anchor);
                var reference = overAnchor && widget is { } shown ? shown : owner.Bounds;
                var area = AreaFor(reference, anchor);
                if (dialog.Width > area.Width || dialog.Height > area.Height) MakeScrollable(dialog, area);

                // A dialog the user has dragged somewhere is only kept on the screen; otherwise it is (re)placed, also after it grew.
                if (placed is { } last && dialog.Location != last)
                {
                    dialog.Location = DialogPlacement.Clamp(dialog.Location, dialog.Size, AreaFor(dialog.Bounds, anchor));
                    return;
                }

                var gap = owner.LogicalToDeviceUnits(DialogPlacement.Gap);
                dialog.Location = overAnchor && widget is { } beside
                    ? DialogPlacement.Beside(beside, dialog.Size, area, gap)
                    : owner.Visible && owner.WindowState != FormWindowState.Minimized
                        ? DialogPlacement.Over(owner.Bounds, dialog.Size, area, widget, gap)
                        : DialogPlacement.Over(area, dialog.Size, area, null, gap); // the owner is hidden: the middle of its screen
                placed = dialog.Location;
            }
            finally
            {
                busy = false;
            }
        }

        EventHandler load = (_, _) =>
        {
            entry.Window = dialog.Handle;
            dialog.PerformLayout();
            Place();
        };
        EventHandler resized = (_, _) =>
        {
            if (placed is not null) Place();
        };
        dialog.Load += load;
        dialog.SizeChanged += resized;
        OpenWindows.Add(entry);
        try
        {
            return dialog.ShowDialog(owner);
        }
        finally
        {
            OpenWindows.Remove(entry);
            dialog.Load -= load;
            dialog.SizeChanged -= resized;
        }
    }

    /// <summary>A message box over <paramref name="owner"/>, placed like a dialog.</summary>
    public static DialogResult Message(Form owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1) =>
        Placed(owner, () => MessageBox.Show(owner, text, caption, buttons, icon, defaultButton));

    /// <summary>A Windows dialog (the folder picker) over <paramref name="owner"/>, placed like a dialog.</summary>
    public static DialogResult Common(CommonDialog dialog, Form owner) => Placed(owner, () => dialog.ShowDialog(owner));

    /// <summary>
    /// How long after a Windows dialog box first becomes active it is still put back in place when it changes its size or position by itself.
    /// The folder picker opens small and grows to the size it remembers only after it has become active.
    /// </summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Runs a call that shows a Windows dialog box and moves that box into place: as it first becomes active, and again while it settles
    /// (see <see cref="SettleTime"/>), until the user moves or sizes it.
    /// </summary>
    private static DialogResult Placed(Form owner, Func<DialogResult> show)
    {
        var anchor = AnchorOf(owner);
        var entry = new Open(null, anchor);
        var settling = new System.Diagnostics.Stopwatch();
        var userHasIt = false;
        var placing = false;

        void Place(IntPtr window)
        {
            if (placing || IsIconic(window) || IsZoomed(window) || !GetWindowRect(window, out var r)) return;
            var current = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            var widget = anchor?.WidgetOnScreen;
            var overAnchor = ReferenceEquals(owner, anchor);
            var area = AreaFor(overAnchor && widget is { } shown ? shown : owner.Bounds, anchor);
            var size = new Size(Math.Min(current.Width, area.Width), Math.Min(current.Height, area.Height));
            var gap = owner.LogicalToDeviceUnits(DialogPlacement.Gap);
            Point At(Size of) => overAnchor && widget is { } beside
                ? DialogPlacement.Beside(beside, of, area, gap)
                : owner.Visible && owner.WindowState != FormWindowState.Minimized
                    ? DialogPlacement.Over(owner.Bounds, of, area, widget, gap)
                    : DialogPlacement.Over(area, of, area, null, gap);

            var at = At(size);
            if (widget is { } standing && IsResizable(window) && standing.IntersectsWith(new Rectangle(at, size)))
            {
                size = DialogPlacement.FitBeside(standing, size, area, gap, owner.LogicalToDeviceUnits(DialogPlacement.NarrowestResizable));
                at = At(size);
            }

            if (new Rectangle(at, size) == current) return;

            placing = true; // moving the window reports a change of its own
            var flags = SWP_NOZORDER | SWP_NOACTIVATE | (size == current.Size ? SWP_NOSIZE : 0);
            SetWindowPos(window, IntPtr.Zero, at.X, at.Y, size.Width, size.Height, flags);
            placing = false;
        }

        HookProc activated = null!;
        var activationHook = IntPtr.Zero;
        activated = (code, wParam, lParam) =>
        {
            if (code == HCBT_ACTIVATE && entry.Window == IntPtr.Zero && ClassOf(wParam) == DialogBoxClass)
            {
                entry.Window = wParam;
                settling.Start();
                Place(wParam);
            }

            return CallNextHookEx(activationHook, code, wParam, lParam);
        };

        HookProc handled = null!;
        var messageHook = IntPtr.Zero;
        handled = (code, wParam, lParam) =>
        {
            var result = CallNextHookEx(messageHook, code, wParam, lParam);
            if (code < 0 || entry.Window == IntPtr.Zero || userHasIt || settling.Elapsed > SettleTime) return result;

            var message = Marshal.PtrToStructure<HandledMessage>(lParam);
            if (message.Window != entry.Window) return result;
            if (message.Message == WM_ENTERSIZEMOVE) userHasIt = true;
            else if (message.Message == WM_WINDOWPOSCHANGED) Place(message.Window);
            return result;
        };

        // The folder picker can also change its size back while it is being moved (inside our own call, when nothing is looked at), so the
        // place is checked again a few times from the box's own message loop for as long as it settles.
        using var settle = new System.Windows.Forms.Timer { Interval = 100 };
        settle.Tick += (_, _) =>
        {
            if (userHasIt || settling.Elapsed > SettleTime) settle.Stop();
            else if (entry.Window != IntPtr.Zero) Place(entry.Window);
        };

        OpenWindows.Add(entry);
        activationHook = SetWindowsHookEx(WH_CBT, activated, IntPtr.Zero, GetCurrentThreadId());
        messageHook = SetWindowsHookEx(WH_CALLWNDPROCRET, handled, IntPtr.Zero, GetCurrentThreadId());
        settle.Start();
        try
        {
            return show();
        }
        finally
        {
            if (messageHook != IntPtr.Zero) UnhookWindowsHookEx(messageHook);
            if (activationHook != IntPtr.Zero) UnhookWindowsHookEx(activationHook);
            OpenWindows.Remove(entry);
            GC.KeepAlive(activated);
            GC.KeepAlive(handled);
        }
    }

    /// <summary>The app window behind an owner: the owner itself, or the one its dialog was opened over.</summary>
    private static IDialogAnchor? AnchorOf(Form owner) => owner as IDialogAnchor ?? OpenWindows.LastOrDefault(open => ReferenceEquals(open.Dialog, owner))?.Anchor;

    private static Rectangle AreaFor(Rectangle reference, IDialogAnchor? anchor)
    {
        var areas = anchor?.DialogWorkAreas ?? Screen.AllScreens.OrderByDescending(s => s.Primary).Select(s => s.WorkingArea).ToList();
        return WidgetPlacement.AreaFor(reference, areas);
    }

    /// <summary>A dialog larger than the screen's usable area (a small or highly scaled screen) is cut to it and scrolls, so its buttons stay reachable.</summary>
    internal static void MakeScrollable(Form dialog, Rectangle area)
    {
        var size = dialog.Size;
        if (!dialog.AutoScroll)
        {
            var content = dialog.Controls.Count == 1 ? dialog.Controls[0] : null;
            dialog.AutoSize = false;
            if (content is not null)
            {
                var wanted = content.GetPreferredSize(Size.Empty);
                content.Dock = DockStyle.None;
                content.Location = new Point(dialog.Padding.Left, dialog.Padding.Top);
                content.Size = wanted;
                dialog.AutoScrollMargin = new Size(dialog.Padding.Right, dialog.Padding.Bottom);
            }

            dialog.AutoScroll = true;
            // room for the scroll bar that appears, so that it does not call for a second one
            if (size.Height > area.Height) size.Width += SystemInformation.VerticalScrollBarWidth;
            if (size.Width > area.Width) size.Height += SystemInformation.HorizontalScrollBarHeight;
        }

        dialog.Size = new Size(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
    }

    private const string DialogBoxClass = "#32770";
    private const int WH_CBT = 5;
    private const int WH_CALLWNDPROCRET = 12;
    private const int HCBT_ACTIVATE = 5;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_THICKFRAME = 0x00040000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private static bool IsTopMost(IntPtr window) => (GetWindowLong(window, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

    private static bool IsResizable(IntPtr window) => (GetWindowLong(window, GWL_STYLE) & WS_THICKFRAME) != 0;

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(64);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    /// <summary>What a WH_CALLWNDPROCRET hook is told about a message a window has just handled (CWPRETSTRUCT).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct HandledMessage
    {
        public IntPtr Result;
        public IntPtr LParam;
        public IntPtr WParam;
        public int Message;
        public IntPtr Window;
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr window);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hook, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);
}
