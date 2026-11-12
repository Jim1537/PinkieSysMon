namespace PinkieSysMon.Editor;

// Sources are edited only in a detached Settings draft. Nothing reaches app.json
// until the parent Editor Settings OK transaction succeeds.
internal sealed class FilesystemSourcesDialog : PinkieEditorForm
{
    private readonly List<FilesystemSourceConfig> _sources;
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false
    };
    private readonly Button _edit = new() { Text = "Edit", AutoSize = true, Enabled = false };
    private readonly Button _remove = new() { Text = "Remove", AutoSize = true, Enabled = false };

    public IReadOnlyList<FilesystemSourceConfig> Sources => _sources.Select(s => s.Copy()).ToArray();

    public FilesystemSourcesDialog(IEnumerable<FilesystemSourceConfig> sources)
    {
        _sources = sources.Select(s => s.Copy()).ToList();
        Text = "Filesystem Sources";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(780, 390);
        ClientSize = new Size(980, 510);
        Padding = new Padding(12);

        _list.Columns.Add("Alias", 200);
        _list.Columns.Add("Type", 95);
        _list.Columns.Add("Path", 490);
        _list.Columns.Add("Status", 125);
        _list.SelectedIndexChanged += (_, _) =>
        {
            _edit.Enabled = _list.SelectedIndices.Count == 1;
            _remove.Enabled = _edit.Enabled;
        };

        var add = new Button { Text = "Add...", AutoSize = true };
        add.Click += (_, _) =>
        {
            using var dialog = new FilesystemSourceEditorDialog(null, _sources);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Source is null) return;
            _sources.Add(dialog.Source);
            RefreshSources();
            _list.Items[^1].Selected = true;
        };
        _edit.Click += (_, _) =>
        {
            if (_list.SelectedIndices.Count != 1) return;
            var position = _list.SelectedIndices[0];
            using var dialog = new FilesystemSourceEditorDialog(_sources[position], _sources);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Source is null) return;
            _sources[position] = dialog.Source;
            RefreshSources();
            _list.Items[position].Selected = true;
        };
        _remove.Click += (_, _) =>
        {
            if (_list.SelectedIndices.Count != 1) return;
            var position = _list.SelectedIndices[0];
            if (MessageBox.Show(this, "Remove this source from the Settings draft?\n\n" +
                    "Existing dashboard bindings will become unavailable after saving.",
                    "Remove Filesystem Source", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            _sources.RemoveAt(position);
            RefreshSources();
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        actions.Controls.Add(add);
        actions.Controls.Add(_edit);
        actions.Controls.Add(_remove);

        var note = new Label
        {
            Dock = DockStyle.Top, AutoSize = true,
            Text = "Sources are checked before admission. Changes are saved only with Editor Settings OK. " +
                   "A configured source may later become unavailable."
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(_list);
        Controls.Add(note);
        Controls.Add(actions);
        Controls.Add(buttons);
        RefreshSources();
    }

    private void RefreshSources()
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var source in _sources)
            {
                var row = new ListViewItem(source.Alias);
                row.SubItems.Add(source.Kind);
                row.SubItems.Add(source.Path);
                row.SubItems.Add("Configured");
                _list.Items.Add(row);
            }
        }
        finally { _list.EndUpdate(); }
        _edit.Enabled = false;
        _remove.Enabled = false;
    }
}

internal sealed class FilesystemSourceEditorDialog : PinkieEditorForm
{
    // A cancelled native I/O call may finish late. The gate remains held until
    // that call terminates; a new preflight must not launch a replacement scan.
    private static readonly SemaphoreSlim PreflightGate = new(1, 1);

    private readonly FilesystemSourceConfig? _original;
    private readonly IReadOnlyList<FilesystemSourceConfig> _siblings;
    private readonly TextBox _alias = new() { Dock = DockStyle.Fill };
    private readonly TextBox _path = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _kind = new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly ProgressBar _progress = new()
    {
        Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false
    };
    private readonly Button _save = new() { Text = "Check and Save", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true };
    private CancellationTokenSource? _activeCancellation;
    private bool _busy;

    public FilesystemSourceConfig? Source { get; private set; }

    public FilesystemSourceEditorDialog(
        FilesystemSourceConfig? original, IEnumerable<FilesystemSourceConfig> existing)
    {
        _original = original?.Copy();
        _siblings = existing.Where(s => original is null ||
            !s.Id.Equals(original.Id, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Copy()).ToArray();
        Text = original is null ? "Add Filesystem Source" : "Edit Filesystem Source";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(740, 270);
        Padding = new Padding(12);

        _kind.Items.AddRange([FilesystemSourceContract.FileKind, FilesystemSourceContract.DirectoryKind]);
        _alias.Text = original?.Alias ?? string.Empty;
        _path.Text = original?.Path ?? string.Empty;
        _kind.SelectedItem = original?.Kind ?? FilesystemSourceContract.FileKind;
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            if (_kind.SelectedItem as string == FilesystemSourceContract.DirectoryKind)
            {
                using var picker = new FolderBrowserDialog { Description = "Choose a directory to monitor" };
                if (picker.ShowDialog(this) == DialogResult.OK) _path.Text = picker.SelectedPath;
            }
            else
            {
                using var picker = new OpenFileDialog { Title = "Choose a file to monitor", CheckFileExists = true };
                if (picker.ShowDialog(this) == DialogResult.OK) _path.Text = picker.FileName;
            }
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 168, ColumnCount = 3, RowCount = 4,
            Padding = new Padding(0, 4, 0, 0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var i = 0; i < 4; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.Controls.Add(new Label { Text = "Alias", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        fields.Controls.Add(_alias, 1, 0);
        fields.Controls.Add(new Label { Text = "Kind", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        fields.Controls.Add(_kind, 1, 1);
        fields.Controls.Add(new Label { Text = "Path", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        fields.Controls.Add(_path, 1, 2);
        fields.Controls.Add(browse, 2, 2);
        fields.Controls.Add(_status, 1, 3);
        fields.Controls.Add(_progress, 2, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_save);
        _cancel.Click += (_, _) =>
        {
            _activeCancellation?.Cancel();
            DialogResult = DialogResult.Cancel;
            Close(); // Never wait on a stalled native filesystem API.
        };
        _save.Click += async (_, _) => await SaveWithPreflightAsync();

        Controls.Add(fields);
        Controls.Add(buttons);
        AcceptButton = _save;
        CancelButton = _cancel;
    }

    private async Task SaveWithPreflightAsync()
    {
        if (_busy) return;
        var candidate = new FilesystemSourceConfig
        {
            Id = _original?.Id ?? FilesystemSourceContract.NewId(),
            Alias = _alias.Text,
            Kind = _kind.SelectedItem as string ?? string.Empty,
            Path = _path.Text
        };
        try
        {
            var combined = new FilesystemConfig
            {
                Sources = [.. _siblings.Select(s => s.Copy()), candidate]
            };
            FilesystemSourceContract.Validate(combined);
        }
        catch (InvalidDataException error)
        {
            _status.Text = error.Message;
            return;
        }

        // Label-only changes have no new filesystem admission to check.
        if (_original is not null && candidate.Path == _original.Path && candidate.Kind == _original.Kind)
        {
            Source = candidate;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _busy = true;
        _save.Enabled = false;
        _alias.Enabled = _path.Enabled = _kind.Enabled = false;
        _progress.Visible = true;
        _status.Text = "Checking path and scanning metadata...";
        var cancellation = new CancellationTokenSource();
        _activeCancellation = cancellation;
        var acquired = false;
        try
        {
            await PreflightGate.WaitAsync(cancellation.Token);
            acquired = true;
            var outcome = await Task.Run(() => FilesystemScanner.Scan(candidate, cancellation.Token));
            if (IsDisposed || Disposing) return;
            if (outcome.Complete)
            {
                Source = candidate;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            _status.Text = outcome.Error ?? outcome.Status.ToString();
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !Disposing) _status.Text = "Preflight cancelled.";
        }
        catch (Exception error)
        {
            if (!IsDisposed && !Disposing) _status.Text = "Preflight failed: " + error.Message;
        }
        finally
        {
            if (acquired) PreflightGate.Release();
            cancellation.Dispose();
            if (!IsDisposed && !Disposing)
            {
                _activeCancellation = null;
                _busy = false;
                _save.Enabled = true;
                _alias.Enabled = _path.Enabled = _kind.Enabled = true;
                _progress.Visible = false;
            }
        }
    }
}
