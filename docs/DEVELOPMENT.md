# Local Development

This guide covers installation, local development, and operational checks for
the completed account, administration, and durable Email workflows. Contracts
and the owner acceptance record are in [STAGE_1.md](stages/STAGE_1.md).

## First-Run Setup

After explicit migrations, a new empty installation redirects browser visitors
to `/setup`. Use the base Compose configuration, which publishes all ports only
on `127.0.0.1` and enables local setup. No installation key is required. CSRF
protection, rate limits and one-time completion remain enforced. Until setup completes, all other application pages redirect to the wizard and the normal header navigation/logo link and footer are hidden. Normal page access and navigation return automatically on completion. If installation state cannot be verified, other pages stay unavailable.

For a remote server, keep its ports bound to loopback and use an SSH tunnel:

```powershell
ssh -L 8443:127.0.0.1:8443 user@server
```

Open `https://localhost:8443/setup` through the tunnel. Set `PUBLIC_ORIGIN` to the
final public HTTPS origin before completing setup; persisted mail settings use
it for confirmation and recovery links. For a purely local development site,
the default is `https://localhost:8443`.

The wizard asks for the site name, city, optional region, time zone, administrator
Email/display name/password, and SMTP host/port/sender/optional credentials.
Select STARTTLS (usually 587), TLS on connection (usually 465), or no TLS for the
isolated local receiver. For Compose Mailpit use `mailpit`, port `1025`, no TLS,
and a fictional sender such as `noreply@cityportal.test`.

Completion sends a test message to the proposed administrator. SMTP or database
failure leaves installation retryable. On success, the administrator is
confirmed and can log in immediately through the normal login page. The site
defaults, administrator, bootstrap audit and encrypted SMTP configuration commit
together. The API and Worker share the persistent protection-key volume; losing
those keys also makes stored SMTP credentials unreadable.

The wizard closes permanently after completion. The Worker reads the persisted
mail settings for each delivery, without a restart. Migrations mark databases
already containing accounts as installed and preserve their data and fallback
deployment settings. Their existing administrator/bootstrap procedure below
continues to work; the wizard never resets an existing account database.

## Opening Network Access After Setup

The base configuration stays local. After completing setup and checking the
site, configure a real DNS hostname in `SITE_DOMAIN` and ensure `PUBLIC_ORIGIN`
was set to its final `https://` origin before installation. Prepare DNS and the
server's firewall for HTTPS. Then deliberately apply the public override:

```powershell
docker compose -f compose.yaml -f compose.public.yaml config --quiet
docker compose -f compose.yaml -f compose.public.yaml up -d --wait
```

The override disables installation, switches API/Worker to Production, replaces
the proxy's loopback development port with network ports 80/443 and uses Caddy's
public HTTPS configuration. API, Worker, database, broker and mail inspection
ports remain loopback-only. The proxy depends on healthy API readiness; an
uninstalled site with setup disabled is unready, so the public proxy cannot
start through this procedure. Public `/setup` is unavailable, and direct setup
completion also returns 403. Completed installations stay closed in local mode.

For standalone API hosting, setup is disabled by default. Enable
`Installation__Enabled=true` only while access is restricted to the server,
then disable it before exposing the host or adding a public reverse proxy.
The forwarded client-IP header is not treated as proof of local installer access.
Host-side Angular SSR binds to loopback unless `HOST` is explicitly set;
Compose sets `HOST=0.0.0.0` only inside the unpublished frontend container.

Compose 2.24.4+ is required for port replacement with `!override`;
see [Docker merge rules](https://docs.docker.com/reference/compose-file/merge/)
and [healthy dependency ordering](https://docs.docker.com/compose/how-tos/startup-order).
This override prepares network access; the development Mailpit configuration is
still a local receiver, not an external mail provider.

## Legacy Administrator Bootstrap and Staff Screens

Fresh installations create their administrator in the wizard. For an existing
installation without an administrator, register an ordinary account and confirm
its Email. After explicit migration, provision it with the existing account Email:

```powershell
docker compose run --rm --no-deps api --bootstrap-admin resident@example.test
```

Use the Email of the intended administrator in place of the sample. The command
accepts no password, refuses missing/unconfirmed accounts, and exits with code 1
if any administrator already exists. It does not start an HTTP server or require
a working Email provider. Concurrent attempts can provision only one account.
The system records the bootstrap actor in retained role history.

After login, open `/staff/accounts` for search, confirmation/role filters,
paginated accounts, role changes and history. Administrative roles require
confirmed Email. RegularUser is always retained, and the last administrator
cannot lose that role. Self-removal keeps personal access and immediately ends
administrative access for subsequent requests using existing cookies.

Open `/staff/email-deliveries` to inspect metadata and sanitized failure causes.
Replay requires a current version, Failed state, and a still-active link. It
reuses that link, records the actor atomically with the queued Outbox message,
and starts a fresh bounded retry budget. Lifetime attempt numbers keep increasing
so old envelopes and attempt records cannot become current again. Replay audit
records survive the 30-day cleanup of resolved delivery records.

Set `CITY_TIME_ZONE` in `.env` for Compose, or `City__TimeZone` for the API directly.
UTC is the neutral development default. IANA identifiers such as `Europe/Moscow`
are suitable; supported Windows identifiers are normalized to IANA. Invalid
configuration fails startup. Public `/api/system/info` includes `timeZone`,
which the administrative screens use to display UTC timestamps. Fresh
installations persist the wizard time zone; deployment configuration is the
fallback for existing databases. There is no general city-settings editing
endpoint or screen.

If using the ignored project-local SDK, enable it only in the current shell:

```powershell
$env:DOTNET_ROOT = Join-Path $PWD '.local/dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
```

The current workflow was verified with the SDK pinned by `global.json`. Local
SDK binaries, credentials, test reports and logs must remain outside Git.

## Recovery and Profile Editing

Use `/account/forgot-password` without login to request recovery. Its generic
response never reveals whether an account exists or an account mail quota was
exhausted. Mailpit captures Russian HTML/plain-text mail linking to
`/account/reset-password`; the link expires after one hour. The page removes
the secret fragment from history and requires an explicit password submission.
Opening the page alone does not change credentials.

Confirmed accounts can edit their display name or password in `/account`.
Both writes require the displayed account version; a conflict reloads current
profile data. Password changes require the current password. Change/reset
revokes all account sessions, invalidates outstanding recovery links, and
requires separate login. Confirmation links and quotas remain independent.

The HTTPS smoke helper also exercises recovery and profile/password changes.
Apply all migrations through the common procedure below before starting updated
API and Worker processes.

## Prerequisites

- Docker Desktop with Linux containers and Docker Compose 2.24.4 or later.
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
not enable that trust. The SSR host allowlist includes local hostnames and, in public mode, only the explicit SITE_DOMAIN hostname; wildcards are rejected.

The API trusts forwarded client IP and scheme headers only from the configured
Caddy address. Compose uses the local subnet `172.30.88.0/24` and reserves
`172.30.88.10` for Caddy. If this conflicts with an existing Docker/VPN network,
set both `COMPOSE_SUBNET` and `PROXY_ADDRESS` in `.env` to a matching private subnet
and address. Never configure arbitrary Internet sources as trusted proxies.

API and Worker share the `protection-keys` volume at `/var/cityportal/keys` with
the application name `CityPortal`. It protects persisted mail payloads and API
cookies across restarts. The runtime creates this directory for the non-root
application user before volume initialization. Keep the volume private and back
it up with PostgreSQL; losing keys makes queued protected payloads and existing
cookies unreadable. Local keys are filesystem-protected; a deployment must also
configure encryption at rest and controlled backup access. Private key material
must never be committed or copied into image build contexts.

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
| `localhost:1025`                         | Mailpit SMTP listener for local confirmation Email.   |
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
For host API/Worker development, set `Security__DataProtectionKeysPath` to the same
ignored local directory in both processes, for example `.local/protection-keys`
resolved to an absolute path. Also set `Mail__SmtpHost=localhost`,
`Mail__SmtpPort=1025`, and `Mail__UseTls=false` for host Worker access to Mailpit.
Keep `Mail__PublicOrigin=https://localhost:8443` when testing links through Compose.

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
Use the Compose HTTPS origin for cookie-authentication acceptance checks.
The generated client workflow and frontend test commands live in the frontend
package scripts. CI runs backend format/build/tests, frontend build/tests, and
the Compose image/migration/HTTPS smoke path on Linux.

## Registration and Confirmation Configuration

Compose configures the local mail sender through the following settings, shared
by API and Worker:

| Configuration | Local value or purpose |
| --- | --- |
| `Mail:PublicOrigin` | `https://localhost:8443`, the origin used in confirmation links. |
| `Mail:PortalName` | `CityPortal`, displayed in Russian HTML and plain-text templates. |
| `Mail:SenderAddress` | `noreply@cityportal.test`, a fictional local sender. |
| `Mail:SmtpHost`, `Mail:SmtpPort` | `mailpit`, `1025`. |
| `Mail:UseTls` | `false` only for the isolated local SMTP receiver; use TLS for deployment. |
| `Mail:Username`, `Mail:Password` | Optional SMTP credentials, supplied outside source control. |
| `Security:DataProtectionKeysPath` | Shared persistent key directory. |
| `Security:TrustedProxies` | Exact trusted proxy IP addresses; untrusted forwarded headers are ignored. |

Open `/account/register`, then sign in separately at `/account/login`. The profile
at `/account` shows the latest confirmation delivery. Inspect captured mail at
Mailpit and open its confirmation link. Opening the page does not confirm the
account; use its confirmation button. The link secret is in the URL fragment so
it is not sent to the reverse proxy, and the page clears it from browser history.
Confirmation resend is available only inside the authenticated profile.

The following HTTPS smoke check creates one disposable account and retains its
mail for inspection. It verifies CSRF rejection, registration, login, SMTP
templates, confirmation, repeated confirmation, profile status, and logout. It
uses the actual registration limit, so repeated runs can return HTTP 429.

```powershell
python scripts/check-registration.py --ca .local/cityportal-root.crt
```

To check public SSR and private staff browser shells without creating another
account or consuming registration quota:

```powershell
python scripts/check-registration.py --ca .local/cityportal-root.crt --shell-only
```

CI runs `check-registration.py --install` against a fresh disposable database in
local setup mode before checking normal account workflows. No key environment
variable is needed. This test mode creates an administrator with random
credentials; use the browser wizard for an installation you intend to keep.
`--smtp-host` is available for this test initialization path; its Compose default
is `mailpit`.

The final browser acceptance used an isolated headless Edge context with a
context-scoped development-certificate exception. Separate Python HTTPS checks
verified the exported CA and hostname. This did not install Windows trust or
change normal browser certificate behavior. See the stage specification for
the acceptance evidence and remaining deployment checks.

## Operational Boundaries

- Health probes verify process/dependency availability. They do not prove Email
  delivery or application business correctness.
- Configure trusted proxy addresses, protected persistent keys, SMTP credentials,
  and production HTTPS for the deployment environment.
- Redis and S3-compatible storage remain selected infrastructure; this foundation
  does not start them before a concrete workflow uses them.

## References

- [Caddy local TLS](https://caddyserver.com/docs/caddyfile/directives/tls).
- [PostgreSQL official image and volume layout](https://hub.docker.com/_/postgres).
- [RabbitMQ official image](https://hub.docker.com/_/rabbitmq).
- [Mailpit configuration](https://mailpit.axllent.org/docs/configuration/).
- [Npgsql GSS encryption configuration](https://www.npgsql.org/doc/security.html).
- [curl certificate and revocation options](https://curl.se/docs/manpage.html).
