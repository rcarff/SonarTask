# SonarTask LSL bridge and examples

Requires Python 3.10+ and a desktop browser. Native SonarTask builds use liblsl directly; only web builds need this bridge.

```sh
python -m venv .venv
# Windows: .venv\Scripts\activate
# macOS/Linux: source .venv/bin/activate
python -m pip install -r requirements.txt
python sonar_lsl_bridge.py --origin https://study.example.edu
```

The listener binds **127.0.0.1 only**, port 16571. Localhost HTTP/HTTPS origins are allowed automatically. Pass `--origin` repeatedly for exact study origins (scheme + host + optional port, no path). Requests without an allowed Origin are refused. One browser page may connect at a time. Use `--port 16572` with `?lsl=on&lslPort=16572` for another port. Each machine needs its own bridge. Consumers may discover LSL over the lab network; the WebSocket listener is never exposed on the network.

Open the study with `?lsl=on`, or run the `LslDemo` package. The status chip reports connection state. Then, in separate activated terminals:

```sh
python example_consumer.py
python example_waterfall_viewer.py
python example_controller.py pause
python example_controller.py resume
python example_controller.py marker "Operator note"
python example_controller.py alert "Contact" 135 --duration 10
```

These consumers/controllers work with desktop builds unchanged. Start consumers before the run to capture lifecycle markers. Use LabRecorder to record the four outlets to XDF. Multiple lab machines publish identical names: select the desired `source_id` if more than one is visible. The simple examples pick the first matching stream.

Timing: every message includes browser `performance.now()` seconds. The bridge uses LSL receipt time until a ping/pong estimates the clock offset from the lowest round-trip time. This includes scheduling and network uncertainty; it is not a hardware trigger. The binary protocol and timing limits are in [LSL.md](../../Documentation/LSL.md). Native publication and bridge LSL calls run on workers. Waterfall queues retain at most eight recent frames; markers are retained while connected. Data generated while disconnected may be lost; CSV recording continues independently.

Troubleshooting:

- Allow the browser's local-network permission when prompted. Loopback WebSocket behavior and Local Network Access policy vary by browser/version and study security headers. Test the deployed HTTPS origin on every supported browser, especially Safari; localhost testing alone does not establish deployed compatibility. If blocked, inspect the browser console and permission settings; do not disable browser security globally.
- `waiting for bridge`: check Python output, port, Origin spelling, local firewall and whether another study tab owns the bridge. Restarting the bridge reconnects automatically, with up to ten seconds of backoff.
- No native streams: check plugin architecture, antivirus/firewall, liblsl discovery permissions and stream source IDs. On Linux, pylsl may require installing liblsl separately or setting `PYLSL_LIB`.
- No control consumer: load an LSL-enabled experiment and leave it open; discovery retries every two seconds. An existing controller must publish a one-channel string outlet named `SonarTask-Control`. Commands only apply where the equivalent button is enabled.
- The shipped liblsl 1.16.2 macOS release is **Intel x86_64**, not universal. For Apple Silicon/universal distribution, run `build_macos_universal.sh` on a Mac, then set the macOS plugin's Editor/Standalone CPU to AnyCPU. Verify architectures with `lipo -info`, sign the dylib with the app's distribution identity, and sign/notarize the completed app. Signing and notarization require the lab's Apple credentials and cannot be verified on Windows.

Protocol tests (no LSL installation required): `python -m unittest discover -s . -p 'test_*.py'`. Browser transport checks (Node.js): `node test_browser.cjs`. Real loopback/LSL integration with the installed requirements: `python integration_check.py`.
