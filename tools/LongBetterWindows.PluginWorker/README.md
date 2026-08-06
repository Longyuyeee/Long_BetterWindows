# Long Plugin Worker Experiment

This project is the experimental headless worker. It is built and launched only by automated tests.

- It does not load installed or built-in plugins.
- It does not reference LongBetterWindows.Host, WPF, or any host capability service.
- Without `--workload`, it accepts the internal synthetic fault-injection commands.
- With `--workload`, it loads exactly one bounded internal workload whose identity must match the authenticated session; synthetic commands are not available in this mode.
- Its only reverse method is the read-only `host.capability.query`; it cannot read user data or invoke a Host service.
- The host creates the named pipe and validates the spawned process id plus an ephemeral 256-bit nonce.
- Host resources are session-owned leases and are released when the worker closes or crashes.
- It is not copied into the Long Assistant product package and is not a supported public SDK surface.

Production plugin migration remains disabled while migration compatibility and release gates are incomplete.
