# fixed33

## External-study allocation

- Added server-side persistent participant assignment for Prolific, CloudResearch Connect, SONA, and Generic launch providers.
- Added four assignment modes: Fixed, Random, Balanced Random, and Counterbalanced.
- External Study groups can contain a single experiment or an ordered experiment sequence. Integer weights are supported.
- Balanced Random and Counterbalanced allocations are made inside a serialized SQLite transaction so concurrent arrivals see up-to-date assignment counts.
- Re-entering with the same provider study + participant ID returns the original group/current sequence step rather than rerandomizing.
- Counterbalanced multi-experiment assignments automatically advance only after the current run has been successfully saved; provider completion occurs only after the final sequence step.
- Participant Web sessions are restricted to the currently assigned experiment package and their own run API calls.
- Settings > External Studies now shows total/group assignment and completion counts and provides group/sequence authoring.
- Allocation-defining fields are locked after the first participant assignment; safe operational fields remain editable.
- Experiment packages referenced by External Study mappings are protected from deletion.
- When possible, legacy fixed31/fixed32 single-experiment mapping JSON is imported into the new SQLite model as Fixed assignments on first startup.

## Results and validation

- External recruitment/assignment fields are written into each run's metadata sidecar; the fixed schema-v3 CSV columns are unchanged.
- Repeated signal IDs remain valid and are scheduled by signal occurrence index so repeated contacts independently generate start/end events.

## Local Web testing

- Local External Studies supports the same Fixed/Random/Balanced Random/Counterbalanced concepts using browser-local development storage.
- Settings and parameter-based launch testing remain available with the fixed local password.

## Deployment

- The API automatically creates `external_studies`, `external_assignments`, and `run_external_context` tables in the existing persistent `Deploy/Config/sonar.db`.
- Upgrade both the WebGL build and `Sonar.Server`; keep existing Config/Experiments/Results bind mounts.
