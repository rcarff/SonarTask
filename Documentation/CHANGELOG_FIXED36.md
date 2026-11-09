# FIXED36

Added confidence prompting, optional persistent BTH selection, per-phase pause/exit controls, in-task phase instructions and automatic instruction prompts. LOFAR jitter and row noise now support independent inherited overrides without changing default deterministic rendering.

Added a common Sounds library across desktop, Web and local Web development, Web sound management with immutable uploads and referenced-file deletion protection, tolerant Yes/No parsing, instruction scrollbars, expired-elevation recovery and result lists that omit missing CSV files. Existing manually edited scene files are preserved.

See DEPLOY_FIXED36.md for the new storage mount and Documentation/TEST_PLAN.md for validation.

Validation completed: Unity runtime/editor C# assemblies and WebGL conditional code compile; API builds; 70 core/API regression checks pass; 630 default LOFAR rows match the previous implementation bit for bit in a headless calculation check; browser bridge checks cover ZIP compatibility, sound uploads, credentials, progress, conflicts and size rejection. Unity Play Mode, full player builds and container deployment remain manual checks.
