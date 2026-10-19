# fixed34

- Added safe External Study **Test Mode** for deployed Web installations.
- Settings can generate a cryptographically random, server-authorized test URL that expires after 24 hours.
- Deployed test mode requires `sonar_test_token`; a participant cannot self-declare test mode with `sonar_test=1`.
- Test participant assignments are stored separately from production assignments and never affect production Random/Balanced Random/Counterbalanced allocation.
- Settings shows separate Production and Test assigned/completed counts per study and per group.
- Test run metadata includes `ExternalTestMode=true`.
- **Clear Test Data** removes test assignments plus associated test result files while preserving all production data/counts.
- Added a strongly confirmed **Reset Production Data** operation for pre-launch cleanup when testing was accidentally performed as production.
- Web Local Test supports the same conceptual separation using browser-local production/test assignment stores; `sonar_test=1` remains a local-development-only shortcut.
- Added WebGL clipboard support for copying generated test URLs.
