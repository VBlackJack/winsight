# Winsight v0.5.0 - 2026-11-23

Minor release driven by a field test against the Hyper-V console window (`vmconnect.exe`): a strict `actionableOnly` filter and lighter scenes, window titles of unrelated applications withheld by default, a cleaner `captureSource="screen"` rectangle, an OCR language fallback, and a warning on single-color captures. This release contains three breaking changes to tool output; read the upgrade notes before updating.

## Highlights

- **Strict `actionableOnly`**: `observe_scene(actionableOnly=true)` keeps an element only if it has at least one action other than `drag_to`, `right_click`, and `double_click`, and is visible or supports `scroll_into_view`. Separators, static texts, and hidden menu items are dropped. The same rule drives the sparse-UIA detection behind `surfaceHint` and the labels drawn by `capture_scene`. `any_change` verification under `actionableOnly` is unchanged and still sees static text changes.
- **Lighter scenes (`windowHandle`)**: scene elements carry a scalar `windowHandle` instead of a full `window` object; window bounds are sent once in `windows[]`.
- **Window titles withheld by default**: `list_windows` without `processId` returns `title: ""` and `titleWithheld: true` for every process other than the session target; pass `includeTitles=true` to opt in. The `winsight://windows` resource follows the same default. `get_focused_element` withholds the top-level window title and the element name when the focus is outside the target.
- **Cleaner `captureSource="screen"`**: only the DWM extended frame of the window is copied, so the invisible resize-border strips stay transparent instead of showing pixels of neighboring windows. The image keeps the window-rectangle size, so pixels still equal window-relative coordinates for `click_point`.
- **OCR language fallback**: `read_text_visually(language=...)` falls back to an installed recognizer of the same script when the requested language has no OCR component (same primary language first, then the user-profile recognizer, then the first same-script one). The result reports `language` (used), `requestedLanguage`, and a `warning` starting with `ocr_language_fallback:`. A malformed tag returns `ocr_language_invalid` instead of an exception.
- **Uniform capture warning (`region_uniform`)**: `capture_screenshot`, `capture_scene`, and `read_text_visually` attach a `warning` starting with `region_uniform:` when the captured area is a single color, so an agent checks the application (for example a stale or disconnected session) before concluding that nothing is displayed.

## Upgrade notes (breaking changes)

- `observe_scene` elements no longer carry `window`. Read `element.windowHandle` and look the bounds up in `windows[]`. DOM elements have no `windowHandle`.
- `list_windows` desktop-wide mode returns empty titles by default. Pick a target by `processName`, `className`, and bounds, or pass `includeTitles=true` when a title is needed. Per-process mode (`processId` set) is unchanged.
- `get_focused_element`, and results embedding the focus information such as `send_key_chord` and clipboard results, return `topLevelWindowTitle: ""` and `name: ""` with `titleWithheld: true` when the focused window does not belong to the target.

## Known limits

- With `captureSource="screen"`, Windows 11 rounded corners can still show a few pixels of a neighboring window inside the DWM frame.
- `region_uniform` also fires on a legitimately blank window. On a full-window read the title bar usually prevents it; pass a `region` to check a specific pane.
- The occlusion error of `captureSource="screen"` still includes the title of the occluding window.

## Install

**Recommended for desktop users:** download and run `Winsight-Setup-0.5.0-win-x64.exe`. The English/French wizard installs for the current Windows user without administrator rights and configures supported MCP clients.

For the portable archive, download `winsight-0.5.0-win-x64.zip` and `SHA256SUMS.txt`, verify the SHA-256 hash with `Get-FileHash -Algorithm SHA256`, extract all files into a dedicated directory, and point your MCP client to `winsight-mcp.exe`.

## Validation

- Release build: 0 warnings, 0 errors (`net10.0-windows10.0.19041.0`).
- Unit tests: 757 passed, 0 failed (`545` Core, `212` MCP, including documentation typography guards; 2 ONNX real-model integration tests skipped when local model fixtures are absent).
- The screen-capture border mask, the OCR language fallback, and the focus withholding are covered by unit tests on their pure logic; they were not validated on a live desktop for this release.

---

[Notes de version en francais](fr/release-notes/v0.5.0.md)
