# Web External Studies: Prolific, Connect, SONA, and group assignment

External Study mappings are administered from the normal password-protected Web interface:

`Startup -> Settings -> External Studies`

The recruitment site identifies the participant and provider study/project in the launch URL. SONAR resolves that information on the server, creates or retrieves a persistent participant assignment, signs the browser into a restricted participant session, and exposes only the experiment currently assigned to that participant.

## Prolific setup

1. Deploy both the fixed33 WebGL client and fixed33 `Sonar.Server` API. Rebuilding only the WebGL files is not sufficient because allocation and participant sessions are server-side.
2. Upload every SONAR experiment package that may be assigned.
3. Open **Settings -> External Studies -> Add External Study**.
4. Choose **Prolific**.
5. Set **Provider Study / Project ID** to the exact Prolific `STUDY_ID` for the study. Do not use the human-readable Prolific study title.
6. Choose the desired assignment mode and configure its group(s)/sequence(s).
7. Paste the Prolific completion URL into **Completion URL** if automatic return is desired.
8. Enable the mapping and save it.
9. In Prolific, launch SONAR at `https://YOUR-SONAR-HOST/?provider=prolific` and configure Prolific to append/pass `PROLIFIC_PID`, `STUDY_ID`, and `SESSION_ID`.

The arriving browser should therefore have a URL equivalent to:

`https://YOUR-SONAR-HOST/?provider=prolific&PROLIFIC_PID=PARTICIPANT&STUDY_ID=STUDY&SESSION_ID=SESSION`

`PROLIFIC_PID` becomes the normal SONAR Subject ID. `STUDY_ID` selects the server-side External Study mapping. `SESSION_ID` is retained in run metadata. A participant does not need the operator password and cannot use the launch URL to choose another production experiment.

Before publishing, manually test with the real configured Study ID:

`https://YOUR-SONAR-HOST/?provider=prolific&PROLIFIC_PID=TEST001&STUDY_ID=YOUR_REAL_STUDY_ID&SESSION_ID=TESTSESSION`

If this fails, check that the External Study is enabled, the incoming `STUDY_ID` exactly matches the configured Provider Study ID, all referenced experiment packages are installed, the deployed API was rebuilt, and `/opt/sonar/Deploy/Config` is writable/persistent. API logs are useful while testing: `docker compose logs -f api`.

## Assignment modes

### Fixed

Every participant receives the one configured group. The group's experiment list may contain one experiment or a sequence.

Example: `Group A -> DetectionTaskA`.

### Random

Every new participant is independently selected from the groups using each group's positive integer **Weight**. With weights 1 and 1 the probability is 50/50. With weights 2 and 1 the long-run target is about 2:1, but short-run counts can differ because choices are independent.

### Balanced Random

SONAR compares the number already assigned to each group relative to its weight and chooses randomly among the least-filled choices. With equal weights this keeps group counts approximately equal while randomizing the order of assignment. This is the recommended default for ordinary between-subject conditions when enrollment totals are not known in advance.

Example:

- `Group A`, weight `1`, sequence `DetectionTaskA`
- `Group B`, weight `1`, sequence `DetectionTaskB`

One Prolific Study ID can then feed both conditions.

### Counterbalanced

Each group represents an **ordered sequence of experiment package names**. Participants are assigned across sequence groups with the same balanced allocation rule.

For two conditions:

- `AB`, weight `1`, sequence `ExperimentA, ExperimentB`
- `BA`, weight `1`, sequence `ExperimentB, ExperimentA`

SONAR runs the first experiment, saves it, advances the server-side assignment, loads the next package, and only offers the recruitment-provider completion redirect after the entire assigned sequence is complete.

For three-condition designs, enter the sequences required by the protocol, for example `ABC`, `BCA`, and `CAB`, or all six permutations if full counterbalancing is required. The software does not automatically derive a Latin square; the administrator explicitly defines the sequences so the intended design is visible and auditable.

## Persistent assignment behavior

Assignments are stored in `Deploy/Config/sonar.db` using provider study + participant ID. Assignment creation uses a serialized SQLite transaction so concurrent arrivals cannot make balancing decisions against the same stale counts. Reloading the browser or returning through the provider gives the participant the same group and current sequence position.

Once a study has participant assignments, its provider/study ID, assignment mode, and groups/sequences are locked to protect the design. You can still enable/disable the mapping and change Auto Start, Show Instructions, or Completion URL. A study with assignments cannot be deleted; disable it instead. An experiment package referenced by any External Study mapping is also protected from deletion until the referencing mapping is changed or removed.

The External Studies list shows total Assigned/Completed counts and group-level counts. `Assigned` includes participants who later abandon the study; balancing is based on assignments, not completions.

## CloudResearch Connect

Use provider **CloudResearch Connect** and configure **Provider Study / Project ID** to the incoming Connect `projectId`. The launch expects `participantId` and may also retain `assignmentId`:

`https://YOUR-SONAR-HOST/?provider=connect&participantId=PARTICIPANT&projectId=PROJECT&assignmentId=ASSIGNMENT`

## SONA

Use provider **SONA** and choose a Provider Study / Project ID you will put in the launch URL. Configure the SONA URL so its survey code is substituted into `subject`:

`https://YOUR-SONAR-HOST/?provider=sona&study=SONAR-STUDY-ID&subject=%SURVEY_CODE%`

## Generic providers

A generic provider can use:

`https://YOUR-SONAR-HOST/?provider=generic&study=STUDY-ID&subject=SUBJECT-ID&session=OPTIONAL-SESSION`

This is useful for other recruitment systems that can pass URL query parameters.

## Local WebGL testing

**SONAR -> Build -> Web Local Test (Build and Run)** supports the same Settings views without Docker. Re-authenticate with `SonarDev123!`, create mappings under **External Studies**, and use the generated localhost URL. Local mappings/assignments are browser-only and never modify the production database.

Local development additionally accepts `experiment=PACKAGE_NAME` as a direct fallback and `autostart=0|1` / `instructions=0|1` as test overrides. Production deployments deliberately ignore `experiment=` as participant authority.

Because production operator and participant authentication use the same browser cookie name, use a separate browser profile/incognito window when you want to keep an administrator Settings session open while simultaneously testing a participant launch.

## Deployment notes for an existing server

Update both sides together:

1. Build fixed33 WebGL and replace `Deploy/WebBuild`.
2. Replace/rebuild `Server/Sonar.Server` from fixed33 and recreate the API container.
3. Keep the existing bind-mounted `Deploy/Config`, `Deploy/Experiments`, and `Deploy/Results` directories.

On API startup, SQLite creates the new external-study/assignment tables inside the existing `sonar.db`; experiment/results data is not erased. If the new External Study table is empty, the server also looks for legacy fixed31/fixed32 mapping files named `external-studies.json`, `external_studies.json`, or `externalStudies.json` under `Deploy/Config` and imports valid single-experiment mappings as Fixed assignments. The legacy file is left untouched as a backup. Back up `Deploy/Config` and `Deploy/Results` before production upgrades as usual.

Prolific Secure External URL/JWT validation is not implemented in this version; fixed33 validates the provider/study mapping and maintains a restricted SONAR participant session, but it does not cryptographically verify a Prolific launch token.


## Safe pre-launch testing and reset (fixed34)

External Studies now keep **test assignments separate from production assignments**. This is the recommended way to verify a deployed Prolific/Connect/SONA configuration before opening recruitment.

From **Settings > External Studies**, edit a saved study and choose **Save + Generate Secure Test URL**. The current editor values are saved first, so the generated test always uses the same Enabled/provider/study/group configuration shown on screen. The server creates a random, 24-hour test token. The generated URL uses a synthetic participant ID and includes that token. Open the copied URL in a private/incognito browser window so the participant session cookie does not replace the administrator Settings session.

Test-mode assignments use a separate SQLite table and do **not** participate in production Random, Balanced Random, or Counterbalanced allocation. The Settings list shows separate Production and Test assigned/completed counts for the study and each group.

Use **Clear Test Data** when testing is complete. It deletes the study's test assignments and associated test result files while leaving production assignments, production results, and production balancing counts unchanged.

If testing was accidentally performed through the production launch URL, **Reset Production Data** is available to administrators. It requires confirmation. The full reset deletes production assignment rows and the result-file families associated with that External Study. Use it only before real recruitment begins. A reset-counts operation may also retain result files while clearing allocation records; returning participant IDs can then be assigned again.

The presence of `sonar_test=1` by itself is accepted only by **Web Local Test**. A deployed server requires a valid server-generated `sonar_test_token`, so a real participant cannot simply edit the URL to remove themselves from production counts.


## fixed35 save-before-test behavior

Test URL generation now performs the same validation and persistence as the normal Save action before requesting a test token. This specifically prevents an unsaved **Enabled** checkbox (or an unsaved Provider / Study ID change) from producing a URL whose launch lookup still sees the older server-side mapping. If a launch fails, the API now tells you whether the provider/study mapping is missing or whether the matching mapping exists but is disabled.
