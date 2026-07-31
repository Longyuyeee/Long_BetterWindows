# Long Plugin IPC

`LongBetterWindows.PluginIpc` is the host-neutral LPWP 1.0 contract, framing,
and named-pipe client package shared by Long Assistant and Long Grid.

The first PR-C slice provides:

- `long.plugin.ipc/1.0` request/response envelopes and standard error codes;
- four-byte little-endian length-prefixed UTF-8 JSON framing with a 1 MiB limit;
- a protocol-major and current-user-SID-derived pipe name;
- a client that requires `host.hello` before every other method;
- strict response correlation and fail-closed protocol/error handling.

The package does not reference Long Assistant Host, WPF, a plugin DLL, or a
concrete Long Grid implementation. Host catalog, command, cancellation, and
open-surface endpoints are delivered by the following PR-C slices.
