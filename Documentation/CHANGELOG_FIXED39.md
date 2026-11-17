# FIXED39: AutoStart countdown

- New optional `AutoStartCountdownSec` (seconds, 0..3600, default 0) in `ExperimentSettings`, overridable per phase. It applies only when the phase's resolved `AutoStart` is `Yes`. 0 or omitted keeps the previous behavior.
- When set, the phase waits at its start (phase 1 once result recording is ready; later phases at the boundary, as an automatic pause) while a countdown banner shows at the top of the task screen: `Phase <name> starts automatically in m:ss`. The banner stays visible above the instructions overlay.
- On expiry, any open instructions are closed and the phase starts/resumes. Closing the instructions or pressing Start/Resume (button or LSL) earlier starts the phase immediately; the countdown is a maximum wait.
- The countdown freezes while the Exit confirmation is open.
- New CSV event types `AutoStart_Countdown_Started` (Alert_Text = configured seconds) and `AutoStart_Countdown_Expired`. The CSV header is unchanged.
- The countdown is a wall-clock UI timer during a pause. The experiment clock does not advance, so deterministic simulation output is unchanged.
- Server package validation rejects non-numeric or out-of-range `AutoStartCountdownSec`. Schema updated.
- Sample: `MultiBandOverlap` phase `OverlapAudio` uses a 10-second countdown.
