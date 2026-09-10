#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/Deploy/Certs"
HOST="${1:-localhost}"
openssl req -x509 -newkey rsa:4096 -sha256 -days 825 -nodes \
  -keyout "$ROOT/Deploy/Certs/sonar.key" -out "$ROOT/Deploy/Certs/sonar.crt" \
  -subj "/CN=$HOST" -addext "subjectAltName=DNS:$HOST"
chmod 600 "$ROOT/Deploy/Certs/sonar.key"
echo "Created a development certificate for $HOST. For real deployments use a trusted certificate."
