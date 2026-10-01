# Fixed31 changes

- Split password-protected Web Settings into separate Experiments, Results, External Studies, and Account views.
- Added server-persisted External Study mappings.
- Added direct participant launches for Prolific, CloudResearch Connect, SONA, and a generic URL mode.
- External participant IDs populate SONAR Subject ID automatically.
- Provider study/project IDs map server-side to a SONAR experiment package.
- External participant sessions are restricted to the assigned experiment.
- Added Auto Start and Show Instructions mapping controls.
- Added server-configured completion redirects and same-tab browser return navigation.
- Completed external runs wait for successful server finalization before the completion redirect is offered.
- Added recruitment-provider fields to run metadata JSON.
- Settings admin authentication remains separate from participant external-launch authentication.
- Preserved all fixed30 changes, including repeated Signal ID support, selected Experiment highlighting, Classification focus-ring fix, Confidence visual reset, and desktop Documents/SonarTask storage.
