# Long Web UI Kit Preview

Reference WebView plugin for UI Kit `1.1.0`. The sample relies entirely on the
CSS and `window.LongUI` helpers injected by Long Assistant. It intentionally
contains no copied stylesheet, remote resource, or host capability request.

Validate it with the production validator:

```powershell
.\validate-plugin.ps1 -Path .\samples\LongWebUiKitPreview
```

Open the validated directory as a local plugin to inspect light/dark themes,
keyboard focus, reduced motion, high contrast, responsive layout, content
states, progress semantics, and dialog focus behavior.
