# Plugin CI templates

These templates are intentionally stored outside `.github/workflows` so they do
not run against the Long Assistant repository itself. Copy the matching file to
`.github/workflows/plugin-ci.yml`, then replace the `PLUGIN_*` paths.

- `native-plugin.yml`: restore and run generated .NET contract tests, build the
  plugin, and production-validate the staged plugin directory.
- `script-plugin.yml`: production-validate and deterministically package a C#
  Script plugin.
- `web-plugin.yml`: run npm tests, production-validate, and deterministically
  package a Web plugin.

The validation and packaging commands assume the plugin is developed in a Long
Assistant repository checkout. When the validator and SDK are published as
standalone packages, external repositories can replace project/script paths with
the corresponding pinned package versions.
