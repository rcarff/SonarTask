# Experiment package authoring

Each experiment lives in its own directory and contains exactly one `experiment.json`. `instructions.md`, `Images/`, and `Sounds/` are conventional but optional except where referenced.

Referenced paths are relative to the package, with a common Sounds fallback for audio files. Parent traversal (`../`), absolute paths, and remote URLs are intentionally unsupported.

## Phase duration

There is no explicit phase duration. The resolved phase duration is:

`max(Signal.AppearSec + EffectiveSignalDurationSec)`

where `EffectiveSignalDurationSec` resolves in this order: signal-instance `DurationSec`, phase `SignalDurationSec`, experiment default `SignalDurationSec`.

A phase therefore must contain at least one signal. Alerts after the computed phase end are semantically invalid and should be corrected by the experiment author.

## Inheritance

Experiment settings provide defaults. Phase fields override those defaults when present. Phase `SignalDefinitions` inherit from the named experiment-level signal definition and override only supplied fields. Signal instances then override the resolved phase signal definition.

## AutoStart

The first phase always starts when the operator presses Start. For subsequent phases, the **incoming phase's** resolved `AutoStart` setting decides whether it begins automatically or enters an automatic pause.

## BTHOverlapAudio

When signal audio is enabled, selecting exactly one signal plays that signal's audio together with any background audio. If the aperture contains multiple signals, `BTHOverlapAudio = Yes` plays all selected signal clips simultaneously; `No` suppresses signal audio for that overlapping selection. Background audio is independent and continues playing in either case.

## HideLofarSignalsIfNoSelectedBTH

`HideLofarSignalsIfNoSelectedBTH` is a `"Yes"`/`"No"` experiment setting and may be overridden per phase. The default is `"No"`.

- `"No"`: when the BTH has no selection, each LOFAR shows frequencies for all signals currently represented in the visible BTH, which is the existing behavior.
- `"Yes"`: when the BTH has no selection, each LOFAR shows deterministic background noise only. After the subject selects a BTH bearing region, the LOFAR shows frequency history for signals matching that selected region.

Clearing the BTH selection returns the LOFAR immediately to the appropriate unselected behavior for the current phase.


## Signal IDs and repeated contacts

`Signal.Id` identifies the physical/contact source represented by the signal; it is **not** a unique event key. The same ID may therefore appear more than once in a phase or in different phases when the same object produces another signal occurrence at a different time, bearing, or other location/state.

Each entry in a phase's `Signals` array is still a separate signal occurrence. Every occurrence receives its own start/end scheduling and result events even when another entry has the same ID. Do not invent a new ID merely to make repeated occurrences unique unless they truly represent different contacts.

## Determinism

`RandomizationSeed` is a non-zero 64-bit integer. Noise/jitter are keyed by seed, phase, signal, waterfall and scan index rather than consuming Unity's mutable random state. This targets mathematically repeatable signal state; exact cross-GPU pixels are not a requirement.

## LOFAR Views

`ExperimentSettings.LOFARViews` must contain at least one explicit view. No implicit default LOFAR view is created.

## End-of-experiment results

`ExperimentSettings.ShowResultsAtEnd` (`"Yes"`/`"No"`, default `"No"`) controls whether the mandatory experiment-end popup includes Correct/Partial/Incorrect totals and percentages, `Responses` (completed classification responses), and `Signals` (actual signals presented in the BTH).

## Waterfall scan rate and history

`WaterfallScanRateHz` changes the temporal resolution of BTH and LOFAR displays; it does not change the configured history duration. The renderer allocates approximately `(TimeEnd - TimeStart) * WaterfallScanRateHz` rows. Definitions requiring more than 4096 rows for any waterfall are rejected.

A signal remains selectable/classifiable while any part of its track is still visible in the BTH history, even after its active signal duration has ended.


## Phase controls and instructions

`DeselectBTHSignalAfterClassification` defaults to `Yes`. Set it to `No` to keep the BTH selection, LOFAR history and selected audio after confidence is recorded; the classification controls reset for another response. Confirm draws an amber border around the confidence row until a confidence level is chosen.

`DisablePauseAndExit` defaults to `No`. When `Yes`, Pause is hidden while Running and Exit is hidden throughout the phase after the initial Start. Start and Resume remain available before a phase starts and between phases. Exit appears after the experiment ends.

`AutoShowInstructions` defaults to `No`. When `Yes`, instructions open before the first phase and between phases. Close starts/resumes that phase only if `AutoStart` is `Yes`; otherwise the operator presses Start/Resume. The in-task Instructions button is available before Start, while paused and between phases, and disabled while Running. Each phase may set `InstructionsFile`; it inherits the experiment file, then `instructions.md`. Use an empty file value to hide the button. The separate Instructions scene is retained for external-study launches and uses phase 1's resolved file.

All three switches can be set in ExperimentSettings and overridden per phase. Yes/No fields accept case-insensitive `Yes`, `No`, `True`, `False`, JSON booleans and integer `1`/`0`; serialized output uses `Yes`/`No`.

## Independent LOFAR jitter and noise

`LoFarJitterFactor` and `LoFarBackgroundNoise` are optional numbers from 0 to 1. Each resolves from signal instance → phase signal-definition override → experiment signal definition → phase LOFAR value → ExperimentSettings LOFAR value → resolved phase `JitterFactor` / `BackgroundNoise`. They affect only LOFAR; BTH retains its existing values. A LOFAR row uses the **maximum effective noise** among signals feeding that row; an empty row uses phase LOFAR noise. Live and reconstructed history use the same calculation and deterministic keys. Omitting these fields preserves existing rendering.

## Optional LSL settings (FIXED37)

Add these experiment-level fields inside `ExperimentSettings`; they are not phase overrides:

```json
"LslEnabled": "No",
"LslStateRateHz": 30,
"LslWaterfallStreams": "Yes",
"LslWaterfallBins": 1024,
"LslAllowExternalAlerts": "Yes"
```

Omitting/nulling fields uses these defaults. State rate is clamped to 1–250 Hz; waterfall bins must be 256, 512 or 1024. The optional streams capture the displayed live intensities, so LOFAR changes with BTH selection. External alerts require a running phase with AlertsEnabled=Yes; set LslAllowExternalAlerts=No to disallow them across the experiment. Operators can force LSL on/off per machine, but cannot bypass the external-alert setting. See [LSL.md](LSL.md) for formats and [LslDemo](../Experiments/LslDemo/experiment.json) for a complete package. Existing packages remain valid with LSL disabled.

## Shared Sounds library

Audio references (`AudioFile` and `BackgroundAudio`) first load the package-relative file. When absent, a flat `name.wav` or `Sounds/name.wav` falls back to the common Sounds library; the `Sounds/` prefix is case-insensitive. Nested paths, `..`, separators and invalid filename characters are rejected for common lookup. Only `.wav` and `.mp3` are shared. Images and instructions remain package-relative.

The common directory is `<project>/Sounds` in Editor/local Web development, `DataRoot/Sounds` in desktop players and `/data/sounds` on the server (`Server/Deploy/Sounds` on the host). Desktop builds seed missing common files even for initialized installs. Local Web builds copy sounds and generate a read-only manifest. Web Settings > Common Audio Files lists names and sizes, uploads files up to 64 MiB, and deletes unused files. Duplicate names return 409. Deletion of a referenced common file returns 409 listing referencing packages; package-local files shadow the common file.
