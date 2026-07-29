# Long Assistant .NET Plugin SDK Testing

This package provides a headless contract test host for native plugins. It does
not reference or start the WPF host.

```csharp
var plugin = new MyPluginImpl();
var host = new PluginTestHost();

PluginContractHarness.VerifyIdentity(plugin, manifest);
await PluginContractHarness.VerifyLifecycleAsync(plugin, host);
PluginContractHarness.VerifyCapabilityDenied(
    host,
    api => api.HotKey,
    "system.hotkey");
```

Register capability test doubles with `host.Grant<IHotKeyService>(fakeHotKey)`.
The host grants the production capability mapped to that service and records
the last denied access. The harness also covers command execution, language
callbacks, and repeatable resource release.
