# fixed30 changes

This update is based on fixed29 and includes the accumulated UI and validation corrections requested afterward.

- Experiment Selection: the selected experiment button uses the same blue selection color as Classification buttons.
- Classification: previously clicked classification buttons no longer retain Unity's blue EventSystem focus/selected tint.
- Confidence: confidence radio circles return to the normal light-gray disabled appearance after a response is recorded.
- Signal IDs: `SignalInstance.ID` may be reused within a phase or across phases to identify the same source/contact at different times or locations.
- Scheduler: repeated-ID signal instances are tracked independently, so each occurrence generates its own start/end events.
- Web upload validation and Unity-side experiment validation no longer reject repeated signal IDs.
- Documentation and validation test guidance were updated for these behaviors.

## Web deployment note

Because fixed30 changes both the Unity client UI and server-side experiment-package validation, an existing Web deployment should receive a newly built Unity Web client and a rebuilt API container using the updated `Server/Sonar.Server/Program.cs`. Existing experiment/result/config data directories should be preserved.
