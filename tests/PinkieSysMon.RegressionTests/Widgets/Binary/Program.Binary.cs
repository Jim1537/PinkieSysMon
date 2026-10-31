using System.ComponentModel;
using System.Drawing.Design;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void BinaryNumericTruthSemantics()
    {
        Assert(BinarySignalContract.TryResolve(0d, out var zero) && !zero, "Numeric zero must be false.");
        Assert(BinarySignalContract.TryResolve(1d, out var one) && one, "Numeric one must be true.");
        Assert(BinarySignalContract.TryResolve(1175d, out var rpm) && rpm, "Any finite non-zero number must be true.");
        Assert(BinarySignalContract.TryResolve(-1d, out var negative) && negative, "Negative non-zero numbers must be true.");
        Assert(!BinarySignalContract.TryResolve(double.NaN, out _), "NaN must be unavailable.");
        Assert(!BinarySignalContract.TryResolve(double.PositiveInfinity, out _), "Infinity must be unavailable.");
    }

    private static void BinarySetpointSemantics()
    {
        Assert(BinarySignalContract.TryResolve(
                81d,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfGreaterThan,
                out var above) && above,
            "Numeric setpoint comparison > failed.");
        Assert(BinarySignalContract.TryResolve(
                80d,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfGreaterThanOrEqual,
                out var equalInclusive) && equalInclusive,
            "Numeric setpoint comparison >= failed at equality.");
        Assert(BinarySignalContract.TryResolve(
                80d,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfEqual,
                out var equal) && equal,
            "Numeric setpoint comparison = failed.");
        Assert(BinarySignalContract.TryResolve(
                79d,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfGreaterThan,
                out var below) && !below,
            "Numeric false setpoint result failed.");

        Assert(BinarySignalContract.TryResolve(
                "READY",
                BinarySignalContract.EvaluationModeSetpoint,
                "READY",
                BinarySignalContract.TrueIfGreaterThan,
                out var exactText) && exactText,
            "String setpoint exact comparison failed.");
        Assert(BinarySignalContract.TryResolve(
                "ready",
                BinarySignalContract.EvaluationModeSetpoint,
                "READY",
                BinarySignalContract.TrueIfEqual,
                out var caseSensitiveText) && !caseSensitiveText,
            "String setpoint comparison must be case-sensitive.");
        Assert(BinarySignalContract.TryResolve(
                string.Empty,
                BinarySignalContract.EvaluationModeSetpoint,
                string.Empty,
                BinarySignalContract.TrueIfEqual,
                out var emptyText) && emptyText,
            "Empty string must remain a valid setpoint value.");

        Assert(BinarySignalContract.TryResolve(
                true,
                BinarySignalContract.EvaluationModeSetpoint,
                "1",
                BinarySignalContract.TrueIfEqual,
                out var trueAsOne) && trueAsOne,
            "Boolean setpoint '1' must mean expected true.");
        Assert(BinarySignalContract.TryResolve(
                false,
                BinarySignalContract.EvaluationModeSetpoint,
                "True",
                BinarySignalContract.TrueIfEqual,
                out var falseVsTrue) && !falseVsTrue,
            "Boolean setpoint must compare the metric value against the expected boolean.");
        Assert(BinarySignalContract.TryResolve(
                false,
                BinarySignalContract.EvaluationModeSetpoint,
                "anything else",
                BinarySignalContract.TrueIfEqual,
                out var falseAsDefault) && falseAsDefault,
            "Boolean setpoint other than True/1 must mean expected false.");

        Assert(!BinarySignalContract.TryResolve(
                null,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfGreaterThan,
                out _),
            "Null metric must be unavailable.");
        Assert(!BinarySignalContract.TryResolve(
                "undefined",
                BinarySignalContract.EvaluationModeSetpoint,
                "undefined",
                BinarySignalContract.TrueIfEqual,
                out _),
            "Undefined metric text must be unavailable, not a string match.");
        Assert(!BinarySignalContract.TryResolve(
                double.NaN,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfGreaterThan,
                out _),
            "NaN metric must be unavailable in setpoint mode.");
        Assert(!BinarySignalContract.TryResolve(
                80d,
                BinarySignalContract.EvaluationModeSetpoint,
                "not-a-number",
                BinarySignalContract.TrueIfGreaterThan,
                out _),
            "Invalid numeric setpoint must produce unavailable state.");
    }

    private static void StateImageColorFidelity()
    {
        const string metric = "test.binary.state-image";
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-state-image-regression");
        try
        {
            const string source = "state-image.png";
            WriteSolidTestImage(Path.Combine(tempRoot, source), SkiaSharp.SKColors.Red, width: 40, height: 10);

            var widget = new WidgetDefinition
            {
                Id = "binary-state-image",
                Type = WidgetTypeContract.Binary,
                X = 0,
                Y = 0,
                Width = 20,
                Height = 20,
                Metric = metric,
                // Deliberately contradict the state profile. Schema 11 Binary rendering must
                // ignore the legacy widget-level Image settings.
                Fit = "stretch",
                Loop = true
            };
            widget.EnsureStateVisualProfiles();
            var trueProfile = widget.Profiles![BinarySignalContract.TrueKey];
            trueProfile.SourceType = StateVisualProfileContract.SourceFile;
            trueProfile.Source = source;
            trueProfile.Color = "#FF00FF00";
            trueProfile.Opacity = 1f;
            trueProfile.Fit = StateVisualProfileContract.FitContain;
            trueProfile.Loop = false;

            var falseProfile = widget.Profiles[BinarySignalContract.FalseKey];
            falseProfile.SourceType = StateVisualProfileContract.SourceFile;
            falseProfile.Source = source;
            falseProfile.Opacity = 1f;
            falseProfile.Fit = StateVisualProfileContract.FitStretch;
            falseProfile.Loop = true;

            using var images = new ImageAssetCache(tempRoot);
            images.Preload(new[] { CurrentWidget(widget) });
            using var icons = new IconAssetCache(tempRoot);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons);

            using (var trueContext = new PinkieSysMon.Widgets.WidgetRenderContext(
                       new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = true },
                       antialias: false,
                       typefaces))
            using (var trueSurface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(20, 20))
                       ?? throw new InvalidOperationException("Could not create state-image regression surface."))
            {
                trueSurface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                PinkieSysMon.Widgets.WidgetTransform.Render(trueSurface.Canvas, CurrentWidget(widget), trueContext, renderer);
                trueSurface.Canvas.Flush();

                using var pixels = trueSurface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect true state-image regression pixels.");
                var center = pixels.GetPixelColor(10, 10);
                var top = pixels.GetPixelColor(10, 2);
                Assert(center.Red > 240 && center.Green < 20 && center.Blue < 20,
                    "File-backed state visuals must preserve source image colors instead of applying the icon color.");
                Assert(top.Alpha < 10,
                    "Binary True state must use its profile Fit=contain rather than the legacy widget-level Fit=stretch.");
            }

            using (var falseContext = new PinkieSysMon.Widgets.WidgetRenderContext(
                       new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = false },
                       antialias: false,
                       typefaces))
            using (var falseSurface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(20, 20))
                       ?? throw new InvalidOperationException("Could not create false state-image regression surface."))
            {
                falseSurface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                PinkieSysMon.Widgets.WidgetTransform.Render(falseSurface.Canvas, CurrentWidget(widget), falseContext, renderer);
                falseSurface.Canvas.Flush();

                using var pixels = falseSurface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect false state-image regression pixels.");
                var top = pixels.GetPixelColor(10, 2);
                Assert(top.Red > 240 && top.Alpha > 240,
                    "Binary False state must independently use its own profile Fit=stretch.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryStateAnimationPlayback()
    {
        var widget = new WidgetDefinition
        {
            Id = "binary-animation-playback",
            Type = WidgetTypeContract.Binary,
            X = 0,
            Y = 0,
            Width = 8,
            Height = 8,
            Metric = "test.binary.animation"
        };
        widget.EnsureStateVisualProfiles();

        var canonicalWidget = PropertyModelNormalizationMigration.MapLegacyWidget(widget);
        var tracker = new PinkieSysMon.Widgets.StateAnimationPlaybackTracker();
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.TrueKey, "same.gif", 1000) == 0,
            "Binary file playback must start at elapsed zero on first activation.");
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.TrueKey, "same.gif", 1125) == 125,
            "Binary file playback must advance relative to the current state activation.");
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.FalseKey, "same.gif", 1200) == 0,
            "Changing Binary state must restart playback even when both states use the same asset.");
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.FalseKey, "same.gif", 1260) == 60,
            "Binary False state playback must advance from its own activation time.");
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.TrueKey, "same.gif", 1300) == 0,
            "Re-entering Binary True state must restart playback from zero.");
        tracker.Deactivate(canonicalWidget);
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.TrueKey, "same.gif", 1500) == 0,
            "Deactivating a Binary visual must clear its playback activation.");
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.TrueKey, "other.gif", 1510) == 0,
            "Changing the active Binary file asset must restart playback.");

        int[] frameEnds = [100, 200];
        Assert(ImageAsset.ResolveFrameIndex(frameEnds, 200, 0, loop: false) == 0,
            "Animation elapsed zero must select the first frame.");
        Assert(ImageAsset.ResolveFrameIndex(frameEnds, 200, 99, loop: false) == 0,
            "Animation must retain the first frame through its declared duration.");
        Assert(ImageAsset.ResolveFrameIndex(frameEnds, 200, 100, loop: false) == 1,
            "Animation must advance to the second frame at the first frame boundary.");
        Assert(ImageAsset.ResolveFrameIndex(frameEnds, 200, 500, loop: false) == 1,
            "Non-looping animation must remain on its final frame after completion.");
        Assert(ImageAsset.ResolveFrameIndex(frameEnds, 200, 200, loop: true) == 0,
            "Looping animation must wrap to the first frame at the total-duration boundary.");

        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-animation-regression");
        try
        {
            const string source = "binary-animation.gif";
            File.WriteAllBytes(
                Path.Combine(tempRoot, source),
                Convert.FromBase64String(
                    "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));

            var trueProfile = widget.Profiles![BinarySignalContract.TrueKey];
            trueProfile.SourceType = StateVisualProfileContract.SourceFile;
            trueProfile.Source = source;
            trueProfile.Fit = StateVisualProfileContract.FitStretch;
            trueProfile.Loop = false;
            trueProfile.Opacity = 1f;

            var falseProfile = widget.Profiles[BinarySignalContract.FalseKey];
            falseProfile.SourceType = StateVisualProfileContract.SourceFile;
            falseProfile.Source = source;
            falseProfile.Fit = StateVisualProfileContract.FitStretch;
            falseProfile.Loop = true;
            falseProfile.Opacity = 1f;

            var currentWidget = (C.BinaryWidgetDefinition)CurrentWidget(widget);
            long nowMs = 10_000;
            using var images = new ImageAssetCache(tempRoot);
            images.Preload(new[] { currentWidget });
            using var icons = new IconAssetCache(tempRoot);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { currentWidget });
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons, () => nowMs);
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [widget.Metric!] = true
                },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(8, 8))
                ?? throw new InvalidOperationException("Could not create Binary animation regression surface.");

            SkiaSharp.SKColor RenderCenter(object? metricValue)
            {
                context.UpdateMetrics(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [widget.Metric!] = metricValue
                });
                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                renderer.Render(surface.Canvas, currentWidget, context);
                surface.Canvas.Flush();
                using var pixels = surface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect Binary animation regression pixels.");
                return pixels.GetPixelColor(4, 4);
            }

            static bool IsRed(SkiaSharp.SKColor color) =>
                color.Red > 240 && color.Green < 20 && color.Blue < 20 && color.Alpha > 240;
            static bool IsBlue(SkiaSharp.SKColor color) =>
                color.Blue > 240 && color.Red < 20 && color.Green < 20 && color.Alpha > 240;

            Assert(IsRed(RenderCenter(true)),
                "Entering a file-backed Binary True state must render animation frame zero.");

            nowMs = 10_150;
            Assert(IsBlue(RenderCenter(true)),
                "Binary True playback must advance from its activation-relative clock.");

            nowMs = 10_500;
            Assert(IsBlue(RenderCenter(true)),
                "Binary True Loop=false playback must stop on the final animation frame.");

            nowMs = 10_510;
            Assert(IsRed(RenderCenter(false)),
                "Switching to Binary False must restart the shared animated asset at frame zero.");

            nowMs = 10_660;
            Assert(IsBlue(RenderCenter(false)),
                "Binary False playback must advance independently after activation.");

            nowMs = 10_710;
            Assert(IsRed(RenderCenter(false)),
                "Binary False Loop=true playback must wrap after the animation duration.");

            nowMs = 10_720;
            Assert(IsRed(RenderCenter(true)),
                "Re-entering Binary True must restart its animation at frame zero.");

            nowMs = 10_730;
            _ = RenderCenter(null);
            nowMs = 10_900;
            Assert(IsRed(RenderCenter(true)),
                "Unavailable Binary state must clear playback so the next activation restarts at frame zero.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void StateImagePropertyVisibility()
    {
        var widget = new C.BinaryWidgetDefinition
        {
            Id = "binary-state-image-properties",
            Metric = FixtureOutputFps,
            Width = 96f,
            Height = 96f
        };
        TextOverflowStateContract.EnsureProfiles(widget);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var trueColorName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Color);
        var falseColorName = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Color);
        var trueFitName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Fit);
        var trueLoopName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Loop);

        var iconProperties = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        Assert(GetNestedProperty(iconProperties, "State: True", trueColorName) is not null &&
               GetNestedProperty(iconProperties, "State: False", falseColorName) is not null &&
               GetNestedProperty(iconProperties, "State: True", trueFitName) is not null &&
               GetNestedProperty(iconProperties, "State: True", trueLoopName) is not null,
            "Icon-backed Binary state visuals must keep Color/Fit/Loop rows available for contextual capability state.");

        var trueProfile = widget.Profiles[BinarySignalContract.TrueKey];
        trueProfile.Asset.ChangeSourceType(C.ImageAssetSourceType.File);
        trueProfile.Asset.Source = "state.png";
        var fileProperties = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        var fileColor = GetNestedProperty(fileProperties, "State: True", trueColorName);
        Assert(fileColor is { IsReadOnly: true } &&
               GetNestedProperty(fileProperties, "State: True", trueFitName) is not null &&
               GetNestedProperty(fileProperties, "State: True", trueLoopName) is not null,
            "File-backed Binary state visuals must keep Fit/Loop while Color remains visible but disabled by Source Type.");
        Assert(GetNestedProperty(fileProperties, "State: False", falseColorName) is not null,
            "Changing one state to a file source must not remove the sibling state's Color row.");

        trueProfile.ContentType = C.StateContentType.Value;
        var valueImageProperties = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        var valueTextProperties = new EditorPropertyView(widget, section: EditorPropertySection.Text).GetProperties();
        Assert(valueImageProperties["State: True"] is null &&
               GetNestedProperty(valueTextProperties, "State: True", "Foreground", trueColorName) is not null,
            "Value-backed Binary states must move Color to Text presentation and omit image-only presentation rows.");
    }

    private static void BinaryValueStateVisual()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-value-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "binary-value",
                Type = WidgetTypeContract.Binary,
                X = 2,
                Y = 30,
                Width = 116,
                Height = 40,
                Metric = metric,
                FontSize = 24,
                Align = "left",
                VerticalAlign = "baseline",
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();

            var trueProfile = widget.Profiles![BinarySignalContract.TrueKey];
            trueProfile.SourceType = StateVisualProfileContract.SourceValue;
            trueProfile.Source = "must-not-be-loaded.png";
            trueProfile.Color = "#FFFF0000";
            trueProfile.Opacity = 1f;

            var falseProfile = widget.Profiles![BinarySignalContract.FalseKey];
            falseProfile.SourceType = StateVisualProfileContract.SourceValue;
            falseProfile.Source = "must-not-be-loaded-either.png";
            falseProfile.Color = "#FF0000FF";
            falseProfile.Opacity = 0.5f;

            foreach (var text in new[] { trueProfile.Text!, falseProfile.Text! })
            {
                text.FontSize = 24f;
                text.Align = "left";
                text.VerticalAlign = "baseline";
                text.OverflowMode = ValueOverflowContract.Clip;
            }

            using var images = new ImageAssetCache(tempRoot);
            images.Preload(new[] { CurrentWidget(widget) });
            using var icons = new IconAssetCache(tempRoot);
            icons.Preload(new[] { CurrentWidget(widget) });
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 42d },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(120, 80))
                ?? throw new InvalidOperationException("Could not create Binary value regression surface.");
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons);

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();
            using (var pixels = surface.PeekPixels()
                   ?? throw new InvalidOperationException("Could not inspect Binary true-value pixels."))
            {
                var sawRed = false;
                for (var y = 0; y < pixels.Height && !sawRed; y++)
                {
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        var color = pixels.GetPixelColor(x, y);
                        if (color.Alpha > 0 && color.Red > 150 && color.Green < 100 && color.Blue < 100)
                        {
                            sawRed = true;
                            break;
                        }
                    }
                }

                Assert(sawRed, "Binary value visual did not render the metric text with the True-state color.");
            }

            context.UpdateMetrics(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 0d });
            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();
            using (var pixels = surface.PeekPixels()
                   ?? throw new InvalidOperationException("Could not inspect Binary false-value pixels."))
            {
                var sawHalfBlue = false;
                for (var y = 0; y < pixels.Height && !sawHalfBlue; y++)
                {
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        var color = pixels.GetPixelColor(x, y);
                        if (color.Blue > 150 && color.Red < 100 && color.Green < 100 &&
                            color.Alpha is >= 100 and <= 160)
                        {
                            sawHalfBlue = true;
                            break;
                        }
                    }
                }

                Assert(sawHalfBlue,
                    "Binary value visual did not apply the False-state color and opacity after a state transition.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryValueContainerLayout()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-value-layout-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "binary-value-layout",
                Type = WidgetTypeContract.Binary,
                X = 10,
                Y = 20,
                Width = 80,
                Height = 40,
                Metric = metric,
                FontSize = 24,
                Align = "center",
                VerticalAlign = "middle",
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();
            widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
            widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceIcon;
            var trueText = GetBinaryText(widget, BinarySignalContract.TrueKey);
            trueText.FontSize = 24f;
            trueText.Align = "center";
            trueText.VerticalAlign = "middle";
            trueText.OverflowMode = ValueOverflowContract.Clip;
            widget.Validate(tempRoot);

            using var images = new ImageAssetCache(tempRoot);
            using var icons = new IconAssetCache(tempRoot);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 42d },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(120, 80))
                ?? throw new InvalidOperationException("Could not create Binary container-layout surface.");
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons);

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect Binary container-layout pixels.");
            var minY = int.MaxValue;
            var maxY = int.MinValue;
            for (var y = 0; y < pixels.Height; y++)
            {
                for (var x = 0; x < pixels.Width; x++)
                {
                    if (pixels.GetPixelColor(x, y).Alpha == 0)
                        continue;
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }

            Assert(minY != int.MaxValue, "Binary value visual rendered no text pixels.");
            Assert(minY >= 20 && maxY < 60,
                $"Binary middle-aligned value escaped its 40px container: y={minY}..{maxY}.");
            var pixelCenter = (minY + maxY) / 2.0;
            Assert(Math.Abs(pixelCenter - 40d) <= 4d,
                $"Binary middle-aligned value is not centered in the widget: pixel center={pixelCenter:0.0}, expected ~40.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryValueContainerHeightClip()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-value-height-clip-regression");
        try
        {
            foreach (var width in new[] { 80f })
            {
                var widget = new WidgetDefinition
                {
                    Id = $"binary-value-height-clip-{width}",
                    Type = WidgetTypeContract.Binary,
                    X = 10,
                    Y = 20,
                    Width = width,
                    Height = 10,
                    Metric = metric,
                    FontSize = 28,
                    Align = "left",
                    VerticalAlign = "middle",
                    OverflowMode = ValueOverflowContract.Clip
                };
                widget.EnsureStateVisualProfiles();
                widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
                widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceIcon;
                var trueText = GetBinaryText(widget, BinarySignalContract.TrueKey);
                trueText.FontSize = 28f;
                trueText.Align = "left";
                trueText.VerticalAlign = "middle";
                trueText.OverflowMode = ValueOverflowContract.Clip;
                widget.Validate(tempRoot);

                using var images = new ImageAssetCache(tempRoot);
                using var icons = new IconAssetCache(tempRoot);
                using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
                using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 42d },
                    antialias: false,
                    typefaces);
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(120, 60))
                    ?? throw new InvalidOperationException("Could not create Binary height-clip surface.");
                using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons);

                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
                surface.Canvas.Flush();

                using var pixels = surface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect Binary height-clip pixels.");
                for (var y = 0; y < pixels.Height; y++)
                {
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        if (pixels.GetPixelColor(x, y).Alpha == 0)
                            continue;

                        Assert(y >= 20 && y < 30,
                            $"Binary value text escaped explicit Height=10 at y={y} (Width={width}).");
                    }
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryValueAutoWidthGeometry()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-value-autowidth-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "binary-value-autowidth",
                Type = WidgetTypeContract.Binary,
                X = 12,
                Y = 18,
                Width = 0,
                Height = 40,
                Metric = metric,
                FontSize = 24,
                Align = "left",
                VerticalAlign = "middle"
            };
            widget.EnsureStateVisualProfiles();
            widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
            widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;
            foreach (var stateKey in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
            {
                var text = GetBinaryText(widget, stateKey);
                text.FontSize = 24f;
                text.Align = "left";
                text.VerticalAlign = "middle";
                text.OverflowMode = ValueOverflowContract.None;
            }

            widget.Validate(tempRoot);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 42d },
                antialias: false,
                typefaces);
            var bounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);

            Assert(bounds.Width > 1f,
                $"Binary value Width=0 did not resolve intrinsic width; got {bounds.Width}.");
            Assert(bounds.Height > 1f && Math.Abs(bounds.Height - 40f) > 1f,
                $"Value-only Binary must use content-driven height instead of dormant Height=40; got {bounds.Height}.");
            Assert(Math.Abs(bounds.Left - 12f) < 0.001f && Math.Abs(bounds.Top - 18f) < 0.001f,
                "Binary value auto geometry changed the top-left container origin.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryValueOnlyGeometryValidation()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-value-validation-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "binary-value-validation",
                Type = WidgetTypeContract.Binary,
                X = 10,
                Y = 20,
                Width = 80,
                Height = 0,
                Metric = metric,
                FontSize = 24,
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();
            widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
            widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;
            foreach (var stateKey in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
                GetBinaryText(widget, stateKey).OverflowMode = ValueOverflowContract.Clip;

            // This is the geometry that previously made Ctrl+D fail in clone.Validate().
            widget.Validate(tempRoot);

            // Value-only Binary also supports intrinsic Width=0, matching the established
            // Binary value auto-width contract while keeping Height content-driven.
            widget.Width = 0;
            foreach (var stateKey in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
                GetBinaryText(widget, stateKey).OverflowMode = ValueOverflowContract.None;
            widget.Validate(tempRoot);

            widget.Width = 80;
            foreach (var stateKey in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
                GetBinaryText(widget, stateKey).OverflowMode = ValueOverflowContract.Clip;
            widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceIcon;
            var rejectedMixedAutoGeometry = false;
            try
            {
                widget.Validate(tempRoot);
            }
            catch (InvalidDataException)
            {
                rejectedMixedAutoGeometry = true;
            }

            Assert(rejectedMixedAutoGeometry,
                "Mixed Binary icon/value states must require explicit Width and Height.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryValuePropertyVisibility()
    {
        var widget = new C.BinaryWidgetDefinition
        {
            Id = "binary-value-properties",
            Metric = SystemMetricContract.Uptime,
            Width = 100f,
            Height = 40f
        };
        TextOverflowStateContract.EnsureProfiles(widget);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var trueSourceTypeName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.SourceType);
        var trueSourceName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Source);
        var trueColorName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Color);
        var trueFontName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.FontFamily);
        var trueOverflowName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.OverflowMode);

        var iconAppearance = new EditorPropertyView(widget, section: EditorPropertySection.Appearance).GetProperties();
        var iconText = new EditorPropertyView(widget, section: EditorPropertySection.Text).GetProperties();
        var iconStates = new EditorPropertyView(widget, section: EditorPropertySection.States).GetProperties();
        Assert(GetNestedProperty(iconAppearance, "Foreground", nameof(C.WidgetDefinition.Color)) is { DisplayName: "Color" } &&
               iconText.Count == 0,
            "Icon-only Binary must expose universal Foreground fallback while hiding Text presentation.");
        Assert(GetNestedProperty(iconStates, "State: True", trueSourceName) is { IsReadOnly: false },
            "Icon-backed Binary state must expose editable Source.");

        var sourceType = GetNestedProperty(iconStates, "State: True", trueSourceTypeName)
            ?? throw new InvalidOperationException("Binary state Source Type property is missing.");
        var values = sourceType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(values.Any(value => string.Equals(value, "Value", StringComparison.OrdinalIgnoreCase)),
            "Binary state Source Type must offer Value.");

        var trueProfile = widget.Profiles[BinarySignalContract.TrueKey];
        trueProfile.ContentType = C.StateContentType.Value;
        TextOverflowStateContract.ApplyContextChange(widget, 100f, 32f);

        var mixedStates = new EditorPropertyView(widget, section: EditorPropertySection.States).GetProperties();
        var mixedTrueSource = GetNestedProperty(mixedStates, "State: True", trueSourceName);
        Assert(mixedTrueSource is { IsReadOnly: true } &&
               mixedTrueSource.Description == "Not applicable for selected Source Type.",
            "Value-backed Binary state must retain Source as a stable disabled row.");

        var falseFitName = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Fit);
        var mixedImage = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        Assert(GetNestedProperty(mixedImage, "State: False", falseFitName) is { DisplayName: "Fit" } &&
               mixedImage["State: True"] is null,
            "Mixed Binary value/icon states must retain image presentation only for the image-backed state.");

        var mixedGeneral = new EditorPropertyView(widget, section: EditorPropertySection.General).GetProperties();
        var mixedHeight = GetNestedProperty(mixedGeneral, "Geometry", nameof(C.WidgetDefinition.Height));
        Assert(mixedHeight is { IsReadOnly: false },
            "Mixed Binary value/icon states must expose editable Height.");

        var mixedText = new EditorPropertyView(widget, section: EditorPropertySection.Text).GetProperties();
        Assert(mixedText["State: True"] is PropertyGroupPropertyDescriptor && mixedText["State: False"] is null,
            "Mixed Binary must expose Text presentation only for its value-backed state.");
        Assert(GetNestedProperty(mixedText, "State: True", "Foreground", trueColorName) is { DisplayName: "Color" } &&
               GetNestedProperty(mixedText, "State: True", "Font", trueFontName) is { DisplayName: "Family" } &&
               GetNestedProperty(mixedText, "State: True", "Alignment",
                   StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Align)) is { DisplayName: "Horizontal" } &&
               GetNestedProperty(mixedText, "State: True", "Overflow", trueOverflowName) is { DisplayName: "Mode" },
            "Binary value state must expose canonical state TextPresentation groups.");

        var mixedData = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        Assert(GetNestedProperty(mixedData, "Value", "SourceUnit") is { IsReadOnly: true } &&
               GetNestedProperty(mixedData, "Value", nameof(C.BinaryWidgetDefinition.Unit)) is not null &&
               GetNestedProperty(mixedData, "Value", nameof(C.BinaryWidgetDefinition.Format)) is not null &&
               GetNestedProperty(mixedData, "Display", nameof(C.BinaryWidgetDefinition.Prefix)) is not null &&
               GetNestedProperty(mixedData, "Display", nameof(C.BinaryWidgetDefinition.Suffix)) is not null &&
               GetNestedProperty(mixedData, "Display", nameof(C.BinaryWidgetDefinition.Fallback)) is not null &&
               GetNestedProperty(mixedData, "Evaluation", nameof(C.BinaryWidgetDefinition.EvaluationMode)) is { DisplayName: "Mode" },
            "Binary value mode must expose Value, Display, and Evaluation groups on Data.");

        widget.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Value;
        TextOverflowStateContract.ApplyContextChange(widget, 100f, 32f);
        var valueOnlyImage = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        var valueOnlyText = new EditorPropertyView(widget, section: EditorPropertySection.Text).GetProperties();
        var valueOnlyGeneral = new EditorPropertyView(widget, section: EditorPropertySection.General).GetProperties();
        var valueOnlyHeight = GetNestedProperty(valueOnlyGeneral, "Geometry", nameof(C.WidgetDefinition.Height));
        Assert(valueOnlyHeight is { IsReadOnly: true },
            "Value-only Binary must expose disabled derived Height.");
        Assert(valueOnlyImage.Count == 0 &&
               valueOnlyText["State: True"] is not null &&
               valueOnlyText["State: False"] is not null,
            "Value-only Binary must move both state presentations entirely to Text.");
    }

    private static void BinaryValueUnitOptions()
    {
        const string provider = "binary-unit-test";
        const string metric = provider + ".download";
        MetricContract.ReplaceProviderDescriptors(
            provider,
            [new MetricDescriptor(metric, MetricValueKind.Number, MetricUnit.BytesPerSecond)]);

        var widget = new C.BinaryWidgetDefinition
        {
            Id = "binary-value-units",
            Metric = metric,
            Width = 100f,
            Height = 40f,
            EvaluationMode = BinarySignalContract.EvaluationModeAuto
        };
        TextOverflowStateContract.EnsureProfiles(widget);
        widget.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Value;
        widget.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Value;
        TextOverflowStateContract.ApplyContextChange(widget, 100f, 32f);

        var data = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        var unit = GetNestedProperty(data, "Value", nameof(C.BinaryWidgetDefinition.Unit))
            ?? throw new InvalidOperationException("Binary value Unit property is missing for a data-rate metric.");
        var unitValues = unit.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(unitValues.Any(value => value!.StartsWith("Mbit/s", StringComparison.Ordinal)),
            "Binary value Unit selector must expose Mbit/s for a byte-rate metric.");
        Assert(unitValues.Any(value => value!.StartsWith("MiB/s", StringComparison.Ordinal)),
            "Binary value Unit selector must expose MiB/s for a byte-rate metric.");

        var fixedText = new EditorPropertyView(widget, section: EditorPropertySection.Text).GetProperties();
        Assert(GetNestedProperty(
                   fixedText, "State: True", "Overflow",
                   $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.OverflowMode}") is not null &&
               GetNestedProperty(
                   fixedText, "State: False", "Overflow",
                   $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.FalseKey}:{StateVisualProfileField.OverflowMode}") is not null,
            "Fixed-width Binary value must expose per-state Overflow Mode.");

        widget.Width = 0;
        TextOverflowStateContract.NormalizeStoredState(widget);
        Assert(string.Equals(widget.Profiles[BinarySignalContract.TrueKey].TextPresentation.OverflowMode, ValueOverflowContract.None, StringComparison.Ordinal) &&
               string.Equals(widget.Profiles[BinarySignalContract.FalseKey].TextPresentation.OverflowMode, ValueOverflowContract.None, StringComparison.Ordinal),
            "Auto-width Binary value must normalize both state Overflow modes to None.");
    }

    private static void MetricUnitOptionsFollowMetricChanges()
    {
        const string provider = "unit-refresh-test";
        const string sizeMetric = provider + ".size";
        const string rateMetric = provider + ".rate";
        MetricContract.ReplaceProviderDescriptors(
            provider,
            [
                new MetricDescriptor(sizeMetric, MetricValueKind.DataSize, MetricUnit.Bytes),
                new MetricDescriptor(rateMetric, MetricValueKind.Number, MetricUnit.BytesPerSecond)
            ]);

        var widget = new C.BinaryWidgetDefinition
        {
            Id = "binary-dynamic-unit-options",
            Metric = sizeMetric,
            Width = 100f,
            Height = 40f,
            EvaluationMode = BinarySignalContract.EvaluationModeAuto
        };
        TextOverflowStateContract.EnsureProfiles(widget);
        widget.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Value;
        widget.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Value;

        var propertyView = new EditorPropertyView(widget, section: EditorPropertySection.Data);
        var unit = GetNestedProperty(propertyView.GetProperties(), "Value", nameof(C.BinaryWidgetDefinition.Unit))
            ?? throw new InvalidOperationException("Binary value Unit property is missing.");

        var initialValues = unit.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(initialValues.Contains("MB", StringComparer.Ordinal),
            "Data-size metric must initially expose data-size units.");
        Assert(!initialValues.Any(value => value!.StartsWith("Mbit/s", StringComparison.Ordinal)),
            "Data-size metric must not expose data-rate units.");

        // Reuse the exact same canonical descriptor instance: Unit choices must follow the
        // live Metric instead of retaining the converter options from descriptor creation.
        widget.Metric = rateMetric;

        var updatedValues = unit.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(updatedValues.Any(value => value!.StartsWith("Mbit/s", StringComparison.Ordinal)),
            "Existing Binary Unit descriptor must follow a metric change to data-rate units.");
        Assert(updatedValues.Any(value => value!.StartsWith("MiB/s", StringComparison.Ordinal)),
            "Existing Binary Unit descriptor must expose binary data-rate units after the metric changes.");
        Assert(!updatedValues.Contains("MB", StringComparer.Ordinal),
            "Existing Binary Unit descriptor must not retain stale data-size unit choices.");
    }

    private static void BinaryStateImageSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-image-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "binary-image-schema.json");

            static string ProfilesJson(bool includeFitLoop) => includeFitLoop
                ? """
                  "profiles": {
                    "true":  { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 1.0, "fit": "contain", "loop": false, "text": { "fontFamily": "Roboto", "fontSize": 32, "fontWeight": 400, "italic": false, "align": "left", "verticalAlign": "baseline", "outlineWidth": 0, "outlineColor": "#FF000000", "overflowMode": "Clip", "scrollSpeed": 0, "bumpPauseMs": 500 } },
                    "false": { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 0.5, "fit": "cover", "loop": true, "text": { "fontFamily": "Roboto", "fontSize": 32, "fontWeight": 400, "italic": false, "align": "left", "verticalAlign": "baseline", "outlineWidth": 0, "outlineColor": "#FF000000", "overflowMode": "Clip", "scrollSpeed": 0, "bumpPauseMs": 500 } }
                  }
                  """
                : """
                  "profiles": {
                    "true":  { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 1.0, "text": { "fontFamily": "Roboto", "fontSize": 32, "fontWeight": 400, "italic": false, "align": "left", "verticalAlign": "baseline", "outlineWidth": 0, "outlineColor": "#FF000000", "overflowMode": "Clip", "scrollSpeed": 0, "bumpPauseMs": 500 } },
                    "false": { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 0.5, "text": { "fontFamily": "Roboto", "fontSize": 32, "fontWeight": 400, "italic": false, "align": "left", "verticalAlign": "baseline", "outlineWidth": 0, "outlineColor": "#FF000000", "overflowMode": "Clip", "scrollSpeed": 0, "bumpPauseMs": 500 } }
                  }
                  """;

            var canonical = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "binary",
                      "id": "binary-schema11",
                      "width": 96,
                      "height": 96,
                      "metric": "{{FixtureOutputFps}}",
                      {{ProfilesJson(includeFitLoop: true)}}
                    }
                  ]
                }
                """,
                dashboardPath);

            var binary = canonical.Widgets.Single();
            Assert(binary.Profiles![BinarySignalContract.TrueKey].Fit == StateVisualProfileContract.FitContain &&
                   binary.Profiles[BinarySignalContract.TrueKey].Loop == false &&
                   binary.Profiles[BinarySignalContract.FalseKey].Fit == StateVisualProfileContract.FitCover &&
                   binary.Profiles[BinarySignalContract.FalseKey].Loop == true,
                "Schema 11 Binary must deserialize independent per-state Fit/Loop values.");

            var serializedProfiles = JsonSerializer.SerializeToNode(binary.Profiles) as JsonObject
                ?? throw new InvalidOperationException("Could not serialize Binary state profiles.");
            foreach (var key in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
            {
                var profile = serializedProfiles[key] as JsonObject
                    ?? throw new InvalidOperationException($"Serialized Binary profile '{key}' is missing.");
                Assert(profile.ContainsKey("fit") && profile.ContainsKey("loop"),
                    "Schema 11 Binary profile serialization must persist Fit/Loop explicitly.");
            }

            var newBinary = new WidgetDefinition { Type = WidgetTypeContract.Binary };
            newBinary.EnsureStateVisualProfiles();
            foreach (var profile in newBinary.Profiles!.Values)
            {
                Assert(profile.Fit == StateVisualProfileContract.FitContain && profile.Loop == true,
                    "New schema 11 Binary profiles must default to Fit=contain and Loop=true.");
            }

            var power = new WidgetDefinition { Type = WidgetTypeContract.Power };
            power.EnsureStateVisualProfiles();
            var serializedPowerProfiles = JsonSerializer.SerializeToNode(power.Profiles) as JsonObject
                ?? throw new InvalidOperationException("Could not serialize Power state profiles.");
            Assert(serializedPowerProfiles.All(pair =>
                       pair.Value is JsonObject profile &&
                       profile["fit"]?.GetValue<string>() == StateVisualProfileContract.FitContain &&
                       profile["loop"]?.GetValue<bool>() == true),
                "Power state profiles must persist standard Fit/Loop presentation in schema 14.");

            var obsoleteTopLevelRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        {
                          "type": "binary",
                          "id": "binary-obsolete-fit-loop",
                          "width": 96,
                          "height": 96,
                          "metric": "{{FixtureOutputFps}}",
                          "fit": "contain",
                          "loop": true,
                          {{ProfilesJson(includeFitLoop: true)}}
                        }
                      ]
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteTopLevelRejected = true;
            }
            Assert(obsoleteTopLevelRejected,
                "Schema 11 must reject obsolete top-level Binary Fit/Loop.");

            var missingProfileImageSettingsRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        {
                          "type": "binary",
                          "id": "binary-missing-state-fit-loop",
                          "width": 96,
                          "height": 96,
                          "metric": "{{FixtureOutputFps}}",
                          {{ProfilesJson(includeFitLoop: false)}}
                        }
                      ]
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                missingProfileImageSettingsRejected = true;
            }
            Assert(missingProfileImageSettingsRejected,
                "Schema 11 Binary profiles must explicitly persist Fit/Loop for both states.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryStateTextSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-text-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "binary-text-schema.json");

            static JsonObject TextNode(
                float size,
                string align,
                string vertical,
                string overflow) => new()
            {
                ["fontFamily"] = "Roboto",
                ["fontSize"] = size,
                ["fontWeight"] = 400,
                ["italic"] = false,
                ["align"] = align,
                ["verticalAlign"] = vertical,
                ["outlineWidth"] = 0f,
                ["outlineColor"] = "#FF000000",
                ["overflowMode"] = overflow,
                ["scrollSpeed"] = 0f,
                ["bumpPauseMs"] = 500
            };

            static JsonObject ProfileNode(
                string sourceType,
                string source,
                float opacity,
                string fit,
                bool loop,
                JsonObject text) => new()
            {
                ["sourceType"] = sourceType,
                ["source"] = source,
                ["color"] = "#FFFFFFFF",
                ["opacity"] = opacity,
                ["fit"] = fit,
                ["loop"] = loop,
                ["text"] = text
            };

            var binaryNode = new JsonObject
            {
                ["type"] = WidgetTypeContract.Binary,
                ["id"] = "binary-state-text-schema",
                ["width"] = 120f,
                ["height"] = 40f,
                ["metric"] = FixtureOutputFps,
                ["profiles"] = new JsonObject
                {
                    [BinarySignalContract.TrueKey] = ProfileNode(
                        StateVisualProfileContract.SourceValue,
                        string.Empty,
                        1f,
                        StateVisualProfileContract.FitContain,
                        true,
                        TextNode(36f, "center", "middle", ValueOverflowContract.Scroll)),
                    [BinarySignalContract.FalseKey] = ProfileNode(
                        StateVisualProfileContract.SourceValue,
                        string.Empty,
                        0.5f,
                        StateVisualProfileContract.FitCover,
                        false,
                        TextNode(18f, "right", "bottom", ValueOverflowContract.Ellipsis))
                }
            };
            var root = new JsonObject
            {
                ["schemaVersion"] = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
                ["canvas"] = new JsonObject
                {
                    ["width"] = FrameGeometry.DefaultNativeWidth,
                    ["height"] = FrameGeometry.DefaultNativeHeight,
                    ["orientation"] = 0,
                    ["backgroundColor"] = "#000000"
                },
                ["widgets"] = new JsonArray(binaryNode)
            };

            var definition = DashboardDefinition.Parse(root.ToJsonString(), dashboardPath);
            var binary = definition.Widgets.Single();
            var trueText = GetBinaryText(binary, BinarySignalContract.TrueKey);
            var falseText = GetBinaryText(binary, BinarySignalContract.FalseKey);
            Assert(trueText.FontSize == 36f &&
                   trueText.Align == "center" &&
                   trueText.VerticalAlign == "middle" &&
                   trueText.OverflowMode == ValueOverflowContract.Scroll &&
                   falseText.FontSize == 18f &&
                   falseText.Align == "right" &&
                   falseText.VerticalAlign == "bottom" &&
                   falseText.OverflowMode == ValueOverflowContract.Ellipsis,
                "Schema 11 Binary must deserialize independent per-state Text presentation.");

            var serializedProfiles = JsonSerializer.SerializeToNode(binary.Profiles) as JsonObject
                ?? throw new InvalidOperationException("Could not serialize Binary text profiles.");
            foreach (var key in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
            {
                var profile = serializedProfiles[key] as JsonObject
                    ?? throw new InvalidOperationException($"Serialized Binary profile '{key}' is missing.");
                var text = profile["text"] as JsonObject
                    ?? throw new InvalidOperationException($"Serialized Binary profile '{key}' Text is missing.");
                foreach (var property in new[]
                {
                    "fontFamily", "fontSize", "fontWeight", "italic", "align", "verticalAlign",
                    "outlineWidth", "outlineColor", "overflowMode", "scrollSpeed", "bumpPauseMs"
                })
                {
                    Assert(text.ContainsKey(property),
                        $"Schema 11 Binary Text serialization is missing '{property}' in state '{key}'.");
                }
            }

            var newBinary = new WidgetDefinition { Type = WidgetTypeContract.Binary };
            newBinary.EnsureStateVisualProfiles();
            Assert(newBinary.Profiles!.Values.All(profile => profile.Text is not null),
                "New Binary profiles must include Text presentation for both states.");

            var power = new WidgetDefinition { Type = WidgetTypeContract.Power };
            power.EnsureStateVisualProfiles();
            Assert(power.Profiles!.Values.All(profile => profile.Text is not null),
                "Power state profiles must include latent Text presentation for Source Type=value.");

            var obsolete = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Binary text schema document.");
            var obsoleteWidget = ((obsolete["widgets"] as JsonArray)![0] as JsonObject)!;
            obsoleteWidget["fontFamily"] = "Roboto";
            var obsoleteRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(obsolete.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteRejected = true;
            }
            Assert(obsoleteRejected,
                "Schema 11 must reject obsolete top-level Binary text presentation.");

            var missingText = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Binary text schema document.");
            var missingWidget = ((missingText["widgets"] as JsonArray)![0] as JsonObject)!;
            var missingProfiles = (missingWidget["profiles"] as JsonObject)!;
            ((missingProfiles[BinarySignalContract.FalseKey] as JsonObject)!).Remove("text");
            var missingRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(missingText.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                missingRejected = true;
            }
            Assert(missingRejected,
                "Schema 11 Binary profiles must explicitly persist Text presentation for both states.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryStateTextIndependence()
    {
        var widget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "binary-state-text-independence",
            Metric = FixtureOutputFps,
            Width = 120f,
            Height = 40f
        };
        widget.EnsureStateVisualProfiles();
        widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
        widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;
        TextOverflowStateContract.ApplyContextChange(widget, 120f, 32f);
        var currentWidget = (C.BinaryWidgetDefinition)CurrentWidget(widget);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var textProperties = new EditorPropertyView(
            currentWidget,
            section: EditorPropertySection.Text).GetProperties();

        var trueFont = GetNestedProperty(
            textProperties, "State: True", "Font",
            StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.FontSize))
            ?? throw new InvalidOperationException("Binary True Font Size property is missing.");
        var falseFont = GetNestedProperty(
            textProperties, "State: False", "Font",
            StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.FontSize))
            ?? throw new InvalidOperationException("Binary False Font Size property is missing.");
        var trueAlign = GetNestedProperty(
            textProperties, "State: True", "Alignment",
            StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Align))
            ?? throw new InvalidOperationException("Binary True Alignment property is missing.");
        var falseAlign = GetNestedProperty(
            textProperties, "State: False", "Alignment",
            StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Align))
            ?? throw new InvalidOperationException("Binary False Alignment property is missing.");
        var trueOverflow = GetNestedProperty(
            textProperties, "State: True", "Overflow",
            StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.OverflowMode))
            ?? throw new InvalidOperationException("Binary True Overflow property is missing.");
        var falseOverflow = GetNestedProperty(
            textProperties, "State: False", "Overflow",
            StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.OverflowMode))
            ?? throw new InvalidOperationException("Binary False Overflow property is missing.");

        trueFont.SetValue(null, 46f);
        falseFont.SetValue(null, 18f);
        trueAlign.SetValue(null, "center");
        falseAlign.SetValue(null, "right");
        trueOverflow.SetValue(null, ValueOverflowContract.Scroll);
        falseOverflow.SetValue(null, ValueOverflowContract.Ellipsis);

        var trueText = currentWidget.Profiles[BinarySignalContract.TrueKey].TextPresentation;
        var falseText = currentWidget.Profiles[BinarySignalContract.FalseKey].TextPresentation;
        Assert(trueText.FontSize == 46f && falseText.FontSize == 18f &&
               trueText.Align == "center" && falseText.Align == "right" &&
               trueText.OverflowMode == ValueOverflowContract.Scroll &&
               falseText.OverflowMode == ValueOverflowContract.Ellipsis,
            "Binary True/False Text presentation must be independently editable.");

        var refreshed = new EditorPropertyView(
            currentWidget,
            section: EditorPropertySection.Text).GetProperties();
        var trueScrollSpeed = GetNestedProperty(
            refreshed, "State: True", "Overflow",
            StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.ScrollSpeed));
        var falseScrollSpeed = GetNestedProperty(
            refreshed, "State: False", "Overflow",
            StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.ScrollSpeed));
        Assert(trueScrollSpeed is { IsReadOnly: false } &&
               falseScrollSpeed is { IsReadOnly: true } &&
               falseScrollSpeed.Description == "Not applicable for selected Overflow Mode.",
            "Mode-specific Binary Text controls must remain stable and become context-disabled per state.");


        var currentFalse = currentWidget.Profiles[BinarySignalContract.FalseKey];
        currentFalse.ContentType = C.StateContentType.Image;
        currentFalse.Asset.ChangeSourceType(C.ImageAssetSourceType.Icon);
        currentFalse.Asset.Source = "lucide:check";
        var mixedText = new EditorPropertyView(
            currentWidget,
            section: EditorPropertySection.Text).GetProperties();
        var mixedImage = new EditorPropertyView(
            currentWidget,
            section: EditorPropertySection.Image).GetProperties();
        Assert(mixedText["State: True"] is not null && mixedText["State: False"] is null,
            "Mixed Binary must hide Text presentation for the image-backed state.");
        Assert(mixedImage["State: True"] is null && mixedImage["State: False"] is not null,
            "Mixed Binary must expose Image presentation for the image-backed state.");
    }

    private static void BinaryStateTextRendering()
    {
        const string metric = FixtureOutputFps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-state-text-rendering");
        try
        {
            var widget = new WidgetDefinition
            {
                Type = WidgetTypeContract.Binary,
                Id = "binary-state-text-rendering",
                Metric = metric,
                X = 10f,
                Y = 10f,
                Width = 0f,
                Height = 0f,
                // Deliberately unrelated legacy widget-level values: Binary rendering must ignore them.
                FontSize = 96f,
                Align = "right",
                VerticalAlign = "bottom",
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();
            widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
            widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;

            var trueText = GetBinaryText(widget, BinarySignalContract.TrueKey);
            trueText.FontSize = 40f;
            trueText.Align = "left";
            trueText.VerticalAlign = "top";
            trueText.OverflowMode = ValueOverflowContract.None;

            var falseText = GetBinaryText(widget, BinarySignalContract.FalseKey);
            falseText.FontSize = 14f;
            falseText.Align = "left";
            falseText.VerticalAlign = "top";
            falseText.OverflowMode = ValueOverflowContract.None;

            widget.Validate(tempRoot);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 42d },
                antialias: false,
                typefaces);

            var trueBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);
            context.UpdateMetrics(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 0d });
            var falseBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);

            Assert(trueBounds.Height > falseBounds.Height * 1.8f,
                $"Binary geometry did not follow active state Font Size: true={trueBounds.Height}, false={falseBounds.Height}.");
            Assert(trueBounds.Height < 80f && falseBounds.Height < 40f,
                "Binary geometry appears to be using obsolete widget-level Font Size instead of state Text profiles.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinarySchema10Migration()
    {
        var root = JsonNode.Parse(
            $$"""
            {
              "schemaVersion": 10,
              "canvas": { "orientation": 0, "backgroundColor": "#000000" },
              "widgets": [
                {
                  "type": "binary",
                  "id": "binary-explicit-legacy-image-settings",
                  "width": 96,
                  "height": 96,
                  "metric": "{{FixtureOutputFps}}",
                  "fit": "CoVeR",
                  "loop": false,
                  "fontFamily": "Roboto",
                  "fontSize": 27,
                  "fontWeight": 700,
                  "italic": true,
                  "align": "center",
                  "verticalAlign": "middle",
                  "outlineWidth": 2,
                  "outlineColor": "#FF112233",
                  "overflowMode": "Clip",
                  "scrollSpeed": 33,
                  "bumpPauseMs": 900,
                  "profiles": {
                    "true":  { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 1.0 },
                    "false": { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 0.5 }
                  }
                },
                {
                  "type": "binary",
                  "id": "binary-fit-only-legacy-image-settings",
                  "width": 96,
                  "height": 96,
                  "metric": "{{FixtureOutputFps}}",
                  "fit": "contain"
                },
                {
                  "type": "binary",
                  "id": "binary-implicit-legacy-image-settings",
                  "width": 96,
                  "height": 96,
                  "metric": "{{FixtureOutputFps}}"
                },
                {
                  "type": "image",
                  "id": "image-global-fit-loop-preserved",
                  "width": 96,
                  "height": 96,
                  "sourceType": "icon",
                  "source": "lucide:check",
                  "fit": "cover",
                  "loop": false
                }
              ]
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 10 migration test document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 10 dashboard must report a migration to the current schema.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Schema 10 migration must end at current schema 21.");

        var widgets = root["widgets"] as JsonArray
            ?? throw new InvalidOperationException("Migrated dashboard widgets are missing.");
        var explicitWidget = widgets[0] as JsonObject
            ?? throw new InvalidOperationException("Explicit legacy Binary is missing.");
        var fitOnlyWidget = widgets[1] as JsonObject
            ?? throw new InvalidOperationException("Fit-only legacy Binary is missing.");
        var implicitWidget = widgets[2] as JsonObject
            ?? throw new InvalidOperationException("Implicit legacy Binary is missing.");
        var imageWidget = widgets[3] as JsonObject
            ?? throw new InvalidOperationException("Legacy Image widget is missing.");

        static JsonObject Profile(JsonObject widget, string key) =>
            ((widget["profiles"] as JsonObject)?[key] as JsonObject)
            ?? throw new InvalidOperationException($"Migrated Binary profile '{key}' is missing.");

        Assert(!explicitWidget.ContainsKey("fit") && !explicitWidget.ContainsKey("loop") &&
               !explicitWidget.ContainsKey("fontFamily") && !explicitWidget.ContainsKey("fontSize") &&
               !explicitWidget.ContainsKey("fontWeight") && !explicitWidget.ContainsKey("italic") &&
               !explicitWidget.ContainsKey("align") && !explicitWidget.ContainsKey("verticalAlign") &&
               !explicitWidget.ContainsKey("outlineWidth") && !explicitWidget.ContainsKey("outlineColor") &&
               !explicitWidget.ContainsKey("overflowMode") && !explicitWidget.ContainsKey("scrollSpeed") &&
               !explicitWidget.ContainsKey("bumpPauseMs"),
            "Schema 10 migration must remove obsolete top-level Binary image and text presentation.");

        foreach (var key in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
        {
            var profile = Profile(explicitWidget, key);
            var asset = profile["asset"] as JsonObject
                ?? throw new InvalidOperationException($"Migrated Binary profile '{key}' asset is missing.");
            var text = profile["textPresentation"] as JsonObject
                ?? throw new InvalidOperationException($"Migrated Binary profile '{key}' TextPresentation is missing.");
            Assert(profile["contentType"]?.GetValue<string>() == C.StateContentType.Image &&
                   asset["fit"]?.GetValue<string>() == StateVisualProfileContract.FitCover &&
                   asset["loop"]?.GetValue<bool>() == false,
                "Schema 10 migration must preserve explicit legacy Fit/Loop semantically in canonical state assets.");
            Assert(text["fontFamily"]!.GetValue<string>() == "Roboto" &&
                   Math.Abs(text["fontSize"]!.GetValue<float>() - 27f) < 0.001f &&
                   text["fontWeight"]!.GetValue<int>() == 700 &&
                   text["italic"]!.GetValue<bool>() &&
                   text["align"]!.GetValue<string>() == "center" &&
                   text["verticalAlign"]!.GetValue<string>() == "middle" &&
                   Math.Abs(text["outlineWidth"]!.GetValue<float>() - 2f) < 0.001f &&
                   text["outlineColor"]!.GetValue<string>() == "#FF112233" &&
                   text["overflowMode"]!.GetValue<string>() == ValueOverflowContract.Clip &&
                   Math.Abs(text["scrollSpeed"]!.GetValue<float>() - 33f) < 0.001f &&
                   text["bumpPauseMs"]!.GetValue<int>() == 900,
                "Schema 10 migration must preserve legacy global Text presentation in canonical state TextPresentation.");
        }

        foreach (var key in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
        {
            var profile = Profile(fitOnlyWidget, key);
            var asset = profile["asset"] as JsonObject
                ?? throw new InvalidOperationException($"Migrated fit-only Binary profile '{key}' asset is missing.");
            Assert(asset["fit"]?.GetValue<string>() == StateVisualProfileContract.FitContain &&
                   asset["loop"]?.GetValue<bool>() == true &&
                   profile["textPresentation"] is JsonObject,
                "Schema 10 migration must preserve explicit Fit, runtime Loop=true, and canonical TextPresentation.");
        }

        foreach (var key in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
        {
            var profile = Profile(implicitWidget, key);
            var asset = profile["asset"] as JsonObject
                ?? throw new InvalidOperationException($"Migrated implicit Binary profile '{key}' asset is missing.");
            Assert(asset["fit"]?.GetValue<string>() == StateVisualProfileContract.FitStretch &&
                   asset["loop"]?.GetValue<bool>() == true &&
                   profile["textPresentation"] is JsonObject,
                "Schema 10 migration must preserve runtime image defaults and create canonical TextPresentation.");
        }

        var imageAsset = imageWidget["asset"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Image asset is missing.");
        Assert(imageAsset["fit"]?.GetValue<string>() == StateVisualProfileContract.FitCover &&
               imageAsset["loop"]?.GetValue<bool>() == false &&
               imageAsset["sourceType"]?.GetValue<string>() == C.ImageAssetSourceType.Icon &&
               imageAsset["source"]?.GetValue<string>() == "lucide:check",
            "Schema 10 migration must preserve ordinary Image source/Fit/Loop in canonical Asset.");

        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-migration-parse-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "migrated-current-schema.json");
            var definition = DashboardJson.ParseCurrent(root.ToJsonString(), dashboardPath);
            Assert(definition.SchemaVersion == DashboardSchemaMigration.CurrentSchemaVersion &&
                   definition.Widgets.Count == 4,
                "Migrated Binary dashboard must satisfy the canonical current schema contract.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

}
