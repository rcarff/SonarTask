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
