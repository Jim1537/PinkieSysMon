using System.ComponentModel;
using System.Reflection;
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
    private readonly EditorThemedTabControl _propertyTabs = new()
    {
        Dock = DockStyle.Fill,
        Alignment = TabAlignment.Top,
        Multiline = false
    };

    private readonly TabPage _generalPropertyTab = new() { Name = "General", Padding = Padding.Empty, ToolTipText = "General", AccessibleName = "General" };
    private readonly TabPage _appearancePropertyTab = new() { Name = "Appearance", Padding = Padding.Empty, ToolTipText = "Appearance", AccessibleName = "Appearance" };
    private readonly TabPage _dataPropertyTab = new() { Name = "Data", Padding = Padding.Empty, ToolTipText = "Data", AccessibleName = "Data" };
    private readonly TabPage _gaugePropertyTab = new() { Name = "Gauge", Padding = Padding.Empty, ToolTipText = "Gauge", AccessibleName = "Gauge" };
    private readonly TabPage _statesPropertyTab = new() { Name = "States", Padding = Padding.Empty, ToolTipText = "States", AccessibleName = "States" };
    private readonly TabPage _imagePropertyTab = new() { Name = "Image", Padding = Padding.Empty, ToolTipText = "Image", AccessibleName = "Image" };
    private readonly TabPage _textPropertyTab = new() { Name = "Text", Padding = Padding.Empty, ToolTipText = "Text", AccessibleName = "Text" };

    private static readonly string[] PropertyGeneralIcons = ["settings-2", "settings", "sliders-horizontal", "grid-3x3"];
    private static readonly string[] PropertyAppearanceIcons = ["palette", "paintbrush", "swatch-book", "focus"];
    private static readonly string[] PropertyDataIcons = ["database", "server", "hard-drive", "folder-open"];
    private static readonly string[] PropertyGaugeIcons = ["gauge", "activity", "focus"];
    private static readonly string[] PropertyStatesIcons = ["list-tree", "toggle-left", "layers"];
    private static readonly string[] PropertyImageIcons = ["image", "images", "frame", "focus"];
    private static readonly string[] PropertyTextIcons = ["type", "text", "pilcrow", "file"];
    private readonly List<Image> _propertyTabImages = [];

    private sealed record PropertyWidgetSelection(CanonicalWidgetDefinition[] Widgets);

    private object? _propertyGridTarget;
    private EditorPropertySection? _presentedPropertySection;
    private readonly EditorPropertyExpansionState _propertyExpansionState = new();
    private EditorPropertySection _lastWidgetPropertySection = EditorPropertySection.General;
    private bool _updatingPropertyTabs;
    private bool _propertyGridColumnsInitialized;
    private int _propertyPresentationGeneration;
    private readonly ToolTip _propertyApplicabilityToolTip = new()
    {
        ShowAlways = true,
        InitialDelay = 350,
        ReshowDelay = 100,
        AutoPopDelay = 5000
    };
    private readonly HashSet<Control> _propertyApplicabilityToolTipControls = new(ReferenceEqualityComparer.Instance);
    private Control? _propertyApplicabilityToolTipOwner;
    private string? _propertyApplicabilityToolTipText;

    private void InitializePropertyPane()
    {
        _propertyTabs.ApplyTheme(_shellTheme);
        ApplyPropertyTabIconSize(_toolbarIconSize);
        ConfigureSinglePropertyTab(enabled: false);
        _propertyTabs.SelectedTab = _generalPropertyTab;
        _generalPropertyTab.Controls.Add(_propertyGrid);

        _propertyTabs.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingPropertyTabs)
                return;

            RememberPresentedPropertyExpansionState();

            if (IsWidgetPropertyTarget(_propertyGridTarget) &&
                TryGetPropertySection(_propertyTabs.SelectedTab, out var section))
            {
                _lastWidgetPropertySection = section;
            }

            PreserveEditorInputFocus(() =>
            {
                AttachPropertyGridToActiveTab();
                RefreshPropertyGridView();
            });
        };
        _propertyTabs.Enter += (_, _) => TrackEditorInputSurface(EditorInputSurface.Properties);
        _propertyTabs.MouseDown += (_, _) => TrackEditorInputSurface(EditorInputSurface.Properties);

        _propertyGrid.HandleCreated += (_, _) =>
        {
            _propertyGridColumnsInitialized = false;
            AttachPropertyApplicabilityToolTip(_propertyGrid);
            SchedulePropertyGridPresentation();
        };
        FormClosed += (_, _) => _propertyApplicabilityToolTip.Dispose();
    }

    private void AttachPropertyApplicabilityToolTip(Control control)
    {
        if (!_propertyApplicabilityToolTipControls.Add(control))
            return;

        control.MouseMove += PropertyApplicabilityToolTipMouseMove;
        control.MouseLeave += PropertyApplicabilityToolTipMouseLeave;
        control.ControlAdded += (_, e) =>
        {
            if (e.Control is { } child)
                AttachPropertyApplicabilityToolTip(child);
        };

        foreach (Control child in control.Controls)
            AttachPropertyApplicabilityToolTip(child);
    }

    private void PropertyApplicabilityToolTipMouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not Control control || !control.IsHandleCreated)
            return;

        var screenPoint = control.PointToScreen(e.Location);
        AccessibleObject? hit = null;
        try
        {
            hit = control.AccessibilityObject.HitTest(screenPoint.X, screenPoint.Y) ??
                  _propertyGrid.AccessibilityObject.HitTest(screenPoint.X, screenPoint.Y);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // PropertyGrid may rebuild its accessibility tree while a contextual edit commits.
        }

        var text = hit?.Description?.Trim();
        if ((string.IsNullOrWhiteSpace(text) ||
             !text.StartsWith("Not applicable for selected ", StringComparison.Ordinal)) &&
            !ReferenceEquals(control, _propertyGrid))
        {
            try
            {
                text = _propertyGrid.AccessibilityObject
                    .HitTest(screenPoint.X, screenPoint.Y)?
                    .Description?
                    .Trim();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                text = null;
            }
        }

        if (string.IsNullOrWhiteSpace(text) ||
            !text.StartsWith("Not applicable for selected ", StringComparison.Ordinal))
        {
            HidePropertyApplicabilityToolTip();
            return;
        }

        if (ReferenceEquals(_propertyApplicabilityToolTipOwner, control) &&
            string.Equals(_propertyApplicabilityToolTipText, text, StringComparison.Ordinal))
        {
            return;
        }

        HidePropertyApplicabilityToolTip();
        _propertyApplicabilityToolTipOwner = control;
        _propertyApplicabilityToolTipText = text;
        _propertyApplicabilityToolTip.Show(text, control, e.X + 12, e.Y + 18, 5000);
    }

    private void PropertyApplicabilityToolTipMouseLeave(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _propertyApplicabilityToolTipOwner))
            HidePropertyApplicabilityToolTip();
    }

    private void HidePropertyApplicabilityToolTip()
    {
        if (_propertyApplicabilityToolTipOwner is not null)
            _propertyApplicabilityToolTip.Hide(_propertyApplicabilityToolTipOwner);
        _propertyApplicabilityToolTipOwner = null;
        _propertyApplicabilityToolTipText = null;
    }

    private List<(TabPage Page, Image Image)> CreatePropertyTabIconReplacements(int size)
    {
        if (size is not (16 or 32 or 48))
            throw new ArgumentOutOfRangeException(nameof(size));

        var replacements = new List<(TabPage Page, Image Image)>();
        try
        {
            replacements.Add((_generalPropertyTab, CreateLucideToolbarImage(size, PropertyGeneralIcons)));
            replacements.Add((_appearancePropertyTab, CreateLucideToolbarImage(size, PropertyAppearanceIcons)));
            replacements.Add((_dataPropertyTab, CreateLucideToolbarImage(size, PropertyDataIcons)));
            replacements.Add((_gaugePropertyTab, CreateLucideToolbarImage(size, PropertyGaugeIcons)));
            replacements.Add((_statesPropertyTab, CreateLucideToolbarImage(size, PropertyStatesIcons)));
            replacements.Add((_imagePropertyTab, CreateLucideToolbarImage(size, PropertyImageIcons)));
            replacements.Add((_textPropertyTab, CreateLucideToolbarImage(size, PropertyTextIcons)));
            return replacements;
        }
        catch
        {
            foreach (var replacement in replacements)
                replacement.Image.Dispose();
            throw;
        }
    }

    private void CommitPropertyTabIconReplacements(int size, IReadOnlyList<(TabPage Page, Image Image)> replacements)
    {
        _propertyTabs.SetIconHeaders(size, replacements);

        var oldImages = _propertyTabImages.ToArray();
        _propertyTabImages.Clear();
        foreach (var replacement in replacements)
            _propertyTabImages.Add(replacement.Image);

        foreach (var image in oldImages)
            image.Dispose();
    }

    private void ApplyPropertyTabIconSize(int size)
    {
        var replacements = CreatePropertyTabIconReplacements(size);
        try
        {
            CommitPropertyTabIconReplacements(size, replacements);
        }
        catch
        {
            foreach (var replacement in replacements)
                replacement.Image.Dispose();
            throw;
        }
    }

    private void DisposePropertyTabImages()
    {
        foreach (var image in _propertyTabImages)
            image.Dispose();
        _propertyTabImages.Clear();
    }

    private void SetPropertyGridTarget(object? target)
    {
        RememberPresentedPropertyExpansionState();

        if (target is CanonicalWidgetDefinition selectedWidget && _selectedWidgets.Contains(selectedWidget))
        {
            var selected = GetOrderedSelectedWidgets();
            if (selected.Count > 1)
                target = new PropertyWidgetSelection(selected.ToArray());
        }

        PreserveEditorInputFocus(() =>
        {
            _propertyGridTarget = target;

            _updatingPropertyTabs = true;
            try
            {
                if (IsWidgetPropertyTarget(target))
                {
                    var includeData = CreatePropertyTargetView(target!, EditorPropertySection.Data).GetProperties().Count > 0;
                    var includeGauge = CreatePropertyTargetView(target!, EditorPropertySection.Gauge).GetProperties().Count > 0;
                    var includeStates = CreatePropertyTargetView(target!, EditorPropertySection.States).GetProperties().Count > 0;
                    var includeImage = CreatePropertyTargetView(target!, EditorPropertySection.Image).GetProperties().Count > 0;
                    var includeText = CreatePropertyTargetView(target!, EditorPropertySection.Text).GetProperties().Count > 0;
                    ConfigureWidgetPropertyTabs(includeData, includeGauge, includeStates, includeImage, includeText);

                    _generalPropertyTab.Enabled = true;
                    _appearancePropertyTab.Enabled = true;
                    _dataPropertyTab.Enabled = true;
                    _gaugePropertyTab.Enabled = true;
                    _statesPropertyTab.Enabled = true;
                    _imagePropertyTab.Enabled = true;
                    _textPropertyTab.Enabled = true;

                    var preferredPage = GetPropertyTab(_lastWidgetPropertySection);
                    _propertyTabs.SelectedTab = preferredPage is not null && _propertyTabs.TabPages.Contains(preferredPage)
                        ? preferredPage
                        : _generalPropertyTab;
                }
                else if (target is CanonicalCanvasDefinition)
                {
                    ConfigureCanvasPropertyTabs();
                    _generalPropertyTab.Enabled = true;
                    _appearancePropertyTab.Enabled = true;
                    _imagePropertyTab.Enabled = true;
                    _propertyTabs.SelectedTab = _generalPropertyTab;
                }
                else
                {
                    ConfigureSinglePropertyTab(enabled: false);
                    _propertyTabs.SelectedTab = _generalPropertyTab;
                }
            }
            finally
            {
                _updatingPropertyTabs = false;
            }

            AttachPropertyGridToActiveTab();
            RefreshPropertyGridView();
        });
    }

    private static bool IsWidgetPropertyTarget(object? target) =>
        target is CanonicalWidgetDefinition or PropertyWidgetSelection;

    private static bool IsNormalizedPropertyTarget(object? target) =>
        IsWidgetPropertyTarget(target) || target is CanonicalCanvasDefinition;

    private CustomTypeDescriptor CreatePropertyTargetView(object target, EditorPropertySection section) =>
        target switch
        {
            CanonicalWidgetDefinition widget => CreatePropertyView(widget, section),
            PropertyWidgetSelection selection => CreateMultiPropertyView(selection.Widgets, section),
            CanonicalCanvasDefinition canvas => CreatePropertyView(canvas, section),
            _ => throw new InvalidOperationException($"Unsupported property target '{target.GetType().Name}'.")
        };

    private void ConfigureWidgetPropertyTabs(bool includeData, bool includeGauge, bool includeStates, bool includeImage, bool includeText)
    {
        var desired = new List<TabPage>
        {
            _generalPropertyTab,
            _appearancePropertyTab
        };
        if (includeData)
            desired.Add(_dataPropertyTab);
        if (includeGauge)
            desired.Add(_gaugePropertyTab);
        if (includeStates)
            desired.Add(_statesPropertyTab);
        if (includeImage)
            desired.Add(_imagePropertyTab);
        if (includeText)
            desired.Add(_textPropertyTab);

        ConfigurePropertyTabs(desired);
    }

    private void ConfigureCanvasPropertyTabs() =>
        ConfigurePropertyTabs([
            _generalPropertyTab,
            _appearancePropertyTab,
            _imagePropertyTab
        ]);

    private void ConfigureSinglePropertyTab(bool enabled)
    {
        _generalPropertyTab.Enabled = enabled;
        ConfigurePropertyTabs([_generalPropertyTab]);
    }

    private void ConfigurePropertyTabs(IReadOnlyList<TabPage> desired)
    {
        var unchanged = _propertyTabs.TabPages.Count == desired.Count;
        if (unchanged)
        {
            for (var i = 0; i < desired.Count; i++)
            {
                if (!ReferenceEquals(_propertyTabs.TabPages[i], desired[i]))
                {
                    unchanged = false;
                    break;
                }
            }
        }

        if (unchanged)
            return;

        _propertyTabs.SuspendLayout();
        try
        {
            _propertyTabs.TabPages.Clear();
            _propertyTabs.TabPages.AddRange(desired.ToArray());
            _propertyTabs.RefreshIconHeaders();
        }
        finally
        {
            _propertyTabs.ResumeLayout();
        }
    }

    private void AttachPropertyGridToActiveTab()
    {
        var page = _propertyTabs.SelectedTab ?? _generalPropertyTab;
        if (!ReferenceEquals(_propertyGrid.Parent, page))
            page.Controls.Add(_propertyGrid);
    }

    private void RefreshPropertyGridView()
    {
        var target = _propertyGridTarget;
        if (target is null)
        {
            _presentedPropertySection = null;
            SetPropertyGridSelectedObject(null);
            return;
        }

        var section = IsNormalizedPropertyTarget(target) &&
                      TryGetPropertySection(_propertyTabs.SelectedTab, out var normalizedSection)
            ? normalizedSection
            : EditorPropertySection.All;

        _propertyGrid.PropertySort = section is EditorPropertySection.General or
            EditorPropertySection.Appearance or
            EditorPropertySection.Data or
            EditorPropertySection.Gauge or
            EditorPropertySection.States or
            EditorPropertySection.Image or
            EditorPropertySection.Text
                ? PropertySort.NoSort
                : PropertySort.Categorized;
        SetPropertyGridSelectedObject(CreatePropertyTargetView(target, section));
        _presentedPropertySection = section;
        SchedulePropertyGridPresentation();
    }

    private bool TryGetPropertySection(TabPage? page, out EditorPropertySection section)
    {
        if (ReferenceEquals(page, _generalPropertyTab))
        {
            section = EditorPropertySection.General;
            return true;
        }
        if (ReferenceEquals(page, _appearancePropertyTab))
        {
            section = EditorPropertySection.Appearance;
            return true;
        }
        if (ReferenceEquals(page, _dataPropertyTab))
        {
            section = EditorPropertySection.Data;
            return true;
        }
        if (ReferenceEquals(page, _gaugePropertyTab))
        {
            section = EditorPropertySection.Gauge;
            return true;
        }
        if (ReferenceEquals(page, _statesPropertyTab))
        {
            section = EditorPropertySection.States;
            return true;
        }
        if (ReferenceEquals(page, _imagePropertyTab))
        {
            section = EditorPropertySection.Image;
            return true;
        }
        if (ReferenceEquals(page, _textPropertyTab))
        {
            section = EditorPropertySection.Text;
            return true;
        }
        section = EditorPropertySection.All;
        return false;
    }

    private TabPage? GetPropertyTab(EditorPropertySection section) => section switch
    {
        EditorPropertySection.General => _generalPropertyTab,
        EditorPropertySection.Appearance => _appearancePropertyTab,
        EditorPropertySection.Data => _dataPropertyTab,
        EditorPropertySection.Gauge => _gaugePropertyTab,
        EditorPropertySection.States => _statesPropertyTab,
        EditorPropertySection.Image => _imagePropertyTab,
        EditorPropertySection.Text => _textPropertyTab,
        _ => null
    };

    private IReadOnlyDictionary<string, bool>? CapturePropertyExpansionState()
    {
        var selected = _propertyGrid.SelectedGridItem;
        if (selected is null)
            return null;

        var root = selected;
        while (root.Parent is not null)
            root = root.Parent;

        var expansion = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (GridItem item in root.GridItems)
        {
            if (item.PropertyDescriptor is PropertyGroupPropertyDescriptor && item.Label is not null)
                expansion[item.Label] = item.Expanded;
        }

        return expansion.Count == 0 ? null : expansion;
    }

    private void RememberPresentedPropertyExpansionState()
    {
        if (_presentedPropertySection is not EditorPropertySection section)
            return;

        var visibleState = CapturePropertyExpansionState();
        if (visibleState is not null)
            _propertyExpansionState.Remember(section, visibleState);
    }

    private void SchedulePropertyGridPresentation()
    {
        if (!IsHandleCreated || IsDisposed || Disposing)
            return;

        var generation = ++_propertyPresentationGeneration;
        BeginInvoke((Action)(() =>
        {
            if (generation != _propertyPresentationGeneration || IsDisposed || Disposing)
                return;

            if (!_propertyGridColumnsInitialized && TrySetPropertyGridColumnRatio(_propertyGrid, 0.5d))
                _propertyGridColumnsInitialized = true;

            if (IsNormalizedPropertyTarget(_propertyGridTarget) &&
                TryGetPropertySection(_propertyTabs.SelectedTab, out var section))
            {
                ApplyPropertyExpansionState(_propertyExpansionState.Get(section));
            }
        }));
    }

    private void ApplyPropertyExpansionState(IReadOnlyDictionary<string, bool> expansion)
    {
        var selected = _propertyGrid.SelectedGridItem;
        if (selected is null)
            return;

        var root = selected;
        while (root.Parent is not null)
            root = root.Parent;

        foreach (GridItem item in root.GridItems)
        {
            var label = item.Label;
            if (label is not null && expansion.TryGetValue(label, out var expanded))
                item.Expanded = expanded;
        }
    }

    private static bool TrySetPropertyGridColumnRatio(PropertyGrid grid, double labelFraction)
    {
        if (labelFraction <= 0d || labelFraction >= 1d)
            return false;

        try
        {
            var view = FindPropertyGridView(grid);
            if (view is null)
                return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = view.GetType();
            var ratioField = type.GetField("_labelRatio", flags) ?? type.GetField("labelRatio", flags);
            if (ratioField is null)
                return false;

            ratioField.SetValue(view, 1d / labelFraction);
            var setConstants = type.GetMethod("SetConstants", flags);
            setConstants?.Invoke(view, null);
            view.Invalidate();
            return true;
        }
        catch
        {
            return false;
        }

        static Control? FindPropertyGridView(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child.GetType().Name.Equals("PropertyGridView", StringComparison.Ordinal))
                    return child;

                var nested = FindPropertyGridView(child);
                if (nested is not null)
                    return nested;
            }

            return null;
        }
    }
}
