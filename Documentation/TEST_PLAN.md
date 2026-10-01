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

## Experiment selection and classification workflow

- Selecting an experiment turns only that experiment button blue; selecting another restores the previous button to its normal color.
- Returning from Instructions preserves the selected experiment highlight.
- Buttons are disabled without a BTH selection.
- Confirm is disabled until a classification is chosen.
- After Confirm, classification/BTH input is locked until confidence is chosen.
- Classification buttons use blue only for the currently selected classification; previously clicked buttons do not retain a blue focus ring.
- Confidence produces one response marker, clears selection, and restores the default state.
- After a confidence value is recorded, its radio circle returns to the same light-gray disabled appearance as the other confidence controls; previously used confidence circles do not remain blue.
- YES/PARTIAL/NO is correct for single and overlapping contacts.
- Feedback conveys state with text/shape as well as color.

## Signal IDs and repeated occurrences

- Upload an experiment in which two signal instances in the same phase share one `ID`; validation must accept the package.
- Run that phase and verify each repeated-ID entry independently emits one `Signal_Start` and one `Signal_End` at its own configured times.
- Repeat the same `ID` across phases and verify it remains valid.
- Verify CSV `Signal_ID` retains the authored repeated source/contact ID for each occurrence.

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


## Fixed31 Web settings navigation

1. Log in to the production Web build and open Settings.
2. Re-authenticate with the operator password.
3. Verify the Settings home shows separate buttons for Experiments, Results, External Studies, and Account.
4. Open each view and verify only that view's controls are shown.
5. Verify Back to Settings returns to the Settings home without another password prompt while the admin token remains valid.

## Fixed31 external study mappings

1. In Settings > External Studies, add one mapping for each provider used in the deployment.
2. Verify only installed SONAR experiments can be selected.
3. Verify duplicate Provider + Study/Project ID mappings are rejected.
4. Disable a mapping and verify its participant launch is rejected.
5. Delete a mapping and verify it no longer appears after refresh.
6. Restart/recreate the API container and verify mappings persist through the Config bind mount.

## Fixed31 Prolific launch

1. Create an enabled Prolific mapping whose Provider Study ID matches a test `STUDY_ID`.
2. Open `/?provider=prolific&PROLIFIC_PID=TESTPARTICIPANT000000&STUDY_ID=<mapped-id>&SESSION_ID=TESTSESSION`.
3. Verify no SONAR operator password is requested.
4. Verify Subject ID is the supplied `PROLIFIC_PID` and only the mapped experiment is accessible.
5. Verify Auto Start / Show Instructions behavior matches the mapping.
6. Complete the task and verify final results are saved before the completion redirect is offered.
7. Verify `.meta.json` contains the Prolific participant/study/session fields.

## Fixed31 Connect launch

1. Create an enabled Connect mapping using the Connect `projectId` as Provider Study / Project ID.
2. Open `/?provider=connect&participantId=TEST&projectId=<mapped-id>&assignmentId=TEST`.
3. Verify `participantId` becomes SONAR Subject ID and provider metadata is stored.
4. Verify the configured Connect completion redirect is used only after successful run finalization.

## Fixed31 SONA launch

1. Create an enabled SONA mapping with a researcher-chosen mapping key.
2. Configure the SONA Study URL as `/?provider=sona&study=<mapping-key>&subject=%SURVEY_CODE%`.
3. Use SONA's integration test or a substituted test survey code.
4. Verify the survey code becomes SONAR Subject ID and the assigned experiment launches.
5. Verify the configured SONA completion URL is used after successful completion.

## Fixed31 external-session access control

1. Start a valid external participant session.
2. Call `/api/experiments` and verify only the assigned experiment is listed.
3. Attempt to fetch another package definition or asset and verify access is denied.
4. Attempt to submit run metadata using a different Subject ID or experiment package and verify access is denied.
5. Verify `/api/admin/*` operations remain unavailable without the operator Settings re-authentication cookie.


## Fixed32 local Web Settings and parameter-launch tests

1. Run **SONAR > Build > Web Local Test (Build and Run)** and log in with `SonarDev123!`.
2. Verify the Startup **Settings** button is visible. Open it and verify Enter submits the local password.
3. Verify Experiments, Results, External Studies, and Account views are reachable.
4. Verify Experiments lists bundled packages and upload/delete are disabled.
5. Verify Results opens and explains production result downloads are server-only.
6. Verify Account opens and password change is disabled.
7. In External Studies, add, edit, enable/disable, and delete/re-create a Generic mapping.
8. Copy/open the generated localhost launch URL and verify Subject ID and assigned experiment are set automatically.
9. Test `?provider=generic&subject=TEST01&study=LOCAL&experiment=<package>&autostart=1&instructions=1`. Verify instructions appear before start.
10. Repeat with `instructions=0` and verify direct task start. Repeat with `autostart=0` and verify the restricted experiment-selection path.
11. Verify a non-existent `experiment=` package gives a readable error.
12. Verify production/non-loopback builds still rely on server-side mappings and do not trust `experiment=`.
