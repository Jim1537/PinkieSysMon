namespace PinkieSysMon.Editor;

internal sealed class FilesystemEnableWarningDialog : PinkieEditorForm
{
    internal const string WarningText =
        "The Filesystem provider may perform resource-intensive directory scans.\n\n" +
        "Recursive directory monitoring can cause significant disk activity and negatively affect system performance, especially on traditional hard disk drives (HDDs).\n\n" +
        "Monitoring individual files generally requires significantly fewer resources.\n\n" +
        "Use this provider with caution. You are responsible for selecting appropriate monitoring paths.\n\n" +
        "Do you want to enable Filesystem?";

    public FilesystemEnableWarningDialog()
    {
        Text = "Enable Filesystem Provider?";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(650, 315);
        Padding = new Padding(16);

        var message = new Label
        {
            Text = WarningText, Dock = DockStyle.Fill,
            AutoSize = false, TextAlign = ContentAlignment.TopLeft
        };
        var enable = new Button { Text = "Enable", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 50,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(enable);
        Controls.Add(message);
        Controls.Add(buttons);
        AcceptButton = enable;
        CancelButton = cancel;
    }
}
