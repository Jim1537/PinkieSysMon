using System.Drawing;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class WidgetActionInteractionTests
{
    public static void RasterAndSnapshot()
    {
        var random = new Random(101);
        for (var pass = 0; pass < 220; pass++)
        {
            var widgets = Enumerable.Range(0, pass % 5 + 1).Select(i => new C.ImageWidgetDefinition
            {
                Id = "s" + i, X = (float)(random.NextDouble() * 90 - 25), Y = (float)(random.NextDouble() * 70 - 20),
                Width = (float)(random.NextDouble() * 55 + .01), Height = (float)(random.NextDouble() * 45 + .01),
                Rotation = pass % 4 == 0 ? (pass % 8) * 90 : random.NextDouble() * 1440 - 720, Z = i % 2,
                BorderWidth = 30, CornerRadius = 100, ShadowEnabled = true,
                Action = new() { Activation = C.WidgetActionDefinition.SingleClick, Target = "target " + i, Arguments = "literal " + i }
            }).ToArray();
            var board = new C.DashboardDefinition { BaseDirectory = Path.GetTempPath(), Widgets = [.. widgets], Canvas = new() { Width = 64, Height = 48 } };
            var map = new WidgetActionInteractionMap(board); var runs = map.Rasterize();
            var covered = new bool[64, 48];
            foreach (var run in runs)
            {
                Check(run.Height == 1 && run.Width > 0 && run.X >= 0 && run.Right <= 64 && run.Y >= 0 && run.Bottom <= 48, "Region runs must be clipped, sorted, disjoint scanlines.");
                for (var x = run.Left; x < run.Right; x++)
                { Check(!covered[x, run.Y], "Native region runs must not overlap."); covered[x, run.Y] = true; }
            }
            for (var y = 0; y < 48; y++)
            for (var x = 0; x < 64; x++)
            {
                var expected = widgets.Select((s, i) => (s, i)).OrderByDescending(e => e.s.Z).ThenByDescending(e => e.i)
                    .FirstOrDefault(e => new WidgetActionInteraction(e.s.Id!, new RectangleF(e.s.X, e.s.Y, e.s.Width, e.s.Height), e.s.Rotation, e.s.Z, e.i, e.s.Action.Target, e.s.Action.Arguments, true).Contains(x + .5, y + .5)).s;
                Check(covered[x, y] == (expected is not null) && map.Hit(new Point(x, y))?.Id == expected?.Id,
                    "Optimized native raster and dispatch must match the brute-force Stage 02 predicate at every pixel.");
            }
            var frozen = map.Rasterize().ToArray(); widgets[0].X = 999; widgets[0].Action.Target = "changed";
            Check(map.Rasterize().SequenceEqual(frozen), "A prepared interaction snapshot must not borrow mutable editor geometry.");
        }
    }

    public static void GesturesAndShellBoundary()
    {
        var gesture = new WidgetActionGesture(500, new Size(6, 6), new Size(8, 8));
        var p = new Point(20, 20);
        gesture.Down("s", 1, p, 100); Check(gesture.Up("s", 1, p, true), "Single activates on its first completed click.");
        gesture.Down("s", 1, p, 200); Check(!gesture.Up("s", 1, p, true), "An ordinary double-click must not duplicate Single activation.");
        gesture.Cancel(); gesture.Down("s", 2, p, uint.MaxValue - 100); Check(!gesture.Up("s", 2, p, false), "Double waits for the second click.");
        gesture.Down("s", 2, p, 50); Check(gesture.Up("s", 2, p, false), "Native timestamp wrap must preserve a qualifying double-click.");
        gesture.Down("s", 3, p, 1000); Check(gesture.Move(new Point(40, 20)) && !gesture.Up("s", 3, p, true), "Drag must cancel activation.");
        gesture.Down("s", 3, p, 1100); Check(!gesture.Up("s", 4, p, true), "A superseded generation must not invoke.");

        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var finished = new ManualResetEventSlim();
        var calls = new List<(string Target, string Arguments, string? Verb)>(); var errors = new List<int>();
        using var dispatcher = new WidgetActionShellDispatcher(code => { lock (errors) errors.Add(code); }, (target, arguments, verb) =>
        {
            lock (calls) calls.Add((target, arguments, verb));
            if (target == "first") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Test did not release dispatcher."); }
            else finished.Set();
            return target == "first" ? 5 : verb == "runas" ? 1223 : 0;
        });
        WidgetActionInteraction Payload(string target, string arguments = "", string runAs = C.WidgetActionDefinition.User) =>
            new("s", new(0, 0, 20, 10), 0, 0, 0, target, arguments, true, runAs);
        Check(WidgetActionShellDispatcher.ResolveVerb(C.WidgetActionDefinition.User) is null &&
              WidgetActionShellDispatcher.ResolveVerb(C.WidgetActionDefinition.Administrator) == "runas",
            "Shell must map User to its existing default verb and Administrator to runas.");
        try
        {
            Check(!dispatcher.TryDispatch(Payload("")), "A blank Target must not enter Shell dispatch.");
            Check(dispatcher.TryDispatch(Payload("first")) && entered.Wait(TimeSpan.FromSeconds(5)), "Explicit activation must reach the injected OS boundary off the caller thread.");
            Check(dispatcher.TryDispatch(Payload("cancelled")), "Pending test request must enqueue.");
            dispatcher.CancelPending();
            Check(dispatcher.TryDispatch(Payload("C:\\missing path\\Юникод.dll", "--literal \"two words\" $unchanged", C.WidgetActionDefinition.Administrator)), "Shell payload must accept any extension and literal arguments.");
            release.Set(); Check(finished.Wait(TimeSpan.FromSeconds(5)), "Dispatcher must continue after an OS failure and skip cancelled work.");
            lock (calls) Check(calls.SequenceEqual(new[]
            {
                ("first", "", (string?)null),
                ("C:\\missing path\\Юникод.dll", "--literal \"two words\" $unchanged", "runas")
            }), "Target, Arguments and elevation verb must reach Shell unchanged; pending cancellation must skip dispatch.");
            lock (errors) Check(errors.SequenceEqual(new[] { 5 }),
                "Ordinary OS errors must propagate; declined UAC must not generate an error popup.");
        }
        finally { release.Set(); }
    }

    public static void NativeDragHandoff()
    {
        // Action/Calculator hit windows are deliberately non-activating.
        // Their drag must enter Windows' own caption move loop, not fake a
        // non-client mouse-down that is subject to foreground activation.
        Check(DesktopWidgetDragHandoff.Message == 0x0112 &&
              DesktopWidgetDragHandoff.MoveFromCaption == 0xF012,
            "Action handoff must use WM_SYSCOMMAND SC_MOVE|HTCAPTION.");

        foreach (var position in new[]
        {
            new Point(0, 0), new Point(-200, -1080),
            new Point(1920, 400), new Point(32767, -32768)
        })
        {
            var packed = DesktopWidgetDragHandoff.PackScreenPoint(position).ToInt64();
            var decoded = new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
            Check(decoded == position,
                "Native handoff must retain signed physical desktop coordinates on every monitor.");
        }

        var gesture = new WidgetActionGesture(500, new Size(6, 6), new Size(8, 8));
        var origin = new Point(20, 20);
        gesture.Down("action", 1, origin, 100);
        Check(!gesture.Move(new Point(22, 22)) && !gesture.Dragged,
            "Small cursor motion must remain a click candidate.");
        Check(gesture.Move(new Point(40, 20)) && gesture.Dragged &&
              !gesture.Move(new Point(50, 20)) &&
              !gesture.Up("action", 1, origin, true),
            "Crossing the drag threshold must hand off once and suppress Action activation.");
        gesture.Cancel();
        gesture.Down("action", 2, origin, 200);
        Check(gesture.Up("action", 2, origin, true),
            "After a cancelled drag, a fresh click must still invoke Action.");
    }

    public static void DesktopHoverCursorRouting()
    {
        // WM_SETCURSOR carries a hit-test code in LOWORD, independent of the triggering mouse message.
        static IntPtr Details(ushort hitTest, ushort origin) =>
            new(unchecked((int)(((uint)origin << 16) | hitTest)));

        const int wmSetCursor = 0x0020;
        Check(DesktopWidgetActionInput.ShouldUseHandCursor(wmSetCursor, Details(1, 0x0200)) &&
              DesktopWidgetActionInput.ShouldUseHandCursor(wmSetCursor, Details(1, 0)),
            "Client-area mouse movement and menu-originated cursor messages must request the hand.");
        foreach (var hitTest in new ushort[] { 0, 2, 3, 0xFFFF })
            Check(!DesktopWidgetActionInput.ShouldUseHandCursor(wmSetCursor, Details(hitTest, 0x0200)),
                "Nonclient and non-hit regions must keep their native cursor behavior.");
        Check(!DesktopWidgetActionInput.ShouldUseHandCursor(0x0200, Details(1, 0x0200)) &&
              !DesktopWidgetActionInput.ShouldUseHandCursor(0x0201, Details(1, 0x0200)),
            "Ordinary mouse movement and clicks must not be mistaken for a cursor-setting message.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
