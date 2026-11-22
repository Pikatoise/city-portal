# Opening Network Access After Setup

Follow [first-run setup](first_run_setup.md) before applying the public override.

Complete setup, check the site and configure DNS and firewall access for
HTTPS. Set `SITE_DOMAIN` to the public hostname. Use the final HTTPS URL in
`PUBLIC_ORIGIN` when installing. Then apply the public override:

```powershell
docker compose -f compose.yaml -f compose.public.yaml config --quiet
docker compose -f compose.yaml -f compose.public.yaml up -d --wait
```

The override disables setup, runs API/Worker in Production and exposes Caddy
on ports 80/443. Other published ports stay on loopback. The proxy waits for
installed, healthy API readiness. Mailpit stays a local test receiver. Port
replacement uses `!override`, which requires Compose 2.24.4+.

For standalone API hosting, enable `Installation__Enabled=true` only with
server-local access and disable it before exposing the host. Host Angular
SSR binds to loopback unless `HOST` is set. Compose sets `HOST=0.0.0.0` inside
the unpublished frontend container.
