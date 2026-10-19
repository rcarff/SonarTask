# Deploying fixed35 to an existing Web server

1. Back up `/data/config/sonar.db` and your Results directory before deployment.
2. Replace/rebuild the Unity WebGL files from fixed35.
3. Replace `Server/Sonar.Server` with the fixed35 server source.
4. Rebuild and recreate the API container while preserving the existing `/data/config`, `/data/experiments`, and `/data/results` bind mounts/volumes.
5. No SQLite migration is required for fixed35.
6. In **Settings > External Studies**, edit a study, confirm **Enabled** is checked, then click **Save + Generate Secure Test URL**. The save completes before the token is generated.
7. Open the copied test URL in a private/incognito window and verify the assignment starts.

Example container rebuild:

```bash
cd /opt/sonar
sudo docker compose build --no-cache api
sudo docker compose up -d --force-recreate api web
```

After deployment, hard-refresh the browser so it loads the new WebGL client.
