# Headless Reference Workload

This PX6B-5 assembly validates the bounded plugin worker loading contract.

- It is not a production plugin and is absent from the authoritative catalog.
- It references only the internal PluginIpc contract, with no Host, WPF, file, network, environment, clipboard, or system service access.
- It exposes deterministic SHA-256 and cancellable delay commands over explicit request data.
- It is built for automated real-process tests and is not copied into the product package.
