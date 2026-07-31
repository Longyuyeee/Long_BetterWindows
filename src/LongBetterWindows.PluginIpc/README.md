# Long Plugin IPC

`LongBetterWindows.PluginIpc` is the host-neutral LPWP 1.0 contract, framing,
and named-pipe client package shared by Long Assistant and Long Grid.

The LPWP 1.0 package provides:

- `long.plugin.ipc/1.0` request/response envelopes and standard error codes;
- four-byte little-endian length-prefixed UTF-8 JSON framing with a 1 MiB limit;
- a protocol-major and current-user-SID-derived pipe name;
- a client that requires `host.hello` before every other method and supports
  multiplexed request/response correlation;
- typed contracts for health, sanitized catalog, command invoke/cancel, and
  `plugin.open`;
- explicit request IDs plus `CancelCommandAsync` for cancellation on the same
  connection;
- strict response correlation and fail-closed protocol/error handling;
- machine-readable request/response Golden Fixtures under `fixtures/ipc` in
  the NuGet package.

The package does not reference Long Assistant Host, WPF, a plugin DLL, or a
concrete Long Grid implementation. Long Assistant owns the server-side
same-user security boundary, command authorization, lifecycle, and UI dispatch.
