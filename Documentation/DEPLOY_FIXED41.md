# Deploy FIXED41

1. Rebuild the Web player (**SONAR → Build → Web**). The WebGL plugin (`SonarBrowser.jslib`) has new download and audio-preview functions, so an old player cannot use the new buttons.
2. Rebuild and restart the API from `Server/`: `docker compose up -d --build`. Verify `curl -k https://localhost/api/health`.
3. Unlock Settings and test each area:
   - **Experiments:** Download a package and confirm the ZIP holds one `<Package>/` folder.
   - **Common Audio Files:** Play/Pause a sound, then Download it.
   - **Results:** Download a run and confirm the ZIP holds its `.csv`, `.meta.json` and `.experiment.json`.
4. No changes to bind mounts, SQLite, the CSV header or experiment packages. Do not rebuild the editable scene layouts; the new controls are created at runtime.
