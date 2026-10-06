namespace PinkieSysMon.Editor;

internal abstract class PinkieEditorForm : Form
{
    protected PinkieEditorForm()
    {
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? (System.Drawing.Icon)SystemIcons.Application.Clone();
    }
}
