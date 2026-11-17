# Result format

Every run has a UUID `RunId` and produces a CSV event stream, a `.meta.json` sidecar, and an `.experiment.json` snapshot of the definition used for the run. Runs are never overwritten.

The current result schema is **version 3**. The fixed CSV columns are:

`Time, TotalElapsedTime, ExpElapsedTime, Phase, PhaseElapsedTimeSec, EventType, Signal_Type, Signal_ID, Signal_Bearing, Signal_Width, Signal_Alpha, BTH_SelectedSignals, BTH_SelectedBearing, Class_Selected, Class_Types, Class_Correct, Class_Confidence, Class_Feedback, Alert_Type, Alert_Text, Alert_Bearing, Alert_SignalId, RunId`


`TotalElapsedTime` is seconds since the experiment was first started and continues increasing during manual and automatic pauses. `ExpElapsedTime` is the pause-sensitive experiment clock; it stops while paused. `PhaseElapsedTimeSec` remains the pause-sensitive elapsed time within the current phase.

Every phase receives a display-only BTH drain interval equal to its visible BTH history span plus one waterfall scan interval. Signals still end at their configured times, but the phase transition is delayed until the last signal track from that phase has scrolled out of the BTH. For the final phase, this also delays `EXP_Ended` until the display has drained.

Unused fields are empty. UTC timestamps are ISO-8601. Lists inside a cell use semicolons. CSV values are RFC-4180-style escaped when needed.

`BTH_SelectedBearing` records the center bearing, in degrees, of the subject's BTH selection aperture. It is populated for `BTH_Selection` and the classification events associated with that selection.

Primary event types are `EXP_Started`, `EXP_AutoPaused`, `EXP_Paused`, `EXP_Resumed`, `EXP_Ended`, `Signal_Start`, `Signal_End`, `BTH_Selection`, `Class_Chosen`, `Class_Confirmed`, `Class_Confidence`, and `Alert_Shown`.

A run is considered completed only after natural completion of the final phase. Operator exits, application shutdowns, and browser disconnects are retained as aborted runs.


## External-study metadata

The fixed CSV event columns remain schema version 3. Recruitment/allocation information is stored in each run's `.meta.json` sidecar so every run can be reconciled with its provider assignment without widening the event CSV.

For an externally launched run the metadata additionally contains `LaunchMode`, `RecruitmentProvider`, `ProviderParticipantId`, `ProviderStudyId`, `ProviderSessionId`, `ProviderAssignmentId`, `ExternalStudyId`, `AssignmentMode`, `AssignmentGroup`, `ExperimentSequence`, `SequenceIndex`, and `SequenceCount`. `SequenceIndex` is one-based in saved metadata. Normal operator/desktop runs leave the external fields empty and use `LaunchMode = "Normal"`.

For a counterbalanced multi-experiment assignment, each experiment produces its normal independent result files. The common `AssignmentGroup` and `ExperimentSequence`, together with `SequenceIndex`, identify the participant's assigned order. The provider completion redirect is offered only after the final sequence step has been saved successfully.

Repeated `Signal_ID` values are valid. Each `Signal_Start`/`Signal_End` row represents one signal occurrence; identical IDs indicate occurrences associated with the same source/contact rather than duplicate records.


## External-study test runs (fixed34)

External-study metadata JSON now includes `ExternalTestMode`. It is `true` only for runs launched with a valid server-generated External Study test token (or the local WebGL development test mode). Test runs are stored as normal CSV/meta/experiment snapshots so they can be inspected, but they use separate assignment counters and do not affect production Random/Balanced Random/Counterbalanced allocation. Settings > External Studies > Clear Test Data removes the test assignment records and associated test result-file families.

## Instruction viewing events (FIXED36)

`Instructions_Opened` and `Instructions_Closed` record in-experiment instruction overlay viewing, including automatic phase instruction prompts. They use the current phase and runtime clocks. No CSV columns were added; the header is unchanged. Viewing instructions before the task scene through the external-study Instructions scene does not create these run events.

## AutoStart countdown events (FIXED39)

When a phase has `AutoStart = Yes` and `AutoStartCountdownSec` > 0, `AutoStart_Countdown_Started` is written when the countdown begins, with the configured seconds in `Alert_Text` (invariant culture). `AutoStart_Countdown_Expired` is written only when the countdown runs out. It is followed by `Instructions_Closed` if instructions were open, then `EXP_Started` or `EXP_Resumed`. If the phase is started early (instructions closed, or Start/Resume pressed), no Expired event is written. The countdown runs while the experiment is paused, so `ExpElapsedTime` does not advance during it. No CSV columns were added; the header is unchanged.

## Instruction video events (FIXED38)

The CSV header is unchanged. Videos played in the in-experiment instruction overlay record:

- `Video_Played`: playback actually started, either from autoplay or the Play control. An autoplay that the browser blocks records nothing.
- `Video_Paused`: the participant paused.
- `Video_Seeked`: the participant moved the seek bar. The position is the seek target.
- `Video_Ended`: playback reached the end of the clip. Looping videos record this once per loop.

`Alert_Type` holds the package-relative video path, and `Alert_Text` holds the playback position in seconds (invariant culture, three decimals). The phase and clock columns use the current runtime clocks, as `Instructions_Opened` does. Closing the overlay while a video plays is captured by `Instructions_Closed`, and no separate video event is written. The pre-task Instructions scene has no run, so it records no video events. Like all CSV events, these rows are mirrored to the LSL marker stream.

## LSL events and metadata (FIXED37)

The CSV header remains unchanged. New event types:

- `LSL_Command`: applied control command, or rejected command. Alert_Type holds the command name or `Rejected:Invalid`, `Rejected:Disabled`, `Rejected:Paused`, `Rejected:Ready`, or `Rejected:AlertsDisabled`. Alert_Text retains external marker/alert text, capped at 200 characters.
- `LSL_Marker`: external annotation; text is in Alert_Text.
- `LSL_Alert_Shown`: accepted external alert, with Alert_Type, Alert_Text, Alert_Bearing (three digits), and Alert_SignalId. It shares authored alert display/expiry behavior.
- `LSL_Connected`, `LSL_Disconnected`: observed transport availability changes during a run. Native connected means local outlets are ready; it does not certify that LabRecorder is recording.

Run metadata adds `LslEnabled` (bool), `LslMode` (Off/Native/Bridge), and `LslSourceId` (stable per-install/browser GUID). The server preserves these fields. All accepted CSV events are mirrored to SonarTask-Markers with identical per-run sequence numbers and full run context. An optional external command `id` is echoed only in marker JSON. `Run_Begin`, `Run_Finished`, and `Waterfall_Axes` are LSL-only markers and never add CSV rows. Their seq is the last accepted event sequence (0 before the first event). See [LSL.md](LSL.md) for the full contract and timing limits.
