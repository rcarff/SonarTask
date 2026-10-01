# SONAR Simulator Task — Unity 6000.3.11f1

Reference implementation of a deterministic 2D passive-sonar experiment with GPU-backed BTH (bearing/time history) and LOFAR (frequency/time) waterfall displays. The same Unity project targets Windows, macOS, and desktop Web builds. The Web build is served with Docker Compose and uses an ASP.NET Core API for authentication, experiment-package management, and durable result storage.

> This is research-software source code and a functional starting implementation, not a validated acoustic model or certified sonar trainer. Validate the visual/acoustic behavior and timing against your experimental protocol before collecting study data.

## Included

- Unity project pinned to **6000.3.11f1**.
- Runtime scenes: Boot, Web Login, Startup, Experiment Selection, Instructions, Web Settings, and Sonar Task. Scene assets/build settings are generated automatically by the editor initialization script on first import.
- Responsive uGUI application interface; target logical resolution 1600×900, resizable without page scrolling.
- Shared `WaterfallDisplay` renderer used by BTH and every LOFAR view.
- Ping-pong `RenderTexture` waterfall shift/injection shader.
- Deterministically keyed noise and jitter; no dependency on Unity's mutable global PRNG.
- BTH circular-bearing selection including 000/360 wrapping.
- Multiple stacked LOFAR ranges.
- Training dropdown/carets, classification/confirm/confidence state machine, color + text/shape feedback, tally, alerts, and one `S` marker per submitted classification.
- Experiment selection and classification use explicit blue selection highlighting; stale EventSystem focus tint is suppressed, and confidence radio visuals reset after each submitted response.
- Per-phase `BTHOverlapAudio` behavior.
- Background/signal audio pause/resume behavior.
- CSV event log + JSON run metadata + exact experiment-definition snapshot.
- Desktop filesystem repository and Web HTTP repository behind the same experiment API abstraction.
- Desktop Windows/macOS data stored under the current user's Documents/SonarTask folder; Unity Editor Play Mode remains project-local.
- Web shared operator-password authentication with independent browser sessions, settings re-authentication, Argon2id password hashing, login throttling, SSH password reset, package upload/delete, result search, and result downloads.
- SQLite server index for completion checks and result listings; research event data itself remains ordinary CSV/JSON files.
- Browser heartbeat/reaper: abandoned active browser runs are retained and marked `ABORTED / BrowserDisconnected` after the grace interval.
- Two example experiment packages, both loose under `Experiments/` and uploadable ZIPs under `ExamplePackages/`.
- JSON Schema and authoring/result documentation under `Documentation/`.

## Repository layout

```text
SonarTaskUnity/
├─ Assets/
│  ├─ Editor/                 Unity setup/build menu scripts
│  ├─ Plugins/WebGL/          Browser file/download bridge
│  ├─ Scripts/
│  │  ├─ Bootstrap/
│  │  ├─ Core/                Deterministic experiment state/model/scoring
│  │  ├─ Rendering/           BTH/LOFAR waterfall component
│  │  ├─ Services/            Filesystem/Web/auth/results/audio
│  │  └─ UI/
│  ├─ Resources/             Runtime-included waterfall shader
│  └─ StreamingAssets/       Bundled first-run desktop experiment seeds
├─ Documentation/
├─ Experiments/               Editor-local experiments + desktop seed source
├─ ExamplePackages/           ZIP copies for testing Web upload
├─ Packages/
├─ ProjectSettings/
└─ Server/
   ├─ Sonar.Server/           ASP.NET Core 10 API
   ├─ nginx/
   ├─ scripts/
   ├─ docker-compose.yml
   └─ Deploy/
      ├─ Experiments/         Host bind-mounted into API container
      ├─ Results/             Host bind-mounted; never baked into container
      ├─ Config/              Password hash + SQLite index
      ├─ Certs/               HTTPS certificate/key
      └─ WebBuild/            Unity Web build served by nginx
```

# 1. Open the Unity project

1. Install **Unity Hub** and Unity Editor **6000.3.11f1**, including the Windows, macOS and/or Web build support modules you need.
2. Add/open this directory (`SonarTaskUnity`) as the Unity project.
3. Allow Package Manager/import to finish. The project manifest requests:
   - Visual Studio Editor integration 2.0.25
   - Newtonsoft JSON for Unity 3.2.1
   - Unity uGUI 2.0.0
4. The editor initialization code automatically creates `Assets/Scenes/*.unity`, configures Build Settings, sets the product name/version, and makes the window resizable. If needed run **SONAR → Initialize Project** manually.
5. Open `Assets/Scenes/Boot.unity` and press Play.

The waterfall shift shader is stored under `Assets/Resources/` intentionally. It is loaded at runtime from Resources so Unity cannot strip the shader from Web/player builds merely because no scene Material directly references it.

When running in the Unity Editor, SONAR intentionally reads and writes only project-local data:

```text
<ProjectRoot>/Experiments
<ProjectRoot>/Results
```

Editor Play Mode does **not** access or modify `Documents/SonarTask`.

## Visual Studio Code

Install current VS Code, Microsoft's **Unity** extension, and its C# Dev Kit dependency. Unity 6 uses the **Visual Studio Editor** package to generate project/solution information used by modern Unity IDE integration; the old `com.unity.ide.vscode` integration is not required. The included `.vscode/extensions.json` recommends the Unity extension.

In Unity, open **Edit → Preferences → External Tools**, select **Visual Studio Code** as External Script Editor, then regenerate project files if requested. Double-clicking a C# asset should then open it in the Unity-generated solution/workspace with Unity references available.

# 2. Desktop builds

Use the included editor commands:

- **SONAR → Build → Windows x64**
- **SONAR → Build → macOS**

Before each desktop build, the build command synchronizes the repository-level `Experiments/` directory into read-only `StreamingAssets` seed data inside the player. The installed application does **not** require writable files beside the executable or `.app` bundle.

## Desktop data location

Built Windows and macOS players default to the current user's Documents folder:

```text
Windows:
C:\Users\<user>\Documents\SonarTask\

macOS:
/Users/<user>/Documents/SonarTask/
```

The application creates:

```text
SonarTask/
├─ Experiments/
├─ Results/
└─ Logs/
```

This avoids write-permission failures when Windows installs the application under `Program Files` or macOS installs it under `/Applications`.

On first run, if the Documents data root is new, the bundled experiment packages are copied into `Documents/SonarTask/Experiments`. Existing files are never overwritten by this seed operation.

If an older portable/installed SONAR version has `Experiments/` or `Results/` beside the application, Startup offers to copy that data into the new Documents location. The migration is a **copy**, never a move; original files remain untouched. Choosing **Skip** initializes the new Documents location with bundled experiments only.

## Optional desktop data-root override

A built Windows/macOS application may use another writable research-data location by setting:

```text
SONAR_DATA_ROOT
```

For example on Windows:

```powershell
$env:SONAR_DATA_ROOT = "D:\SonarResearch"
```

or configure the environment variable system-wide for the experiment workstation. The selected root will contain `Experiments/`, `Results/`, and `Logs/`.

`SONAR_DATA_ROOT` is deliberately ignored by Unity Editor Play Mode so development/testing remains project-local.

## Updating bundled desktop experiments

The normal SONAR Windows/macOS build commands synchronize `Experiments/` automatically. You can also run:

**SONAR → Build → Sync Desktop Seed Experiments**

This updates `Assets/StreamingAssets/SonarTaskSeed/Experiments` from the project-level `Experiments/` directory.

# 3. Result files

Every run is independent and is never overwritten. Native runs produce a group such as:

```text
S0138__BasicContactClassification__20260902T203117438Z__20a06365.csv
S0138__BasicContactClassification__20260902T203117438Z__20a06365.meta.json
S0138__BasicContactClassification__20260902T203117438Z__20a06365.experiment.json
```

The CSV is the canonical event table for R, Python/pandas, MATLAB, SAS, SPSS, Excel, etc. The JSON sidecar contains run metadata; the experiment snapshot makes historical runs reproducible even after an experiment package is edited. See `Documentation/RESULT_FORMAT.md`.

# 4. Experiment packages

Each package owns its assets:

```text
Experiments/MyExperiment/
├─ experiment.json
├─ instructions.md
├─ Images/
└─ Sounds/
```

Instruction files use a deliberately small Markdown subset: headings, paragraphs, bold/italic, numbered/bulleted lists, block quotes, horizontal rules and local images. The same renderer is used on native and Web builds, avoiding an embedded native browser dependency.

Asset references are package-relative. Path traversal and absolute/external paths are rejected by the server.

Phase duration is computed rather than authored explicitly:

```text
PhaseDuration = max(Signal.AppearSec + EffectiveSignalDurationSec)
```

with duration inheritance `signal instance → phase default → experiment default`.


Signal instance `ID` values identify a source/contact and are intentionally **not required to be unique**. Multiple signal entries may reuse the same ID within one phase or across phases to represent the same object producing signals at different times/locations; each array entry is still scheduled and logged as a separate occurrence.

See `Documentation/EXPERIMENT_AUTHORING.md` and `Documentation/experiment.schema.json`.

# 5. Build the Web client

In Unity choose **SONAR → Build → Web**.

The command builds to `Build/Web/` and copies the resulting Web build to:

```text
Server/Deploy/WebBuild/
```

The project uses Brotli Web compression with Unity decompression fallback. Nginx also contains MIME/encoding rules for normal Unity `.br`/`.gz` build artifacts.

Official project target: current desktop versions of Chrome, Edge, Firefox and Safari. Mobile/tablet browsers are deliberately not part of the supported experiment environment.

# 6. Ubuntu 26.04 LTS Web server installation

The following assumes a fresh Ubuntu 26.04 LTS host and SSH/sudo access.

## Install Docker Engine + Compose plugin

Use Docker's current official Ubuntu repository instructions rather than Ubuntu's old distro Docker package. In condensed form:

```bash
sudo apt update
sudo apt install -y ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
  -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc

. /etc/os-release
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $VERSION_CODENAME stable" \
  | sudo tee /etc/apt/sources.list.d/docker.list >/dev/null

sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker
```

Docker's exact repository/bootstrap commands can change; for a production machine compare these steps against <https://docs.docker.com/engine/install/ubuntu/> at installation time.

## Copy the server deployment

For example:

```bash
sudo mkdir -p /opt/sonar
sudo chown "$USER":"$USER" /opt/sonar
cp -a Server/. /opt/sonar/
cd /opt/sonar
```

The host directories below are bind-mounted into the API container and survive container rebuilds/removal:

```text
/opt/sonar/Deploy/Experiments
/opt/sonar/Deploy/Results
/opt/sonar/Deploy/Config
/opt/sonar/Deploy/WebBuild
/opt/sonar/Deploy/Certs
```

This is intentional: experiment definitions and research data are **not stored inside a disposable Docker container**.

## Add the Unity Web build

Copy the contents of your Unity `Build/Web/` output into:

```bash
/opt/sonar/Deploy/WebBuild/
```

The Unity **SONAR → Build → Web** command already populates this directory when building on a machine using this repository layout.

## TLS certificate

HTTPS is required for the Secure authentication cookies. For local/lab testing you can create a self-signed certificate:

```bash
cd /opt/sonar
./scripts/generate-self-signed.sh sonar-lab.example.edu
```

Browsers will warn unless that certificate/CA is trusted by the client machines. For real use, replace `Deploy/Certs/sonar.crt` and `sonar.key` with certificates from your institution, reverse proxy, or a trusted ACME/Let's Encrypt workflow.

## Initialize/reset the operator password

Before first start:

```bash
cd /opt/sonar
docker compose build api
docker compose run --rm api init-password
```

Enter a password of at least 12 characters. Only an Argon2id password hash is stored in `Deploy/Config/auth.json`.

If the password is forgotten, an SSH administrator can reset it without knowing the old password:

```bash
cd /opt/sonar
docker compose run --rm api reset-password
```

Resetting the password also rotates the elevated Settings token key. Existing ordinary login sessions are not forcibly destroyed by this reference implementation; restart the API or clear browser sessions if your protocol requires immediate global logout.

## Start the server

```bash
cd /opt/sonar
docker compose up -d --build
```

Check:

```bash
docker compose ps
curl -k https://localhost/api/health
```

View logs:

```bash
docker compose logs -f api web
```

Stop without deleting host data:

```bash
docker compose down
```

Because the data directories are bind mounts, `docker compose down`, image upgrades and container recreation do not erase experiments/results.

# 7. Web behavior and administration

The Web app begins at the shared operator-password login. Each browser has an independent cookie session, so several subjects can run simultaneously.

The Startup screen includes **Settings**. Entering Settings prompts for the operator password again and grants a 15-minute elevated settings token.

Settings can:

- Change the shared operator password.
- Upload an experiment ZIP. ZIP extraction is path-sanitized and limited to 64 MB in this reference implementation.
- Delete existing experiment packages.
- Search/filter result filenames.
- Select multiple results and download them as a ZIP; selected downloads include the CSV and matching metadata/experiment snapshot files.
- Download all result files whose names match the visible filter.

The API maintains `Deploy/Config/sonar.db` only as a lightweight index for completion checks, run status, duplicate event-sequence protection and result listings. The authoritative study data remains in `Deploy/Results` as portable files.

## Browser interruption

While a Web experiment is active the Unity client sends a heartbeat every 10 seconds. The API checks active runs every 30 seconds and marks a run aborted after roughly 75 seconds without a heartbeat. This intentionally preserves refreshes, tab closures, power/network losses and crashed sessions rather than silently discarding them.

HTTP event submissions contain a per-run monotonically increasing sequence number. The server ignores a duplicate sequence number, allowing retry-safe ingestion.

# 8. Waterfall rendering and deterministic simulation

`WaterfallDisplay` owns two GPU RenderTextures. Each scan performs a shader blit that shifts the previous texture and injects the new scan line; the textures exchange roles after each update. BTH and every LOFAR display use this same component.

The reference signal/noise model includes:

- Gaussian-like bearing and frequency energy rather than hard rectangular bars.
- Narrowband harmonic tuples.
- Low-frequency wander plus short-period jitter.
- Deterministically keyed random background noise.
- Configurable background-noise level and jitter factor.
- Multiple palettes: Green, Grayscale and Cividis-style color-blind-safer rendering.

The random function is keyed by experiment seed, phase, signal ID, waterfall and scan index rather than Unity frame count. The scientific target is mathematically repeatable state, not byte-identical pixels across Metal/Direct3D/WebGL.

The traditional Green palette encodes signal strength primarily by luminance, not red/green hue discrimination. Feedback additionally uses symbols/text (`✓ CORRECT`, `△ PARTIAL`, `✕ INCORRECT`), so color is never the sole cue. For a study involving mixed color vision, prefer Grayscale or Cividis and treat palette as an experiment-controlled variable.

# 9. Pause and phase rules

Pause freezes the experiment clock, phase clock, BTH/LOFAR history and alert schedule, and pauses background/signal audio at its current playback position. Resume continues those states.

The first phase starts only when **Start** is pressed. At a phase boundary, the *incoming phase's* resolved `AutoStart` decides whether it begins immediately or the task enters `EXP_AutoPaused` and waits for Resume.

Background audio and selected-signal audio use independent audio channels and are mixed together. When signal audio is enabled and exactly one signal is selected, its clip plays together with the background audio. For selections containing multiple signals, `BTHOverlapAudio = Yes` plays all selected signal clips simultaneously; `No` suppresses signal audio for that overlapping selection. Clearing/changing the selection fades/stops only the selected-signal sources; it does not stop the background.

Classification controls remain disabled until a BTH aperture exists. After Confirm, classification/BTH input is locked until a confidence value is chosen. One `S` marker represents the response, regardless of how many true contacts were under the aperture.

# 10. Backups and research operations

Back up at minimum:

```text
Deploy/Experiments/
Deploy/Results/
Deploy/Config/
```

For a study, also version-control every released experiment package or archive the ZIP submitted to the server. The per-run experiment snapshot is a second line of defense against later definition changes.

Do not edit result CSV files in place during collection. Analyze copies or read-only mounts. Excel is intentionally not the primary storage format; if investigators want `.xlsx`, create it downstream from CSV/JSON rather than making Excel the authoritative raw record.

# 11. Example packages

`BasicContactClassification` exercises:

- Training vs test phases
- auto-pause between phases
- one LOFAR range
- training carets
- valid and invalid alerts
- feedback/tally visibility overrides
- moving contacts

`MultiBandOverlap` exercises:

- two stacked LOFAR ranges
- full 000–360 BTH wrapping
- multiple simultaneous contacts
- `BTHOverlapAudio = Yes` then `No`
- automatic phase transition
- Cividis-style palette
- alerts and overlapping bearing apertures

Upload-ready copies are under `ExamplePackages/*.zip`.

# 12. Development/testing notes

Recommended automated tests for a production study include:

1. Given definition + seed + simulation time, assert active signal IDs, bearings, phase and alerts.
2. Assert inherited signal durations and computed phase duration.
3. Assert 000/360 bearing selection intersections.
4. Assert YES/NO/PARTIAL classification scoring for single and overlapping contacts.
5. Assert pause leaves simulation clocks unchanged.
6. Assert incoming-phase AutoStart behavior.
7. Assert CSV quoting and event-column order.
8. Assert retrying the same Web event sequence number cannot duplicate a row.
9. Run long-duration browser soak tests under Chrome/Edge/Firefox/Safari.
10. Verify timing on the exact study hardware/browser configuration before collecting subjects.

The archive does not include Unity's generated `Library/`, `Temp/`, `Logs/`, IDE binaries or build outputs. Unity recreates those locally.


### End-of-experiment results

`ExperimentSettings.ShowResultsAtEnd` controls whether the mandatory completion popup also displays Correct, Partial, Incorrect percentages/counts, Responses, and Signals. `Responses` is the number of completed classification responses; `Signals` is the total number of actual signals presented in the BTH. The default is `No`.

## Editable Unity scene layouts

The SONAR scenes now contain editable static UI shells instead of relying on a completely code-generated Canvas at runtime. On project initialization, Unity creates/saves the following scene-resident hierarchy where applicable:

- `Canvas/SceneUIRoot`
- fixed top bars, panels, labels, buttons, input fields, and right-side task areas
- placeholder containers for dynamic content such as the experiment list, classification buttons, confidence controls, and instruction content
- the `SonarTask/Left` area remains a dynamic placeholder because the number/range of LOFAR views comes from the Experiment Definition

Runtime scripts bind to these named scene objects and populate only the dynamic content. You can therefore adjust anchors, panel sizes, spacing, fonts, and fixed-control placement directly in the Unity Scene view.

Run **SONAR > Initialize Project** after importing this update to add editable UI shells to older empty scenes. Existing `SceneUIRoot` hierarchies are preserved so later initialization does not overwrite your GUI edits.

Use **SONAR > Rebuild Editable Scene Layouts** only when you intentionally want to restore all generated scene layouts to their defaults; this command replaces the generated `SceneUIRoot` objects and therefore removes edits made inside them.

## fixed13 UI layout update

If upgrading an existing project whose editable scenes were already generated before fixed13, run **SONAR > Rebuild Editable Scene Layouts** once after backing up any scene GUI changes you want to keep. fixed13 changes the Startup hierarchy to add a centered Continue row and a platform-specific footer containing Web Settings or Desktop Exit Program. A fresh project generates this hierarchy automatically.

### Web audio unlock

The WebGL build installs capture-phase browser interaction handlers that resume Unity's Web Audio context on the first pointer, touch, or keyboard gesture. Browsers may still log a single autoplay-policy warning before the first user interaction; repeated warnings after interaction should not occur.

Feedback status icons, dropdown arrows, confidence radio controls, and LOFAR training carets are rendered as UI geometry/sprites rather than Unicode glyphs so they remain visible across WebGL, Windows, and macOS builds.

### Waterfall timing and historical classification

`WaterfallScanRateHz` controls temporal sampling resolution only. Each BTH/LOFAR texture now uses approximately `visible history seconds × WaterfallScanRateHz` rows, so changing scan rate does not change the configured time span. A maximum of 4096 history rows per waterfall is enforced by validation.

BTH selection/classification uses all signal tracks still visible in the configured BTH history window, including signals whose active `SignalDurationSec` has already ended. Classification `S` markers remain until the latest selected signal track has fully drained from the BTH. Each phase continues through its BTH drain interval before transitioning.

When multiple LOFAR views are configured, one shared `LOFAR` heading is shown above the full stack; individual views display only their own frequency/time axes.

### Fixed17 waterfall/history update

- BTH and LOFAR history duration is independent of `WaterfallScanRateHz`; scan rate changes temporal resolution only.
- Signals remain selectable/classifiable while their tracks are still visible in BTH history.
- Classification `S` markers remain until the selected signal track(s) drain from the BTH.
- Multi-view LOFAR layouts use one shared `LOFAR` heading above the stack.

## Responsive Web browser sizing

The Web build uses the project template at:

```text
Assets/WebGLTemplates/SONARResponsive/
```

The generated Web page fills the complete browser viewport. The Unity canvas uses CSS `width: 100%` and `height: 100%`, and Unity keeps its Web render target synchronized with the changing canvas size as the browser window is resized.

The Web template also sets `devicePixelRatio: 1`. This intentionally avoids rendering the complete SONAR interface at 2x/3x Retina/high-DPI resolution, which reduces WebGL GPU and memory load while preserving the same logical UI layout.

Unity UI canvases use:

```text
UI Scale Mode:          Scale With Screen Size
Reference Resolution:  1920 x 1080
Screen Match Mode:     Match Width Or Height
Match:                 0.5
```

Static scene layouts continue to use anchors/layout groups, so BTH, LOFAR, and right-side panels resize with the available browser viewport. The Web build is intended for desktop browsers rather than mobile/tablet layouts.

## Web fullscreen and experiment uploads

The responsive Web template includes a **Fullscreen** button in the lower-right corner. Entering fullscreen keeps the same responsive canvas behavior; pressing Escape returns to the normal browser-window-sized application.

Experiment ZIP uploads in **Settings** now display upload progress, validation/install status, and a final success/failure message. The experiment list refreshes automatically after a successful upload. The Settings page also tests whether the server-side Experiments directory is writable. If it reports a permissions problem, check the host bind mount:

```bash
cd Server
ls -ld Deploy/Experiments
docker compose exec api sh -c 'id; ls -ld /data/experiments; touch /data/experiments/.write-test && rm /data/experiments/.write-test'
```

For a normal local Linux filesystem, a suitable host-side repair is commonly:

```bash
sudo mkdir -p Deploy/Experiments
sudo chmod 755 Deploy/Experiments
```

If the directory is on NFS or another filesystem using root-squash/ACLs, grant write permission to the identity used by the API container rather than relying on container root.

### Experiment ZIP upload limit

The Web Settings uploader accepts experiment ZIP packages up to **64 MiB**. The Docker stack is configured with slightly larger Nginx/Kestrel multipart limits so the API can return a readable error instead of rejecting the request before the upload endpoint runs. After changing server code or Nginx configuration, rebuild both containers with `docker compose up -d --build`.

If an upload returns HTTP 413, verify the deployed server is from fixed21 or later.


## Branding

The project includes the supplied SONAR branding assets in `Assets/Branding/`:

- `SonarIcon.png` — Windows/macOS application icon and Web favicon.
- `SonarSplash.png` — Unity splash background and Web loading background.

`SONAR > Initialize Project` and all `SONAR > Build` commands apply the branding automatically. The Unity logo is disabled and the supplied splash artwork is used as the splash background. The custom Web template uses the title **SONAR Simulator Task**, the SONAR icon as its favicon, and the supplied splash image while the WebGL player loads.

If branding settings ever need to be reapplied manually, choose **SONAR > Apply Branding**.

### Docker bind-mount upload staging

Experiment ZIP installation validates/extracts the archive in the container temporary directory, then copies the validated package into a staging directory under `/data/experiments/.staging/`. The final package replacement is performed with directory renames entirely inside the `/data/experiments` filesystem. This avoids Linux `EXDEV` / `Invalid cross-device link` failures when `/tmp` and the bind-mounted Experiments directory are different filesystems. Existing packages are temporarily backed up during replacement and restored if installation fails before the new package is committed.

## WebGL waterfall stability

The waterfall renderer uses point-sampled internal ping-pong RenderTextures with exact texel-row addressing. A separate bilinear presentation RenderTexture remains attached to the UI, preventing browser/GPU differences from becoming visible when the internal ping-pong buffers swap. This is especially important for deterministic background noise in WebGL.


## Local WebGL Build-and-Run development mode

Unity **Build and Run** can test the browser/WebGL renderer without deploying Docker. When the WebGL player is served from `localhost`, `127.0.0.1`, or `::1`, SONAR automatically enters a local development mode.

- Test password: `SonarDev123!`
- The password field is pre-filled on the Login screen.
- The post-build step copies the project's `Experiments/` packages into the temporary Web build and generates a local manifest.
- Experiment audio, instructions, BTH, LOFAR, training, and classifications run from those bundled local assets.
- The Settings page is available in this mode after re-authenticating with the local test password.
- Settings > Experiments shows bundled experiments read-only; upload/delete remain server-only.
- Settings > Results is visible for UI testing; production result browsing/download remains server-only.
- Settings > External Studies is fully editable in local browser storage for provider-launch testing.
- Settings > Account is visible, but password changes remain server-only.
- Result events are kept only in browser memory and are **not** sent to the production API.
- The mode is disabled automatically when the build is opened through any non-loopback hostname or IP address. Production deployments therefore continue to require server authentication.

Use **SONAR > Build > Web Local Test (Build and Run)** for the simplest path. Unity's normal **File > Build Profiles > Web > Build And Run** also works. This mode is intended for renderer/UI troubleshooting, including WebGL waterfall behavior.

Local WebGL builds also recognize external-study URL parameters on loopback hosts. Local mode permits a development-only `experiment=PACKAGE_NAME` override plus `autostart=0|1` and `instructions=0|1` so launch behavior can be tested without a production API server. Production deployments continue to require server-side study mappings and do not trust the `experiment=` parameter.

### Markdown instruction layout

The built-in instruction renderer supports formatted text, headings, lists, local images, rules, and block quotes. Images are sized to the available instruction width while preserving aspect ratio, participate in ScrollRect layout, and may be fully scrolled even when they are the final item. Headings include top spacing similar to conventional Markdown previews. Consecutive numbered and bulleted list items are grouped with compact spacing instead of using normal paragraph spacing between every item.

## fixed28 interaction/settings update

`HideLofarSignalsIfNoSelectedBTH` is available in experiment defaults and as a phase override. It defaults to `No`. When set to `Yes`, an unselected BTH leaves LOFAR signal energy hidden while deterministic background noise continues to fill the waterfalls; selecting a BTH region reveals the matching historical/current frequency tracks.

The Startup screen now uses labeled `Location`, `Experimenter ID`, and `Subject ID` rows. Tab and Shift+Tab move between those three text fields. The Web login and Web Settings re-authentication password fields submit when Enter is pressed.

The Feedback tally shows only Correct, Partial, and Incorrect counts; the end-of-experiment results popup still reports both Responses and Signals when enabled.

Existing projects whose editable Startup scene has already been generated should run **SONAR > Rebuild Editable Scene Layouts** once to create the new labeled field rows. This overwrites edits inside `SceneUIRoot`, so preserve any manual scene changes first.


## External participant-study integration (fixed31)

The Web build can now be launched directly from Prolific, CloudResearch Connect, SONA, or a generic URL-based participant platform. Configure mappings under **Settings > External Studies**. A validated external launch sets the SONAR Subject ID from the provider participant ID, restricts the browser session to the assigned experiment, optionally skips directly to instructions/task start, stores provider identifiers in run metadata, and returns the participant to a server-configured completion URL only after the completed run has been finalized. See `Documentation/WEB_EXTERNAL_STUDIES.md`.

Web Settings is now organized into separate **Experiments**, **Results**, **External Studies**, and **Account** views.
