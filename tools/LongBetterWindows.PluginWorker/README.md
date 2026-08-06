# Long Plugin Worker Experiment

This project is the PX6B-2 synthetic headless worker. It is built and launched only by automated tests.

- It does not load installed or built-in plugins.
- It does not reference LongBetterWindows.Host, WPF, or any host capability service.
- It accepts only the internal `long.plugin.worker/experimental-1` lifecycle and synthetic command contract.
- The host creates the named pipe and validates the spawned process id plus an ephemeral 256-bit nonce.
- It is not copied into the Long Assistant product package and is not a supported public SDK surface.

Production plugin migration remains disabled until capability proxy, resource lease, compatibility, and release gates are implemented.
