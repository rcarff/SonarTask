# Deploying fixed33 to an existing Web server

Back up the persistent host data first, especially `Deploy/Config` and `Deploy/Results`.

## Required updates

1. Open fixed33 in Unity and create a new production WebGL build.
2. Replace the contents of the deployed `Deploy/WebBuild` directory with that new build.
3. Replace the deployed `Server/Sonar.Server` source with the fixed33 server source.
4. Rebuild/recreate the API container, for example from the deployment root:

```bash
sudo docker compose build --no-cache api
sudo docker compose up -d --force-recreate api
```

If your Compose configuration builds the Web/Nginx image rather than bind-mounting `WebBuild`, rebuild that service too.

Do not delete the persistent `Deploy/Config`, `Deploy/Experiments`, or `Deploy/Results` directories. On startup fixed33 creates the new assignment tables inside the existing `Deploy/Config/sonar.db`.

If a fixed31/fixed32 single-experiment External Study mapping file exists under `Deploy/Config` as `external-studies.json`, `external_studies.json`, or `externalStudies.json`, and the new SQLite external-study table is empty, fixed33 attempts to import valid mappings as Fixed assignments. The old JSON file is not deleted.

## Verification

After deployment:

```bash
sudo docker compose ps
sudo docker compose logs --tail=100 api
```

Open the normal Web app and verify operator login and Settings. Under **Settings -> External Studies**, create or verify a mapping. Then test the provider launch URL with a synthetic participant ID before publishing the recruitment study.

For Prolific, the important test is equivalent to:

`https://YOUR-HOST/?provider=prolific&PROLIFIC_PID=TEST001&STUDY_ID=YOUR_REAL_STUDY_ID&SESSION_ID=TESTSESSION`

The configured SONAR **Provider Study / Project ID** must exactly match `STUDY_ID`.

Use a separate private/incognito browser window for the participant test if you want to keep the operator Settings session open, because the participant launch intentionally replaces the normal SONAR session cookie in that browser profile.
