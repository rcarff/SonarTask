# Suggested study-validation test plan

Before using the simulator for data collection, exercise the exact Unity build, browser versions, operating systems, GPU families, audio hardware, and server topology intended for the study.

## Deterministic core

- Resolve an experiment twice and compare phase durations, IDs, classifications and inherited fields.
- Evaluate the same deterministic-noise key repeatedly and compare exact floating-point bit patterns on the same runtime.
- Sample bearings/frequencies at predetermined experiment times on Windows, macOS and Web; compare within the study's mathematical tolerance.
- Verify frame drops do not change signal start/end/alert threshold times recorded in the event stream.

## Bearing geometry

- Selection centered near 000 must include contacts on both sides of north.
- BTH views whose end wraps past 360 must display labels modulo 360, with 360 labeled 000.
- A moving contact crossing 359→000 must remain continuous.

## Phase/timing

- Duration equals the largest resolved `AppearSec + DurationSec`.
- First phase waits for Start.
- Incoming `AutoStart=Yes` transitions without operator action.
- Incoming `AutoStart=No` emits automatic pause and waits for Resume.
- Pause freezes experiment/phase clocks, waterfall history, alerts and audio position.

## Classification workflow

- Buttons are disabled without a BTH selection.
- Confirm is disabled until a classification is chosen.
- After Confirm, classification/BTH input is locked until confidence is chosen.
- Confidence produces one response marker, clears selection, and restores the default state.
- YES/PARTIAL/NO is correct for single and overlapping contacts.
- Feedback conveys state with text/shape as well as color.

## Result integrity

- Every run has a unique RunId and separate files.
- Natural final-phase completion is the only path to Completed=true.
- Operator exit and app shutdown preserve an aborted run.
- Web refresh/tab close eventually becomes BrowserDisconnected.
- Retried sequence numbers do not duplicate CSV rows.
- CSV imports correctly into Python/R/Excel with commas, quotes and line breaks in alert text.
- The experiment snapshot and metadata hash correspond to the definition used by the client.

## Accessibility

- Check text scaling and controls at supported window sizes.
- Test Green, Grayscale and Cividis palettes with color-vision-deficiency simulation and, ideally, representative users.
- Ensure no task-critical status is encoded by hue alone.

## Soak/concurrency

- Run several simultaneous browser subjects for longer than the longest intended experiment.
- Exercise experiment/result administration while trials are active.
- Confirm Docker container recreation leaves host Experiments/Results/Config intact.
