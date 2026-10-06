using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace PinkieSysMon;

internal enum RuntimeNotificationKind
{
    Information,
    Warning,
    Error
}

internal static class RuntimeNotificationContract
{
    public const string DisplayName = "PinkieSysMon";

    public static string GetTitle(RuntimeNotificationKind kind) => kind switch
    {
        RuntimeNotificationKind.Warning => "PinkieSysMon warning",
        RuntimeNotificationKind.Error => "PinkieSysMon error",
        _ => DisplayName
    };
}

internal sealed class WindowsAppNotificationService : IDisposable
{
    private readonly FileLogger _log;
    private AppNotificationManager? _manager;
    private bool _registeredForActivation;
    private bool _canShow;
    private bool _disposed;

    public WindowsAppNotificationService(FileLogger log)
    {
        _log = log;
        Initialize();
    }

    public bool IsAvailable => _canShow && !_disposed;

    public bool Show(string message, RuntimeNotificationKind kind = RuntimeNotificationKind.Information)
    {
        if (!IsAvailable || _manager is null)
            return false;

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(RuntimeNotificationContract.GetTitle(kind))
                .AddText(message)
                .BuildNotification();

            _manager.Show(notification);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Windows app notification could not be shown", ex);
            return false;
        }
    }

    private void Initialize()
    {
        AppNotificationManager? manager = null;
        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                _log.Info("Windows app notifications are not supported on this system; notification delivery is disabled.");
                return;
            }

            manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "PinkieSysMon.ico");
            if (File.Exists(iconPath))
            {
                manager.Register(RuntimeNotificationContract.DisplayName, new Uri(iconPath));
            }
            else
            {
                _log.Info($"Windows app notification icon was not found at '{iconPath}'; using shell application identity.");
                manager.Register();
            }

            _manager = manager;
            _registeredForActivation = true;
            _canShow = true;
            _log.Info("Windows app notifications registered.");
        }
        catch (Exception ex) when (IsKnownSelfContainedRegistrationFailure(ex) && manager is not null)
        {
            // Windows App SDK 2.5.1 has a confirmed self-contained unpackaged-app bug where
            // Register() fails while trying to load Microsoft.WindowsAppRuntime.Insights.Resource.dll.
            // Notification display still works; only activation callback registration is unavailable.
            // Keep the manager for display-only notifications until a Windows App SDK release ships
            // the upstream fix. Do not broaden this fallback to unrelated registration failures.
            try
            {
                manager.NotificationInvoked -= OnNotificationInvoked;
            }
            catch
            {
                // Registration already failed; cleanup must not affect runtime startup.
            }

            _manager = manager;
            _registeredForActivation = false;
            _canShow = true;
            _log.Warn("Windows App SDK notification activation registration hit the known self-contained Resource.dll failure; display-only notifications remain enabled.");
        }
        catch (Exception ex)
        {
            if (manager is not null)
            {
                try
                {
                    manager.NotificationInvoked -= OnNotificationInvoked;
                }
                catch
                {
                    // Registration already failed; cleanup must not affect runtime startup.
                }
            }

            _manager = null;
            _registeredForActivation = false;
            _canShow = false;
            _log.Error("Windows app notification registration failed; notification delivery is disabled", ex);
        }
    }

    internal static bool IsKnownSelfContainedRegistrationFailure(Exception exception)
    {
        return exception is COMException comException &&
               comException.HResult == unchecked((int)0x8007007E) &&
               comException.Message.Contains(
                   "Microsoft.WindowsAppRuntime.Insights.Resource.dll",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        _log.Info("Windows app notification invoked.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var manager = _manager;
        _manager = null;

        if (manager is null)
            return;

        try
        {
            manager.NotificationInvoked -= OnNotificationInvoked;
        }
        catch (Exception ex)
        {
            _log.Error("Windows app notification handler cleanup failed", ex);
        }

        _canShow = false;
        if (!_registeredForActivation)
            return;

        _registeredForActivation = false;
        try
        {
            manager.Unregister();
        }
        catch (Exception ex)
        {
            _log.Error("Windows app notification unregister failed", ex);
        }
    }
}
