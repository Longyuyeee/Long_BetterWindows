# Long Web UI Kit Preview

Reference WebView plugin for UI Kit `1.3.0`. The sample relies entirely on the
CSS and `window.LongUI` helpers injected by Long Assistant. It intentionally
contains no copied stylesheet, remote resource, or host capability request.

Validate it with the production validator:

```powershell
.\validate-plugin.ps1 -Path .\samples\LongWebUiKitPreview
```

Open the validated directory as a local plugin to inspect light/dark themes,
keyboard focus, reduced motion, high contrast, responsive layout, content
states, progress semantics, in-plugin toast feedback, and dialog focus behavior.

Capture the engineering visual regression matrix:

```powershell
.\capture-web-ui-kit-matrix.ps1 -OutputDirectory artifacts\quality\web-ui-kit-<commit>
```

The matrix covers both themes, high contrast, reduced motion, and the 920/640
pixel layouts. It does not replace physical DPI, Windows forced-colors, or
assistive-technology validation.

The environment badge consumes `LongUI.onLanguageChanged` and
`LongUI.onViewportChanged`. Both subscriptions immediately receive the current
snapshot and return an unsubscribe function.
