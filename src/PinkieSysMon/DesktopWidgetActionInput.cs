using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PinkieSysMon;

// UI-thread owned, input-only HWND. The layered visual retains its normal Lock style.
internal sealed class DesktopWidgetActionInput : NativeWindow, IDisposable
{
    private const int WmSetCursor = 0x0020;
    private const ushort HtClient = 1;

    // Only an active widget Action region owns the link-select cursor.
    internal static bool ShouldUseHandCursor(int message, IntPtr details) =>
        message == WmSetCursor && unchecked((ushort)details.ToInt64()) == HtClient;

    private WidgetActionInteractionMap _map;
    private readonly Form _visual;
    private readonly DesktopCanvasViewport _viewport;
    private readonly bool _locked;
    private readonly Action<Point> _drag;
    private readonly WidgetActionShellDispatcher _shell;
    private readonly Action<string, string, IntPtr>? _calculatorKey;
    private readonly WidgetActionGesture _gesture = new((uint)SystemInformation.DoubleClickTime,
        SystemInformation.DoubleClickSize, SystemInformation.DragSize);
    private bool _empty;
    private bool _visible;
    private bool _releasing;
    private long _generation;

    internal DesktopWidgetActionInput(WidgetActionInteractionMap map, Form visual, bool locked,
        Action<Point> drag, Action<int> reportFailure, FileLogger? log = null,
        DesktopCanvasViewport? viewport = null, Action<string, string, IntPtr>? calculatorKey = null)
    {
        _map = map; _visual = visual; _locked = locked; _drag = drag;
        _calculatorKey = calculatorKey;
        _viewport = viewport ?? DesktopCanvasViewport.ForCanvas(map.Canvas);
        if (_viewport.Canvas != map.Canvas)
            throw new InvalidOperationException("Desktop input and Canvas geometry disagree.");
        var rectangles = map.Rasterize();
        _empty = rectangles.Count == 0;
        _shell = new WidgetActionShellDispatcher(log is null ? reportFailure : _ => { }, log: log);
        try
        {
            CreateHandle(new CreateParams
            {
                Caption = "PinkieSysMon WidgetAction input", ClassStyle = 0x0008,
                Style = unchecked((int)0x80000000), ExStyle = 0x00200000 | 0x08000000 | 0x00000080,
                X = _viewport.CanvasOriginForSurface(visual.Location).X,
                Y = _viewport.CanvasOriginForSurface(visual.Location).Y,
                Width = map.Canvas.Width, Height = map.Canvas.Height
            });
            ValidateClientSize(Handle, _map.Canvas);
            ApplyRegion(rectangles);
        }
        catch { Dispose(); throw; }
    }

    internal void UpdateMap(WidgetActionInteractionMap updated)
    {
        if (updated.Canvas != _map.Canvas)
            throw new InvalidOperationException("Changing Canvas geometry requires a new Desktop Action input surface.");
        var rectangles = updated.Rasterize();
        ApplyRegion(rectangles);
        // A visual reflow invalidates an in-progress gesture, but must not cancel
        // already accepted Shell requests belonging to the same Output generation.
        _gesture.Cancel();
        _generation++;
        ReleaseOwnCapture();
        _map = updated;
        _empty = rectangles.Count == 0;
        if (_empty && _visible)
        {
            ShowWindow(Handle, 0);
            _visible = false;
        }
        else if (!_empty && !_visible && _visual.Visible)
            ShowRestricted();
    }

    private void ApplyRegion(IReadOnlyList<Rectangle> rectangles)
    {
        var region = IntPtr.Zero;
        try
        {
            if (rectangles.Count == 0)
                region = CreateRectRgn(0, 0, 0, 0); // never a null/unrestricted region
            else
            {
                // RGNDATAHEADER + top/left sorted disjoint scanline RECTs.
                var data = new int[checked(8 + rectangles.Count * 4)];
                data[0] = 32; data[1] = 1; data[2] = rectangles.Count; data[3] = checked(rectangles.Count * 16);
                data[4] = rectangles.Min(r => r.Left); data[5] = rectangles.Min(r => r.Top);
                data[6] = rectangles.Max(r => r.Right); data[7] = rectangles.Max(r => r.Bottom);
                for (var i = 0; i < rectangles.Count; i++)
                {
                    var r = rectangles[i]; var offset = 8 + i * 4;
                    data[offset] = r.Left; data[offset + 1] = r.Top; data[offset + 2] = r.Right; data[offset + 3] = r.Bottom;
                }
                var bytes = checked(data.Length * 4); var buffer = Marshal.AllocHGlobal(bytes);
                try { Marshal.Copy(data, 0, buffer, data.Length); region = ExtCreateRegion(IntPtr.Zero, (uint)bytes, buffer); }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            if (region == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create WidgetAction region.");
            if (SetWindowRgn(Handle, region, false) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot restrict WidgetAction input.");
            region = IntPtr.Zero; // successful SetWindowRgn transfers HRGN ownership to Windows
        }
        finally { if (region != IntPtr.Zero) DeleteObject(region); }
    }

    internal bool IsPairBroken => _visible && _visual.Visible &&
        (!IsWindowVisible(Handle) || GetWindow(Handle, 2) != _visual.Handle);

    internal void ShowRestricted()
    {
        if (_empty || !_visual.Visible) return;
        MoveWithVisual();
        ValidateClientSize(_visual.Handle, _viewport.Surface);
        ValidateClientSize(Handle, _map.Canvas);
        ShowWindow(Handle, 8); _visible = true; PlaceAboveVisual();
    }

    private static void ValidateClientSize(IntPtr window, Size expected)
    {
        if (!GetClientRect(window, out var client))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot read Desktop input coordinate mapping.");
        if (client.Right - client.Left != expected.Width || client.Bottom - client.Top != expected.Height)
            throw new InvalidOperationException("Desktop visual/input client pixels disagree with the Canvas viewport; input remains hidden.");
    }

    internal void Hide()
    {
        Cancel(); if (Handle != IntPtr.Zero) ShowWindow(Handle, 0); _visible = false;
    }

    internal void MoveWithVisual()
    {
        Cancel();
        var canvasOrigin = _viewport.CanvasOriginForSurface(_visual.Location);
        if (Handle != IntPtr.Zero && !SetWindowPos(Handle, IntPtr.Zero, canvasOrigin.X, canvasOrigin.Y,
            _map.Canvas.Width, _map.Canvas.Height, 0x0014))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot move WidgetAction companion.");
    }

    internal void PlaceAboveVisual()
    {
        if (!_visible || !_visual.Visible) return;
        if (!IsWindowVisible(Handle)) ShowWindow(Handle, 8);
        var previous = GetWindow(_visual.Handle, 3);
        if (previous != Handle && !SetWindowPos(Handle, previous, 0, 0, 0, 0, DesktopBottomPlacement.Flags))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot pair WidgetAction input with its visual.");
    }

    private void Cancel()
    {
        _gesture.Cancel(); _generation++; _shell.CancelPending(); ReleaseOwnCapture();
    }

    private static string? GestureIdentity(WidgetActionInteraction? hit) =>
        hit is null ? null : hit.IsCalculatorKey ? hit.Id + "|key:" + hit.CalculatorKey : hit.Id;

    protected override void WndProc(ref Message m)
    {
        if (ShouldUseHandCursor(m.Msg, m.LParam))
        {
            SetCursor(Cursors.Hand.Handle);
            m.Result = new IntPtr(1); // WM_SETCURSOR handled; prevent the class cursor from replacing it.
            return;
        }

        var point = new Point(unchecked((short)m.LParam.ToInt64()), unchecked((short)(m.LParam.ToInt64() >> 16)));
        switch (m.Msg)
        {
            case 0x0084: m.Result = new IntPtr(1); return;
            case 0x0021: m.Result = new IntPtr(3); return;
            case 0x0014: m.Result = new IntPtr(1); return;
            case 0x000f: ValidateRect(Handle, IntPtr.Zero); return;
            case 0x0201:
            case 0x0203:
                _gesture.Down(GestureIdentity(_map.Hit(point)), _generation, point, unchecked((uint)GetMessageTime()));
                SetCapture(Handle); return;
            case 0x0200:
                if (_gesture.Move(point) && !_locked)
                {
                    ReleaseOwnCapture();
                    var canvasOrigin = _viewport.CanvasOriginForSurface(_visual.Location);
                    _drag(new Point(checked(canvasOrigin.X + point.X), checked(canvasOrigin.Y + point.Y)));
                }
                return;
            case 0x0202:
                var hit = _map.Hit(point);
                var invoke = _gesture.Up(GestureIdentity(hit), _generation, point,
                    hit?.SingleClick ?? false, hit?.IsCalculatorKey ?? false);
                ReleaseOwnCapture();
                if (invoke && hit is not null)
                {
                    if (hit.IsCalculatorKey) _calculatorKey?.Invoke(hit.Id, hit.CalculatorKey!, Handle);
                    else _shell.TryDispatch(hit);
                }
                return;
            case 0x0215: if (!_releasing) _gesture.Cancel(); break;
            case 0x001f: Cancel(); break;
        }
        base.WndProc(ref m);
    }

    private void ReleaseOwnCapture()
    {
        if (Handle == IntPtr.Zero || GetCapture() != Handle) return;
        _releasing = true;
        try { ReleaseCapture(); } finally { _releasing = false; }
    }

    public void Dispose()
    {
        Cancel(); _shell.Dispose();
        if (Handle != IntPtr.Zero) DestroyHandle();
    }

    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr ExtCreateRegion(IntPtr transform, uint bytes, IntPtr data);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] private static extern int GetMessageTime();
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ValidateRect(IntPtr window, IntPtr rectangle);
}

