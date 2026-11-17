# Deploy FIXED38

1. Open the project in Unity once so that the newly enabled Video module (`com.unity.modules.video`) is resolved. Then rebuild the Web player with SONAR → Build → Web.
2. From `Server/`, run `docker compose up -d --build` so that the API serves package assets with HTTP Range support and video content types. Verify `/api/health`.
3. To check this, upload a package with a short H.264 `.mp4` in its instructions. Then confirm that it plays and seeks in the browser, and that `curl -k -I -H "Range: bytes=0-99"` (with a session cookie) against the asset returns `206` with `Content-Type: video/mp4`.
4. The package ZIP upload limit stays at 64 MiB (nginx `client_max_body_size 70m`), so keep instruction videos short and compressed.
5. No SQLite migration, scene rebuild or CSV header change is needed. Existing packages and instructions render unchanged.
