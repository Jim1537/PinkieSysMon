using System.Drawing;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class ShortcutInteractionTests
{
    public static void RasterAndSnapshot()
    {
        var random = new Random(101);
        for (var pass = 0; pass < 220; pass++)
        {
            var widgets = Enumerable.Range(0, pass % 5 + 1).Select(i => new C.ShortcutWidgetDefinition
            {
                Id = "s" + i, X = (float)(random.NextDouble() * 90 - 25), Y = (float)(random.NextDouble() * 70 - 20),
                Width = (float)(random.NextDouble() * 55 + .01), Height = (float)(random.NextDouble() * 45 + .01),
                Rotation = pass % 4 == 0 ? (pass % 8) * 90 : random.NextDouble() * 1440 - 720, Z = i % 2,
                Label = "outside input", Gap = 100, BorderWidth = 30, CornerRadius = 100, ShadowEnabled = true, Opacity = 0,
                Target = "target " + i, Arguments = "literal " + i
            }).ToArray();
            var board = ShortcutModelTests.Board(widgets); board.Canvas.Width = 64; board.Canvas.Height = 48;
            var map = new ShortcutInteractionMap(board); var runs = map.Rasterize();
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
                    .FirstOrDefault(e => new ShortcutProbeGraphic(e.s.Id!, new RectangleF(e.s.X, e.s.Y, e.s.Width, e.s.Height), e.s.Rotation, e.s.Z, e.i).Contains(x + .5, y + .5)).s;
                Check(covered[x, y] == (expected is not null) && map.Hit(new Point(x, y))?.Id == expected?.Id,
                    "Optimized native raster and dispatch must match the brute-force Stage 02 predicate at every pixel.");
            }
            var frozen = map.Rasterize().ToArray(); widgets[0].X = 999; widgets[0].Target = "changed";
            Check(map.Rasterize().SequenceEqual(frozen), "A prepared interaction snapshot must not borrow mutable editor geometry.");
        }
    }

    public static void GesturesAndShellBoundary()
    {
        var gesture = new ShortcutGesture(500, new Size(6, 6), new Size(8, 8));
        var p = new Point(20, 20);
        gesture.Down("s", 1, p, 100); Check(gesture.Up("s", 1, p, true), "Single activates on its first completed click.");
        gesture.Down("s", 1, p, 200); Check(!gesture.Up("s", 1, p, true), "An ordinary double-click must not duplicate Single activation.");
        gesture.Cancel(); gesture.Down("s", 2, p, uint.MaxValue - 100); Check(!gesture.Up("s", 2, p, false), "Double waits for the second click.");
        gesture.Down("s", 2, p, 50); Check(gesture.Up("s", 2, p, false), "Native timestamp wrap must preserve a qualifying double-click.");
        gesture.Down("s", 3, p, 1000); Check(gesture.Move(new Point(40, 20)) && !gesture.Up("s", 3, p, true), "Drag must cancel activation.");
        gesture.Down("s", 3, p, 1100); Check(!gesture.Up("s", 4, p, true), "A superseded generation must not invoke.");

        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var finished = new ManualResetEventSlim();
        var calls = new List<(string Target, string Arguments)>(); var errors = new List<int>();
        using var dispatcher = new ShortcutShellDispatcher(code => { lock (errors) errors.Add(code); }, (target, arguments) =>
        {
            lock (calls) calls.Add((target, arguments));
            if (target == "first") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Test did not release dispatcher."); }
            else finished.Set();
            return target == "first" ? 5 : 0;
        });
        ShortcutInteraction Payload(string target, string arguments = "") => new("s", new(0, 0, 20, 10), 0, 0, 0, target, arguments, true);
        try
        {
            Check(!dispatcher.TryDispatch(Payload("")), "A blank Target must not enter Shell dispatch.");
            Check(dispatcher.TryDispatch(Payload("first")) && entered.Wait(TimeSpan.FromSeconds(5)), "Explicit activation must reach the injected OS boundary off the caller thread.");
            Check(dispatcher.TryDispatch(Payload("cancelled")), "Pending test request must enqueue.");
            dispatcher.CancelPending();
            Check(dispatcher.TryDispatch(Payload("C:\\missing path\\Юникод.dll", "--literal \"two words\" $unchanged")), "Shell payload must accept any extension and literal arguments.");
            release.Set(); Check(finished.Wait(TimeSpan.FromSeconds(5)), "Dispatcher must continue after an OS failure and skip cancelled work.");
            lock (calls) Check(calls.SequenceEqual(new[] { ("first", ""), ("C:\\missing path\\Юникод.dll", "--literal \"two words\" $unchanged") }), "Target and Arguments must remain separate, unchanged, with no invocation after cancellation.");
            lock (errors) Check(errors.SequenceEqual(new[] { 5 }), "An OS failure must remain a technical result without a Shortcut popup.");
        }
        finally { release.Set(); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
