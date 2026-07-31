# Long IPC Probe

`long-ipc-probe` is a host-neutral LPWP 1.0 interoperability client. Start
Long助手 normally, then run:

```powershell
dotnet run --project tools/LongBetterWindows.PluginIpcProbe -- --ping
dotnet run --project tools/LongBetterWindows.PluginIpcProbe -- --get com.long.quick-note
dotnet run --project tools/LongBetterWindows.PluginIpcProbe -- --invoke <plugin-id> <command-id> --arg key=value
dotnet run --project tools/LongBetterWindows.PluginIpcProbe -- --open <plugin-id>
```

With no operation it performs `host.hello` and prints the sanitized plugin
catalog. Output is JSON; exit code `2` is a broker error and `3` means the host
was unavailable. The tool references only `LongBetterWindows.PluginIpc`.
