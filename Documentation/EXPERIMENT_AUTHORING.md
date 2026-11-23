# Experiment package authoring

This guide describes every field an experiment package can use and what each one does at run time. The machine-readable schema is [experiment.schema.json](experiment.schema.json). Result files are described in [RESULT_FORMAT.md](RESULT_FORMAT.md).

## Package layout

Each experiment lives in its own directory and contains exactly one `experiment.json`:

```
Experiments/<Package>/
  experiment.json      required
  instructions.md      default instructions file (optional unless referenced)
  Images/              images used by instructions (convention)
  Videos/              videos used by instructions (convention)
  Sounds/              package-local audio (convention)
```

Referenced paths are relative to the package. Audio files can also fall back to the common Sounds library (see [Shared Sounds library](#shared-sounds-library)). Parent traversal (`../`), absolute paths and remote URLs are intentionally unsupported.

Edit packages under the top-level `Experiments/` folder. `Assets/StreamingAssets/SonarTaskSeed/` is a generated copy. On Web, upload a ZIP whose single top-level folder is the package (64 MiB limit). A package downloaded from Web Settings already has this layout.

**Validate before use.** In the Unity Editor, **SONAR → Validate All Experiment Packages** reports schema errors, missing assets and the resolved phase durations. The Web server repeats the checks on upload.

## Minimal example

```json
{
  "SchemaVersion": 1,
  "Name": "Minimal Example",
  "Version": "1.0",
  "InstructionsFile": "instructions.md",
  "ExperimentSettings": {
    "RandomizationSeed": 12345,
    "PhaseOrder": ["Main"],
    "LOFARViews": [ { "HzStart": 0, "HzEnd": 1000 } ],
    "SignalDefinitions": {
      "Merchant": {
        "Name": "Merchant Vessel",
        "Classification": "Merchant",
        "AudioFile": "Sounds/merchant.wav",
        "SignalStrength": { "Width": 18, "Alpha": 0.7 },
        "FreqTuples": [ { "Frequency": 120, "Width": 9, "Alpha": 0.85 } ]
      }
    }
  },
  "Phases": {
    "Main": {
      "Signals": [ { "Signal": "Merchant", "AppearSec": 0, "Bearing": 45, "RateDegSec": 0.2 } ]
    }
  }
}
```

The packages in `Experiments/` are fuller working examples. `MultiBandOverlap` shows overlapping signals, several LOFAR views and an AutoStart countdown. `LslDemo` shows LSL.

## Top-level fields

| Field | Default | Meaning |
|---|---|---|
| `SchemaVersion` | — | Required. Must be `1`. |
| `Name` | — | Required. Display name shown in experiment selection and written to results. |
| `DisplayedName` | unset | Optional. Participant-facing name shown instead of `Name` while the experiment runs (instructions title and the task screen's top bar). The selection list, Settings and results still use `Name`. |
| `Version` | `"1.0"` | Author's version string, written to result metadata. |
| `InstructionsFile` | `"instructions.md"` | Package-relative Markdown file. Phases can override it (see [Phase controls and instructions](#phase-controls-and-instructions)). |
| `ExperimentSettings` | — | Required. Experiment-wide settings and the defaults that phases inherit. |
| `Phases` | — | Required. Object of phase definitions keyed by phase name. |

## Inheritance

Experiment settings provide defaults. Phase fields override those defaults when present. Phase `SignalDefinitions` inherit from the named experiment-level signal definition and override only supplied fields. Signal instances then override the resolved phase signal definition.

Object-valued settings are replaced as a whole, not merged field by field. A phase `BTHView`, a phase `LOFARViews` list, an instance `SignalStrength` or an instance `FreqTuples` list replaces the inherited value completely. Fields left out of such an object use the built-in defaults listed below, not the experiment's values. Copy the whole object when you override it.

Yes/No fields accept case-insensitive `Yes`, `No`, `True`, `False`, JSON booleans and integer `1`/`0`. Serialized output uses `Yes`/`No`.

## ExperimentSettings reference

"Phase" in the last column means the field can also be set inside a phase to override the experiment value for that phase.

### Structure and determinism

| Field | Default | Phase | Meaning |
|---|---|---|---|
| `PhaseOrder` | — | no | Required, non-empty. Phase names in run order. Every name must exist in `Phases`. Phases not listed are ignored. |
| `SignalDefinitions` | `{}` | no | Signal types keyed by an identifier (see [Signal definitions](#signal-definitions)). |
| `RandomizationSeed` | — | no | Required non-zero 64-bit integer (see [Determinism](#determinism)). |

### Display

| Field | Default | Phase | Meaning |
|---|---|---|---|
| `BTHView` | see [BTHView](#bthview) | yes | Bearing/time waterfall layout. |
| `LOFARViews` | — | yes | Required, at least one view. One LOFAR waterfall per entry, stacked in list order (see [LOFARView](#lofarview)). |
| `WaterfallScanRateHz` | `20` | yes | Scan rows per second for BTH and LOFAR. Must be > 0 (see [Waterfall scan rate and history](#waterfall-scan-rate-and-history)). |
| `WaterfallPalette` | `"Green"` | yes | `Green`, `Grayscale` or `Cividis` (a color-vision-deficiency-friendly blue→yellow map). Unrecognized values render as Green. Recorded in run metadata. |
| `JitterFactor` | `0.5` | yes | 0..1. Deterministic bearing wander of BTH traces. Also the fallback for LOFAR jitter. |
| `BackgroundNoise` | `0.18` | yes | 0..1. Deterministic background noise level of the BTH. Also the fallback for LOFAR noise. |
| `LoFarJitterFactor`, `LoFarBackgroundNoise` | unset | yes | LOFAR-only jitter and noise (see [Independent LOFAR jitter and noise](#independent-lofar-jitter-and-noise)). |
| `HideLofarSignalsIfNoSelectedBTH` | `No` | yes | See [HideLofarSignalsIfNoSelectedBTH](#hidelofarsignalsifnoselectedbth). |
| `ShowElapsedTime` | `Yes` | yes | Show the `Elapsed mm:ss` experiment clock. |
| `ShowRemainingTime` | `Yes` | yes | Show `Remaining mm:ss`. It counts down the whole experiment, including the BTH drain intervals of all phases. |
| `ShowPhaseName` | `Yes` | yes | Show the `Phase: <name>` label in the top bar. With `No`, the phase name is hidden from the participant: the label is removed, the auto-start countdown says "Next phase", and the end-of-run phase results list phases as `Phase 1`, `Phase 2`, … Results files still record the real phase name. |

### Signals and timing

| Field | Default | Phase | Meaning |
|---|---|---|---|
| `SignalDurationSec` | `30` | yes | Default active duration of a signal instance. Must be > 0. |

### Task panels and scoring

| Field | Default | Phase | Meaning |
|---|---|---|---|
| `TrainingAreaEnabled` | `Yes` | yes | Show the Training area (see [Training area](#training-area)). |
| `FeedbackEnabled` | `Yes` | yes | Show correct/partial/incorrect feedback after each classification (see [Classification and scoring](#classification-and-scoring)). |
| `ClassificationTallyEnabled` | `Yes` | yes | Show the running Correct/Partial/Incorrect tally. |
| `ResetScoreWhenPhaseStarts` | `Yes` | yes | See [ResetScoreWhenPhaseStarts](#resetscorewhenphasestarts). |
| `DeselectBTHSignalAfterClassification` | `Yes` | yes | See [Phase controls and instructions](#phase-controls-and-instructions). |
| `ShowResultsAtEnd` | `No` | no | See [End-of-experiment results](#end-of-experiment-results). |
| `ShowCompletionCode` | `No` | no | See [Completion code](#completion-code). |

### Alerts

| Field | Default | Phase | Meaning |
|---|---|---|---|
| `AlertsEnabled` | `Yes` | yes | Show the Alerts panel and display alerts. When `No`, scheduled alerts are not shown or recorded. |
| `AlertOffsetSec` | `5` | yes | Default delay from a signal's `AppearSec` to its signal-linked alert. |
| `AlertDurationSec` | `10` | yes | Default time an alert stays listed. |

### Audio

| Field | Default | Phase | Meaning |
|---|---|---|---|
| `SignalAudioEnabled` | `Yes` | yes | Play the selected signal's `AudioFile` while a BTH region is selected. |
| `BTHOverlapAudio` | `No` | yes | See [BTHOverlapAudio](#bthoverlapaudio). |
| `BackgroundAudio` | `""` | yes | Audio file looped while the phase is running. Empty means none. |
| `BackgroundAudioVolume` | `0.8` | yes | 0..1. |

### Phase flow and instructions

| Field | Default | Phase | Meaning |
|---|---|---|---|
| `AutoStart` | `Yes` | yes | See [AutoStart](#autostart). |
| `AutoStartCountdownSec` | `0` | yes | See [AutoStartCountdownSec](#autostartcountdownsec). |
| `AutoShowInstructions` | `No` | yes | See [Phase controls and instructions](#phase-controls-and-instructions). |
| `DisablePauseAndExit` | `No` | yes | See [Phase controls and instructions](#phase-controls-and-instructions). |

### Lab Streaming Layer

`LslEnabled`, `LslStateRateHz`, `LslWaterfallStreams`, `LslWaterfallBins` and `LslAllowExternalAlerts` are experiment-only. See [Optional LSL settings](#optional-lsl-settings-fixed37).

## Phases

`Phases` is an object keyed by phase name. A phase can contain any field marked "Phase" above, plus:

| Field | Meaning |
|---|---|
| `InstructionsFile` | Per-phase instructions file. An empty string hides the Instructions button for that phase. |
| `SignalDefinitions` | List of per-phase signal overrides or new signal types (see [Phase signal definitions](#phase-signal-definitions)). |
| `Signals` | Required, at least one. The signal instances presented in this phase (see [Signal instances](#signal-instances)). |
| `Alerts` | Optional standalone alerts (see [Phase alerts](#phase-alerts)). |

### Phase duration

There is no explicit phase duration. The resolved phase duration is:

`max(Signal.AppearSec + EffectiveSignalDurationSec)`

where `EffectiveSignalDurationSec` resolves in this order: signal-instance `DurationSec`, phase `SignalDurationSec`, experiment default `SignalDurationSec`.

A phase therefore must contain at least one signal. Alerts after the computed phase end are rejected by validation.

After the last signal ends, every phase also runs a **BTH drain interval** of `|BTHView.TimeEnd − BTHView.TimeStart| + 1/WaterfallScanRateHz` seconds. This lets the final track scroll fully out of the BTH history before the phase changes or the experiment ends. Signals stay selectable during the drain while their track is still visible. `ShowRemainingTime` includes the drain.

## Views

### BTHView

| Field | Default | Meaning |
|---|---|---|
| `DegStart`, `DegEnd` | `180`, `180` | Integers 0..360. The bearing axis runs clockwise from `DegStart` to `DegEnd`, wrapping through 000. Equal values give a full 360° view, so the default is a 360° view centered on 000. |
| `DegTicksEvery`, `DegLabelsEvery` | `90`, `90` | Tick and label spacing in degrees (1..360). Labels show three-digit bearings. |
| `TimeStart`, `TimeEnd` | `0`, `30` | Visible history in seconds. Also sets the BTH drain interval. |
| `TimeTicksEvery`, `TimeLabelsEvery` | `15`, `15` | Time-axis tick and label spacing in seconds. |
| `SelectionWidth` | `15` | Width in degrees (> 0, ≤ 360) of the subject's BTH selection aperture. |

### LOFARView

| Field | Default | Meaning |
|---|---|---|
| `HzStart`, `HzEnd` | `0`, `1750` | Frequency range. `HzEnd` must be greater than `HzStart`. |
| `HzTicksEvery`, `HzLabelsEvery` | `50`, `250` | Tick and label spacing in Hz (> 0). |
| `TimeStart`, `TimeEnd` | `0`, `30` | Visible history in seconds. `TimeEnd` must be greater than `TimeStart`. |
| `TimeTicksEvery`, `TimeLabelsEvery` | `15`, `15` | Time-axis tick and label spacing in seconds (> 0). |

`ExperimentSettings.LOFARViews` must contain at least one explicit view. No implicit default LOFAR view is created. A phase `LOFARViews` override must also contain at least one view. Use several views to show different frequency bands of the same signals, for example a low band and a wide band.

## Signals

### Signal definitions

`ExperimentSettings.SignalDefinitions` is an object of reusable signal types. The key (for example `"Merchant"`) is the identifier that phases and instances reference. It is not shown to the subject.

| Field | Default | Meaning |
|---|---|---|
| `Name` | — | Required. Friendly name shown in the Training area. |
| `Classification` | — | Required. Correct classification label. It also becomes a classification button. Several types may share one label (for example Tanker and Merchant Vessel both classify as `Merchant`). |
| `TrainingAllowed` | `Yes` | Whether this type is listed in the Training area. |
| `AudioFile` | `""` | Package-relative or common-library `.wav`/`.mp3` played when the signal is selected. |
| `AudioVolume` | `0.8` | 0..1. |
| `SignalStrength` | `{ "Width": 20, "Alpha": 0.6 }` | BTH trace. `Width` is the trace's full width at half maximum in degrees (> 0). `Alpha` is the peak intensity (0..1). |
| `FreqTuples` | — | Required, at least one. LOFAR lines: `{ "Frequency": Hz ≥ 0, "Width": Hz > 0 (default 5), "Alpha": 0..1 (default 0.8) }`. `Width` is the line's full width at half maximum. |
| `LoFarJitterFactor`, `LoFarBackgroundNoise` | unset | Per-type LOFAR overrides. |

Overlapping traces add together and are clipped at full intensity.

### Phase signal definitions

A phase's `SignalDefinitions` is a **list** (not an object). Each entry has a `Name` that matches an experiment-level key, plus only the fields to change for this phase, for example a different `AudioFile` or `TrainingAllowed: "No"`. The friendly name is not changed.

If `Name` does not match an experiment key, the entry defines a new signal type for this phase only. In that case every field must be supplied: `TrainingAllowed`, `AudioFile`, `AudioVolume`, `Classification`, `SignalStrength` and `FreqTuples`. The new type's friendly name is its `Name`.

The phase list also controls which types appear in the Training area and the classification buttons (see below). When the list is omitted or empty, the types used by the phase's `Signals` are used instead.

### Signal instances

Each entry in a phase's `Signals` list is one occurrence of a signal.

| Field | Default | Meaning |
|---|---|---|
| `Signal` | — | Required. Key of a signal definition available in this phase. |
| `AppearSec` | `0` | Seconds after the phase starts. Must be ≥ 0. |
| `Bearing` | `0` | Starting bearing, 0..360. |
| `RateDegSec` | `0` | Bearing drift in degrees per second. Negative values drift counter-clockwise. The bearing wraps through 000/360. |
| `DurationSec` | phase `SignalDurationSec` | Active duration. Must be > 0. |
| `ID` | auto `SIG-0001`… | Contact identifier written to results (see [Signal IDs and repeated contacts](#signal-ids-and-repeated-contacts)). |
| `Classification` | from definition | Overrides the correct answer for this occurrence. A label not used by any type in the phase still gets its own classification button. |
| `AudioFile`, `AudioVolume` | from definition | Per-occurrence audio. |
| `SignalStrength`, `FreqTuples` | from definition | Per-occurrence appearance. Replaces the inherited object or list as a whole. |
| `LoFarJitterFactor`, `LoFarBackgroundNoise` | from definition | Per-occurrence LOFAR overrides. |
| `AlertEnabled` | `No` | Schedule an alert linked to this signal. |
| `AlertText` | the classification | Alert text. |
| `AlertOffsetSec` | phase `AlertOffsetSec` | Delay from `AppearSec` to the alert. |
| `AlertBearing` | starting `Bearing` rounded | Bearing shown with the alert, 0..360. It does not follow the signal's drift. |
| `AlertDurationSec` | phase `AlertDurationSec` | How long the alert stays listed. |
| `AlertType` | `VALID` | `VALID` or `INVALID`. Recorded with the alert so analysis can separate true and false alerts. The subject sees no difference. |

### Signal IDs and repeated contacts

`Signal.Id` identifies the physical/contact source represented by the signal; it is **not** a unique event key. The same ID may therefore appear more than once in a phase or in different phases when the same object produces another signal occurrence at a different time, bearing, or other location/state.

Each entry in a phase's `Signals` array is still a separate signal occurrence. Every occurrence receives its own start/end scheduling and result events even when another entry has the same ID. Do not invent a new ID merely to make repeated occurrences unique unless they truly represent different contacts.

## Phase alerts

`Alerts` in a phase lists standalone alerts that are not tied to a signal instance's scheduling, such as distractor or false alerts.

| Field | Default | Meaning |
|---|---|---|
| `Text` | `""` | Alert text. |
| `AppearSec` | `0` | Seconds after the phase starts. Must be ≥ 0 and no later than the computed phase end. |
| `Bearing` | `0` | Bearing shown with the alert, 0..360. |
| `DurationSec` | phase `AlertDurationSec` | How long the alert stays listed. |
| `Type` | `INVALID` | `VALID` or `INVALID`. Recorded only. |
| `SignalID` | `""` | Optional contact ID to associate in results. |

The Alerts panel shows the five newest active alerts as `<Text> at <Bearing>`, newest first. Each alert disappears after its duration. Alerts are recorded as `Alert_Shown` events. LSL can inject further alerts at run time (see [LSL.md](LSL.md)).

## Classification and scoring

The classification buttons for a phase are the distinct `Classification` labels of the phase's signal types, in order. The order comes from the phase `SignalDefinitions` list (or from the first use in `Signals` when the list is omitted), followed by any instance-level labels. Buttons are laid out three per row.

The subject selects a BTH region, chooses a classification, presses Confirm and then picks a confidence level. The selection captures every visible signal whose trace overlaps the aperture, that is every signal within `SelectionWidth/2 + SignalStrength.Width/2` degrees of the selection center. The response scores as:

- **YES / Correct**: every captured signal has the chosen classification.
- **PARTIAL**: at least one captured signal, but not all, has the chosen classification.
- **NO / Incorrect**: no captured signal has the chosen classification, or nothing was captured.

With `FeedbackEnabled = Yes`, the subject sees a feedback flash with an icon and text, and the CSV `Class_Feedback` column holds `CORRECT`, `PARTIAL` or `INCORRECT`. With `No`, the column holds `NONE`. The score is always recorded in `Class_Correct`.

A signal remains selectable/classifiable while any part of its track is still visible in the BTH history, even after its active signal duration has ended.

## Training area

When `TrainingAreaEnabled = Yes`, a Training dropdown lists `Instructions` plus every phase signal type with `TrainingAllowed = Yes`, sorted by friendly name and shown as `<Name> - Classification: <label>`. Choosing a type marks its default `FreqTuples` frequencies with carets on each LOFAR view whose range includes them, and highlights the matching classification button. Training does not record a response.

## AutoStart

The first phase always starts when the operator presses Start. For subsequent phases, the **incoming phase's** resolved `AutoStart` setting decides whether it begins automatically or enters an automatic pause.

### AutoStartCountdownSec

`AutoStartCountdownSec` (number of seconds, 0..3600, default `0`) can be set in `ExperimentSettings` and overridden per phase. It only has an effect when the phase's resolved `AutoStart` is `Yes`. `0` or omitting it keeps the behavior described above.

When it is greater than 0:

- The phase waits at its start while a banner at the top of the screen shows `Phase <name> starts automatically in m:ss`. For the first phase, the countdown begins once result recording is ready, so the operator does not need to press Start. Later phases enter an automatic pause at the boundary.
- The banner stays visible above the instructions overlay. With `AutoShowInstructions = Yes`, the instructions open as usual and the countdown runs while they are shown.
- When the countdown expires, any open instructions are closed and the phase starts.
- The countdown is a maximum wait. Closing the instructions, or pressing Start/Resume (button or LSL), before it expires starts the phase immediately.
- The countdown freezes while the Exit confirmation is open.

The experiment clock does not advance during the countdown, so it does not change signal timing or determinism. Results record `AutoStart_Countdown_Started` and, on expiry, `AutoStart_Countdown_Expired`.

## Phase controls and instructions

`DeselectBTHSignalAfterClassification` defaults to `Yes`. Set it to `No` to keep the BTH selection, LOFAR history and selected audio after confidence is recorded; the classification controls reset for another response. Confirm draws an amber border around the confidence row until a confidence level is chosen.

`DisablePauseAndExit` defaults to `No`. When `Yes`, Pause is hidden while Running and Exit is hidden throughout the phase after the initial Start. Start and Resume remain available before a phase starts and between phases. Exit appears after the experiment ends.

`AutoShowInstructions` defaults to `No`. When `Yes`, instructions open before the first phase and between phases. Close starts/resumes that phase only if `AutoStart` is `Yes`; otherwise the operator presses Start/Resume. The in-task Instructions button is available before Start, while paused and between phases, and disabled while Running. Each phase may set `InstructionsFile`; it inherits the experiment file, then `instructions.md`. Use an empty file value to hide the button. The separate Instructions scene is retained for external-study launches and uses phase 1's resolved file.

All three switches can be set in ExperimentSettings and overridden per phase.

## Audio

### BTHOverlapAudio

When signal audio is enabled, selecting exactly one signal plays that signal's audio together with any background audio. If the aperture contains multiple signals, `BTHOverlapAudio = Yes` plays all selected signal clips simultaneously; `No` suppresses signal audio for that overlapping selection. Background audio is independent and continues playing in either case.

### Shared Sounds library

Audio references (`AudioFile` and `BackgroundAudio`) first load the package-relative file. When absent, a flat `name.wav` or `Sounds/name.wav` falls back to the common Sounds library; the `Sounds/` prefix is case-insensitive. Nested paths, `..`, separators and invalid filename characters are rejected for common lookup. Only `.wav` and `.mp3` are shared. Images and instructions remain package-relative.

The common directory is `<project>/Sounds` in Editor/local Web development, `DataRoot/Sounds` in desktop players and `/data/sounds` on the server (`Server/Deploy/Sounds` on the host). Desktop builds seed missing common files even for initialized installs. Local Web builds copy sounds and generate a read-only manifest. Web Settings > Common Audio Files lists names and sizes, previews (Play/Pause) and downloads files, uploads files up to 64 MiB, and deletes unused files. Duplicate names return 409. Deletion of a referenced common file returns 409 listing referencing packages; package-local files shadow the common file.

## HideLofarSignalsIfNoSelectedBTH

`HideLofarSignalsIfNoSelectedBTH` is a `"Yes"`/`"No"` experiment setting and may be overridden per phase. The default is `"No"`.

- `"No"`: when the BTH has no selection, each LOFAR shows frequencies for all signals currently represented in the visible BTH, which is the existing behavior.
- `"Yes"`: when the BTH has no selection, each LOFAR shows deterministic background noise only. After the subject selects a BTH bearing region, the LOFAR shows frequency history for signals matching that selected region.

Clearing the BTH selection returns the LOFAR immediately to the appropriate unselected behavior for the current phase.

## Waterfall scan rate and history

`WaterfallScanRateHz` changes the temporal resolution of BTH and LOFAR displays; it does not change the configured history duration. The renderer allocates approximately `(TimeEnd - TimeStart) * WaterfallScanRateHz` rows. Definitions requiring more than 4096 rows, or fewer than 2, for any waterfall are rejected.

## Independent LOFAR jitter and noise

`LoFarJitterFactor` and `LoFarBackgroundNoise` are optional numbers from 0 to 1. Each resolves from signal instance → phase signal-definition override → experiment signal definition → phase LOFAR value → ExperimentSettings LOFAR value → resolved phase `JitterFactor` / `BackgroundNoise`. They affect only LOFAR; BTH retains its existing values. A LOFAR row uses the **maximum effective noise** among signals feeding that row; an empty row uses phase LOFAR noise. Live and reconstructed history use the same calculation and deterministic keys. Omitting these fields preserves existing rendering.

## Determinism

`RandomizationSeed` is a non-zero 64-bit integer. Noise/jitter are keyed by seed, phase, signal, waterfall and scan index rather than consuming Unity's mutable random state. This targets mathematically repeatable signal state; exact cross-GPU pixels are not a requirement. Running the same package with the same seed produces the same signal state. Change the seed to get a different but equally repeatable noise pattern.

## Scoring display and end-of-experiment results

### ResetScoreWhenPhaseStarts

`ResetScoreWhenPhaseStarts` (`"Yes"`/`"No"`, default `"Yes"`) can be set in `ExperimentSettings` and overridden per phase. When the resolved value for a phase is `Yes`, the Correct/Partial/Incorrect tally in the feedback section returns to 0 when that phase begins. When it is `No`, the tally keeps counting from the previous phase. It only changes what the subject sees: the CSV records every classification either way, and the end-of-experiment overall totals are unaffected. The first phase always starts at 0.

### End-of-experiment results

`ExperimentSettings.ShowResultsAtEnd` (`"Yes"`/`"No"`, default `"No"`) controls whether the mandatory experiment-end popup includes Correct/Partial/Incorrect totals and percentages, `Responses` (completed classification responses), and `Signals` (actual signals presented in the BTH).

When the experiment has more than one phase, the popup first lists a `Phase Results` line for each phase, then the `Overall Results` for the whole run. Each phase line shows the tally exactly as the feedback section displayed it when that phase ended, plus the signals presented in that phase. If a phase sets `ResetScoreWhenPhaseStarts` to `No`, its line therefore includes the carried-over counts from earlier phases. The overall totals always count every response once.

### Completion code

`ExperimentSettings.ShowCompletionCode` (`"Yes"`/`"No"`, default `"No"`) gives each participant a unique code to prove they finished, for example to paste into a recruitment provider. When it is `Yes` and the final phase ends naturally, the app generates a new code such as `K7QMX-3PRDA` and shows it at the top of the end popup, together with a **Copy Code** button. The code is also saved with the run (see `Documentation/RESULT_FORMAT.md`). Runs that are exited or aborted get no code.

Codes are 10 random characters from an alphabet without look-alike characters (no `0`/`O`, `1`/`I`/`L` or `U`), drawn from a cryptographic random generator for every run. They are not derived from the seed, so they differ even between identical runs, and the chance of two runs sharing a code is negligible (about 1 in 590 trillion per pair).

In an external-study sequence, each experiment with `ShowCompletionCode` = `Yes` saves its own code, but only the final experiment's popup is shown, so set it on the last experiment in the sequence for the participant to see it.

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

## Instructions Markdown

Instructions files use a small Markdown subset. Native and Web builds render it the same way. Each construct must start at the beginning of a line:

| Syntax | Result |
|---|---|
| `# `, `## `, `### ` | Headings, three sizes. |
| `- item` or `* item` | Bulleted list, indented, with a drawn bullet. Wrapped lines align under the item text. |
| `1. item` | Numbered list, indented like bulleted lists. |
| two leading spaces per level before `-`, `*` or `1.` | Indents that list item one more level (up to four levels). |
| `> text` | Quote: lighter text beside a gray bar. Consecutive `>` lines form one block; a bare `>` line adds a small gap inside it. |
| `---` | Horizontal rule. |
| `**bold**`, `*italic*`, `` `code` `` | Inline emphasis, usable within any text line. |
| blank line | Paragraph break; also ends a list. |
| `![alt](path){attrs}` on its own line | Image or video (below). |

Other Markdown, such as links, tables and HTML, is not interpreted and appears as plain text. Long instructions scroll.

### Images and videos in instructions

Images and videos use the Markdown image line, on a line by itself, with an optional attribute block. The file extension selects the media type: `.mp4`, `.m4v`, `.webm` and `.mov` are videos; anything else is loaded as an image.

```markdown
![Layout](Images/layout.png)
![Layout](Images/layout.png){width=50% align=right}
![Intro demo](Videos/intro.mp4){width=640 align=center}
![Looping clip](Videos/contact.mp4){width=480 height=270 align=center autoplay loop muted nocontrols}
```

| Attribute | Applies to | Meaning |
|---|---|---|
| `width=640` / `width=640px` / `width=50%` | images, videos | Pixel width or percentage of the instruction column. Capped at the column width. Default: full column width. |
| `height=360` | images, videos | Pixel height. If only one dimension is given the other follows the media aspect ratio. If both are given the box is used as-is and the media is letterboxed inside it. |
| `align=left` / `center` / `right` | images, videos | Horizontal placement within the column. Default `left`. |
| `autoplay` | videos | Start playing when the instructions open. |
| `loop` | videos | Repeat at the end. |
| `muted` | videos | Play without audio. |
| `nocontrols` | videos | Hide the Play/Pause button, seek bar and time readout. Clicking the video still toggles playback. |

Without attributes, images keep their previous behavior: full width with the original aspect ratio. Media stay in the document flow, so they never overlap text, and the scroll height always includes them. Unknown or invalid attributes are ignored, and a warning is logged.

Video guidance:

- Use **H.264 video and AAC audio in `.mp4`**. It is the only combination that plays on Windows, macOS and every supported browser. `.webm` (VP8/VP9) does not play in Safari, and `.mov` support depends on the platform.
- Browsers usually block autoplay *with sound* until the participant has interacted with the page. For reliable autoplay on Web, add `muted`. If autoplay is blocked, the video waits with its Play control showing.
- Keep the package ZIP under the 64 MiB upload limit. Prefer short, compressed clips (for example 720p at about 1–2 Mbit/s). Put videos in a `Videos/` folder by convention; paths must be package-relative.
- Playback happens only while the instructions are visible. Closing the instructions stops the video. Video playback is not part of the deterministic simulation.
- When videos play in the in-task instructions overlay, `Video_Played`, `Video_Paused`, `Video_Seeked` and `Video_Ended` events are recorded. See [RESULT_FORMAT.md](RESULT_FORMAT.md).

## Validation rules summary

Validation (Editor menu, Web upload and package load) rejects a package when:

- `SchemaVersion` is not 1, `Name` is empty, or `RandomizationSeed` is 0.
- `PhaseOrder` is empty or names a phase missing from `Phases`.
- `ExperimentSettings.LOFARViews` is empty, a phase `LOFARViews` override is empty, or a view has `HzEnd ≤ HzStart`, `TimeEnd ≤ TimeStart` or a tick/label interval ≤ 0.
- Any waterfall would need fewer than 2 or more than 4096 scan rows.
- `SignalDurationSec` or `WaterfallScanRateHz` is ≤ 0, or `AutoStartCountdownSec` is outside 0..3600.
- A signal definition lacks `Name`, `Classification` or at least one `FreqTuple`, or a new phase-only signal type is missing any field.
- A phase has no `Signals`, or an instance references an unknown signal type, has negative `AppearSec`, bearing outside 0..360, duration ≤ 0 or `AudioVolume` outside 0..1.
- A `Width` is ≤ 0, or an `Alpha`, `LoFarJitterFactor` or `LoFarBackgroundNoise` is outside 0..1.
- A phase alert has negative `AppearSec`, appears after the computed phase end, or has a bearing outside 0..360.
- A referenced instructions or audio file (`InstructionsFile`, `AudioFile`, `BackgroundAudio`) is missing or uses a disallowed path. Images and videos referenced inside instructions are not checked at upload, so open the instructions once to confirm they display.
- `LslWaterfallBins` is not 256, 512 or 1024.
