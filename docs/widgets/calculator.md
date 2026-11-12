# Calculator

## User Guide

Add **Calculator** from the specialized section of **Add Widget** in the Dashboard Editor. The default 240 × 330 Panel contains a readout and fixed keypad. Resize or rotate the Panel normally; the buttons, text, spacing, and clickable regions scale together.

Only a **Windows Desktop Output** accepts mouse input. Editor preview and physical-device Outputs draw the same keypad with a **0** readout but never perform calculations, capture input, or use the clipboard. Calculator is independent of telemetry.

| Keys | Behavior |
| --- | --- |
| 0–9, decimal point, ±, BS (Backspace) | Enter and edit the current number |
| +, −, ×, ÷, = | Left-to-right chained arithmetic; equals can repeat the last operation |
| C, CE | Clear everything, or clear the current entry |
| Copy, Paste | Copy the numeric readout to the Windows clipboard, or enter a valid numeric clipboard value |

The bottom-right **=** key spans the final two keypad rows; **Copy** and **Paste** share the remaining three cells on the last row.

Divide-by-zero and unrepresentable results show **Error**; use C or CE to recover. Paste accepts a single finite signed decimal value (including ordinary scientific notation), but ignores ambiguous, nonnumeric, or unavailable clipboard contents. Copy/Paste are activated only by clicking their Desktop keys. Clipboard failures do not display modal dialogs.

### Interaction and Action

Calculator keys always take precedence over the inherited **Action**. Configure **Action → Activation/Target** to launch an optional Windows Shell action when clicking the readout or remaining unused Panel space. **Action = None** does not disable calculator keys. Desktop **Lock** permits the active key/Action hit regions while ordinary inactive areas remain click-through; an unlocked panel retains normal drag behavior. The Calculator does not accept keyboard input.

### Appearance

The dedicated **Calculator** property tab contains only:

| Group | Property |
| --- | --- |
| Font | Family |
| Buttons | Text Color, Fill Color, Border Width, Border Color, Corner Radius |
| Display | Text Color, Fill Color, Border Width, Border Color, Corner Radius |

Both key and readout fills are independent of their own borders. **Corner Radius** always shapes the fill, including with **Border Width = 0**. Zero width suppresses only the border and makes **Border Color** read-only; Corner Radius remains editable. With a positive width, the same radius rounds both fill and border. The readout fill defaults to transparent with no border, preserving earlier dashboards.

These settings are independent of the common **Panel** background, foreground, border, corner radius, shadow, padding, rotation, and Action. The common **General → Data Source → Metric** remains visible as **Not applicable**. A single bundled font family applies to keys and readout; font sizes are automatic. See [Property Dictionary](properties.md#calculator-specific-properties).

## Technical Information

Canonical type: `calculator` (introduced in schema 34). **Schema 35** adds five appearance fields with defaults: `buttonCornerRadius`, `displayFillColor`, `displayBorderWidth`, `displayBorderColor`, and `displayCornerRadius`. Existing schema-34 Calculator widgets migrate without changing earlier settings; six previous appearance fields remain `buttonFillColor`, `buttonBorderColor`, `buttonBorderWidth`, `buttonTextColor`, `displayTextColor`, and `fontFamily`.

The shared Skia renderer draws the same static keypad for Editor and physical Outputs. Each active Desktop renderer owns a separate transient calculation state per calculator widget ID; normal refreshes and window movement preserve it, while a new session or dashboard reload resets it. State and clipboard text are **never serialized** or published as telemetry.

Native Desktop mouse routing uses the existing [restricted input companion](../outputs/desktop.md#placement-dpi-and-mouse-routing), with the same key geometry shared by drawing and hit-testing; keys are resolved ahead of their Panel Action in the same Z layer. No separate native calculator control, additional process, USB touch handling, global mouse/keyboard hook, or Shell activation from a key click is involved.

A schema-35 dashboard cannot be reopened by a release that only understands schema 34 or earlier, even if it already supports Calculator. Retain a pre-migration dashboard copy when rollback to an older release might be necessary.
