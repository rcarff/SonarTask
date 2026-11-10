# Lab Streaming Layer integration

LSL is optional and never drives the deterministic model except through explicitly recorded control commands. CSV columns are unchanged. All result storage modes (desktop, hosted web, local web development) mirror accepted events through the same recorder hook. Transport failures do not stop the experiment.

## Enable and override

ExperimentSettings accepts `LslEnabled` (Yes/No, default No), `LslStateRateHz` (default 30, finite values clamped to 1–250), `LslWaterfallStreams` (default Yes), `LslWaterfallBins` (256/512/1024, default 1024), and `LslAllowExternalAlerts` (default Yes). Omitted/null optional fields use defaults; old packages remain valid. Waterfall and alert defaults apply when an operator forces LSL on too.

Desktop reads `<DesktopStorage.DataRoot>/lsl.json`, for example `{"Mode":"Experiment","BridgePort":16571}`. Modes are Experiment, On and Off; `SONAR_LSL=on|off|experiment` takes precedence. The Editor uses the project data folder. Web accepts `?lsl=on|off|experiment&lslPort=16571` and persists the choice per browser profile in PlayerPrefs. Port must be 1–65535. Startup displays the override; the task chip displays the effective state. Source IDs are stable GUIDs stored per install/browser profile in PlayerPrefs. Clearing preferences creates a new identity.

## Streams

| Name | Type | Channels | Nominal rate |
| --- | --- | --- | --- |
| SonarTask-Markers | Markers | 1 string: compact JSON | irregular |
| SonarTask-State | SonarState | 6 float32 | LslStateRateHz |
| SonarTask-BTH | SonarWaterfall | 2+B float32 | shared phase scan rate, otherwise irregular |
| SonarTask-LOFAR | SonarWaterfall | 3+V×B float32 | shared phase scan rate, otherwise irregular |
| SonarTask-Control | SonarControl | 1 string: JSON, published by external controller | irregular |

State order is TotalElapsed, ExpElapsed, PhaseIndex (zero based), PhaseElapsed, Paused (0/1), SelectedBearing (NaN when no selection). It runs while Ready/Running/Paused; sampling is limited by Unity Update frequency and skips missed deadlines instead of sending duplicate catch-up samples.

B is the configured bin count; V is the maximum LOFAR view count across resolved phases. BTH order: PhaseIndex, ScanIndex, then B intensities. LOFAR order: PhaseIndex, ScanIndex, ActiveViewCount, then view k at 3+k×B. Unused slots contain NaN. Intensities are the exact pre-palette live render arrays; smaller bin counts average contiguous bins. No noise is recomputed. LOFAR reflects the participant's current selection and HideLofarSignalsIfNoSelectedBTH. History reconstruction is never streamed. Paused runs produce no waterfall scans. ScanIndex is the runtime's global scan index (float32, exact through 2²⁴).

Channel shapes/rates are fixed while an outlet is open. Outlets persist between runs with identical stream configuration and reopen when shapes/rates change. Descriptions contain only static app_version, platform and schema_version=1. The plan's earlier proposal to put per-run package/hash/seed in the description conflicts with persistent outlets; those fields appear in every marker instead. Each outlet source_id is `<installation GUID>-<stream suffix>`.

## Markers and lifecycle

Every marker starts with `eventType` and carries runId, seq, package, experimentName, experimentHash, seed, subjectId, operatorId, phaseName, phaseIndex and TotalElapsedTimeSec/ExpElapsedTimeSec/PhaseElapsedTimeSec. CSV-mirrored markers also include all nonempty ResultEvent fields. `seq` exactly matches the recorder's accepted event sequence, beginning at 1. LSL-only markers use the last accepted sequence (0 before the first event); filter by eventType when comparing to CSV.

`Run_Begin` is emitted after recorder initialization succeeds, before the task can start; it includes phase names and effective stream configuration. `Run_Finished` includes completed and reason, including Completed, OperatorExit, OperatorExitBeforeStart and ApplicationClosed. Web tab termination cannot guarantee delivery; server heartbeat handling still preserves an aborted run. `Waterfall_Axes` is emitted at phase starts/automatic phase pauses and upon bridge connection; it carries bth DegStart/DegEnd, lofar HzStart/HzEnd per active view and bins. These three marker types are LSL-only. Native/bridge publishers retain queued markers until a consumer subscribes, preserving capture timestamps so initial discovery does not lose Run_Begin. Once published, events are not replayed to later subscribers; start consumers before the run.

## Control and external alerts

Commands are JSON with cmd = start, pause, resume, marker or alert. Start/pause/resume use the task's existing button handler and availability gates. Marker text is recorded as LSL_Marker.Alert_Text. Applied commands record LSL_Command; unknown/malformed/unavailable commands record a rejected LSL_Command with Alert_Type `Rejected:Invalid` or `Rejected:Disabled`. Commands are bounded to 8192 characters and are processed on the main thread, at most 32 per Update. Control input queues are capped at 256. Commands queued between runs are discarded at the next run.

```json
{"cmd":"alert","text":"Contact","bearing":135,"type":"EXTERNAL","signalId":"","durationSec":10,"id":"py-42"}
```

Alert text and a finite numeric bearing are required. Text is capped at 200 characters and rendered literally with rich text disabled, so `<b>x</b>` cannot style the panel. Bearing is normalized to integer 0–359. Type defaults to EXTERNAL. Duration defaults to the phase alert duration and is clamped to 1–600 seconds. SignalId is optional and only recorded. Optional id (capped at 200 characters) is echoed on the marker JSON, including rejection markers; it adds no CSV column.

External alerts require Running, phase AlertsEnabled=Yes and experiment LslAllowExternalAlerts=Yes. Rejections record Alert_Text and Alert_Type `Rejected:Paused`, `Rejected:Ready`, `Rejected:AlertsDisabled` or `Rejected:Invalid`. Accepted alerts use the authored panel insertion/expiry path, record LSL_Alert_Shown with all Alert_* columns, and also record LSL_Command. Expiry follows phase time and pauses with the run.

LSL_Connected/LSL_Disconnected mark observed transport changes during a run. Native connected means local outlets are available, independent of whether a remote consumer subscribes; bridge connected means hello was accepted and outlets are ready. These events do not imply end-to-end recording by LabRecorder.

## Web wire format and timing

The [loopback bridge](../Tools/LslBridge/README.md) listens on 127.0.0.1 and enforces an Origin allowlist. The browser sends hello `{kind:"hello",config:{...},timestamp:performanceSeconds}`; bridge responds ready. JSON marker/state frames contain kind, json or sample, and timestamp. Commands return as `{kind:"command",json:"..."}`. Bridge ping carries sent=local_clock; pong echoes sent and the browser timestamp. The lowest-RTT exchange estimates the LSL/browser offset; until then samples use receipt time. Future estimates are capped at receipt time. This is software timing with scheduling/transport uncertainty.

Waterfall frames are little endian: uint32 stream ID (1 BTH, 2 LOFAR), float64 performance.now()/1000 timestamp, then float32 channels including the stream's sample headers. Exactly 12+4×channelCount bytes are required. Binary data avoids large JSON encodings. Browser backpressure drops new waterfall frames above 256 KiB queued data; the bridge and native workers retain eight recent waterfall frames, dropping the oldest. Connected marker frames use a separate reliable queue. Disconnected data is not guaranteed/replayed. Native sample timestamps map a monotonic capture clock into LSL local_clock on the worker, preserving capture times even when publishing is delayed.

## Native dependencies and validation

Vendored liblsl 1.16.2 comes from the [official releases](https://github.com/sccn/liblsl/releases/tag/v1.16.2); the C# binding comes from [liblsl-Csharp](https://github.com/labstreaminglayer/liblsl-Csharp). Licenses are in Assets/Plugins/LSL. Native plugins are excluded from WebGL and the binding is conditionally compiled. Windows x64 is included. The upstream macOS 1.16.2 asset is Intel only: build universal on a Mac using the supplied script before Apple Silicon deployment; verify Editor load, app signing and notarization on macOS. See TEST_PLAN.md for the study checklist.
