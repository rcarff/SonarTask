Yes. For your setup, I’d keep Certbot **outside Docker on the Ubuntu host**, use nginx only to answer the ACME HTTP challenge, and have Certbot copy each successfully renewed certificate into:

```text
/opt/sonar/Deploy/Certs/sonar.crt
/opt/sonar/Deploy/Certs/sonar.key
```

Certbot officially recommends its snap installation, and its renewal mechanism supports a deploy hook that runs only after successful issuance/renewal. ([Certbot][1])

## 1. Install Certbot on the Ubuntu host

```bash
sudo apt update
sudo apt install -y snapd

sudo snap install --classic certbot
sudo ln -sf /snap/bin/certbot /usr/local/bin/certbot
```

Verify it:

```bash
certbot --version
```

Don't install the nginx Certbot plugin. Your nginx is inside Docker, so `certbot --nginx` would be trying to manage an nginx installation that doesn't exist on the host.

## 2. Create a webroot for Let's Encrypt challenges

On the host:

```bash
sudo mkdir -p /var/www/certbot/.well-known/acme-challenge
sudo chmod -R 755 /var/www/certbot
```

You need to make this directory visible inside the nginx container.

For example, in your `docker-compose.yml`:

```yaml
services:
  nginx:
    volumes:
      - /var/www/certbot:/var/www/certbot:ro
      - /opt/sonar/Deploy/Certs:/opt/sonar/Deploy/Certs:ro
```

The exact destination of the Certs volume can be different if your nginx config expects it elsewhere.

## 3. Configure nginx to serve the ACME challenge

Add something along these lines to the nginx server handling `sonartask.ihmc.org`:

```nginx
server {
    listen 80;
    server_name sonartask.ihmc.org;

    location ^~ /.well-known/acme-challenge/ {
        root /var/www/certbot;
        default_type "text/plain";
        try_files $uri =404;
    }

    location / {
        return 301 https://$host$request_uri;
    }
}
```

The important part is that this URL:

```text
http://sonartask.ihmc.org/.well-known/acme-challenge/...
```

is served directly from:

```text
/var/www/certbot/.well-known/acme-challenge/...
```

HTTP-01 validation requires inbound TCP port **80** to be publicly reachable. ([letsencrypt.org][2])

Restart/reload the nginx container after making that change.

You can test the path before involving Certbot:

```bash
echo "acme-test" | sudo tee /var/www/certbot/.well-known/acme-challenge/test
```

Then from another machine:

```bash
curl http://sonartask.ihmc.org/.well-known/acme-challenge/test
```

You should get:

```text
acme-test
```

Remove the test file afterward:

```bash
sudo rm /var/www/certbot/.well-known/acme-challenge/test
```

## 4. Create the certificate deployment script

Certbot normally keeps certificates under `/etc/letsencrypt/live/...`. Don't make nginx depend directly on those paths, especially across a Docker bind mount. Instead, copy them to your required filenames after successful renewal.

Create:

```bash
sudo nano /usr/local/sbin/deploy-sonar-cert
```

Put this in it:

```bash
#!/bin/sh
set -eu

DEST="/opt/sonar/Deploy/Certs"

mkdir -p "$DEST"

# nginx should normally use fullchain.pem, not cert.pem.
install -m 0644 "$RENEWED_LINEAGE/fullchain.pem" \
    "$DEST/.sonar.crt.new"

install -m 0600 "$RENEWED_LINEAGE/privkey.pem" \
    "$DEST/.sonar.key.new"

mv -f "$DEST/.sonar.crt.new" "$DEST/sonar.crt"
mv -f "$DEST/.sonar.key.new" "$DEST/sonar.key"

# Optional: gracefully reload nginx after certificate replacement.
# Replace YOUR_NGINX_CONTAINER with the actual Docker container name.
#
# /usr/bin/docker kill --signal=HUP YOUR_NGINX_CONTAINER
```

Make it executable:

```bash
sudo chmod 755 /usr/local/sbin/deploy-sonar-cert
```

The resulting files will be:

```text
/opt/sonar/Deploy/Certs/sonar.crt
/opt/sonar/Deploy/Certs/sonar.key
```

`sonar.crt` will contain the **full certificate chain**, which is normally what nginx should use for `ssl_certificate`.

The private key will be mode `0600`. If your nginx container runs completely as a non-root UID and needs direct read access to the private key, you'll need to adjust its group ownership/mode accordingly.

## 5. Request the initial certificate

Run:

```bash
sudo certbot certonly \
    --webroot \
    --webroot-path /var/www/certbot \
    --domain sonartask.ihmc.org \
    --cert-name sonartask.ihmc.org \
    --deploy-hook /usr/local/sbin/deploy-sonar-cert
```

Certbot will ask for your administrative email address and agreement to the Let's Encrypt terms.

On success, you should see:

```bash
sudo ls -l /opt/sonar/Deploy/Certs/sonar.*
```

Something resembling:

```text
-rw-r--r-- 1 root root ... sonar.crt
-rw------- 1 root root ... sonar.key
```

And you can inspect the certificate:

```bash
openssl x509 \
    -in /opt/sonar/Deploy/Certs/sonar.crt \
    -noout -subject -issuer -dates
```

## 6. Point nginx at those names

Your nginx configuration should contain something equivalent to:

```nginx
ssl_certificate     /opt/sonar/Deploy/Certs/sonar.crt;
ssl_certificate_key /opt/sonar/Deploy/Certs/sonar.key;
```

The path here is the path **as seen inside the container**. If you mount the host directory somewhere else, use that container-side path instead.

## 7. Gracefully reload nginx after renewals

This step is important. Replacing the certificate files doesn't necessarily cause an already-running nginx process to start using them.

Find your container name:

```bash
docker ps --format 'table {{.Names}}\t{{.Image}}'
```

Suppose it is called:

```text
sonar-nginx
```

Then change the end of `/usr/local/sbin/deploy-sonar-cert` to:

```bash
/usr/bin/docker kill --signal=HUP sonar-nginx
```

A HUP tells nginx to gracefully reload its configuration/certificates rather than abruptly terminating connections.

The complete ending would therefore be:

```bash
mv -f "$DEST/.sonar.crt.new" "$DEST/sonar.crt"
mv -f "$DEST/.sonar.key.new" "$DEST/sonar.key"

/usr/bin/docker kill --signal=HUP sonar-nginx
```

## 8. Test automatic renewal

Certbot's installation provides automatic renewal through a timer/cron mechanism, so you shouldn't need to create your own daily cron job. ([Certbot][1])

First test certificate renewal:

```bash
sudo certbot renew --dry-run
```

Then test the complete process, **including your deploy hook**:

```bash
sudo certbot renew --dry-run --run-deploy-hooks
```

`--run-deploy-hooks` is useful here because normal dry runs don't need to deploy the temporary test certificate; Certbot instead runs the deploy hook using the current active certificate after a successful simulated renewal. ([Certbot Documentation][3])

You can also check the Certbot timer:

```bash
systemctl list-timers | grep -i certbot
```

## The resulting flow

Your final arrangement will essentially be:

```text
                           Internet
                              |
                              | TCP 80
                              v
                    +-------------------+
                    | Docker nginx      |
                    |                   |
                    | /.well-known/...  |
                    +---------+---------+
                              |
                    bind-mounted directory
                              |
                              v
                      /var/www/certbot
                              ^
                              |
                         Certbot host
                              |
                       certificate issued
                              |
                              v
                    /etc/letsencrypt/live/
                    sonartask.ihmc.org/
                       |            |
                  fullchain.pem  privkey.pem
                       |            |
                       +-----+------+
                             |
                       deploy hook
                             |
                             v
               /opt/sonar/Deploy/Certs/
                    sonar.crt
                    sonar.key
                             |
                         HUP nginx
```

One thing worth checking before the first attempt is DNS:

```bash
dig +short A sonartask.ihmc.org
dig +short AAAA sonartask.ihmc.org
```

The `A` record needs to point to this server. If there's an `AAAA` record, make sure IPv6 actually reaches this same nginx instance; Let's Encrypt prefers IPv6 initially when both records exist, and a bad AAAA record is a common reason for otherwise mysterious validation failures. ([Let's Encrypt][4])

This approach gives you unattended renewal, preserves the exact `sonar.crt` / `sonar.key` names your deployment expects, and avoids putting Certbot itself into the nginx container.

[1]: https://certbot.eff.org/instructions?os=snap&ws=nginx&utm_source=chatgpt.com "Certbot Instructions | Certbot"
[2]: https://letsencrypt.org/docs/challenge-types/?utm_source=chatgpt.com "Challenge Types - Let's Encrypt"
[3]: https://eff-certbot.readthedocs.io/en/stable/man/certbot.html?utm_source=chatgpt.com "certbot — Certbot 5.8.0 documentation"
[4]: https://letsencrypt.org/ca/docs/ipv6-support/?utm_source=chatgpt.com "- Let's Encrypt"
