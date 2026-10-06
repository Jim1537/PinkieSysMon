using System.Drawing.Imaging;
using PinkieSysMon;
using CanonicalDashboardDefinition = PinkieSysMon.DashboardModel.DashboardDefinition;
using CanonicalWidgetDefinition = PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalCanvasDefinition = PinkieSysMon.DashboardModel.CanvasDefinition;
using CanonicalStateVisualWidgetDefinition = PinkieSysMon.DashboardModel.StateVisualWidgetDefinition;
using CanonicalValueWidgetDefinition = PinkieSysMon.DashboardModel.ValueWidgetDefinition;
using CanonicalBinaryWidgetDefinition = PinkieSysMon.DashboardModel.BinaryWidgetDefinition;
using CanonicalGaugeWidgetDefinition = PinkieSysMon.DashboardModel.GaugeWidgetDefinition;
using CanonicalBarWidgetDefinition = PinkieSysMon.DashboardModel.BarWidgetDefinition;
using CanonicalImageWidgetDefinition = PinkieSysMon.DashboardModel.ImageWidgetDefinition;
using CanonicalPowerWidgetDefinition = PinkieSysMon.DashboardModel.PowerWidgetDefinition;
using CanonicalMediaSystemWidgetDefinition = PinkieSysMon.DashboardModel.MediaSystemWidgetDefinition;
using CanonicalMediaPlayerWidgetDefinition = PinkieSysMon.DashboardModel.MediaPlayerWidgetDefinition;
using CanonicalStateContentType = PinkieSysMon.DashboardModel.StateContentType;
using CanonicalImageAssetSourceType = PinkieSysMon.DashboardModel.ImageAssetSourceType;

namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm
{
    private void RebuildPreviewRenderer()
    {
        if (_definition is null)
            return;

        var scroll = CaptureScrollPosition();
        ReplacePreviewRenderer(_definition);
        ApplyZoom(restoreScroll: false);
        UpdateStatusBar();
        RestoreScrollPositionDeferred(scroll);
    }

    private void ReplacePreviewRenderer(CanonicalDashboardDefinition definition)
    {
        DashboardPreviewRenderer? replacement = null;
        try
        {
            replacement = new DashboardPreviewRenderer(definition);

            // Prepare everything that can fail before replacing the currently usable preview.
            // This keeps reload/property edits transactional from the editor's point of view.
            var requiredMetrics = DashboardMetricUsage.Collect(definition);
            var systemMetrics = GetRequiredSourceMetrics(_systemTelemetry, requiredMetrics);
            var networkMetrics = GetRequiredSourceMetrics(_networkTelemetry, requiredMetrics);
            var powerMetrics = GetRequiredSourceMetrics(_powerTelemetry, requiredMetrics);
            var mediaMetrics = GetRequiredSourceMetrics(_mediaTelemetry, requiredMetrics);
            var lhmMetrics = GetRequiredSourceMetrics(_lhmTelemetry, requiredMetrics);
            var icueMetrics = GetRequiredSourceMetrics(_icueTelemetry, requiredMetrics);

            var previous = _preview;
            _preview = replacement;
            replacement = null;
            _previewRequiredMetrics = requiredMetrics;
            _previewSystemMetrics = systemMetrics;
            _previewNetworkMetrics = networkMetrics;
            _previewPowerMetrics = powerMetrics;
            _previewMediaMetrics = mediaMetrics;
            _previewLhmMetrics = lhmMetrics;
            _previewIcueMetrics = icueMetrics;
            UpdatePreviewTimerState();

            if (_canvasImage is not null &&
                (_canvasImage.Width != _preview.Width || _canvasImage.Height != _preview.Height))
            {
                _canvasImage.Dispose();
                _canvasImage = null;
            }

            previous?.Dispose();
        }
        catch
        {
            replacement?.Dispose();
            throw;
        }
    }

    private void UpdatePreviewTimerState()
    {
        _previewTimer.Stop();
        if (_preview is null || _dragging || WindowState == FormWindowState.Minimized || !_preview.RequiresFrequentRefresh)
            return;

        _previewTimer.Interval = _preview.RecommendedRefreshIntervalMs;
        _previewTimer.Start();
    }

    private static string[] GetRequiredSourceMetrics(
        IMetricSource source,
        IReadOnlySet<string> requiredMetrics) =>
        requiredMetrics.Where(source.CanProvide).ToArray();

    private void RenderPreview(bool force = false, bool immediate = false)
    {
        if (_definition is null || _preview is null || (!force && WindowState == FormWindowState.Minimized))
            return;

        try
        {
            var bitmap = EnsurePreviewBitmap();
            var bitmapRect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(bitmapRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                if (data.Stride <= 0)
                    throw new InvalidOperationException($"Unexpected editor preview bitmap stride: {data.Stride}.");
                _preview.RenderToBgra(_lastMetrics, data.Scan0, data.Stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            if (immediate)
                _workspaceViewport.Refresh();
            else
                _workspaceViewport.Invalidate();
        }
        catch (Exception ex)
        {
            SetStatus($"Preview: {ex.Message}");
        }
    }

    private Bitmap EnsurePreviewBitmap()
    {
        if (_preview is null)
            throw new InvalidOperationException("Editor preview renderer is not initialized.");

        if (_canvasImage is not null &&
            _canvasImage.Width == _preview.Width &&
            _canvasImage.Height == _preview.Height)
        {
            return _canvasImage;
        }

        _canvasImage?.Dispose();
        _canvasImage = new Bitmap(_preview.Width, _preview.Height, PixelFormat.Format32bppPArgb);
        return _canvasImage;
    }

    private void RefreshPreviewTelemetry()
    {
        // Editor telemetry is deliberately snapshot-based. Never call this from the preview
        // render timer: external providers may block briefly and make editing feel frozen.
        _lastMetrics = CapturePreviewMetrics();
    }

    private IReadOnlyDictionary<string, object?> CapturePreviewMetrics()
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (MetricProviderContract.IsEnabled(_appConfig.MetricProviders, MetricProviderContract.System))
        {
            CaptureSourceMetrics(result, _systemTelemetry, _previewSystemMetrics);
            CaptureSourceMetrics(result, _networkTelemetry, _previewNetworkMetrics);
            CaptureSourceMetrics(result, _powerTelemetry, _previewPowerMetrics);
            CaptureSourceMetrics(result, _mediaTelemetry, _previewMediaMetrics);

            AddPreviewMetric(result, RuntimeMetricContract.Version, RuntimeVersion.Current);
            AddPreviewMetric(result, RuntimeMetricContract.FrameCount, 12345L);
            AddPreviewMetric(result, RuntimeMetricContract.Fps, 24.0);
            AddPreviewMetric(result, RuntimeMetricContract.RenderDuration, 18.4);
            AddPreviewMetric(result, RuntimeMetricContract.EncodeDuration, 7.1);
            AddPreviewMetric(result, RuntimeMetricContract.UsbDuration, 16.2);
            AddPreviewMetric(result, RuntimeMetricContract.FrameDuration, 41.7);
            AddPreviewMetric(result, RuntimeMetricContract.JpegSize, 184_321);
            AddPreviewMetric(result, RuntimeMetricContract.UsbState, "EDITOR");
        }

        if (MetricProviderContract.IsEnabled(_appConfig.MetricProviders, MetricProviderContract.LibreHardwareMonitor))
            CaptureSourceMetrics(result, _lhmTelemetry, _previewLhmMetrics);

        if (MetricProviderContract.IsEnabled(_appConfig.MetricProviders, MetricProviderContract.Icue))
            CaptureSourceMetrics(result, _icueTelemetry, _previewIcueMetrics);
        return result;
    }

    private void AddPreviewMetric(IDictionary<string, object?> target, string metric, object? value)
    {
        if (_previewRequiredMetrics.Contains(metric))
            target[metric] = value;
    }

    private static void CaptureSourceMetrics(
        IDictionary<string, object?> target,
        IMetricSource source,
        IReadOnlyCollection<string> requestedMetrics)
    {
        if (requestedMetrics.Count == 0)
            return;

        MergeMetrics(target, source.Capture(requestedMetrics));
    }

    private static void MergeMetrics(
        IDictionary<string, object?> target,
        IReadOnlyDictionary<string, object?> source)
    {
        foreach (var pair in source)
            target[pair.Key] = pair.Value;
    }

    private void SaveDashboardSnapshot()
    {
        if (_preview is null || _definition is null)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "Save Dashboard Snapshot",
            Filter = "PNG image (*.png)|*.png|All files (*.*)|*.*",
            DefaultExt = "png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{_dashboardName}.png"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var png = _preview.RenderToPng(_lastMetrics);
            File.WriteAllBytes(dialog.FileName, png);
            SetStatus($"Saved dashboard snapshot {_preview.Width}×{_preview.Height}: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Dashboard snapshot was not saved.\n\n{ex.Message}", "Dashboard Snapshot", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CanvasPaint(object? sender, PaintEventArgs e)
    {
        if (_preview is null)
            return;

        var scroll = CaptureScrollPosition();
        var state = e.Graphics.Save();
        try
        {
            // The viewport is the only scrolling surface. There are deliberately no child
            // controls whose Location can be rewritten by ScrollableControl. Content always
            // has logical origin (0,0); scrolling is represented only by this paint transform.
            e.Graphics.TranslateTransform(-scroll.X, -scroll.Y);

            var canvasWidth = _preview.Width * _zoom;
            var canvasHeight = _preview.Height * _zoom;

            if (_canvasImage is not null)
            {
                // The preview bitmap is already antialiased by the same Skia pipeline as runtime.
                // A second bicubic pass visibly softens thin bars and can make gradient caps
                // look translucent. Scale source pixels without re-filtering them.
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                e.Graphics.DrawImage(_canvasImage, new RectangleF(0, 0, canvasWidth, canvasHeight));
            }

            using (var borderPen = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
                e.Graphics.DrawRectangle(borderPen, 0, 0, Math.Max(1, canvasWidth - 1), Math.Max(1, canvasHeight - 1));

            if (_gridEnabled && _gridStep > 0)
            {
                using var gridPen = new Pen(Color.FromArgb(85, 255, 255, 255), 1f);
                for (var x = _gridStep; x < _preview.Width; x += _gridStep)
                {
                    var sx = x * _zoom;
                    e.Graphics.DrawLine(gridPen, sx, 0, sx, canvasHeight);
                }
                for (var y = _gridStep; y < _preview.Height; y += _gridStep)
                {
                    var sy = y * _zoom;
                    e.Graphics.DrawLine(gridPen, 0, sy, canvasWidth, sy);
                }
            }

            foreach (var widget in _selectedWidgets)
            {
                try
                {
                    var outline = _preview.GetWidgetOutline(widget, _lastMetrics);
                    var primary = ReferenceEquals(widget, _selectedWidget);
                    using var pen = new Pen(primary ? Color.DeepSkyBlue : Color.Gold, primary ? 2f : 1.5f)
                    {
                        DashStyle = System.Drawing.Drawing2D.DashStyle.Dash
                    };
                    var points = outline
                        .Select(point => new PointF(point.X * _zoom, point.Y * _zoom))
                        .ToArray();
                    if (points.Length >= 3)
                        e.Graphics.DrawPolygon(pen, points);

                    if (primary && points.Length >= 3)
                    {
                        const float handle = 7f;
                        using var brush = new SolidBrush(Color.DeepSkyBlue);
                        var corner = points[2];
                        e.Graphics.FillRectangle(brush, corner.X - handle / 2f, corner.Y - handle / 2f, handle, handle);
                    }
                }
                catch
                {
                }
            }
        }
        finally
        {
            e.Graphics.Restore(state);
        }
    }

}
