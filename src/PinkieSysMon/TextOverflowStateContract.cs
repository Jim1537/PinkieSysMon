using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

/// <summary>
/// Keeps text overflow state consistent with the shared widget width contract. Text/Value owns
/// one presentation directly on WidgetDefinition; state-content widgets such as Binary and Power
/// own one presentation per value-backed state while Width remains shared geometry.
/// </summary>
internal static class TextOverflowStateContract
{
    public static bool UsesTextPresentation(WidgetDefinition widget)
    {
        if (WidgetTypeContract.Is(widget.Type, WidgetTypeContract.Value))
            return true;

        if (!StateVisualProfileContract.SupportsValueSource(widget.Type))
            return false;

        widget.EnsureStateVisualProfiles();
        return widget.Profiles!.Values.Any(profile =>
            StateVisualProfileContract.IsValueSource(profile.SourceType));
    }

    public static bool SupportsAutoWidth(WidgetDefinition widget) =>
        WidgetTypeContract.Is(widget.Type, WidgetTypeContract.Value) ||
        StateVisualProfileContract.SupportsValueSource(widget.Type) &&
        StateVisualProfileContract.UsesOnlyValueSources(widget);

    public static IReadOnlyList<string> GetSupportedModes(WidgetDefinition widget) =>
        SupportsAutoWidth(widget)
            ? ValueOverflowContract.SupportedModes
            : ValueOverflowContract.ConstrainedModes;

    public static bool NormalizeStoredState(WidgetDefinition widget)
    {
        if (!UsesTextPresentation(widget))
            return false;

        if (WidgetTypeContract.Is(widget.Type, WidgetTypeContract.Value))
            return NormalizeValueWidget(widget);

        return NormalizeStateValueProfiles(widget);
    }

    public static bool ApplyWidthChange(
        WidgetDefinition widget,
        float currentContentWidth,
        float minimumWidth)
    {
        if (!UsesTextPresentation(widget))
            return false;

        if (WidgetTypeContract.Is(widget.Type, WidgetTypeContract.Value))
            return ApplyValueWidthChange(widget, currentContentWidth, minimumWidth);

        var changed = false;
        if (SupportsAutoWidth(widget) && widget.Width <= 0f)
        {
            foreach (var profile in EnumerateValueProfiles(widget))
            {
                var text = RequireText(widget, profile);
                if (!string.Equals(text.OverflowMode, ValueOverflowContract.None, StringComparison.Ordinal))
                {
                    text.OverflowMode = ValueOverflowContract.None;
                    changed = true;
                }
            }

            return changed;
        }

        if (widget.Width <= 0f)
        {
            widget.Width = ResolveConstraintWidth(currentContentWidth, minimumWidth);
            changed = true;
        }

        return NormalizeStateValueProfiles(widget) || changed;
    }

    /// <summary>
    /// Applies the global Text/Value Overflow Mode edit. Stateful value-source widgets use the
    /// state-key overload.
    /// </summary>
    public static bool ApplyOverflowModeChange(
        WidgetDefinition widget,
        float currentContentWidth,
        float minimumWidth)
    {
        if (!UsesTextPresentation(widget))
            return false;

        if (!WidgetTypeContract.Is(widget.Type, WidgetTypeContract.Value))
            return ApplyContextChange(widget, currentContentWidth, minimumWidth);

        var changed = false;
        var mode = ValueOverflowContract.Normalize(widget.OverflowMode);

        if (mode == ValueOverflowContract.None)
        {
            if (SupportsAutoWidth(widget))
            {
                if (widget.Width != 0f)
                {
                    widget.Width = 0f;
                    changed = true;
                }
            }
            else
            {
                mode = ValueOverflowContract.Clip;
            }
        }
        else if (widget.Width <= 0f)
        {
            widget.Width = ResolveConstraintWidth(currentContentWidth, minimumWidth);
            changed = true;
        }

        if (!string.Equals(widget.OverflowMode, mode, StringComparison.Ordinal))
        {
            widget.OverflowMode = mode;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Applies an Overflow Mode edit for one value-backed state. None owns shared auto-width:
    /// when every state is value-backed it switches Width to zero and normalizes every value state
    /// to None. A concrete mode materializes shared Width and therefore normalizes sibling None
    /// modes to Clip. Mixed value/image state widgets never permit None.
    /// </summary>
    public static bool ApplyOverflowModeChange(
        WidgetDefinition widget,
        string stateKey,
        float currentContentWidth,
        float minimumWidth)
    {
        if (!StateVisualProfileContract.SupportsValueSource(widget.Type))
            return ApplyOverflowModeChange(widget, currentContentWidth, minimumWidth);

        widget.EnsureStateVisualProfiles();
        if (widget.Profiles is null ||
            !widget.Profiles.TryGetValue(stateKey, out var profile) ||
            profile is null ||
            !StateVisualProfileContract.IsValueSource(profile.SourceType))
        {
            return false;
        }

        var text = RequireText(widget, profile);
        var mode = ValueOverflowContract.Normalize(text.OverflowMode);
        var changed = false;

        if (mode == ValueOverflowContract.None)
        {
            if (SupportsAutoWidth(widget))
            {
                if (widget.Width != 0f)
                {
                    widget.Width = 0f;
                    changed = true;
                }

                foreach (var valueProfile in EnumerateValueProfiles(widget))
                {
                    var siblingText = RequireText(widget, valueProfile);
                    if (!string.Equals(siblingText.OverflowMode, ValueOverflowContract.None, StringComparison.Ordinal))
                    {
                        siblingText.OverflowMode = ValueOverflowContract.None;
                        changed = true;
                    }
                }

                return changed;
            }

            mode = ValueOverflowContract.Clip;
        }
        else if (widget.Width <= 0f)
        {
            widget.Width = ResolveConstraintWidth(currentContentWidth, minimumWidth);
            changed = true;
        }

        if (!string.Equals(text.OverflowMode, mode, StringComparison.Ordinal))
        {
            text.OverflowMode = mode;
            changed = true;
        }

        return NormalizeStateValueProfiles(widget) || changed;
    }

    /// <summary>
    /// Reconciles state after a source/profile change. Switching a state-content widget from
    /// value-only auto width to a mixed value/icon or value/file container materializes shared
    /// Width. Newly active value states then adopt the mode required by the resulting geometry.
    /// </summary>
    public static bool ApplyContextChange(
        WidgetDefinition widget,
        float currentContentWidth,
        float minimumWidth)
    {
        if (WidgetTypeContract.Is(widget.Type, WidgetTypeContract.Value))
            return UsesTextPresentation(widget) && NormalizeValueWidget(widget);

        if (!StateVisualProfileContract.SupportsValueSource(widget.Type))
            return false;

        // Source Type changes can remove the final value-backed state. Shared auto Width must
        // still be materialized before the widget becomes an icon/file container, even though
        // the resulting widget no longer has active Text presentation.
        var changed = false;
        if (!SupportsAutoWidth(widget) && widget.Width <= 0f)
        {
            widget.Width = ResolveConstraintWidth(currentContentWidth, minimumWidth);
            changed = true;
        }

        return (UsesTextPresentation(widget) && NormalizeStateValueProfiles(widget)) || changed;
    }

    public static bool UsesTextPresentation(CanonicalDashboard.WidgetDefinition widget)
    {
        if (widget is CanonicalDashboard.ValueWidgetDefinition)
            return true;

        return widget is CanonicalDashboard.StateVisualWidgetDefinition stateWidget &&
               stateWidget.Profiles.Values.Any(profile => CanonicalDashboard.StateContentType.IsValue(profile.ContentType));
    }

    public static bool SupportsAutoWidth(CanonicalDashboard.WidgetDefinition widget) =>
        widget is CanonicalDashboard.ValueWidgetDefinition ||
        widget is CanonicalDashboard.StateVisualWidgetDefinition stateWidget && stateWidget.UsesOnlyValueSources();

    public static IReadOnlyList<string> GetSupportedModes(CanonicalDashboard.WidgetDefinition widget) =>
        SupportsAutoWidth(widget)
            ? ValueOverflowContract.SupportedModes
            : ValueOverflowContract.ConstrainedModes;

    public static bool NormalizeStoredState(CanonicalDashboard.WidgetDefinition widget)
    {
        if (!UsesTextPresentation(widget))
            return false;

        return widget is CanonicalDashboard.ValueWidgetDefinition value
            ? NormalizeValueWidget(value)
            : NormalizeStateValueProfiles((CanonicalDashboard.StateVisualWidgetDefinition)widget);
    }

    public static bool ApplyWidthChange(
        CanonicalDashboard.WidgetDefinition widget,
        float currentContentWidth,
        float minimumWidth)
    {
        if (!UsesTextPresentation(widget))
            return false;

        if (widget is CanonicalDashboard.ValueWidgetDefinition value)
            return ApplyValueWidthChange(value, currentContentWidth, minimumWidth);

        var stateWidget = (CanonicalDashboard.StateVisualWidgetDefinition)widget;
        var changed = false;
        if (SupportsAutoWidth(widget) && widget.Width <= 0f)
        {
            foreach (var profile in EnumerateValueProfiles(stateWidget))
            {
                if (!string.Equals(profile.TextPresentation.OverflowMode, ValueOverflowContract.None, StringComparison.Ordinal))
                {
                    profile.TextPresentation.OverflowMode = ValueOverflowContract.None;
                    changed = true;
                }
            }
            return changed;
        }

        if (widget.Width <= 0f)
        {
            widget.Width = ResolveConstraintWidth(currentContentWidth, minimumWidth);
            changed = true;
        }

        return NormalizeStateValueProfiles(stateWidget) || changed;
    }

    public static bool ApplyOverflowModeChange(
        CanonicalDashboard.WidgetDefinition widget,
        float currentContentWidth,
        float minimumWidth)
    {
        if (!UsesTextPresentation(widget))
            return false;

        if (widget is not CanonicalDashboard.ValueWidgetDefinition value)
            return ApplyContextChange(widget, currentContentWidth, minimumWidth);

        var changed = false;
        var mode = ValueOverflowContract.Normalize(value.TextPresentation.OverflowMode);
        if (mode == ValueOverflowContract.None)
        {
            if (SupportsAutoWidth(widget))
            {
                if (widget.Width != 0f)
                {
                    widget.Width = 0f;
                    changed = true;
                }
            }
            else
            {
                mode = ValueOverflowContract.Clip;
            }
        }
        else if (widget.Width <= 0f)
        {
            widget.Width = ResolveConstraintWidth(currentContentWidth, minimumWidth);
            changed = true;
        }

        if (!string.Equals(value.TextPresentation.OverflowMode, mode, StringComparison.Ordinal))
        {
            value.TextPresentation.OverflowMode = mode;
            changed = true;
        }
        return changed;
    }

    public static bool ApplyOverflowModeChange(
        CanonicalDashboard.WidgetDefinition widget,
        string stateKey,
        float currentContentWidth,
        float minimumWidth)
    {
        if (widget is not CanonicalDashboard.StateVisualWidgetDefinition stateWidget)
            return ApplyOverflowModeChange(widget, currentContentWidth, minimumWidth);

        EnsureProfiles(stateWidget);
        if (!stateWidget.Profiles.TryGetValue(stateKey, out var profile) ||
            profile is null ||
            !CanonicalDashboard.StateContentType.IsValue(profile.ContentType))
        {
            return false;
        }

        var mode = ValueOverflowContract.Normalize(profile.TextPresentation.OverflowMode);
        var changed = false;
        if (mode == ValueOverflowContract.None)
        {
            if (SupportsAutoWidth(widget))
            {
                if (widget.Width != 0f)
                {
                    widget.Width = 0f;
                    changed = true;
                }

                foreach (var valueProfile in EnumerateValueProfiles(stateWidget))
                {
                    if (!string.Equals(valueProfile.TextPresentation.OverflowMode, ValueOverflowContract.None, StringComparison.Ordinal))
                    {
                        valueProfile.TextPresentation.OverflowMode = ValueOverflowContract.None;
                        changed = true;
                    }
                }
                return changed;
            }
            mode = ValueOverflowContract.Clip;
        }
        else if (widget.Width <= 0f)
        {
            widget.Width = ResolveConstraintWidth(currentContentWidth, minimumWidth);
            changed = true;
        }

        if (!string.Equals(profile.TextPresentation.OverflowMode, mode, StringComparison.Ordinal))
        {
            profile.TextPresentation.OverflowMode = mode;
            changed = true;
        }
        return NormalizeStateValueProfiles(stateWidget) || changed;
    }

    public static bool ApplyContextChange(
        CanonicalDashboard.WidgetDefinition widget,
        float currentContentWidth,
        float minimumWidth)
    {
        if (widget is CanonicalDashboard.ValueWidgetDefinition value)
            return NormalizeValueWidget(value);

        if (widget is not CanonicalDashboard.StateVisualWidgetDefinition stateWidget)
            return false;

        EnsureProfiles(stateWidget);
        var changed = false;
        if (!SupportsAutoWidth(widget) && widget.Width <= 0f)
        {
            widget.Width = ResolveConstraintWidth(currentContentWidth, minimumWidth);
            changed = true;
        }

        return (UsesTextPresentation(widget) && NormalizeStateValueProfiles(stateWidget)) || changed;
    }

    public static void EnsureProfiles(CanonicalDashboard.StateVisualWidgetDefinition widget)
    {
        var specs = StateVisualProfileContract.GetSpecs(widget.Type);
        var normalized = new Dictionary<string, CanonicalDashboard.StateVisualProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            if (widget.Profiles.TryGetValue(spec.Key, out var existing) && existing is not null)
            {
                normalized[spec.Key] = existing;
                continue;
            }

            var valueContent = StateVisualProfileContract.IsValueSource(spec.DefaultSourceType);
            normalized[spec.Key] = new CanonicalDashboard.StateVisualProfileDefinition
            {
                ContentType = valueContent ? CanonicalDashboard.StateContentType.Value : CanonicalDashboard.StateContentType.Image,
                Asset = new CanonicalDashboard.ImageAssetPresentationDefinition
                {
                    SourceType = StateVisualProfileContract.IsFileSource(spec.DefaultSourceType)
                        ? CanonicalDashboard.ImageAssetSourceType.File
                        : CanonicalDashboard.ImageAssetSourceType.Icon,
                    Source = valueContent ? null : spec.DefaultSource,
                    Fit = spec.DefaultFit ?? StateVisualProfileContract.FitContain,
                    Loop = spec.DefaultLoop ?? true
                },
                Color = spec.DefaultColor,
                Opacity = spec.DefaultOpacity,
                TextPresentation = new CanonicalDashboard.TextPresentationDefinition()
            };
        }
        widget.Profiles = normalized;
    }

    private static bool NormalizeValueWidget(CanonicalDashboard.ValueWidgetDefinition widget)
    {
        var targetMode = ValueOverflowContract.Normalize(widget.TextPresentation.OverflowMode);
        if (SupportsAutoWidth(widget) && widget.Width <= 0f)
            targetMode = ValueOverflowContract.None;
        else if (targetMode == ValueOverflowContract.None)
            targetMode = ValueOverflowContract.Clip;

        if (string.Equals(widget.TextPresentation.OverflowMode, targetMode, StringComparison.Ordinal))
            return false;
        widget.TextPresentation.OverflowMode = targetMode;
        return true;
    }

    private static bool ApplyValueWidthChange(
        CanonicalDashboard.ValueWidgetDefinition widget,
        float currentContentWidth,
        float minimumWidth)
    {
        var changed = false;
        if (SupportsAutoWidth(widget) && widget.Width <= 0f)
        {
            if (!string.Equals(widget.TextPresentation.OverflowMode, ValueOverflowContract.None, StringComparison.Ordinal))
            {
                widget.TextPresentation.OverflowMode = ValueOverflowContract.None;
                changed = true;
            }
            return changed;
        }

        if (widget.Width <= 0f)
        {
            widget.Width = ResolveConstraintWidth(currentContentWidth, minimumWidth);
            changed = true;
        }

        var normalized = ValueOverflowContract.Normalize(widget.TextPresentation.OverflowMode);
        if (normalized == ValueOverflowContract.None)
            normalized = ValueOverflowContract.Clip;
        if (!string.Equals(widget.TextPresentation.OverflowMode, normalized, StringComparison.Ordinal))
        {
            widget.TextPresentation.OverflowMode = normalized;
            changed = true;
        }
        return changed;
    }

    private static bool NormalizeStateValueProfiles(CanonicalDashboard.StateVisualWidgetDefinition widget)
    {
        EnsureProfiles(widget);
        var changed = false;
        var autoWidth = SupportsAutoWidth(widget) && widget.Width <= 0f;
        foreach (var profile in EnumerateValueProfiles(widget))
        {
            var normalized = ValueOverflowContract.Normalize(profile.TextPresentation.OverflowMode);
            var target = autoWidth
                ? ValueOverflowContract.None
                : normalized == ValueOverflowContract.None
                    ? ValueOverflowContract.Clip
                    : normalized;
            if (!string.Equals(profile.TextPresentation.OverflowMode, target, StringComparison.Ordinal))
            {
                profile.TextPresentation.OverflowMode = target;
                changed = true;
            }
        }
        return changed;
    }

    private static IEnumerable<CanonicalDashboard.StateVisualProfileDefinition> EnumerateValueProfiles(
        CanonicalDashboard.StateVisualWidgetDefinition widget)
    {
        EnsureProfiles(widget);
        return widget.Profiles.Values.Where(profile => CanonicalDashboard.StateContentType.IsValue(profile.ContentType));
    }

    private static bool NormalizeValueWidget(WidgetDefinition widget)
    {
        var targetMode = ValueOverflowContract.Normalize(widget.OverflowMode);
        if (SupportsAutoWidth(widget) && widget.Width <= 0f)
            targetMode = ValueOverflowContract.None;
        else if (targetMode == ValueOverflowContract.None)
            targetMode = ValueOverflowContract.Clip;

        if (string.Equals(widget.OverflowMode, targetMode, StringComparison.Ordinal))
            return false;

        widget.OverflowMode = targetMode;
        return true;
    }

    private static bool ApplyValueWidthChange(
        WidgetDefinition widget,
        float currentContentWidth,
        float minimumWidth)
    {
        var changed = false;
        if (SupportsAutoWidth(widget) && widget.Width <= 0f)
        {
            if (!string.Equals(widget.OverflowMode, ValueOverflowContract.None, StringComparison.Ordinal))
            {
                widget.OverflowMode = ValueOverflowContract.None;
                changed = true;
            }
            return changed;
        }

        if (widget.Width <= 0f)
        {
            widget.Width = ResolveConstraintWidth(currentContentWidth, minimumWidth);
            changed = true;
        }

        var normalized = ValueOverflowContract.Normalize(widget.OverflowMode);
        if (normalized == ValueOverflowContract.None)
            normalized = ValueOverflowContract.Clip;

        if (!string.Equals(widget.OverflowMode, normalized, StringComparison.Ordinal))
        {
            widget.OverflowMode = normalized;
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeStateValueProfiles(WidgetDefinition widget)
    {
        var changed = false;
        var autoWidth = SupportsAutoWidth(widget) && widget.Width <= 0f;
        foreach (var profile in EnumerateValueProfiles(widget))
        {
            var text = RequireText(widget, profile);
            var normalized = ValueOverflowContract.Normalize(text.OverflowMode);
            var target = autoWidth
                ? ValueOverflowContract.None
                : normalized == ValueOverflowContract.None
                    ? ValueOverflowContract.Clip
                    : normalized;

            if (!string.Equals(text.OverflowMode, target, StringComparison.Ordinal))
            {
                text.OverflowMode = target;
                changed = true;
            }
        }

        return changed;
    }

    private static IEnumerable<StateVisualProfileDefinition> EnumerateValueProfiles(WidgetDefinition widget)
    {
        widget.EnsureStateVisualProfiles();
        return widget.Profiles!.Values.Where(profile =>
            StateVisualProfileContract.IsValueSource(profile.SourceType));
    }

    private static TextPresentationDefinition RequireText(
        WidgetDefinition widget,
        StateVisualProfileDefinition profile) =>
        profile.Text ?? throw new InvalidDataException(
            $"State-content widget '{widget.Id}' requires a text presentation profile for every state.");

    private static float ResolveConstraintWidth(float currentContentWidth, float minimumWidth)
    {
        var minimum = float.IsFinite(minimumWidth) && minimumWidth > 0f ? minimumWidth : 1f;
        return float.IsFinite(currentContentWidth) && currentContentWidth > 1f
            ? Math.Max(minimum, currentContentWidth)
            : minimum;
    }
}
