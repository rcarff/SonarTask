# Desktop storage locations

The installed Windows and macOS applications do not write experiment or result data beside the executable/application bundle. This avoids write-permission problems under Windows `Program Files` and macOS `/Applications`.

## Unity Editor

Play Mode is deliberately isolated from the user's Documents folder. It uses project-local directories:

```text
<ProjectRoot>/Experiments
<ProjectRoot>/Results
```

The `SONAR_DATA_ROOT` environment variable is ignored in the Unity Editor.

## Windows and macOS players

Built desktop players default to the current user's Documents folder:

```text
Windows: C:\Users\<user>\Documents\SonarTask\
macOS:   /Users/<user>/Documents/SonarTask/
```

The application creates:

```text
SonarTask/
├─ Experiments/
├─ Results/
└─ Logs/
```

## First-run experiment seeds

Desktop builds contain read-only seed experiment packages under Unity `StreamingAssets`. On first run, missing seed files are copied into `Documents/SonarTask/Experiments`. Existing user files are not overwritten.

The normal Windows/macOS build commands synchronize the project-level `Experiments/` folder before building. The same sync can be run manually with:

**SONAR → Build → Sync Desktop Seed Experiments**

## Existing installation migration

If an older installation has writable `Experiments/` or `Results/` directories beside the application, Startup offers **Copy Data** or **Skip**. Migration copies data into the new data root and never deletes the original files.

## Optional data-root override

Built Windows/macOS applications honor the environment variable:

```text
SONAR_DATA_ROOT
```

When set, that directory becomes the root containing `Experiments/`, `Results/`, and `Logs/`. This is useful for dedicated research drives or institution-managed storage.
