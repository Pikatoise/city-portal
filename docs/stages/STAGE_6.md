# Stage 6: Integrations

## Goal and status

All five opt-in integrations are implemented. The owner closed the stage on
2026-11-27 with the live-provider checks deferred as documented below.
See [GitHub issue #17](https://github.com/Pikatoise/city-portal/issues/17) and
[GitHub Project 3](https://github.com/users/Pikatoise/projects/3) for tracking.
See the [deployment guide](../development_instructions/installation_and_deployment/integrations.md)
for provider setup.

## Workflow and implementation order

1. Add the Integrations module, database migration, administrative settings
   and audit. Work proceeds in parallel on public delivery, editorial
   announcements, and MCP after agreeing on the shared settings contract.
2. Deliver RSS and homepage weather. Reuse News reads, Angular SSR and Quartz.
3. Deliver manual Telegram announcements with Wolverine and transactional
   delivery state. Add optional OpenAI suggestions through
   `Microsoft.Extensions.AI`. Telegram delivery and recovery work with manual text.
4. Deliver read-only MCP using the official C# SDK, Streamable HTTP,
   Auth0 and a preregistered MCP Inspector client.
5. Verify migrations, generated client, role enforcement, recovery, and
   the workflows through the HTTPS reverse proxy.

## Permissions and contracts

| Audience | Contract |
| --- | --- |
| Confirmed, unblocked administrator | Read and update `/api/admin/integrations`. Updates require CSRF, the current settings version, and a complete settings document. PostgreSQL stores the committed settings and their actor and timestamp. Token and key values come from deployment secrets. The page shows configured status, weather refresh time and Telegram backlog. |
| Confirmed, unblocked editor or administrator | Open an announcement in `/staff/publications`. Review or edit its plain-text preview, optionally generate a suggestion, and confirm with the current published revision. |
| Visitor | Subscribe to `/rss.xml` or view current weather on the homepage when enabled. |
| Trusted MCP client and assigned Auth0 user | With a token for the MCP resource and the `news:read` scope, list the latest published news, search, or read an article. |

### Public integrations

RSS 2.0 returns 20 items ordered by first publication. Each has a stable GUID,
the current public title and summary, and a canonical absolute link. Public
revisions and withdrawal apply on the next fetch. An enabled feed is discoverable
from the site and its metadata.

`GET /api/weather/current` returns the current observation or HTTP 204 when
none is available. Quartz schedules a weather refresh every 30 minutes for the configured coordinates
and uses the persistent job store. The Worker stores the last successful observation
with its fetch time. A result for settings that changed during the fetch is discarded.
After one hour the widget marks the observation stale. At three hours it disappears.
Changing coordinates invalidates the previous snapshot. The widget attributes
Open-Meteo. The noncommercial demo uses the open-access endpoint.

### Editorial announcements

Administrators configure one public Telegram channel. The editor confirms a
message for the current public article revision and channel. The API appends a
canonical HTTPS link and commits the delivery and durable dispatch together.
The Worker claims each send before contacting Telegram and checks the current
article revision and channel at dispatch. An invalidated send requires fresh
editorial confirmation. A confirmed post is unique per article and configured channel handle.
A withdrawal committed after dispatch can leave a post in the channel. The
editor handles posted announcements in Telegram.

Honor Telegram's retry delay on confirmed flood-control rejection. An
indeterminate response or interrupted send requires editorial reconciliation.
The editor opens the recovery list in the publication workspace, verifies the
original channel and can attach the existing post, authorize a retry, or close
an unresolved delivery after confirming there is no post. The recovery list
also includes withdrawn articles. Record the resolution and actor. The
channel handle also determines post links. Resolve pending and uncertain sends
before renaming the channel or transferring its handle.

The AI action uses a fixed OpenAI model and sends the current public title and
summary to the provider. An editor reviews the suggestion and separately confirms
the send. Bounded calls and response validation protect the form from provider
failures. The manual text remains available.

The [local review stand](../development_instructions/local_startup/local_review_stand.md#telegram-and-openai-stubs)
can substitute local HTTP responses for Telegram and OpenAI. This exercises the
editorial flow and Worker delivery without provider credentials.

### MCP

The API exposes `/api/mcp` when enabled and offers `list_latest_news`,
`search_news`, and `get_article`. These use the same visibility rules as the
public site. Lists are capped at 20 results. The protected resource metadata
advertises the HTTPS Auth0 issuer. Verify JWT signature, lifetime, issuer,
audience, scope and allowed client ID. The OAuth identity grants access to MCP
news reads only. Keep browser CityPortal roles and sessions independent.

## Acceptance criteria

- An upgrade preserves Stage 5 data and starts with the five integrations off.
  Confirm permissions, CSRF, version conflicts, audit, and the absence of secrets
  in responses and logs.
- Verify the current public revision from RSS, Telegram and MCP, including edits,
  withdrawal and republication. Exercise weather expiry and coordinate changes.
- Verify Telegram delivery, broker outage, duplicates, the retry delay, Worker
  restart and reconciliation. Verify AI success, refusal or malformed output, and
  manual sending during provider failure.
- Verify missing, wrong-audience, unauthorized-client and insufficient-scope
  MCP requests.
- Run CI against controlled provider responses.

## Verification

On 2026-11-26, all 16 focused .NET cases passed against disposable services.
They covered settings permissions, required fields and audit, continued
readiness after PostgreSQL recovery, migration repeatability, RSS visibility
and ordering, weather expiry and location changes, MCP token validation and
publication visibility, and Telegram revision checks, throttling and manual
reconciliation. The Telegram test queued a delivery while RabbitMQ was stopped,
restored the broker, started the Worker and observed exactly one request to the
fake Bot API. The AI test verified the provider input and manual queueing after a
failed suggestion.

All 115 Angular tests and the production build passed. The live OpenAPI document
matched the generated client. The Integrations EF model matched its migration.
A read-only Open-Meteo request returned the expected current-weather fields.

On a clean Docker Compose stack, all application images built and the migration
command completed. API and Worker readiness passed. The HTTPS proxy returned
404 for the disabled RSS and MCP routes. The disposable containers, volumes
and images were removed.

An earlier full Release run was stopped after fixes to the system-metadata
property count and readiness probes after PostgreSQL unpause. The three affected
cases passed in the focused rerun.

On 2026-11-26, the full .NET Release run on `f704caa` (same implementation as
`1c60bd9`) passed with SDK `10.0.403` and Docker Testcontainers. The command
`dotnet test CityPortal.slnx --configuration Release` ran 242 tests in 13 min 23 s.
All 242 passed, with zero failures and zero skips.

On 2026-11-26, a fresh Compose stack with the provider stubs verified the HTTPS
editorial flow. OpenAI returned a draft from the public title and summary. The
Worker sent the edited text and canonical article URL, recorded `Sent`, and
returned a link with the stub's message ID. The first run exposed a Telegram URI
resolution error caused by the colon in the token. The fixed client resolves the
path relative to the Bot API base URL, and the Worker test asserts that URL.
The stub retained the post after restart. All four focused Telegram and AI
tests passed. The Production API rejected the stub override at startup.

On 2026-11-27, the frontend formatting failure from
[CI run 45862216848](https://github.com/Pikatoise/city-portal/actions/runs/45862216848)
reproduced on an LF checkout of `f704caa`. After formatting the two Telegram
files, the same checkout passed Prettier, the generated API client check,
the production build, and all 115 Angular tests.

## Owner acceptance and limitations

On 2026-11-27, the owner instructed us to close Stage 6. The recorded evidence
includes automated and stub-based checks, and a live read-only Open-Meteo request.
The following live checks have no recorded result and were deferred at closure:

- Send an approved announcement through a real Telegram bot to a test channel.
  Match the editorial status and post link against the message in the channel.
- Generate a draft with a real OpenAI API key, edit it, and confirm that Telegram
  receives the approved text.
- Sign in to [MCP Inspector with Auth0](../development_instructions/installation_and_deployment/mcp_auth0.md)
  over HTTPS and verify that the assigned reader can list, search, and open
  published news.
