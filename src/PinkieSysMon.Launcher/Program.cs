using System.Windows.Forms;
using PinkieSysMon.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var root = Path.GetFullPath(AppContext.BaseDirectory);
        try
        {
            if (args.Length != 0)
            {
                if (args.Length != 1 || args[0] is not ("--editor" or "--runtime" or "--both"))
                    throw new ArgumentException("Unknown launcher command.");
                ProjectLauncherActions.Run(root, args[0]);
                return;
            }

            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            Application.Run(new ProjectInfoWindow(root, showLaunchButtons: true, version));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PinkieSysMon Launcher",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
