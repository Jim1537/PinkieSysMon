using System.Drawing;

namespace PinkieSysMon.RegressionTests;

internal static class ShortcutInputPrototypeTests
{
    public static void LifetimeReadDoesNotBlockCaller()
    {
        using var input = new BlockingReader();
        using var returned = new ManualResetEventSlim();
        Task? lifetime = null;
        Exception? failure = null;
        var callerThread = 0;
        var caller = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            try { lifetime = ShortcutProbeLifetime.WaitForExitAsync(TextReader.Synchronized(input)); }
            catch (Exception error) { failure = error; }
            finally { returned.Set(); }
        }) { IsBackground = true };
        caller.Start();
        bool returnedBeforeEof;
        bool readWhileOpen;
        try
        {
            returnedBeforeEof = returned.Wait(TimeSpan.FromSeconds(5));
            readWhileOpen = input.Entered.Wait(TimeSpan.FromSeconds(5));
            Check(returnedBeforeEof && readWhileOpen && failure is null && lifetime is not null && !lifetime.IsCompleted,
                "An open console-style stdin must not block the caller/message loop or complete its lifetime early.");
            Check(input.ReadThread != callerThread, "Blocking console reads must run outside the window's caller thread.");
        }
        finally
        {
            input.Eof.Set();
            Check(caller.Join(TimeSpan.FromSeconds(5)), "Lifetime fixture caller did not terminate.");
            if (lifetime is not null) Check(lifetime.Wait(TimeSpan.FromSeconds(5)), "Stdin EOF must complete the lifetime wait.");
        }
        using var commands = new StringReader("ignore\nclose\nnot-consumed\n");
        ShortcutProbeLifetime.WaitForExitAsync(commands).GetAwaiter().GetResult();
        Check(commands.ReadLine() == "not-consumed", "Only EOF or the exact close command terminates the borrowed reader loop.");
    }

    private sealed class BlockingReader : TextReader
    {
        public readonly ManualResetEventSlim Entered = new();
        public readonly ManualResetEventSlim Eof = new();
        public int ReadThread;
        public override string? ReadLine()
        {
            ReadThread = Environment.CurrentManagedThreadId;
            Entered.Set();
            Eof.Wait();
            return null;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Entered.Dispose(); Eof.Dispose(); }
            base.Dispose(disposing);
        }
    }

    public static void RegionAndDispatchAgree()
    {
        foreach (var angle in new[] { 0d, 30, 90, -37, 390 })
        {
            var graphic = new ShortcutProbeGraphic("rotated", new RectangleF(13.25f, 12.75f, 30, 12), angle, 0, 0);
            var map = new ShortcutProbeMap(new Size(64, 48), 1, [graphic]);
            var rectangles = map.Rasterize();
            for (var y = -1; y <= 48; y++)
            for (var x = -1; x <= 64; x++)
            {
                var point = new Point(x, y);
                var covered = rectangles.Any(r => r.Contains(point));
                Check(covered == (map.Hit(point) is not null), "Native region and dispatch disagree.");
                if (x >= 0 && x < 64 && y >= 0 && y < 48)
                    Check(covered == graphic.Contains(x + .5, y + .5), "Rasterization changed the graphic geometry.");
            }
        }
        var square = new ShortcutProbeGraphic("square", new RectangleF(10, 10, 20, 10), 90, 0, 0);
        Check(square.Contains(20, 5.1) && square.Contains(20, 24.9) &&
              !square.Contains(14.9, 15) && !square.Contains(25.1, 15), "Rotation must use the graphic center.");
        var clipped = new ShortcutProbeMap(new Size(8, 8), 1,
            [new("clipped", new RectangleF(-4, -4, 8, 8), 0, 0, 0)]);
        Check(clipped.Rasterize().Sum(r => r.Width * r.Height) == 16 && clipped.Hit(new(-1, 0)) is null,
            "The native union must be clipped to the canvas, including negative origins.");
    }

    public static void UnionAndOverlap()
    {
        var map = new ShortcutProbeMap(new Size(80, 40), 2,
        [
            new("back", new RectangleF(2, 2, 10, 10), 0, 0, 0),
            new("front", new RectangleF(5, 5, 10, 10), 0, 1, 1),
            new("tie", new RectangleF(7, 7, 10, 10), 0, 1, 2),
            new("distant", new RectangleF(60, 20, 10, 10), 0, 0, 3)
        ]);
        Check(map.Hit(new(8, 8))?.Id == "tie" && map.Hit(new(5, 5))?.Id == "front" &&
              map.Hit(new(2, 2))?.Id == "back", "Winner must follow reverse stable paint order.");
        Check(map.Hit(new(35, 20)) is null && !map.Rasterize().Any(r => r.Contains(35, 20)),
            "Separated hotspots must not turn into a bounding-box desktop blocker.");
        Check(new ShortcutProbeMap(new Size(10, 10), 3, []).Rasterize().Count == 0,
            "Empty maps must have an empty union, never unrestricted capture.");
    }

    public static void ClickModesAndCancellation()
    {
        var gesture = new ShortcutProbeGesture(500, new Size(8, 8), new Size(8, 8));
        foreach (var single in new[] { false, true })
        {
            gesture.Cancel();
            gesture.Down("a", 1, new(10, 10), 100);
            Check(gesture.Up("a", 1, new(10, 10), single) == single, "First click mode mismatch.");
            gesture.Down("a", 1, new(11, 11), 300);
            Check(gesture.Up("a", 1, new(11, 11), single) == !single, "An ordinary double-click must invoke exactly once.");
            Check(!gesture.Up("a", 1, new(11, 11), single), "A release without a press must never activate.");
        }
        gesture.Down("a", 1, new(10, 10), 1000);
        Check(!gesture.Move(new(12, 12)) && gesture.Move(new(15, 10)), "Drag must use native threshold dimensions.");
        Check(!gesture.Up("a", 1, new(15, 10), true), "Dragging must cancel activation.");
        gesture.Down("a", 1, new(10, 10), 2000);
        Check(!gesture.Up("a", 2, new(10, 10), true), "Reload must invalidate a pending press.");
        gesture.Down("a", 1, new(10, 10), 3000);
        gesture.Cancel();
        Check(!gesture.Up("a", 1, new(10, 10), true), "Capture loss must cancel activation.");
        gesture.Down("a", 1, new(10, 10), 4000);
        Check(!gesture.Up("b", 1, new(10, 10), true), "Release on another target must not activate.");
    }

    public static void TimeDistanceAndIdentity()
    {
        var g = new ShortcutProbeGesture(500, new Size(8, 8), new Size(8, 8));
        bool Click(string id, uint time, int x = 10, int generation = 1)
        {
            g.Down(id, generation, new(x, 10), time);
            return g.Up(id, generation, new(x, 10), false);
        }
        Check(!Click("a", 100) && !Click("b", 200), "Different widgets cannot form a double-click.");
        g.Cancel();
        Check(!Click("a", 100) && !Click("a", 601), "Timed-out clicks must not activate Double mode.");
        g.Cancel();
        Check(!Click("a", 100) && !Click("a", 200, 15), "Distant clicks must not form a pair.");
        g.Cancel();
        Check(!Click("a", 100) && !Click("a", 200, generation: 2), "Generations cannot form a pair.");
        g.Cancel();
        Check(!Click("a", uint.MaxValue - 100) && Click("a", 100), "Native message-time wrap must retain a valid pair.");
        g.Cancel();
        Check(!Click("a", 100) && Click("a", 600), "The configured double-click interval is inclusive.");
        g.Cancel();
        Check(!Click("a", 100) && Click("a", 200) && !Click("a", 300), "A triple click must not reuse a consumed pair.");
    }

    public static void DiagnosticAdmission()
    {
        var output = new List<string>();
        Check(ShortcutNativeDiagnostic.TryRun([], output.Add) is null && output.Count == 0,
            "Ordinary regressions must not open the experimental desktop surfaces.");
        Check(ShortcutNativeDiagnostic.TryRun([ShortcutNativeDiagnostic.Argument, "extra"], output.Add) == 2,
            "Malformed arguments must fail before native window/process creation.");
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
