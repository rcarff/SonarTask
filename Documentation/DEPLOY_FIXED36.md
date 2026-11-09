# Deploy FIXED36

1. Create `Server/Deploy/Sounds` and grant the API container read/write access. Compose now mounts `./Deploy/Sounds:/data/sounds`. Keep this directory with the other backed-up research configuration/assets.
2. Rebuild the Web player and API, then run `docker compose up -d --build` from Server. Existing clients also receive common-audio fallback from the package asset endpoint. Verify `/api/health`.
3. Unlock Settings > Common Audio Files and verify upload/list/delete. WAV and MP3 files are limited to 64 MiB, existing names cannot be replaced, and referenced sounds cannot be deleted. Existing packages require no edits.
4. Place desktop/local-development common audio in top-level `Sounds/`. Sync Desktop Seed Experiments before desktop builds; initialized installs receive missing bundled sounds on next launch. Local Web Build and Run generates Sounds/index.json and provides read-only listing.
5. Do not rebuild editable scene layouts to upgrade existing scenes. New UI is created at runtime; the layout builder supports fresh scenes. No SQLite migration or CSV header change is needed.
