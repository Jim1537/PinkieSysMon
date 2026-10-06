namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm
{
    private readonly EditorInputFocusState _editorInputFocusState = new();
    private int _inputFocusTrackingSuppression;
    private int _inputFocusRestoreGeneration;

    private void TrackEditorInputSurface(EditorInputSurface surface)
    {
        if (_inputFocusTrackingSuppression > 0)
            return;

        _editorInputFocusState.Claim(surface);
    }

    private void ClaimEditorInputSurface(EditorInputSurface surface, Control control)
    {
        TrackEditorInputSurface(surface);
        if (control.CanSelect && !control.ContainsFocus)
            control.Select();
    }

    private void ResetEditorInputSurface() =>
        TrackEditorInputSurface(EditorInputSurface.None);

    private bool IsEditorInputSurfaceActive(EditorInputSurface surface) =>
        _editorInputFocusState.Surface == surface &&
        GetActualEditorInputSurface() == surface;

    private EditorInputSurface GetActualEditorInputSurface()
    {
        if (_workspaceViewport.ContainsFocus)
            return EditorInputSurface.Canvas;
        if (_widgetTree.ContainsFocus)
            return EditorInputSurface.Layers;
        if (_propertyTabs.ContainsFocus || _propertyGrid.ContainsFocus)
            return EditorInputSurface.Properties;
        return EditorInputSurface.None;
    }

    private void PreserveEditorInputFocus(Action action)
    {
        var actual = GetActualEditorInputSurface();
        var snapshot = _editorInputFocusState.CaptureForPreservation(actual);
        _inputFocusTrackingSuppression++;
        try
        {
            action();
        }
        finally
        {
            _inputFocusTrackingSuppression--;
        }

        ScheduleEditorInputFocusRestore(snapshot);
    }

    private void SetPropertyGridSelectedObject(object? selectedObject)
    {
        var suppressLocally = _inputFocusTrackingSuppression == 0;
        if (suppressLocally)
            _inputFocusTrackingSuppression++;
        try
        {
            _propertyGrid.SelectedObject = selectedObject;
        }
        finally
        {
            if (suppressLocally)
                _inputFocusTrackingSuppression--;
        }
    }

    private void ScheduleEditorInputFocusRestore(EditorInputFocusSnapshot snapshot)
    {
        if (snapshot.Surface == EditorInputSurface.None ||
            !IsHandleCreated || IsDisposed || Disposing)
        {
            return;
        }

        var generation = ++_inputFocusRestoreGeneration;
        BeginInvoke((Action)(() =>
        {
            if (generation != _inputFocusRestoreGeneration || IsDisposed || Disposing)
                return;
            if (!_editorInputFocusState.CanRestore(snapshot))
                return;

            var actual = GetActualEditorInputSurface();
            if (actual != snapshot.Surface && actual != EditorInputSurface.Properties)
                return;

            Control? control = snapshot.Surface switch
            {
                EditorInputSurface.Canvas => _workspaceViewport,
                EditorInputSurface.Layers => _widgetTree,
                EditorInputSurface.Properties => _propertyGrid,
                _ => null
            };
            if (control is null || !control.CanSelect)
                return;

            _inputFocusTrackingSuppression++;
            try
            {
                control.Select();
            }
            finally
            {
                _inputFocusTrackingSuppression--;
            }
        }));
    }
}
