# Shortcut

Shortcut displays artwork and an optional literal label, and opens a Windows Shell target after an explicit Runtime click. Add it from **Add widget → Shortcut**. Editor selection, preview, dragging, save, paste and Undo never invoke the target.

## Configure the target and artwork

In **General → Activation**, edit [Target](properties.md#shortcut-target), use its `…` file picker, and enter optional [Arguments](properties.md#shortcut-arguments) separately. The picker accepts all files and preserves a selected `.lnk`; it does not import the target into the dashboard. Folders, URLs and other Shell parsing names can be entered directly. Empty or unavailable targets are allowed. Windows chooses associations, launches applications and owns any system dialogs; PinkieSysMon adds no Shortcut error popup.

[Activation](properties.md#shortcut-activation) defaults to **Double click**. **Single click** invokes on the first completed click and suppresses the second click of an ordinary double-click. Dragging cancels activation. Native timing and movement thresholds come from Windows.

Choose the artwork in **General → Source**, then configure it in **Image**:

| Source | Acquisition |
| --- | --- |
| Auto | Windows Shell icon for the current Target. A missing custom Source also uses Auto. Unavailable icons leave the graphic empty. |
| Image | Dashboard image selected through the existing [image import workflow](image.md#image-dashboard-file). |
| Icon | Logical shared-library icon selected in the icon browser. |

The shared [image source selector](properties.md#image-source-type) clears only the custom Source when changed. A custom image is independent of Target. Fit, opacity and animation use the [Image presentation contract](image.md); foreground tint applies only to a tintable library resource. Shell icons keep their intrinsic colors and resolution. Width and Height define an arbitrary positive rectangular graphic viewport, independently of the source aspect ratio.

## Add a label

**Text → Label** configures [Label](properties.md#shortcut-label), [Position](properties.md#shortcut-label-position), [Gap](properties.md#shortcut-label-gap) and [Color](properties.md#shortcut-label-color). See the Label reference for the Editor's initial-name suggestion when changing Target. Edit the stored text or clear it to omit the caption.

The label shares the graphic width and uses the standard bundled-font, alignment, outline and [constrained overflow](properties.md#overflow-mode) properties. Its height follows the measured text block, including wrapped lines. The graphic center remains the rotation pivot for both artwork and label. The label paints after the graphic border, outside the rounded graphic clip; selection and shadow bounds include it.

See [Desktop input routing](../outputs/desktop.md#placement-dpi-and-mouse-routing) for Lock, activation geometry and panel dragging. Physical Outputs share rendering; this adds no inbound USB touch transport. Windows-promoted touch mouse events use the same recognizer, but touchscreen behavior requires device-specific acceptance. Current Windows acceptance is tracked in [Issue #101](https://github.com/Jim1537/PinkieSysMon/issues/101).

## Persistence and resources

Schema **26** introduces canonical `type: "shortcut"`. Migration from 25 changes only the schema version; existing widgets retain their fields. Earlier application versions cannot read a saved schema-26 document. Keep the earlier dashboard file when testing a source rollback; reverting application code does not downgrade saved data.

Shell extraction runs off the render/UI thread and is cached per renderer and exact Target: at most 256 entries and 4 MiB decoded Shell artwork. Process-wide extraction concurrency is two; disposal cancels queued work and releases late results. An in-flight native Shell extension cannot be forcibly interrupted. This budget is separate from the unchanged [image cache limits](image.md). Failed extraction is cached until the renderer is replaced. No Shell icon lookup or custom-image decode runs per frame. Output refresh intervals bound visible updates, including GIF playback.
