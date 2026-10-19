# Deploying fixed34 to an existing Web server

1. Back up `/data/config/sonar.db` and your Results directory before deployment.
2. Replace/rebuild the Unity WebGL files from fixed34.
3. Replace `Server/Sonar.Server` with the fixed34 server source.
4. Rebuild and recreate the API container. Keep the existing `/data/config`, `/data/experiments`, and `/data/results` bind mounts/volumes.
5. No manual SQLite migration is required. On startup fixed34 creates the new test-assignment, test-token, and test-run-context tables alongside the existing production tables.
6. Sign into Settings > External Studies, generate a Secure Test URL, and verify the study in a private/incognito window before opening recruitment.

Example container rebuild:

```bash
cd /opt/sonar
sudo docker compose build --no-cache api
sudo docker compose up -d --force-recreate api web
```

After deployment, hard-refresh the browser so it loads the new WebGL client.
