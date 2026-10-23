using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using PinkieSysMon;
using SkiaSharp;

namespace PinkieSysMon.RegressionTests;

// Explicit Stage 02 native experiment; excluded from normal regression execution.
// Two real processes receive real user clicks. No Shell dispatch, USB, hooks,
// synthetic desktop input, app settings or persisted dashboards are involved.
internal static class ShortcutNativeDiagnostic
{
    public const string Argument = "--shortcut-native-check";
    private const string UnderlayArgument = "--shortcut-native-underlay";
    internal static readonly Size Canvas = new(560, 320);

    public static int? TryRun(string[] args, Action<string> write)
    {
        if (args.Length == 0 || (args[0] != Argument && args[0] != UnderlayArgument)) return null;
        if (args.Length != 1)
        {
            write("Shortcut native experiment: invalid arguments; no experiment started.");
            return 2;
        }
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
        {
            write("Shortcut native experiment: NOT RUN (interactive Windows desktop required).");
            return 2;
        }
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            if (args[0] == UnderlayArgument)
            {
                using var underlay = new Underlay(write);
                Application.Run(underlay);
            }
            else
            {
                write($"Shortcut Stage 02 experiment; OS={Environment.OSVersion.Version}");
                write("Source identity: matching builder.log or standalone script SHA-256. No Shell targets are loaded or launched.");
                write("Native feasibility remains UNPROVEN until traces are reviewed. Closing the experiment does not mean PASS.");
                using var controller = new Controller(write);
                Application.Run(controller);
                return controller.Failed ? 1 : 0;
            }
            return 0;
        }
        catch (Exception error)
        {
            write($"Shortcut native experiment: ERROR ({error.GetType().Name}; HRESULT 0x{error.HResult:X8}).");
            return 1;
        }
    }

    internal static ShortcutProbeMap CreateMap(int generation, double angle, bool empty = false) =>
        new(Canvas, generation, empty ? [] :
        [
            new("A", new RectangleF(40.25f, 50.75f, 110, 70), angle, 0, 0),
            new("B", new RectangleF(350.25f, 80.75f, 110, 100), -angle, 0, 1),
            new("C", new RectangleF(105.25f, 95.75f, 70, 50), angle, 1, 2)
        ]);

    private sealed class Underlay : Form
    {
        private readonly Action<string> _write;
        private int _clicks;
        public Underlay(Action<string> write)
        {
            _write = write;
            Text = "Shortcut native experiment — separate-process underlay";
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = Canvas;
            Location = new Point(Screen.PrimaryScreen!.WorkingArea.Left + 40, Screen.PrimaryScreen.WorkingArea.Top + 50);
            BackColor = Color.White;
            DoubleBuffered = true;
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PublishBounds();
            // EOF is the ownership signal: the child exits when its controller exits/crashes.
            _ = ReadLifetimeAsync();
        }
        private async Task ReadLifetimeAsync()
        {
            try { while (await Console.In.ReadLineAsync() is { } line) if (line == "close") break; }
            finally { if (!IsDisposed) Close(); }
        }
        private void PublishBounds()
        {
            if (!Visible) return;
            var origin = PointToScreen(Point.Empty);
            _write(JsonSerializer.Serialize(new { kind = "bounds", x = origin.X, y = origin.Y, hwnd = Handle.ToInt64() }));
        }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); PublishBounds(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            _write(JsonSerializer.Serialize(new { kind = "underlay", count = ++_clicks, x = e.X, y = e.Y }));
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.LightGray);
            for (var x = 0; x < Canvas.Width; x += 20) e.Graphics.DrawLine(pen, x, 0, x, Canvas.Height);
            for (var y = 0; y < Canvas.Height; y += 20) e.Graphics.DrawLine(pen, 0, y, Canvas.Width, y);
            e.Graphics.DrawString($"UNDERLAY releases: {_clicks}\nReal user clicks only. Empty space / purple rectangle must reach this process.",
                Font, Brushes.Black, new RectangleF(10, 230, 530, 80));
        }
        protected override void WndProc(ref Message m)
        {
            // A click-through test must not raise the underlay over its own test surfaces.
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref m);
        }
    }

    private sealed class Controller : Form
    {
        private readonly Action<string> _write;
        private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(650, 0) };
        private readonly CheckBox _locked = new() { Text = "Lock", Checked = true, AutoSize = true };
        private readonly CheckBox _single = new() { Text = "Single click (otherwise Double)", AutoSize = true };
        private readonly CheckBox _invisible = new() { Text = "Graphic opacity = 0", AutoSize = true };
        private readonly CheckBox _empty = new() { Text = "No hotspots", AutoSize = true };
        private readonly CheckBox _hide = new() { Text = "Hide surfaces", AutoSize = true };
        private readonly NumericUpDown _angle = new() { Minimum = -360, Maximum = 360, Value = 30, Width = 65 };
        private readonly List<ProbePanel> _panels = [];
        private Process? _child;
        private Point _origin;
        private IntPtr _underlay;
        private int _generation;
        private int _activations;
        private int _underlayClicks;
        private bool _closing;
        private bool _desktopPlacement;
        private bool _sibling;
        public bool Failed { get; private set; }

        public Controller(Action<string> write)
        {
            _write = write;
            Text = "Shortcut Stage 02 — input proof controller (not a feature preview)";
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Screen.PrimaryScreen!.WorkingArea.Left + 40, Screen.PrimaryScreen.WorkingArea.Top + 430);
            ClientSize = new Size(700, 245);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            controls.Controls.AddRange([_locked, _single, _invisible, _empty, _hide,
                new Label { Text = "Rotation", AutoSize = true }, _angle]);
            AddButton(controls, "Reload / cancel gesture", Rebuild);
            AddButton(controls, "Toggle overlapping panel", () => { _sibling = !_sibling; Rebuild(); });
            AddButton(controls, "Place above underlay", () => { _desktopPlacement = false; PlacePairs(); });
            AddButton(controls, "Place at desktop back", () => { _desktopPlacement = true; PlacePairs(); });
            controls.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(650, 0),
                Text = "A = blue, B = green, C = topmost orange overlap. Purple is an ordinary opaque widget. " +
                    "Lock: only graphic rectangles capture. Unlock: click activates; drag moves. " +
                    "Test gaps, edges, labels/effects, invisible graphics, other apps, Win+D and teardown. " +
                    "No result is automatically accepted. Close this controller to remove all experiment windows."
            });
            controls.Controls.Add(_status);
            Controls.Add(controls);
            foreach (var checkbox in new[] { _locked, _single, _invisible, _empty, _hide })
                checkbox.CheckedChanged += (_, _) => Safely(Rebuild);
            _angle.ValueChanged += (_, _) => Safely(Rebuild);
            Shown += async (_, _) => await StartUnderlayAsync();
            FormClosing += (_, _) => Cleanup();
        }

        private void AddButton(FlowLayoutPanel controls, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (_, _) => Safely(action);
            controls.Controls.Add(button);
        }
        private void Safely(Action action)
        {
            if (_closing) return;
            try { action(); }
            catch (Exception error)
            {
                Failed = true;
                foreach (var panel in _panels) panel.Hide();
                _write($"EXPERIMENT ERROR: {error.GetType().Name}; HRESULT 0x{error.HResult:X8}; surfaces hidden.");
                _status.Text = "Experiment failed. Surfaces hidden; close the controller and retain the console trace.";
            }
        }
        private async Task StartUnderlayAsync()
        {
            try
            {
                // This project has UseAppHost=false; relaunch its own entry assembly via the same dotnet host.
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true
                };
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                start.ArgumentList.Add(UnderlayArgument);
                _child = Process.Start(start) ?? throw new InvalidOperationException("Underlay did not start.");
                _ = _child.StandardError.ReadToEndAsync(); // drain; never publish arbitrary machine diagnostics
                var first = await _child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
                if (_closing) return;
                if (first is null) throw new InvalidOperationException("Underlay exited before readiness.");
                ApplyChildMessage(first);
                if (_underlay == IntPtr.Zero) throw new InvalidOperationException("No underlay window was observed.");
                _write($"Processes: controller={Environment.ProcessId}; underlay={_child.Id}; canvas={Canvas.Width}x{Canvas.Height}; dpi={DeviceDpi}");
                Rebuild();
                while (!_closing && await _child.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (_closing) return;
                    ApplyChildMessage(line);
                }
                if (!_closing) throw new InvalidOperationException("Underlay terminated unexpectedly.");
            }
            catch (Exception error) when (_closing && error is ObjectDisposedException or InvalidOperationException or IOException) { }
            catch (Exception error) { Safely(() => throw new InvalidOperationException("Native experiment startup/read failed.", error)); Cleanup(); }
        }
        private void ApplyChildMessage(string line)
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            switch (root.GetProperty("kind").GetString())
            {
                case "bounds":
                    _origin = new Point(root.GetProperty("x").GetInt32(), root.GetProperty("y").GetInt32());
                    _underlay = new IntPtr(root.GetProperty("hwnd").GetInt64());
                    foreach (var panel in _panels) panel.Visual.Location = _origin;
                    break;
                case "underlay":
                    _underlayClicks = root.GetProperty("count").GetInt32();
                    _write("UNDERLAY " + line);
                    UpdateStatus();
                    break;
                default: throw new InvalidDataException("Unexpected experiment child message.");
            }
        }
        private void Rebuild()
        {
            if (_underlay == IntPtr.Zero) return;
            foreach (var panel in _panels) panel.Dispose();
            _panels.Clear();
            var map = CreateMap(++_generation, (double)_angle.Value, _empty.Checked);
            try
            {
                for (var index = 0; index < (_sibling ? 2 : 1); index++)
                {
                    var panelId = index;
                    var panel = new ProbePanel(map, _locked.Checked, _single.Checked, _invisible.Checked,
                        _origin + new Size(index * 65, index * 35),
                        id => { _activations++; _write($"ACTIVATE panel={panelId}; widget={id}; generation={map.Generation}; total={_activations}"); UpdateStatus(); }, _write);
                    _panels.Add(panel);
                    if (!_hide.Checked) panel.Show();
                }
                PlacePairs();
                _write($"CONFIG generation={map.Generation}; lock={_locked.Checked}; single={_single.Checked}; opacity0={_invisible.Checked}; empty={_empty.Checked}; angle={_angle.Value}; panels={_panels.Count}");
                UpdateStatus();
            }
            catch { foreach (var panel in _panels) panel.Dispose(); _panels.Clear(); throw; }
        }
        private void PlacePairs()
        {
            if (_underlay != IntPtr.Zero) Native.ShowWindow(_underlay, _desktopPlacement ? 0 : 4);
            foreach (var panel in _panels.AsEnumerable().Reverse())
                if (panel.Visual.Visible)
                {
                    if (_desktopPlacement) DesktopBottomPlacement.PlaceAtDesktopBack(panel.Visual.Handle);
                    else Native.Place(panel.Visual.Handle, IntPtr.Zero);
                    panel.PlaceInput();
                }
        }
        private void UpdateStatus() => _status.Text = $"Activations: {_activations}; underlay releases: {_underlayClicks}; generation: {_generation}. Native acceptance: PENDING.";
        private void Cleanup()
        {
            if (_closing) return;
            _closing = true;
            foreach (var panel in _panels) panel.Dispose();
            _panels.Clear();
            if (_child is not null)
            {
                try { _child.StandardInput.Close(); } catch (IOException) { }
                // Child's stdin EOF closes its message loop. Never kill an unrelated process.
                if (!_child.WaitForExit(3000))
                    _write("Underlay exit is still pending after stdin EOF; native teardown is NOT CONFIRMED.");
                _child.Dispose();
                _child = null;
            }
            _write("Experiment surfaces disposed. Native/Visual/Touch acceptance: NOT AUTOMATICALLY ESTABLISHED.");
        }
        protected override void Dispose(bool disposing) { if (disposing) Cleanup(); base.Dispose(disposing); }
    }

    private sealed class ProbePanel : IDisposable
    {
        private readonly ShortcutProbeMap _map;
        private readonly InputSurface _input;
        public VisualSurface Visual { get; }
        public ProbePanel(ShortcutProbeMap map, bool locked, bool single, bool invisible, Point origin, Action<string> activate, Action<string> write)
        {
            _map = map;
            Visual = new VisualSurface(locked) { Location = origin, ClientSize = Canvas };
            try
            {
                _input = new InputSurface(map, Visual, locked, single, activate, write);
                Visual.LocationChanged += (_, _) => _input.MoveWithVisual();
                Visual.ClientSizeChanged += (_, _) => _input.MoveWithVisual();
                Visual.DragEnded = PlaceInput;
                Visual.DrawFrame = () => Draw(invisible);
            }
            catch { Visual.Dispose(); throw; }
        }
        public void Show()
        {
            Visual.Show();
            DrawFrame();
            _input.ShowRestricted();
            PlaceInput();
        }
        public void Hide() { _input.Hide(); Visual.Hide(); }
        private void DrawFrame() => Visual.DrawFrame?.Invoke();
        private void Draw(bool invisible)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(Canvas.Width, Canvas.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = new SKColor(150, 50, 180), IsAntialias = true };
            canvas.DrawRect(new SKRect(210, 40, 290, 130), paint); // ordinary opaque widget: must pass through
            if (!invisible)
            {
                for (var y = 0; y < Canvas.Height; y++)
                for (var x = 0; x < Canvas.Width; x++)
                {
                    var hit = _map.Hit(new Point(x, y));
                    if (hit is null) continue;
                    // Raster the EXACT interactive pixels, including fractional rotated edges.
                    paint.Color = hit.Id == "A" ? SKColors.DodgerBlue : hit.Id == "B" ? SKColors.LimeGreen : SKColors.Orange;
                    canvas.DrawRect(x, y, 1, 1, paint);
                }
            }
            using var surface = new DesktopLayeredSurface(Canvas.Width, Canvas.Height);
            surface.Present(Visual.Handle, bitmap);
        }
        public void PlaceInput() => _input.PlaceAboveVisual();
        public void Dispose() { _input.Dispose(); Visual.Dispose(); }
    }

    private sealed class VisualSurface : Form
    {
        private readonly bool _locked;
        public Action? DrawFrame;
        public Action? DragEnded;
        public VisualSurface(bool locked)
        {
            _locked = locked;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= DesktopPointerStyle.WsExLayered | 0x80 | 0x08000000;
                if (_locked) p.ExStyle |= DesktopPointerStyle.WsExTransparent;
                return p;
            }
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            DrawFrame?.Invoke();
        }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084 && !_locked) { m.Result = new IntPtr(2); return; }
            base.WndProc(ref m);
            if (m.Msg == DesktopOutputWindow.WmExitSizeMove) DragEnded?.Invoke();
            if (m.Msg == 0x02e0) // WM_DPICHANGED: match the production physical-canvas invariant
            {
                ClientSize = Canvas;
                DrawFrame?.Invoke();
            }
        }
    }

    private sealed class InputSurface : NativeWindow, IDisposable
    {
        private readonly ShortcutProbeMap _map;
        private readonly VisualSurface _visual;
        private readonly bool _locked;
        private readonly bool _single;
        private readonly Action<string> _activate;
        private readonly Action<string> _write;
        private readonly ShortcutProbeGesture _gesture = new((uint)SystemInformation.DoubleClickTime, SystemInformation.DoubleClickSize, SystemInformation.DragSize);
        private readonly bool _empty;
        private bool _releasing;
        private bool _visible;

        public InputSurface(ShortcutProbeMap map, VisualSurface visual, bool locked, bool single, Action<string> activate, Action<string> write)
        {
            _map = map; _visual = visual; _locked = locked; _single = single; _activate = activate; _write = write;
            var rectangles = map.Rasterize();
            _empty = rectangles.Count == 0;
            try
            {
                CreateHandle(new CreateParams
                {
                    Caption = "Shortcut Stage 02 input companion", ClassStyle = 0x0008, // CS_DBLCLKS
                    Style = unchecked((int)0x80000000), // WS_POPUP; initially hidden
                    ExStyle = 0x00200000 | 0x08000000 | 0x00000080, // NOREDIRECTIONBITMAP | NOACTIVATE | TOOLWINDOW
                    X = visual.Left, Y = visual.Top, Width = Canvas.Width, Height = Canvas.Height
                });
                ApplyRegion(rectangles);
                VerifyNativeRegion();
            }
            catch { Dispose(); throw; }
        }
        private void ApplyRegion(IReadOnlyList<Rectangle> rectangles)
        {
            var region = Native.CreateRectRgn(0, 0, 0, 0); // empty is a real region, NEVER a null/unrestricted region
            if (region == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                foreach (var r in rectangles)
                {
                    var row = Native.CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
                    if (row == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError());
                    try { if (Native.CombineRgn(region, region, row, 2) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError()); }
                    finally { Native.DeleteObject(row); }
                }
                if (Native.SetWindowRgn(Handle, region, false) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                region = IntPtr.Zero; // successful SetWindowRgn transfers ownership to Windows
            }
            finally { if (region != IntPtr.Zero) Native.DeleteObject(region); }
        }
        private void VerifyNativeRegion()
        {
            var copy = Native.CreateRectRgn(0, 0, 0, 0);
            if (copy == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                if (Native.GetWindowRgn(Handle, copy) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                for (var y = 0; y < Canvas.Height; y++)
                for (var x = 0; x < Canvas.Width; x++)
                    if (Native.PtInRegion(copy, x, y) != (_map.Hit(new Point(x, y)) is not null))
                        throw new InvalidOperationException("Native HRGN and logical dispatch membership disagree.");
                _write($"HRGN pixel parity verified; generation={_map.Generation}; empty={_empty}. Mouse delivery remains a separate observation.");
            }
            finally { Native.DeleteObject(copy); }
        }
        public void ShowRestricted()
        {
            if (_empty) return;
            MoveWithVisual();
            Native.ShowWindow(Handle, 8); // SW_SHOWNA
            _visible = true;
        }
        public void Hide() { _gesture.Cancel(); if (Handle != IntPtr.Zero) Native.ShowWindow(Handle, 0); _visible = false; }
        public void MoveWithVisual()
        {
            _gesture.Cancel();
            if (Handle != IntPtr.Zero && !Native.SetWindowPos(Handle, IntPtr.Zero, _visual.Left, _visual.Top, Canvas.Width, Canvas.Height, 0x0014))
                throw new Win32Exception(Marshal.GetLastPInvokeError()); // NOZORDER | NOACTIVATE
        }
        public void PlaceAboveVisual()
        {
            if (!_visible || !_visual.Visible) return;
            var previous = Native.GetWindow(_visual.Handle, 3); // GW_HWNDPREV
            if (previous != Handle) Native.Place(Handle, previous);
        }
        protected override void WndProc(ref Message m)
        {
            var point = new Point(unchecked((short)(m.LParam.ToInt64() & 0xffff)), unchecked((short)((m.LParam.ToInt64() >> 16) & 0xffff)));
            switch (m.Msg)
            {
                case 0x0084: m.Result = new IntPtr(1); return; // HTCLIENT; native region alone determines pass-through
                case 0x0021: m.Result = new IntPtr(3); return; // MA_NOACTIVATE, do not eat the click
                case 0x0014: m.Result = new IntPtr(1); return; // no background to erase
                case 0x000f: Native.ValidateRect(Handle, IntPtr.Zero); return;
                case 0x0201: // WM_LBUTTONDOWN
                case 0x0203: // WM_LBUTTONDBLCLK: second press, not a second dispatch route
                    var downHit = _map.Hit(point);
                    _gesture.Down(downHit?.Id, _map.Generation, point, unchecked((uint)Native.GetMessageTime()));
                    Native.SetCapture(Handle);
                    Trace("down", point, downHit?.Id);
                    return;
                case 0x0200:
                    if (_gesture.Move(point) && !_locked)
                    {
                        Trace("drag", point, null);
                        ReleaseOwnCapture();
                        var screen = point + new Size(_visual.Left, _visual.Top);
                        var packed = new IntPtr(unchecked((int)((ushort)screen.X | ((uint)(ushort)screen.Y << 16))));
                        // Native move handoff only to OUR visual HWND; no replay to Explorer/other apps.
                        Native.SendMessage(_visual.Handle, 0x00a1, new IntPtr(2), packed); // WM_NCLBUTTONDOWN, HTCAPTION
                    }
                    return;
                case 0x0202:
                    var upHit = _map.Hit(point);
                    var invoke = _gesture.Up(upHit?.Id, _map.Generation, point, _single);
                    ReleaseOwnCapture();
                    Trace("up", point, upHit?.Id);
                    if (invoke && upHit is not null) _activate(upHit.Id);
                    return;
                case 0x0215: if (!_releasing) _gesture.Cancel(); break; // WM_CAPTURECHANGED
                case 0x001f: _gesture.Cancel(); ReleaseOwnCapture(); break; // WM_CANCELMODE
            }
            base.WndProc(ref m);
        }
        private void ReleaseOwnCapture()
        {
            if (Native.GetCapture() != Handle) return;
            _releasing = true;
            try { Native.ReleaseCapture(); }
            finally { _releasing = false; }
        }
        private void Trace(string kind, Point p, string? winner) =>
            _write($"INPUT {kind}; generation={_map.Generation}; x={p.X}; y={p.Y}; winner={winner ?? "none"}; time={unchecked((uint)Native.GetMessageTime())}");
        public void Dispose()
        {
            _gesture.Cancel();
            if (Handle != IntPtr.Zero) { ReleaseOwnCapture(); DestroyHandle(); }
        }
    }

    private static class Native
    {
        public static void Place(IntPtr window, IntPtr after)
        {
            if (!SetWindowPos(window, after, 0, 0, 0, 0, 0x0213)) // NOSIZE|NOMOVE|NOACTIVATE|NOOWNERZORDER
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)] internal static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
        [DllImport("user32.dll", SetLastError = true)] internal static extern int GetWindowRgn(IntPtr window, IntPtr region);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PtInRegion(IntPtr region, int x, int y);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern int CombineRgn(IntPtr result, IntPtr first, IntPtr second, int mode);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] internal static extern IntPtr SetCapture(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetCapture();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern int GetMessageTime();
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ValidateRect(IntPtr window, IntPtr rectangle);
    }
}
