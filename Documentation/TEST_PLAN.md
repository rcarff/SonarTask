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


## fixed30/fixed32 regression checks

- Select several Experiment buttons and verify only the currently selected Experiment has the blue selected background.
- Click several Classification buttons during a run and verify previously selected buttons do not retain Unity's blue focus ring.
- Complete several Confidence selections and verify the radio circles return to their light-gray idle state after each response.
- Author two signal occurrences with the same `Id` in one phase. Verify validation accepts the package and both occurrences independently generate `Signal_Start` and `Signal_End` events.
- Run **Web Local Test (Build and Run)**. Verify Settings is available after local re-authentication and the separate Experiments, Results, External Studies, and Account views open.
- In Local External Studies, create a mapping and open the generated provider URL. Verify Subject ID and experiment assignment are automatic.

## fixed33 external-study assignment tests

### Fixed and persistent assignment

1. Configure one enabled Generic/Prolific mapping in Fixed mode with one group and one experiment.
2. Launch with participant `P001`; verify Login/Startup are bypassed and the configured experiment is loaded.
3. Reload/relaunch with the same participant ID; verify the same group is returned.
4. Change only the participant ID to `P002`; verify a second persistent assignment is created.
5. Confirm run metadata contains provider IDs, ExternalStudyId, AssignmentMode, AssignmentGroup, ExperimentSequence, SequenceIndex, and SequenceCount.

### Random

1. Configure at least two groups in Random mode with weights 1/1, then 2/1.
2. Launch many unique test participant IDs.
3. Verify every participant receives exactly one persistent group; repeated launch of the same ID does not reroll.
4. Verify assignment counts are visible in Settings. Do not require exact equal totals because Random is intentionally independent.

### Balanced Random

1. Configure two equal-weight groups in Balanced Random mode.
2. Launch at least 20 unique participant IDs, including several concurrently from separate browser profiles.
3. Verify assigned totals remain approximately equal and each participant remains in the original group after refresh/relaunch.
4. Repeat with weights 2/1 and verify allocation tracks approximately the configured ratio.

### Counterbalanced sequence

1. Install `ExperimentA` and `ExperimentB`.
2. Configure Counterbalanced groups `AB -> ExperimentA, ExperimentB` and `BA -> ExperimentB, ExperimentA` with equal weights.
3. Launch several unique participants. Verify assignment counts are balanced across AB/BA and each participant starts the first experiment from its assigned sequence.
4. Naturally complete the first experiment. Verify the first run is saved before the assignment advances and SONAR automatically loads/routes to the second experiment.
5. Verify there is no provider completion redirect after step 1.
6. Complete step 2. Verify the assignment is marked Completed and the completion action/redirect appears only now.
7. Verify the two run metadata files share AssignmentGroup/ExperimentSequence and contain SequenceIndex 1 and 2 respectively.
8. Interrupt a participant in the middle, then relaunch using the same participant ID. Verify SONAR returns to the current unfinished sequence step rather than reassigning the participant.

### Configuration protection/security

1. After at least one assignment exists, attempt to edit provider/study ID, mode, group names/weights, or experiment sequence. Verify the server rejects the allocation-definition change.
2. Verify Enabled, Auto Start, Show Instructions, and Completion URL can still be changed.
3. Verify a study with assignments cannot be deleted and can instead be disabled.
4. As a participant, attempt to request another installed experiment definition/assets directly. Verify the API denies access.
5. Add `experiment=AnotherPackage` to a production launch URL. Verify server-side assignment still determines the experiment.
6. Verify an unknown/disabled Provider Study ID produces a readable external-launch error.
7. In Local WebGL mode only, verify `experiment=`, `autostart=`, and `instructions=` development overrides still work.


## Fixed34 external-study test-mode/reset tests

1. Deploy the fixed34 WebGL build and API server while preserving `/data/config`, `/data/experiments`, and `/data/results`.
2. In Settings > External Studies, verify each study shows separate Production and Test assigned/completed counts.
3. Edit a saved Prolific mapping and click **Generate Secure Test URL**. Verify the URL contains `PROLIFIC_PID=SONAR-TEST-...`, the configured `STUDY_ID`, and a `sonar_test_token`.
4. Open the copied URL in a private/incognito window. Complete enough test launches to exercise all groups. Verify only Test counts change and Production counts remain unchanged.
5. For Balanced Random/Counterbalanced modes, verify test allocation balances against Test counts only and does not affect the next production group choice.
6. Complete a test run and verify its `.meta.json` contains `ExternalTestMode: true`.
7. Click **Clear Test Data** and confirm. Verify Test counts return to zero, production counts are unchanged, and the corresponding test CSV/meta/experiment snapshot files are removed.
8. Verify an arbitrary deployed URL containing only `sonar_test=1` is not treated as test mode. A deployed server requires a valid generated `sonar_test_token`.
9. Verify expired/invalid test tokens return a readable authorization error.
10. Before any real recruitment, exercise **Reset Production Data** on a disposable study. Verify the double confirmation and that production assignment counts return to zero. With full reset, verify associated production result files are removed.
11. In Web Local Test, verify `sonar_test=1` uses separate browser-local Test counts and **Clear Test Data** clears only those test assignments.


## Fixed35 External Study save-before-test regression tests

1. Open an existing External Study, uncheck **Enabled**, save, reopen it, check **Enabled**, and click **Save + Generate Secure Test URL** without clicking the separate Save button. Open the generated URL and verify the study starts.
2. Repeat in **Web Local Test** and verify the local test starts and the External Studies list shows the mapping as Enabled after returning to Settings.
3. Create a new External Study and click **Save + Generate Secure Test URL** as the first persistence action. Verify the study is saved and the generated URL launches successfully.
4. Change the Provider Study / Project ID in the editor and immediately use **Save + Generate Secure Test URL**. Verify the generated URL contains the new ID and launches against that new saved ID.
5. Deliberately disable a saved mapping and launch its normal participant URL. Verify the error explicitly says the matching External Study is disabled.
6. Use an unknown Provider Study / Project ID and verify the error explicitly says no matching mapping exists.
