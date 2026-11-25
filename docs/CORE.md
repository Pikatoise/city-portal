# CityPortal Technical Design

[VALUE.md](VALUE.md) defines product behavior and scope. This document owns
architecture and delivery order. Stage specifications own workflow contracts
and acceptance evidence. [DEVELOPMENT.md](DEVELOPMENT.md) indexes instructions
for local startup, deployment, development checks and recovery.
[OPTIMIZATIONS.md](OPTIMIZATIONS.md) records engineering history.

## System Shape and Module Boundaries

Run a modular monolith on Linux with Docker Compose behind one HTTPS origin.
The reverse proxy routes `/` to the Angular Node.js SSR server and `/api/` to
the .NET 10 ASP.NET Core Web API using Controllers. A separate .NET Worker
process handles asynchronous work and scheduled jobs.

Start with one project per business module. Each module owns its internal
model, EF Core DbContext, PostgreSQL schema and migrations in one database per
deployment. Modules interact through explicit operations. Extract contract
projects when dependency direction requires them and share only technical
primitives. Keep cross-cutting workflow concerns with their owners until a
separate module has a concrete responsibility.

## Frontend and Public Delivery

Use one feature-organized Angular app with stable, compatible dependency
versions. SSR serves public content using anonymous reads. The browser renders
account and staff areas, loading staff features on demand. Keep state in
components and services until a global state library serves a concrete need.
Follow [UI_STYLE.md](UI_STYLE.md) for shared styling.

Define explicit API DTOs, consistent Problem Details, pagination and bounded
results. Publish OpenAPI and Scalar and generate the TypeScript client from
the contract.

## Deployment and City Configuration

Configure each installation independently and keep its own database. Store
timestamps in UTC and display them in the configured city time zone.
[VALUE.md](VALUE.md#product-scope) lists the configurable settings. Specify
their persistence and permissions with the relevant workflow.

Identity owns installation state, public defaults and administrator setup.
Notifications owns encrypted SMTP settings. Setup persists both modules in
one transaction. Public readiness requires a completed installation. See
the [setup contract](stages/STAGE_1.md#first-run-setup-contract) and
[installation and deployment instructions](DEVELOPMENT.md#installation-and-deployment).

## Identity and Authorization

Authenticate browsers with a Secure, HttpOnly ASP.NET Core Identity cookie.
The API enforces current session, roles, blocking, ownership and resource
state. Angular guards only control navigation. Committed blocks and role
changes apply to subsequent requests. Define in-flight behavior for each
transaction. See [VALUE.md](VALUE.md#users-and-accounts) for role rules and
the relevant stage for its permissions matrix.

An account block restricts content operations. Authentication, existing
sessions, and profile access remain available. Identity includes the current
block reason in the owner's profile and checks the committed block state when
authorizing content writes and staff operations. Owners can read their existing
content during a block.

The API issues and validates a separate CSRF token for every HTTP write,
including anonymous writes. Angular's browser-only `ApiWrite` uses the
generated `GET /api/auth/csrf` operation and the configured same-origin API
root. It sends a fresh token in `X-CSRF-TOKEN` and serializes token requests
within the application instance so the browser can store the initial
antiforgery cookie before the next request. Token acquisition failure prevents
the write. Callers receive the original HTTP error and decide when to retry.

## Transactions, Concurrency, and Module Interaction

Use synchronous operations for immediate results and coordinated commits.
A scenario-specific service coordinates modules through a shared DbConnection
and DbTransaction. Each module writes only its own data. Verify transaction
and Outbox behavior with real PostgreSQL. Use explicit record versions and
return HTTP 409 for conflicting writes.

`AccountAuthorization` acquires the administration advisory lock, then the
normalized Email and actor account locks, then rechecks the session and
effective permissions before workflow resource locks. Role changes hold the
administration lock exclusively through dependent module updates. Setup and
bootstrap also coordinate through that lock. Profile and password changes
lock Email before account. Login and registration lock Email. Logout revokes
the server-side session record. Transaction locks release at commit before
post-commit notification flushing.

Ordinary private draft saves share the administration lock, then lock the
article and its image IDs in sorted order. Other coordinated writes use
exclusive mode. If a save needs the approved-version path, end the shared
transaction before making changes, then recheck the actor, ownership and
version in an exclusive transaction. See the
[draft write measurements](OPTIMIZATIONS.md#draft-write-concurrency).

Record significant actions and their actors in the owning module. Keep
business decisions and reasons independently of notification delivery.

## Domain Implementation Requirements

### News

News owns taxonomy, working and published revisions, immutable submissions
and editorial history. Role changes release ineligible reviewers through
`IAccountRoleChangeObserver` in the Identity transaction. Article changes,
Media references, history and notification work commit together.
[Stage 2](stages/STAGE_2.md) defines the editorial and document contracts.

### Citizen Submissions

Citizen Submissions coordinates assignment and return with News in one
transaction and releases unpublished assignments in the Identity role-change
transaction. Media owns private attachment references independently of News
revision references. Reads enforce expiry before Quartz cleanup runs. See
[Stage 3](stages/STAGE_3.md) for the lifecycle and contracts.

### Classifieds and Moderation

Classifieds owns private drafts and the installation catalog of categories
and districts. New drafts, submission and recall share the Identity
administration lock. Existing listing saves, deletions, visibility changes,
staff decisions and catalog writes take it exclusively. This serializes
publication, renewal and photo reference changes with account blocking and
prevents new catalog associations from racing archival.
Media owns working and publication photo references. Classifieds and Media
commit revision changes and photo references in the same transaction.
See [Stage 4](stages/STAGE_4.md) for API contracts and delivery status.

Store expiry and enforce it in public reads independently of cleanup. The
public catalog searches only current publication snapshots. PostgreSQL keeps
the generated Russian full-text search vector in sync with edits. Account
blocking and hiding its listings commit or roll back together. Stage 4 must
verify both commit orders for a block racing with publication or renewal,
including staff publication. Design lock ordering around the acting account
and listing owner and verify it with PostgreSQL. See the
[product rules](VALUE.md#account-blocking).

Classifieds and Identity retain their own moderation decisions. Listing review,
direct listing block, and account block or unblock queue the agreed notices on
the same PostgreSQL connection and transaction as the decision. A listing block
from Reports also shares that transaction. Notification delivery and retention
use the existing Notifications module independently of decision history.

### Content Reports

Reports owns the shared submission, queue and decision model in its own
PostgreSQL schema. News and Classifieds supply the current publication preview
and apply their existing withdrawal or direct block in the staff decision
transaction. Anonymous writes use the shared Identity administration lock
and a per-subject transaction lock. The subject lock keeps report sequence
order consistent with commit order. Staff decisions take the exclusive
administration lock and cover reports up to the sequence returned by the
preview. The report, target snapshot and decision history survive deletion.

### Comments

Define reporting and abuse controls before implementation. Follow the
[comment rules](VALUE.md#article-comments).

### Notifications

Notifications owns preferences, delivery and attempts. Specify events,
defaults and retention with each workflow. Keep passwords, cookies and
confirmation/recovery links out of application logs.

## Background Processing and Integrations

Use Wolverine, RabbitMQ and EF Core Transactional Outbox/Inbox for durable
message delivery. Save outgoing work with the business change and process
it in the Worker through module operations. Handle at-least-once delivery
with message IDs and idempotent changes. Acknowledge after the durable result.
Use bounded delayed retries, an error queue, sanitized failure details and
audited staff replay. Broker or SMTP outages leave committed Outbox work
available for recovery. SMTP can deliver duplicates if recording success fails.
Use provider idempotency where available.

Use Quartz.NET in the Worker with a persistent PostgreSQL job store. Define
missed-trigger, restart and concurrency behavior per job. Public reads enforce
visibility and expiry before cleanup. Use timeouts, rate limits and safe
retries for external integrations.

## Media Storage and Processing

Media owns image metadata, references and template decisions. SkiaSharp
processes uploads. Media stages processed payloads in PostgreSQL and the
Worker writes deterministic keys to private S3-compatible storage through
AWSSDK.S3. Keep state changes and reference checks coordinated with attachment
and cleanup. The [Stage 2 media contract](stages/STAGE_2.md#image-processing-and-shared-templates)
defines processing, access and recovery.

## Data Access, Search, and Cache

Start with PostgreSQL Full-Text Search and indexed, bounded queries. Public
search uses current published content. See the
[news query contract](stages/STAGE_2.md#public-news-and-discovery).

Use Redis when a workflow benefits from caching. Define TTL, invalidation and
database fallback while preserving authorization, current visibility and
expiry on cache hits. Exclude personalized pages from shared caches. Use
Dapper or a dedicated search engine only for a demonstrated need. Set numerical
targets and a representative dataset before load testing.

## Testing, Observability, and Operations

Use xUnit and Testcontainers for PostgreSQL transactions, migrations,
concurrency and query tests. Cover state transitions and selected Angular/API
end-to-end workflows. Prioritize authorization, revision visibility, rollback,
duplicate delivery and restart recovery. Test messaging and scheduling against
their infrastructure where those guarantees matter.

Use OpenTelemetry, structured logs, correlated operation IDs, health probes and
background failure/backlog monitoring. Keep operational telemetry separate from
content analytics and protect sensitive data.

Persist infrastructure data and run migrations explicitly before starting API
and Worker. Verify database and Media backup/restore before demonstration
deployment. GitHub Actions runs verification. Follow [CODE_STYLE.md](CODE_STYLE.md)
and the root `.editorconfig` for C# and enforce format checks in build and CI.

## Delivery Order

| Stage                  | Scope                                                                           | Owner acceptance |
| ---------------------- | ------------------------------------------------------------------------------- | ---------------- |
| [1](stages/STAGE_1.md) | Accounts, roles, Email, installation and the Identity/Notifications foundation. | 2026-11-13       |
| [2](stages/STAGE_2.md) | News, Media, editorial history, public SSR and basic search.                    | 2026-11-17       |
| [3](stages/STAGE_3.md) | Citizen suggestions, assignment, article conversion and expiry.                 | 2026-11-19       |
| [4](stages/STAGE_4.md) | Classifieds, moderation, reports, blocking and lifecycle management.            | In development   |
| 5                      | Comments and moderation.                                                        | Planned          |
| 6                      | Telegram, weather, RSS, MCP and AI integrations                                 | Planned          |

Each stage includes its relevant workspaces, notifications and audit history.
Follow [AGENTS.md](../AGENTS.md#stage-workflow) for stage planning, owner review,
specification updates and GitHub tracking. The learning review traces the
data flow, explains decisions and walks through a failure or changed
requirement before the next stage.
