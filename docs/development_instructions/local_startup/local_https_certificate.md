# Trust the Local HTTPS Certificate

Start the proxy using the [Compose startup instructions](compose_startup.md) before exporting the certificate.

Caddy issues a certificate through its local certificate authority. Its private
key remains in the Docker volume. Export only the public root certificate:

```powershell
New-Item -ItemType Directory -Force .local
docker compose cp proxy:/data/caddy/pki/authorities/local/root.crt .local/cityportal-root.crt
curl.exe --ssl-revoke-best-effort --cacert .local/cityportal-root.crt https://localhost:8443/api/system/info
```

For browser access without a certificate warning, import this public certificate
into the current user's trusted root store through the operating system's
certificate UI. Trust installation is a manual local machine choice. Remove that
trust when the local environment is no longer needed. Do not copy or commit the
Caddy private key. Deleting the Caddy data volume creates a new authority and
requires exporting and trusting its new public root certificate.
The Windows curl command uses best-effort revocation checks because the local
authority has no public revocation-list endpoint. Certificate-chain and hostname
validation still use the exported root. Linux curl needs only `--cacert`.

## References

- [Caddy local TLS](https://caddyserver.com/docs/caddyfile/directives/tls).
- [curl certificate and revocation options](https://curl.se/docs/manpage.html).
