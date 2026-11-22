# Build, Migrate, and Start

Check the [prerequisites](prerequisites.md) and [configure local credentials](local_credentials.md) before starting. Run the commands from the repository root.

```powershell
docker compose config --quiet
docker compose build
docker compose up -d --wait postgres rabbitmq mailpit media-store
docker compose run --rm --no-deps api --migrate
docker compose up -d api worker frontend proxy
docker compose ps
```

Run the migration command before first startup and after changes that add
migrations. It applies module migrations and initializes configured Media
storage. API/Worker startup leaves migrations to this command. Readiness
checks schemas and configured bucket access.

Browse to `https://localhost:8443`. Local API Development mode exposes
OpenAPI and Scalar. Only Compose enables frontend trust of Caddy's forwarded
headers. The SSR allowlist permits local hostnames and the explicit
`SITE_DOMAIN` in public mode.

The API trusts forwarded client IP and scheme headers only from the configured
Caddy address. Compose uses the local subnet `172.30.88.0/24` and reserves
`172.30.88.10` for Caddy. If this conflicts with an existing Docker/VPN network,
set both `COMPOSE_SUBNET` and `PROXY_ADDRESS` in `.env` to a matching private subnet
and address. Never configure arbitrary Internet sources as trusted proxies.

API and Worker share the `protection-keys` volume at `/var/cityportal/keys` with
the application name `CityPortal`. It protects persisted mail payloads and API
cookies across restarts. The runtime creates this directory for the non-root
application user before volume initialization. Keep the volume private and back
it up with PostgreSQL. Losing the keys makes queued protected payloads,
persisted SMTP credentials and existing cookies unreadable. Local keys are filesystem-protected; a deployment must also
configure encryption at rest and controlled backup access. Private key material
must never be committed or copied into image build contexts.
