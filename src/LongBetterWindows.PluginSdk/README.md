# Long Assistant .NET Plugin SDK

`LongBetterWindows.PluginSdk` is the stable compile-time boundary for native
Long Assistant plugins. It contains:

- plugin lifecycle and optional extension interfaces;
- manifest, command, localization, and host response contracts;
- capability interfaces and their data-transfer models.
- the canonical service-to-capability map used by production and test hosts.

The SDK intentionally excludes host services, windows, engine internals, and
interaction coordinators. Native plugins should reference this project (or its
future NuGet package) and obtain operating-system features through `IHostApi`.

The assembly version follows the native SDK compatibility line. The API version
exposed by `ApiVersion.Current` remains the protocol compatibility source of
truth.

Headless lifecycle and permission tests are provided separately by
[`LongBetterWindows.PluginSdk.Testing`](../../sdk/dotnet/LongBetterWindows.PluginSdk.Testing/README.md).
