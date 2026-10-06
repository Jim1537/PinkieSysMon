using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Windows.Graphics;
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
    private const int WmSettingChange = 0x001A;
    private const int WmSysColorChange = 0x0015;
    private const int WmThemeChanged = 0x031A;

    private readonly EditorTitleBarMenuStrip _mainMenuStrip = new();
    private readonly EditorTitleBarHost _titleBarHost = new();
    private AppWindow? _appWindow;
    private AppWindowTitleBar? _appWindowTitleBar;
    private InputNonClientPointerSource? _nonClientPointerSource;
    private bool _usesExtendedTitleBar;
    private EditorThemeMode _themeMode = EditorThemeMode.System;
    private EditorShellTheme _shellTheme = EditorShellTheme.CaptureCurrent();
    private bool _restartAfterClose;

    private void InitializeWindowChrome()
    {
        _shellTheme = EditorShellTheme.CaptureCurrent();
        _titleBarHost.ApplyTheme(_shellTheme);
        _titleBarHost.SetWindowIcon(Icon);
        _mainMenuStrip.ApplyTheme(_shellTheme);

        SizeChanged += (_, _) => LayoutWindowChromeMenu();
        DpiChanged += (_, _) => LayoutWindowChromeMenu();
        TextChanged += (_, _) =>
        {
            if (_appWindow is not null)
                _appWindow.Title = Text;
            InvalidateWindowChrome();
        };
        Activated += (_, _) => InvalidateWindowChrome();
        Deactivate += (_, _) => InvalidateWindowChrome();
        _mainMenuStrip.Layout += (_, _) => UpdateTitleBarInteractiveRegions();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InitializeNativeTitleBar();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            _nonClientPointerSource?.ClearRegionRects(NonClientRegionKind.Passthrough);
        }
        catch
        {
            // The AppWindow lifetime ends with the HWND; cleanup is best-effort here.
        }

        _nonClientPointerSource = null;
        _appWindowTitleBar = null;
        _appWindow = null;
        _usesExtendedTitleBar = false;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (!IsHandleCreated)
            return;

        if (m.Msg is WmSettingChange or WmSysColorChange or WmThemeChanged)
        {
            if (_themeMode == EditorThemeMode.System && _formShown)
                SetStatus("Windows theme changed. Restart Editor to refresh the System theme safely.");

            ApplyNativeTitleBarTheme();
        }
    }

    private void InitializeNativeTitleBar()
    {
        _usesExtendedTitleBar = false;
        _nonClientPointerSource = null;
        _appWindowTitleBar = null;
        _appWindow = null;

        try
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("The Editor UI thread has no Windows App SDK DispatcherQueue.");

            var windowId = Win32Interop.GetWindowIdFromWindow(Handle);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.AssociateWithDispatcherQueue(dispatcherQueue);
            _appWindow.Title = Text;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "PinkieSysMon.ico");
            if (File.Exists(iconPath))
            {
                // Use the dedicated Windows App SDK surfaces instead of relying on
                // the legacy HWND icon to flow into a fully extended title bar.
                _appWindow.SetIcon(iconPath);
                _appWindow.SetTitleBarIcon(iconPath);
                _appWindow.SetTaskbarIcon(iconPath);
            }
            else
            {
                _log.Warn($"Editor window icon was not found at '{iconPath}'.");
            }

            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                ConfigureStandardTitleBarFallback();
                _log.Warn("Windows App SDK title-bar customization is not supported on this Windows build; using the standard system title bar with the Editor menu below it.");
                return;
            }

            _appWindowTitleBar = _appWindow.TitleBar;
            _appWindowTitleBar.ExtendsContentIntoTitleBar = true;
            _appWindowTitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            _appWindowTitleBar.IconShowOptions = IconShowOptions.ShowIconAndSystemMenu;
            _nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(windowId);
            _usesExtendedTitleBar = true;
            _titleBarHost.ShowWindowIcon = true;
            _mainMenuStrip.ShowWindowTitle = true;

            ApplyNativeTitleBarTheme();
            LayoutWindowChromeMenu();
            _log.Info("Windows App SDK extended title bar enabled; non-client menu passthrough active.");
        }
        catch (Exception ex)
        {
            _log.Warn("Windows App SDK title-bar integration failed; using the standard system title bar with the Editor menu below it.", ex);
            ConfigureStandardTitleBarFallback();
        }
    }

    private void ConfigureStandardTitleBarFallback()
    {
        _usesExtendedTitleBar = false;
        _nonClientPointerSource = null;

        try
        {
            _appWindowTitleBar?.ResetToDefault();
        }
        catch
        {
            // Fallback must not prevent the Editor from opening.
        }

        _appWindowTitleBar = null;
        _titleBarHost.ShowWindowIcon = false;
        _mainMenuStrip.ShowWindowTitle = false;
        LayoutWindowChromeMenu();
    }

    private void ApplyNativeTitleBarTheme()
    {
        if (!_usesExtendedTitleBar || _appWindowTitleBar is null)
            return;

        _appWindowTitleBar.PreferredTheme = _themeMode switch
        {
            EditorThemeMode.Light => TitleBarTheme.Light,
            EditorThemeMode.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode
        };

        // The client title-bar host is the single background owner. Keep the
        // system caption buttons transparent so their normal/disabled surfaces
        // always inherit that exact background instead of approximating it.
        _appWindowTitleBar.ButtonBackgroundColor = Colors.Transparent;
        _appWindowTitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private void RefreshEditorShellTheme()
    {
        var previous = _shellTheme;
        _shellTheme = EditorShellTheme.CaptureCurrent();

        _titleBarHost.ApplyTheme(_shellTheme);
        _mainMenuStrip.ApplyTheme(_shellTheme);

        _widgetTree.LineColor = _shellTheme.InactiveForeground;
        _workspaceViewport.BackColor = _shellTheme.WorkspaceBackground;
        _propertyTabs.ApplyTheme(_shellTheme);
        EditorPropertyGridTheme.Apply(_propertyGrid, _shellTheme);

        if (_toolbar is not null && !_toolbar.IsDisposed &&
            (previous.Foreground.ToArgb() != _shellTheme.Foreground.ToArgb() ||
             previous.SelectionBackground.ToArgb() != _shellTheme.SelectionBackground.ToArgb()))
        {
            try
            {
                ApplyToolbarIconSize(_toolbarIconSize);
            }
            catch (Exception ex)
            {
                _log.Warn("Editor shell theme changed, but toolbar icons could not be regenerated.", ex);
            }
        }

        UpdateTreeSelectionVisuals();
        ApplyNativeTitleBarTheme();
        LayoutWindowChromeMenu();
        Invalidate(true);
    }

    private void InvalidateWindowChrome()
    {
        _titleBarHost.Invalidate();
        _mainMenuStrip.Invalidate();
    }

    private void LayoutWindowChromeMenu()
    {
        var titleBarHeight = _usesExtendedTitleBar && _appWindowTitleBar is not null
            ? Math.Max(1, _appWindowTitleBar.Height)
            : Math.Max(1, _mainMenuStrip.GetTitleBarPreferredHeight(verticalPadding: 3));
        var leftInset = _usesExtendedTitleBar && _appWindowTitleBar is not null
            ? Math.Max(0, _appWindowTitleBar.LeftInset)
            : 0;
        var rightInset = _usesExtendedTitleBar && _appWindowTitleBar is not null
            ? Math.Max(0, _appWindowTitleBar.RightInset)
            : 0;

        _titleBarHost.UpdateNativeMetrics(titleBarHeight, leftInset, rightInset);

        if (_titleBarHost.Width <= 0 || _titleBarHost.Height <= 0)
            return;

        var availableBounds = _titleBarHost.MenuBounds;
        var preferredHeight = _mainMenuStrip.GetTitleBarPreferredHeight(ScaleTitleBarMetric(3));
        var localBounds = EditorWindowChromeContract.CenterVertically(availableBounds, preferredHeight);
        _mainMenuStrip.Bounds = localBounds;
        _mainMenuStrip.UpdateTitleBarMetrics(ScaleTitleBarMetric(8));
        _mainMenuStrip.BringToFront();
        _mainMenuStrip.PerformLayout();
        UpdateTitleBarInteractiveRegions();
    }

    private void UpdateTitleBarInteractiveRegions()
    {
        if (!_usesExtendedTitleBar || _nonClientPointerSource is null || !IsHandleCreated)
            return;

        try
        {
            var menuWindowBounds = new Rectangle(
                _titleBarHost.Left + _mainMenuStrip.Left,
                _titleBarHost.Top + _mainMenuStrip.Top,
                _mainMenuStrip.Width,
                _mainMenuStrip.Height);
            var menuRegions = EditorWindowChromeContract.GetMenuPassthroughRegions(
                menuWindowBounds,
                _mainMenuStrip.GetInteractiveItemBounds());
            var nativeRegions = menuRegions
                .Select(bounds => new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height))
                .ToArray();

            if (nativeRegions.Length == 0)
                _nonClientPointerSource.ClearRegionRects(NonClientRegionKind.Passthrough);
            else
                _nonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, nativeRegions);
        }
        catch (Exception ex)
        {
            _log.Warn("Editor title-bar interactive regions could not be updated.", ex);
        }
    }

    private int ScaleTitleBarMetric(int value) =>
        Math.Max(1, (int)Math.Round(value * DeviceDpi / 96d));

    private void RequestEditorRestartForTheme()
    {
        _restartAfterClose = true;
        Close();
    }

    private void LaunchRestartedEditor()
    {
        if (!_restartAfterClose)
            return;

        _restartAfterClose = false;
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return;

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("--root");
            startInfo.ArgumentList.Add(_root);
            if (!string.IsNullOrWhiteSpace(_dashboardName))
            {
                startInfo.ArgumentList.Add("--dashboard");
                startInfo.ArgumentList.Add(_dashboardName);
            }

            using var process = System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _log.Warn("Editor theme was saved, but the Editor could not restart automatically.", ex);
        }
    }
}
