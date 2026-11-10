# FIXED37 — optional LSL integration

- Per-experiment LSL settings with per-machine desktop/environment or browser URL overrides.
- Shared recorder marker hook and run lifecycle/context markers; unchanged CSV header and extended run metadata.
- Native worker transport, loopback WebSocket bridge, state stream and exact live BTH/LOFAR arrays with fixed dimensions and optional average downsampling.
- Recorded inbound start/pause/resume, marker and external alert commands with phase/state gates, literal text, correlation IDs and shared alert expiry.
- Connection status and result events, origin-restricted bridge, examples, protocol checks and LslDemo package.
- Vendored official liblsl 1.16.2 Windows x64 / macOS Intel libraries and C# binding. Universal macOS library build script provided; Apple Silicon runtime and signing/notarization require macOS verification.
