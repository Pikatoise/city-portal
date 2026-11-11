# Local Development

This guide covers the stage 1 application foundation. Registration, authentication,
Email delivery, administrative workflows, and their outage guarantees are not yet
implemented. The presence of PostgreSQL, RabbitMQ, and Mailpit does not imply those
business workflows are available.

## Prerequisites

- Docker Desktop with Linux containers and Docker Compose v2 or later.
- For host development, the .NET SDK pinned by `global.json` and Node.js 26.8.1.
- Git. No cloud Email provider or public domain is required for local development.

Run the commands below from the repository root. In Windows PowerShell, use
`npm.cmd` instead of `npm` when the execution policy blocks `npm.ps1`.

## Configure Local Credentials

Copy `.env.example` to `.env`. Set `POSTGRES_PASSWORD` and `RABBITMQ_PASSWORD`
to different random values containing only letters, digits, hyphens, or underscores.
Random hexadecimal values are suitable for both passwords. PostgreSQL credentials
form part of an Npgsql connection string, and RabbitMQ credentials form part of an
AMQP URI; the restricted local alphabet avoids quoting and URI-escaping ambiguity.
`.env` is ignored by Git. Do not reuse these development credentials for a deployment.

```powershell
Copy-Item .env.example .env
```

The Compose stack deliberately requires nonempty passwords. PostgreSQL and
RabbitMQ initialize credentials only on the first creation of their data volumes.
Changing `.env` later does not change an existing database user's password.
The local PostgreSQL connection disables GSS encryption because this development
stack uses password authentication without Kerberos.

## Build, Migrate, and Start

```powershell
docker compose config --quiet
docker compose build
docker compose up -d --wait postgres rabbitmq mailpit
docker compose run --rm --no-deps api --migrate
docker compose up -d api worker frontend proxy
docker compose ps
```

The migration command applies module-owned migrations explicitly. Normal API and
Worker startup never applies migrations. Run the migration command once before
starting the application and again when a change introduces migrations.

The site is at `https://localhost:8443`. Caddy routes `/api/` to the API and `/`
to the Angular Node server, keeping browser requests on one HTTPS origin. Local
development runs the API in `Development` to expose OpenAPI and Scalar. This
Compose configuration is a development environment, not a public deployment.
The frontend trusts Caddy's forwarded headers only in Compose. Its port is not
published to the host; requests enter through Caddy. Direct host development does
not enable that trust. The SSR host allowlist remains limited to local hostnames.

## Trust the Local HTTPS Certificate

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

## Services and Diagnostics

| Address                                  | Purpose                                               |
| ---------------------------------------- | ----------------------------------------------------- |
| `https://localhost:8443/`                | Angular application through the HTTPS proxy.          |
| `https://localhost:8443/api/system/info` | Public foundation metadata.                           |
| `https://localhost:8443/health/live`     | API process liveness.                                 |
| `https://localhost:8443/health/ready`    | API dependency readiness.                             |
| `https://localhost:8443/openapi/v1.json` | Development OpenAPI contract.                         |
| `https://localhost:8443/scalar/v1`       | Development API explorer.                             |
| `http://localhost:5080/`                 | Direct loopback API for host tooling.                 |
| `http://localhost:5081/health/ready`     | Worker dependency readiness.                          |
| `http://localhost:8025/`                 | Mailpit captured Email interface.                     |
| `localhost:1025`                         | Mailpit SMTP listener for future Email workflow work. |
| `http://localhost:15672/`                | RabbitMQ management UI with `.env` credentials.       |
| `localhost:5432`                         | PostgreSQL database `cityportal`, user `cityportal`.  |
| `localhost:5672`                         | RabbitMQ AMQP.                                        |

All published ports bind to loopback. PostgreSQL, RabbitMQ, Mailpit, and Caddy
retain their state in named volumes across `docker compose down` and restart.
Never remove volumes as a routine recovery step.

```powershell
docker compose logs --tail 100 api worker
docker compose stop
docker compose start
docker compose down
```

## Host Development and Checks

Keep PostgreSQL, RabbitMQ, and Mailpit running in Compose. Set
`ConnectionStrings__Database` and `ConnectionStrings__Messaging` in the current
process or .NET user secrets, using `localhost` and the `.env` credentials.
The API uses port 5080 for local development. Never commit real connection strings.

```powershell
dotnet restore CityPortal.slnx
dotnet build CityPortal.slnx --configuration Release --no-restore
dotnet format CityPortal.slnx --verify-no-changes --no-restore
dotnet test CityPortal.slnx --configuration Release --no-build
dotnet run --project src/CityPortal.Api
```

In a separate terminal:

```powershell
cd frontend
npm.cmd ci
npm.cmd start
```

The frontend development server proxies `/api/` to `http://localhost:5080`.
Use the Compose HTTPS origin for later cookie-authentication acceptance checks.
The generated client workflow and frontend test commands live in the frontend
package scripts. CI runs backend format/build/tests, frontend build/tests, and
the Compose image/migration/HTTPS smoke path on Linux.

## Operational Boundaries

- The Worker currently exposes health endpoints; durable Email handlers,
  Wolverine Outbox/Inbox integration, SMTP sending, and Quartz retention will
  arrive with the account workflows. No placeholder job claims to send Email.
- Health probes verify process/dependency availability. They do not prove Email
  delivery or application business correctness.
- Configure API trusted proxy addresses, persistent data-protection keys, SMTP
  credentials, and production HTTPS before deploying authenticated workflows.
- Redis and S3-compatible storage remain selected infrastructure; this foundation
  does not start them before a concrete workflow uses them.

## References

- [Caddy local TLS](https://caddyserver.com/docs/caddyfile/directives/tls).
- [PostgreSQL official image and volume layout](https://hub.docker.com/_/postgres).
- [RabbitMQ official image](https://hub.docker.com/_/rabbitmq).
- [Mailpit configuration](https://mailpit.axllent.org/docs/configuration/).
- [Npgsql GSS encryption configuration](https://www.npgsql.org/doc/security.html).
- [curl certificate and revocation options](https://curl.se/docs/manpage.html).
