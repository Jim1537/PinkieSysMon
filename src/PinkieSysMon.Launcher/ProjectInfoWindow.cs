using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace PinkieSysMon.Launcher;

// This file is compiled by both the standalone Launcher and the Editor.
// All product information and Advanced actions have one source of truth.
internal static class ProjectPresentation
{
    public const string Title = "Pinkie's System Monitor";
    public const string SupportedDevicesText = "supported USB devices";
    public const string SupportedDevicesUrl = "https://jim1537.github.io/PinkieSysMon/supported-devices/";
    public const string Description =
        "Pinkie's System Monitor is a fully local Windows application for building telemetry dashboards and " +
        "displaying them on independent Windows Desktop panels or " + SupportedDevicesText + ".";
    public const string Website = "https://jim1537.github.io/PinkieSysMon";

    public const string LauncherExe = "Launcher.exe";
    public static readonly (string Name, string Arguments)[] Shortcuts =
    [
        ("Pinkie's System Monitor", ""),
        ("PinkieSysMon Control Center", "--editor"),
        ("PinkieSysMon Runtime", "--runtime")
    ];
}

internal static class ProjectLauncherActions
{
    public static void Run(string root, string action) => Run(root, action, null);

    internal static void Run(string root, string action, Action<string>? progress)
    {
        var bin = Path.Combine(Path.GetFullPath(root), "bin");
        if (action is "--runtime" or "--both")
        {
            // The existing Runtime has a machine-wide IPC/process identity. Do not
            // start a duplicate when another instance already owns that identity.
            progress?.Invoke("runtime.process.check");
            var processes = Process.GetProcessesByName("PinkieSysMon");
            try
            {
                if (processes.Length == 0)
                {
                    progress?.Invoke("runtime.start");
                    Start(Path.Combine(bin, "PinkieSysMon.exe"), bin);
                    progress?.Invoke("runtime.start.returned");
                }
                else progress?.Invoke("runtime.process.alreadyPresent");
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        if (action is "--editor" or "--both")
        {
            progress?.Invoke("editor.start");
            Start(Path.Combine(bin, "PinkieSysMon.Editor.exe"), bin);
            progress?.Invoke("editor.start.returned");
        }

        if (action is not ("--editor" or "--runtime" or "--both"))
            throw new ArgumentException("Unknown launcher command.", nameof(action));
    }

    private static void Start(string executable, string workingDirectory)
    {
        if (!File.Exists(executable))
            throw new FileNotFoundException("The portable application executable is missing.", executable);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Windows did not start " + Path.GetFileName(executable) + ".");
    }
}

internal static class ProjectShortcutInstaller
{
    internal static void Create(string root, bool startMenu, IWin32Window owner, Action<string>? progress = null)
    {
        progress?.Invoke("launcher.validate");
        root = Path.GetFullPath(root);
        var launcher = Path.Combine(root, ProjectPresentation.LauncherExe);
        if (!File.Exists(launcher))
            throw new FileNotFoundException("The portable Launcher is missing.", launcher);

        var directory = startMenu
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PinkieSysMon")
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Windows did not provide the user shortcut directory.");

        Directory.CreateDirectory(directory);
        progress?.Invoke("shortcut.service.create");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host shortcut service is unavailable.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create the Windows shortcut service.");

        var created = 0;
        var replaced = 0;
        var unchanged = 0;
        var skipped = 0;
        var index = 0;
        try
        {
            foreach (var (name, arguments) in ProjectPresentation.Shortcuts)
            {
                var phase = "shortcut." + index++;
                progress?.Invoke(phase + ".begin");
                var path = Path.Combine(directory, name + ".lnk");
                if (Directory.Exists(path))
                    throw new IOException("The shortcut name is occupied by a directory: " + path);

                dynamic link = ((dynamic)shell).CreateShortcut(path);
                try
                {
                    var exists = File.Exists(path);
                    if (exists &&
                        SamePath((string)link.TargetPath, launcher) &&
                        string.Equals(((string)link.Arguments).Trim(), arguments, StringComparison.OrdinalIgnoreCase) &&
                        SamePath((string)link.WorkingDirectory, root) &&
                        string.Equals((string)link.IconLocation, launcher + ",0", StringComparison.OrdinalIgnoreCase))
                    {
                        unchanged++;
                        progress?.Invoke(phase + ".unchanged");
                        continue;
                    }

                    if (exists &&
                        MessageBox.Show(owner,
                            "An existing shortcut has a different target or settings:\n\n" +
                            path + "\n\nReplace this shortcut?",
                            "PinkieSysMon Shortcuts",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        skipped++;
                        progress?.Invoke(phase + ".replacement.declined");
                        continue;
                    }

                    link.TargetPath = launcher;
                    link.Arguments = arguments;
                    link.WorkingDirectory = root;
                    link.IconLocation = launcher + ",0";
                    link.Description = name;
                    link.Save();
                    if (exists) replaced++; else created++;
                    progress?.Invoke(phase + ".saved");
                }
                finally
                {
                    if (Marshal.IsComObject(link))
                        Marshal.FinalReleaseComObject(link);
                }
            }
        }
        finally
        {
            if (Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }

        progress?.Invoke($"shortcuts.returned; created={created}; replaced={replaced}; unchanged={unchanged}; skipped={skipped}");
        MessageBox.Show(owner,
            $"Created: {created}\nUpdated: {replaced}\nAlready correct: {unchanged}\n\nLocation: {directory}",
            "PinkieSysMon Shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static bool SamePath(string existing, string expected)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(existing), Path.GetFullPath(expected),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}

internal sealed class ProjectInfoWindow : Form
{
    private readonly string _root;
    private readonly Action<string, Exception?>? _report;
    private readonly int _baseHeight;
    private readonly Panel _advanced;
    private readonly Button _advancedToggle;
    private bool _expanded;

    public ProjectInfoWindow(string root, bool showLaunchButtons, string version, Action<string, Exception?>? report = null)
    {
        _root = Path.GetFullPath(root);
        _report = report;
        Text = showLaunchButtons ? ProjectPresentation.Title : "About PinkieSysMon";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = showLaunchButtons;
        StartPosition = showLaunchButtons ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        ClientSize = new Size(606, showLaunchButtons ? 319 : 252);
        _baseHeight = ClientSize.Height;

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch (Exception error)
        {
            ProjectActionDiagnostics.ReportSafely(_report, "Project window icon fallback.", error);
            Icon = SystemIcons.Application;
        }

        var heading = new Label
        {
            Text = ProjectPresentation.Title,
            Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
            Location = new Point(20, 14),
            Size = new Size(558, 36)
        };
        Controls.Add(heading);

        var versionLabel = new Label
        {
            Text = "Version " + version,
            Location = new Point(21, 55),
            Size = new Size(550, 20),
            ForeColor = SystemColors.GrayText
        };
        Controls.Add(versionLabel);

        var description = new LinkLabel
        {
            Text = ProjectPresentation.Description,
            Location = new Point(21, 82),
            Size = new Size(557, 60)
        };
        description.Links.Add(
            ProjectPresentation.Description.IndexOf(ProjectPresentation.SupportedDevicesText, StringComparison.Ordinal),
            ProjectPresentation.SupportedDevicesText.Length,
            ProjectPresentation.SupportedDevicesUrl);
        description.LinkClicked += (_, e) =>
        {
            if (e.Link?.LinkData is string url)
                OpenWebPage(url, "Open Supported Devices");
        };
        Controls.Add(description);

        var website = new LinkLabel
        {
            Text = ProjectPresentation.Website,
            Location = new Point(21, 150),
            Size = new Size(560, 22),
            TabStop = true
        };
        website.LinkClicked += (_, _) => OpenWebPage(ProjectPresentation.Website, "Open Project Website");
        Controls.Add(website);

        var launchY = 190;
        if (showLaunchButtons)
        {
            var launchButtons = new (string Text, string Command)[]
            {
                ("Launch Control Center", "--editor"),
                ("Launch Runtime", "--runtime"),
                ("Launch Both", "--both")
            };
            for (var i = 0; i < launchButtons.Length; i++)
            {
                var (text, command) = launchButtons[i];
                var button = new Button
                {
                    Text = text,
                    Location = new Point(21 + i * 189, launchY),
                    Size = new Size(181, 38),
                    UseVisualStyleBackColor = true
                };
                button.Click += (_, _) =>
                {
                    try
                    {
                        LaunchAndCloseCore(command, ProjectLauncherActions.Run);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "PinkieSysMon Launcher",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };
                Controls.Add(button);
            }
        }

        var advancedY = showLaunchButtons ? 247 : 190;
        _advancedToggle = new Button
        {
            Text = "Advanced ▼",
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(17, advancedY),
            Size = new Size(565, 30)
        };
        _advancedToggle.FlatAppearance.BorderSize = 0;
        _advancedToggle.Click += (_, _) => ToggleAdvanced();
        Controls.Add(_advancedToggle);

        _advanced = new Panel
        {
            Visible = false,
            Location = new Point(21, advancedY + 32),
            Size = new Size(560, 88)
        };
        var desktop = new Button
        {
            Text = "Create Desktop shortcuts",
            Location = new Point(0, 6),
            Size = new Size(263, 34)
        };
        var menu = new Button
        {
            Text = "Create Start Menu group",
            Location = new Point(274, 6),
            Size = new Size(263, 34)
        };
        desktop.Click += (_, _) => InstallShortcuts(startMenu: false);
        menu.Click += (_, _) => InstallShortcuts(startMenu: true);
        _advanced.Controls.Add(desktop);
        _advanced.Controls.Add(menu);
        Controls.Add(_advanced);
    }

    internal void LaunchAndClose(string command, Action<string, string> launch) =>
        LaunchAndCloseCore(command, (root, action, _) => launch(root, action));

    private void LaunchAndCloseCore(string command, Action<string, string, Action<string>> launch) =>
        ProjectActionDiagnostics.Execute("launcher.launch",
            command is "--runtime" or "--editor" or "--both" ? command : "invalid", progress =>
            {
                progress("dispatch");
                launch(_root, command, progress);
                progress("processStart.returned");
                Close();
            }, _report);

    private void OpenWebPage(string url, string title)
    {
        try
        {
            ProjectActionDiagnostics.Execute("project.website.open", "documentation", progress =>
            {
                progress("shell.dispatch");
                using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                progress(process is null ? "shell.returned.noProcessHandle" : "shell.returned.processHandle");
            }, _report);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, title,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleAdvanced()
    {
        _expanded = !_expanded;
        _advanced.Visible = _expanded;
        _advancedToggle.Text = _expanded ? "Advanced ▲" : "Advanced ▼";
        ClientSize = new Size(ClientSize.Width, _baseHeight + (_expanded ? 72 : 0));
    }

    private void InstallShortcuts(bool startMenu)
    {
        try
        {
            ProjectActionDiagnostics.Execute("project.shortcuts.create", startMenu ? "startMenu" : "desktop",
                progress => ProjectShortcutInstaller.Create(_root, startMenu, this, progress), _report);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PinkieSysMon Shortcuts",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
