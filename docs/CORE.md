# CityPortal Technical Design and Implementation Direction

## Purpose

This document records the approved technical direction, working method, and delivery order for CityPortal. Product purpose, user capabilities, business rules, and scope are defined in [VALUE.md](VALUE.md).

The architectural choices below are confirmed. Detailed state models, schemas, permissions, field constraints, notification events, cache policies, and operational limits are designed before implementing the relevant workflow. Approval of this direction does not imply that those details are already specified.

CityPortal is a portfolio and learning project focused primarily on .NET backend development and understanding a complete system. Angular provides the application interface. The project should demonstrate domain modeling, authorization, concurrency, transactions, background work, integrations, caching, testing, observability, and deployment. Correctness and maintainability take priority over unnecessary complexity.

## System Shape and Module Boundaries

Use a modular monolith built with ASP.NET Core Web API and Controllers. Do not use Minimal APIs or introduce microservices solely to increase complexity.

The deployed application has three application processes:

- One Angular application with a Node.js SSR server for public pages, personal accounts, and staff workspaces.
- A .NET API for user operations, authorization, business rules, and persistence.
- A separate .NET Worker for asynchronous processing and Quartz.NET jobs.

Business modules have separate .NET projects, internal models, explicit public operations, and documented data ownership. Other modules use those operations rather than accessing internal types or tables. Keep classifieds and citizen suggestions distinct from editorial news.

Begin with Identity and Notifications. Add News and Media with the news workflow, then Citizen Submissions, Classifieds, and Comments at their delivery stages. Responsibilities such as moderation, search, audit, analytics, and portal settings do not automatically require separate projects. Place them with their owning workflows until a separate module has a concrete responsibility and contract.

Start with one main project per module. Extract contract projects when dependency direction requires them; do not automatically create several layer projects for every module. Keep shared code limited to technical primitives, without a shared domain model or a general-purpose collection of business services.

Each module owns its EF Core DbContext, PostgreSQL schema, and migrations within one application database per deployment. Cross-module workflows may share an explicitly coordinated transaction. Separate DbContexts do not imply separate databases or eventual consistency for every interaction.

UI styling follows [UI_STYLE.md](UI_STYLE.md), with shared CSS variables for the application and visual prototypes. Preserve accepted public layouts while applying the same controls and typography to account/staff/editor screens.

## Technology Stack

| Area | Confirmed direction or constraint |
| --- | --- |
| Language and runtime | C# and .NET 10. |
| HTTP API | ASP.NET Core Web API with Controllers. |
| Frontend | One Angular application, SSR for public content and browser rendering for personal and staff areas. |
| Database and data access | PostgreSQL and EF Core, with module-owned schemas, DbContexts, and migrations. |
| Targeted query access | Dapper only when a specific query benefits; not an initial dependency. |
| Identity | ASP.NET Core Identity with cookie authentication. |
| Cache | Redis, with feature-specific scope, expiration, and invalidation. |
| Search | PostgreSQL Full-Text Search initially. |
| Messaging | Wolverine with RabbitMQ and EF Core Transactional Outbox/Inbox integration. |
| Scheduling | Quartz.NET in the Worker, with a persistent PostgreSQL job store. |
| Media storage | S3-compatible object storage. |
| Testing | xUnit, Testcontainers, and selected frontend/API end-to-end scenarios. |
| API documentation | OpenAPI and Scalar; generate the TypeScript API client from the contract. |
| Telemetry | OpenTelemetry and structured logging. |
| Packaging and local infrastructure | Docker and Docker Compose. |
| CI/CD | GitHub Actions. |
| Hosting | Linux. |

Pin compatible stable Angular and dependency versions when creating the solution. A dedicated search engine requires a demonstrated capability or scale need. Redis, RabbitMQ, Wolverine, Quartz.NET, and the Worker are selected infrastructure; implement their feature-specific use with the relevant workflows.

## Media Storage and Processing

Media owns processed-image metadata, usage references, template proposals,
moderation history, and removal requests. SkiaSharp generates a master and
three WebP variants with long edges capped at 480, 1280, and 2560 pixels.
It encodes all four at quality 85 without upscaling. Media atomically stores the
Processing row and variant payloads in PostgreSQL. The Worker writes deterministic
keys to private S3-compatible storage through AWSSDK.S3, marks the image Ready and clears
the staged payloads in one database transaction. Only Ready images can be
attached. Partial writes retain payloads for retry with a 20-second to
15-minute backoff. Missing payloads mark the image Failed.

Reconciliation runs every ten seconds without concurrent execution in the
existing persistent Quartz scheduler, named `CityPortal.Email`. Cleanup takes
the same resource lock as attachment, rechecks retention, and marks an image
Deleting before removing its objects. Failed object deletion keeps the row
available for retry. See [local storage and recovery](DEVELOPMENT.md#stage-2-upgrade-and-recovery).

## Frontend and Public Delivery

Use one Angular application for the public website, personal account, and CMS. Organize it by features such as news, account, editorial work, classifieds, and administration. Load staff areas on demand. Start with component and service state; introduce a separate global state library only for a concrete need.

Use SSR for public content from the first news release to support search-engine indexing and metadata. Personal and staff areas render in the browser. The SSR server retrieves only public data for public pages. Drafts, personal notifications, and staff data must not enter shared HTML or shared caches. Do not initially cache personalized pages.

Expose one HTTPS origin: Angular at `/` and the .NET API at `/api/`. A reverse proxy routes requests to the appropriate process. Angular SSR and the API remain separate processes behind that origin.

Implement search-friendly article URLs and metadata, configurable portal SEO information, chronological retrieval, related content, search, filters, and pagination. Withdrawal must affect direct URLs as well as lists and search; review media accessibility as part of the withdrawal scenario.

Use explicit request and response DTOs rather than EF entities. Provide a consistent API error format, pagination, and bounded result sizes. OpenAPI describes the contract and supports TypeScript client generation. This API serves the application's frontend; a supported public API for third-party developers remains excluded.

## Deployment and City Configuration

Use one city per deployment, with independent configuration and databases for different installations. Multi-tenancy is not required.

City-specific configuration includes portal name and branding, city and region names, time zone, geographic coordinates, districts and locations, contact information, SEO metadata, and external service settings. Do not hardcode these values. Use a fictional city in development, demonstrations, and tests. Changing city settings must not migrate or reinterpret existing content.

Specify the configuration model, persistence, and permissions before implementing the relevant settings. Store timestamps in UTC and display them in the configured city time zone.

The completed Stage 1 wizard persists Identity-owned installation/public defaults and the initial administrator with Notifications-owned encrypted SMTP settings in one coordinated transaction. The Worker reads persisted SMTP settings for each delivery. Existing account databases migrate to installed state while retaining deployment defaults and legacy administrator bootstrap.

Setup is enabled only for local installation and disabled in public mode. Public API readiness requires completed installation. Node page gating and Angular guards keep other application pages unavailable until setup succeeds; status failures keep access closed. Public metadata exposes branding, city, region, and IANA time zone without credentials.

The detailed setup, concurrency, and migration contract belongs in [STAGE_1.md](stages/STAGE_1.md#first-run-setup-contract). Local access, SSH tunnels, public-origin configuration, and the public Compose override belong in [DEVELOPMENT.md](DEVELOPMENT.md).

## Identity and Authorization

Use unified accounts with Regular User, Author, Editor, Moderator, and Administrator roles. Accounts may hold multiple roles. Registration, Email confirmation, recovery, and role responsibilities follow VALUE.md.

Authenticate browser requests with an ASP.NET Core Identity Secure, HttpOnly authentication cookie. Protect state-changing requests against CSRF. Angular sends a separate CSRF token in a header; the API issues and validates the token. The CSRF token and the HttpOnly authentication cookie serve different purposes. Do not introduce browser bearer tokens or JWT authentication as an additional default.

Enforce roles, policies, resource ownership, and state-dependent permissions in the API. Angular route guards and hidden controls improve the interface but do not grant or enforce backend access.

Administrator grants all effective Editor permissions without requiring an assigned Editor role. Preserve the distinction between assigned roles and effective permissions. Ownership restrictions still apply: another author's draft is private before submission, including from editors and administrators.

Check current account blocking and effective permissions for protected operations instead of trusting stale cookie claims alone. Blocking and role removal apply to requests beginning after the change is committed. Define the handling of already-running operations while designing their transactions.

Account blocking and hiding all of the account's advertisements form one atomic cross-module operation. After a successful block, subsequent public reads must not show those listings, including direct URLs and cached results. If either module update fails, roll back the entire operation and report the failure to staff. Unblocking does not automatically relist advertisements.

Registration may create an unconfirmed account while its confirmation Email is queued during a provider outage. Follow the user-facing delivery states and loading/error presentation defined in VALUE.md and the stage 1 specification. Existing Email-confirmation restrictions remain in force.

A detailed permissions matrix is required before implementing the affected operations.

## Transactions, Concurrency, and Module Interaction

Use synchronous module operations for workflows requiring an immediate result or one atomic transaction. Use messages for work that can finish later, such as Email, Telegram announcements, and media processing. RabbitMQ is not the default transport for every module call.

An application service for a specific cross-module scenario coordinates module operations and commit or rollback. Each module still changes only its own data. Shared EF Core transactions require participating DbContexts to share a DbConnection and DbTransaction. Test this coordination with the selected PostgreSQL provider and Outbox integration before relying on it in business workflows.

Use optimistic concurrency with an explicit record version. Reject conflicting writes with HTTP 409 Conflict and let the interface load current data; do not silently overwrite another user's changes. Design transaction boundaries and rechecks for concurrent publication, withdrawal, moderation, account blocking, advertisement creation, and renewal.

Writes using `AccountAuthorization` recheck the actor after acquiring the
authorization locks. Role changes keep exclusive coordination and update
dependent modules before commit. The [draft save lock policy](OPTIMIZATIONS.md#draft-write-concurrency)
records lock ordering, the scope of shared locking and regression evidence.

Record significant editorial, moderation, and administrative actions with the responsible actor. Define the audit schema and retention for each workflow. Business decisions and their reasons remain in the owning module independently of notification delivery.

## Domain Implementation Requirements

### News

News owns drafting, taxonomy, independent working/published revisions, immutable
submitted snapshots and retained history. Identity joins assignment release through
`IAccountRoleChangeObserver` in the role-change transaction; Administrator preserves
effective editorial permissions. News, Media references and notifications commit
atomically. The editorial state transitions and API contracts are in
[STAGE_2.md](stages/STAGE_2.md).

Article revisions store `schemaVersion: 1` and a ProseMirror JSON body edited
with Tiptap. Title, summary, taxonomy IDs, cover/gallery placements, attribution
and SEO are separate revision fields. The server validates and renders the
document using an independent allowlist. Supported nodes are `doc`,
`paragraph`, `heading` (H2/H3), `text`, `bulletList`, `orderedList`,
`listItem`, `blockquote`, `hardBreak` and `mediaFigure`. Text and hard
breaks can carry bold, italic and absolute HTTP/HTTPS link marks. Ordered-list
starts range from 1 to 1,000,000. Canonicalization strips editor-only link
attributes. Rendering escapes attributes and sets `rel="noopener noreferrer"`.
The editor converts pasted rich content to plain text. Images reference Media
IDs and carry contextual alternative text and captions.

The body is limited to 2 MiB of UTF-8 JSON, 2,000 blocks, 20,000 nodes and 24
levels. A complete save request is limited to 4 MiB. The server checks the
structure and any supplied fields on draft save, then enforces required fields
on submission and when saving an approved version.

Scheduled publication remains deferred scope even though scheduling infrastructure is available earlier. When introduced, define durable scheduling, concurrency, and restart recovery behavior.

### Citizen Submissions

The [Stage 3 specification](stages/STAGE_3.md) records the resident submission
contract and owner acceptance.

Taking a suggestion creates its News draft in the same transaction as the
assignment. Returning it deletes the unpublished draft through News and resumes
the queue timer. Reads exclude expired suggestions immediately, and the
persistent Quartz job deletes their rows. News retains its own article,
origin history and Media references when staff delete the source suggestion.

Citizen Submissions joins removal of the Author role in the Identity transaction
to release unpublished assignments. Manual deletion and its audit record commit
together. The audit record has no suggestion content or resident contact field.

Media owns private suggestion uploads and attachment references. Expiry and
manual deletion remove those references within the suggestion transaction.
Imported images use independent News revision references. News queues the
resident's first-publication notice through Notifications in the publication
transaction.

Any future clarification communication requires a scoped product agreement rather than introducing general-purpose messaging.

### Classifieds and Moderation

Implement drafts, review, publication, rejection, blocking, withdrawal, expiration, and renewal according to VALUE.md. Categories and relevant listing attributes are configurable. Seller contacts are limited to phone and/or Email; advertisement chat and Telegram contacts are excluded.

Keep the approved revision public while an owner's changed revision awaits moderation. Pending changes must never become public before approval. Moderators make decisions rather than rewriting listings.

Store publication expiry and check it in public queries as well as cleanup jobs. Worker downtime must not extend a listing's public lifetime. Coordinate account blocks with Identity atomically and distinguish account-level hiding from direct listing blocks and owner withdrawal.

The Stage 4 specification must cover both commit orders for account blocking
versus listing publication or renewal. A committed block prevents a later
publication or renewal, including publication by staff on the blocked account's
listing. When publication or renewal commits first, a successful block hides
the listing in both public catalog and direct reads. A failure to hide the
listings rolls back the block. Design the lock order around both the acting
account and the listing owner, and verify these cases with real PostgreSQL.

Implement reports, moderation history, and renewed report handling as defined in VALUE.md. Finalize detailed states and transitions before implementation.

### Comments

Implement the flat, immediately visible anonymous and authenticated comments defined in VALUE.md. Enforce ownership for deletion and moderator or administrator permissions for hiding. Define comment-report handling and abuse controls before implementing that workflow.

### Notifications

Notifications owns channel settings and delivery attempts. Owning business modules retain review outcomes, feedback, and decisions regardless of whether Email is disabled or delivery fails. Confirmation and password-reset messages are excluded from user notification switches.

Specify notification events and defaults per workflow. Do not log passwords, authentication cookies, or recovery and confirmation links in ordinary application logs.

Template decisions commit with their audit records, notification deduplication
receipts, account notices and Email Outbox work in one PostgreSQL transaction.
News preserves current working and publication references separately from
retained history so Media can select affected authors when recording a template
block or unblock. Notification content excludes private titles, template names
and decision reasons. Delivery and replay recheck the Email preference and
the original event deadline. Retention clears protected payloads for expired
unsent work and leaves deduplication receipts intact.

## Background Processing and Integrations

Use Wolverine with RabbitMQ and EF Core Transactional Outbox/Inbox integration. Save outgoing messages in the same PostgreSQL transaction as the business change. Worker infrastructure relays committed messages and executes handlers using shared module logic; it does not call the application's own HTTP API just to invoke business operations.

Assume at-least-once delivery. Give messages identifiers, prevent duplicate business changes, and acknowledge processing only after the required durable result. Configure bounded retries with increasing delay and an error queue after exhaustion. Record error causes, attempts, and operation identifiers. Record staff-triggered replay in history. Avoid infinite immediate retry loops.

External Email delivery is not guaranteed to happen exactly once: a send followed by a failure before recording success can produce a duplicate, especially with SMTP. Use provider idempotency where available without claiming a universal guarantee.

A RabbitMQ or Email outage must not roll back an otherwise valid business change whose outgoing work is durably recorded in the Outbox. Resume delivery after recovery and monitor backlog age, size, and failures. The outage behavior assumes that PostgreSQL and the local transaction remain available.

Use Quartz.NET in the Worker with PostgreSQL persistence for scheduled jobs. Specify missed-trigger handling, restart recovery, and concurrent execution rules. RabbitMQ handles message delivery, while Quartz.NET owns scheduled business jobs. Public visibility checks enforce expiry independently of whether a scheduled cleanup has run.

Email is introduced with accounts and each workflow's notifications. Telegram, weather, and RSS are later integrations. Automatic news import and a supported public API are excluded. Use timeouts, rate limits, failure tracking, and safe retries for external calls.

## Media and Upload Security

Validate the decoded format against the declared MIME type and check dimensions,
file size and animation, including animated containers with one frame. Enforce
the [upload limits](stages/STAGE_2.md#engineering-defaults) and CSRF checks before
processing. Acquire one upload slot per process before multipart model binding.
The 11 MiB request limit bounds in-memory buffering. Generate keys on the server
and retain only processed output.

Keep buckets and objects private. The API checks the authorized audience or
current publication reference and template block on every image response. Serve
images with `no-store` and `nosniff`, with range and conditional responses
disabled. Preserve these access checks when considering caching or signed
delivery. Suggestion images stay outside editorial libraries. The suggestion
module rechecks the queue or assignment state for each attachment list and
image request. Imported images receive independent article references.

News updates Media references in the same authorized transaction as the article
change. Editorial actions update existing reference visibility through Media.
See the [Media reference optimization](OPTIMIZATIONS.md#media-reference-visibility).
When a new revision inherits an archived template, News passes the
validated preceding revision ID from the same article. Direct corrections use
the editing actor for upload selection and preserve the original author on the
references. Cleanup follows the [product retention rules](VALUE.md#editorial-and-administrative-workspace)
and the [suggestion attachment rules](stages/STAGE_3.md#attachments-and-notifications).

## Data Access, Search, and Cache

See the [News discovery optimization](OPTIMIZATIONS.md#news-discovery-queries)
for the index, storage policy and measurements, and the
[upgrade procedure](DEVELOPMENT.md#stage-2-upgrade-and-recovery) for migration
and recovery commands.

Use efficient PostgreSQL queries, suitable indexes, pagination, and bounded reads. Start with PostgreSQL Full-Text Search over published content. Use ordinary query filters for category, district, price, and related attributes. Draft content must not enter public search.

News search builds a generated Russian `tsvector` on revisions and weights
title A, summary B and body C. GIN indexes support search and tags. Public
discovery joins the current `PublishedRevisionId` and checks article visibility
on every query. Author names are fetched through one bounded Identity batch
operation. SSR public API calls omit credentials and bypass browser and
transfer caches. API and SSR responses use `no-store`.

Use Redis for caching. Specify scope, TTL, invalidation, and failure behavior per feature, covering publication, revision replacement, withdrawal, blocking, and expiration. PostgreSQL remains the source of truth. Cache hits must not bypass authorization or current visibility checks. Immediate account blocking also applies to cached catalogs and direct listing pages.

Redis failure should permit database-backed reads while preserving visibility guarantees. Do not initially cache personalized pages. Do not assume TTL alone satisfies immediate withdrawal or blocking requirements; settle and verify the invalidation and visibility design before caching those results.

Use Dapper only for a demonstrated query benefit. Evaluate a dedicated search engine only for an unmet capability or measured scale need. Define numerical performance targets and a representative data set before load testing. Avoid speculative infrastructure, Kubernetes, and unnecessary distribution beyond the selected architecture.

## Testing, Observability, and Operations

Local review starts through `scripts/start-stand.py`, including loopback pgAdmin
and five confirmed role-specific demo accounts whose persisted local credentials
are printed on every successful launch. `compose.stand.yaml` isolates the review
database, broker vhost and Media bucket. Empty review taxonomy dictionaries
receive initial demo categories/tags through the administrator API; nonempty
dictionaries and normal installation data are preserved;
public deployment does not enable this fixture setup or pgAdmin by default.

Follow [CODE_STYLE.md](CODE_STYLE.md) and the root `.editorconfig` for C#. Keep build-time code-style diagnostics and the CI formatting check; code review covers conventions that built-in tooling cannot enforce.

Use xUnit and Testcontainers with real PostgreSQL for transaction, query, migration, and concurrency integration tests. Add unit tests for state transitions and selected end-to-end Angular/API workflows.

Prioritize unauthorized access to another user's resource, unconfirmed and blocked accounts, conflicting edits, revision visibility, atomic cross-module rollback, duplicate messages, and recovery after restarts. Verify messaging and scheduling against their actual infrastructure where those guarantees matter.

Provide structured logs, correlated operation identifiers, health checks, background failure visibility, and OpenTelemetry from the relevant stages. Monitor delivery backlogs and scheduled work. Keep content analytics distinct from operational telemetry and avoid exposing sensitive data in diagnostics.

Package Angular SSR, API, Worker, PostgreSQL, RabbitMQ, Redis, and object storage with Docker Compose for the Linux target. Persist PostgreSQL, RabbitMQ, and object-storage data. Run migrations as an explicit deployment step rather than racing API and Worker startup migrations. Automate appropriate checks with GitHub Actions.

Before a demonstration deployment, verify backup and restore for both database and media. Develop deployment instructions and operational tooling with the application. Security, correctness, reliability, tests, monitoring, and deployment preparation are ongoing requirements rather than a final hardening phase.

## Delivery Order

Deliver functional stages in this order:

| Stage | Scope |
| --- | --- |
| 1 (complete) | Accounts, roles, Email, first-run installation, and the Identity/Notifications foundation. |
| 2 (complete) | Complete news workflow, including published corrections, revision history, Media, public SSR pages, and basic news search. |
| 3 (complete) | Citizen suggestions, assignment, conversion into articles, and 72-hour deletion. |
| 4 | Classifieds, moderation, reports, account and listing blocking, and lifecycle management. |
| 5 | Comments and their moderation. |
| 6 | Telegram, weather, and RSS integrations. |

Each stage includes the relevant personal and staff interfaces, notifications, and significant action history. Quality and operational requirements apply throughout. Scheduled publication, advanced search, advanced revision comparison, and additional media capabilities remain deferred rather than automatically entering stage 6.

## Working Method and Planning

The owner designs scenarios, business rules, module boundaries, interactions, and key architectural decisions. AI proposes implementation details, implements agreed decisions, and explains how they work. The owner checks that the result follows the intended design.

Complete and understand each workflow before moving to the next module. The learning review covers data flow, reasons for decisions, and a changed requirement or failure scenario. Address gaps in understanding before continuing.

VALUE.md is the product baseline. Discuss unresolved details with the owner before implementing the relevant workflow. Do not add business rules or features without confirmation.

Use GitHub Issues and GitHub Projects for tasks, priorities, milestones, and tracking. Record workflows, permissions, module contracts, dependencies, and open questions in concise written specifications. Plan small sessions with enough task-local context to implement without loading the entire project specification.

Document each delivery stage in `docs/stages/STAGE_X.md`, using the stage number from the delivery order. Each specification contains the goal, scope, tools, ordered implementation steps, workflows, permissions, contracts, acceptance criteria, open decisions, and implementation status. Describe the next stage only after the current stage is implemented, verified, and reviewed with the owner. The execution rules are recorded in [AGENTS.md](../AGENTS.md#stage-workflow).

During implementation, update affected specifications whenever requirements, contracts, or decisions change. Product changes belong in VALUE.md, architecture and delivery changes belong here, and stage details belong in the relevant stage specifications. Keep these sources and GitHub tracking consistent. New business rules and features still require owner confirmation.

The owner accepted [Stage 1: Accounts, Roles, and Email](stages/STAGE_1.md) on
2026-11-13, [Stage 2: News and Media](stages/STAGE_2.md) on 2026-11-17, and
[Stage 3: Citizen Story Suggestions](stages/STAGE_3.md) on 2026-11-19.
Their specifications retain delivered contracts, verification and limitations.
Stage 2 issues #7-#14 are complete.
