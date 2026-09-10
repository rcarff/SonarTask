# Experiment package authoring

Each experiment lives in its own directory and contains exactly one `experiment.json`. `instructions.md`, `Images/`, and `Sounds/` are conventional but optional except where referenced.

All referenced paths are relative to the package. Parent traversal (`../`), absolute paths, and remote URLs are intentionally unsupported.

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

## Determinism

`RandomizationSeed` is a non-zero 64-bit integer. Noise/jitter are keyed by seed, phase, signal, waterfall and scan index rather than consuming Unity's mutable random state. This targets mathematically repeatable signal state; exact cross-GPU pixels are not a requirement.

## LOFAR Views

`ExperimentSettings.LOFARViews` must contain at least one explicit view. No implicit default LOFAR view is created.

## End-of-experiment results

`ExperimentSettings.ShowResultsAtEnd` (`"Yes"`/`"No"`, default `"No"`) controls whether the mandatory experiment-end popup includes Correct/Partial/Incorrect totals and percentages, `Responses` (completed classification responses), and `Signals` (actual signals presented in the BTH).

## Waterfall scan rate and history

`WaterfallScanRateHz` changes the temporal resolution of BTH and LOFAR displays; it does not change the configured history duration. The renderer allocates approximately `(TimeEnd - TimeStart) * WaterfallScanRateHz` rows. Definitions requiring more than 4096 rows for any waterfall are rejected.

A signal remains selectable/classifiable while any part of its track is still visible in the BTH history, even after its active signal duration has ended.
