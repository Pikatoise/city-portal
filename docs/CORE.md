# CityPortal Technical Design and Implementation Direction

## Purpose and Status

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

## Identity and Authorization

Use unified accounts with Regular User, Author, Editor, Moderator, and Administrator roles. Accounts may hold multiple roles. Registration, Email confirmation, recovery, and role responsibilities follow VALUE.md.

Authenticate browser requests with an ASP.NET Core Identity Secure, HttpOnly authentication cookie. Protect state-changing requests against CSRF. Angular sends a separate CSRF token in a header; the API issues and validates the token. The CSRF token and the HttpOnly authentication cookie serve different purposes. Do not introduce browser bearer tokens or JWT authentication as an additional default.

Enforce roles, policies, resource ownership, and state-dependent permissions in the API. Angular route guards and hidden controls improve the interface but do not grant or enforce backend access.

Check current account blocking and effective permissions for protected operations instead of trusting stale cookie claims alone. Blocking and role removal apply to requests beginning after the change is committed. Define the handling of already-running operations while designing their transactions.

Account blocking and hiding all of the account's advertisements form one atomic cross-module operation. After a successful block, subsequent public reads must not show those listings, including direct URLs and cached results. If either module update fails, roll back the entire operation and report the failure to staff. Unblocking does not automatically relist advertisements.

Registration may create an unconfirmed account while its confirmation Email is queued during a provider outage. Follow the user-facing delivery states and loading/error presentation defined in VALUE.md and the stage 1 specification. Existing Email-confirmation restrictions remain in force.

A detailed permissions matrix is required before implementing the affected operations.

## Transactions, Concurrency, and Module Interaction

Use synchronous module operations for workflows requiring an immediate result or one atomic transaction. Use messages for work that can finish later, such as Email, Telegram announcements, and media processing. RabbitMQ is not the default transport for every module call.

An application service for a specific cross-module scenario coordinates module operations and commit or rollback. Each module still changes only its own data. Shared EF Core transactions require participating DbContexts to share a DbConnection and DbTransaction. Test this coordination with the selected PostgreSQL provider and Outbox integration before relying on it in business workflows.

Use optimistic concurrency with an explicit record version. Reject conflicting writes with HTTP 409 Conflict and let the interface load current data; do not silently overwrite another user's changes. Design transaction boundaries and rechecks for concurrent publication, withdrawal, moderation, account blocking, advertisement creation, and renewal.

Record significant editorial, moderation, and administrative actions with the responsible actor. Define the audit schema and retention for each workflow. Business decisions and their reasons remain in the owning module independently of notification delivery.

## Domain Implementation Requirements

### News

Implement the full editorial process and published article corrections during delivery stage 2. Follow VALUE.md for review ownership, returns, final rejection, approval, publication permissions, direct editor corrections, withdrawal, and republication.

Separate the published revision from unpublished work from the beginning. Preserve article versions and significant action history from the first implementation that allows published content to change. Advanced comparison tools may come later; revision safety and basic history may not.

Store article content as a structured editor document with a limited set of blocks. Images reference Media records. Validate document structure, URLs, and content on the server and sanitize rendered content. Arbitrary HTML is not supported. Choose the concrete editor through a small prototype before implementing the article form.

Scheduled publication remains deferred scope even though scheduling infrastructure is available earlier. When introduced, define durable scheduling, concurrency, and restart recovery behavior.

### Citizen Submissions

Implement the shared author queue and assignment process from VALUE.md, separately from editorial article review. Authors may take a suggestion, return it to the queue, and create a linked article. They cannot reject or delete suggestions; manual deletion belongs to administrators. Creating an article does not publish it.

Fully delete suggestion records after 72 hours from creation without an archived copy. Quartz.NET schedules cleanup. Expired suggestions must not remain accessible while physical cleanup is pending. Articles and materials transferred into them survive independently; article history retains that the topic came from a resident. References must tolerate deletion of the original suggestion.

Any future clarification communication requires a scoped product agreement rather than introducing general-purpose messaging.

### Classifieds and Moderation

Implement drafts, review, publication, rejection, blocking, withdrawal, expiration, and renewal according to VALUE.md. Categories and relevant listing attributes are configurable. Seller contacts are limited to phone and/or Email; advertisement chat and Telegram contacts are excluded.

Keep the approved revision public while an owner's changed revision awaits moderation. Pending changes must never become public before approval. Moderators make decisions rather than rewriting listings.

Store publication expiry and check it in public queries as well as cleanup jobs. Worker downtime must not extend a listing's public lifetime. Coordinate account blocks with Identity atomically and distinguish account-level hiding from direct listing blocks and owner withdrawal.

Implement reports, moderation history, and renewed report handling as defined in VALUE.md. Finalize detailed states and transitions before implementation.

### Comments

Implement the flat, immediately visible anonymous and authenticated comments defined in VALUE.md. Enforce ownership for deletion and moderator or administrator permissions for hiding. Define comment-report handling and abuse controls before implementing that workflow.

### Notifications

Notifications owns channel settings and delivery attempts. Owning business modules retain review outcomes, feedback, and decisions regardless of whether Email is disabled or delivery fails. Confirmation and password-reset messages are excluded from user notification switches.

Specify notification events and defaults per workflow. Do not log passwords, authentication cookies, or recovery and confirmation links in ordinary application logs.

## Background Processing and Integrations

Use Wolverine with RabbitMQ and EF Core Transactional Outbox/Inbox integration. Save outgoing messages in the same PostgreSQL transaction as the business change. Worker infrastructure relays committed messages and executes handlers using shared module logic; it does not call the application's own HTTP API just to invoke business operations.

Assume at-least-once delivery. Give messages identifiers, prevent duplicate business changes, and acknowledge processing only after the required durable result. Configure bounded retries with increasing delay and an error queue after exhaustion. Record error causes, attempts, and operation identifiers. Record staff-triggered replay in history. Avoid infinite immediate retry loops.

External Email delivery is not guaranteed to happen exactly once: a send followed by a failure before recording success can produce a duplicate, especially with SMTP. Use provider idempotency where available without claiming a universal guarantee.

A RabbitMQ or Email outage must not roll back an otherwise valid business change whose outgoing work is durably recorded in the Outbox. Resume delivery after recovery and monitor backlog age, size, and failures. The outage behavior assumes that PostgreSQL and the local transaction remain available.

Use Quartz.NET in the Worker with PostgreSQL persistence for scheduled jobs. Specify missed-trigger handling, restart recovery, and concurrent execution rules. RabbitMQ handles message delivery, while Quartz.NET owns scheduled business jobs. Public visibility checks enforce expiry independently of whether a scheduled cleanup has run.

Email is introduced with accounts and each workflow's notifications. Telegram, weather, and RSS are later integrations. Automatic news import and a supported public API are excluded. Use timeouts, rate limits, failure tracking, and safe retries for external calls.

## Media and Upload Security

Use S3-compatible object storage for files and the Media module for metadata, ownership, and usage links. Uploaded files remain private until validated. Check actual format, file size, and image dimensions; generate storage keys on the server rather than trusting user filenames.

The initial upload implementation supports images. Other file types require separate agreement. Choose exact formats, upload limits, processing rules, and storage organization before implementing uploads. Keep draft materials private and grant public access only according to the owning content's publication rules.

Materials transferred from suggestions into articles need independent retained references. Define cleanup of unused files without deleting files still used by surviving articles or revisions. Improved media capabilities can follow later, but basic upload security, sanitization, authorization, XSS prevention, rate limiting, and anti-spam controls apply from the first relevant release.

## Data Access, Search, and Cache

Use efficient PostgreSQL queries, suitable indexes, pagination, and bounded reads. Start with PostgreSQL Full-Text Search over published content. Use ordinary query filters for category, district, price, and related attributes. Draft content must not enter public search.

Use Redis for caching. Specify scope, TTL, invalidation, and failure behavior per feature, covering publication, revision replacement, withdrawal, blocking, and expiration. PostgreSQL remains the source of truth. Cache hits must not bypass authorization or current visibility checks. Immediate account blocking also applies to cached catalogs and direct listing pages.

Redis failure should permit database-backed reads while preserving visibility guarantees. Do not initially cache personalized pages. Do not assume TTL alone satisfies immediate withdrawal or blocking requirements; settle and verify the invalidation and visibility design before caching those results.

Use Dapper only for a demonstrated query benefit. Evaluate a dedicated search engine only for an unmet capability or measured scale need. Define numerical performance targets and a representative data set before load testing. Avoid speculative infrastructure, Kubernetes, and unnecessary distribution beyond the selected architecture.

## Testing, Observability, and Operations

Follow [CODE_STYLE.md](CODE_STYLE.md) and the root `.editorconfig` for C#. Introduce build-time code-style diagnostics and a formatting check when scaffolding the solution; code review covers conventions that built-in tooling cannot enforce.

Use xUnit and Testcontainers with real PostgreSQL for transaction, query, migration, and concurrency integration tests. Add unit tests for state transitions and selected end-to-end Angular/API workflows.

Prioritize unauthorized access to another user's resource, unconfirmed and blocked accounts, conflicting edits, revision visibility, atomic cross-module rollback, duplicate messages, and recovery after restarts. Verify messaging and scheduling against their actual infrastructure where those guarantees matter.

Provide structured logs, correlated operation identifiers, health checks, background failure visibility, and OpenTelemetry from the relevant stages. Monitor delivery backlogs and scheduled work. Keep content analytics distinct from operational telemetry and avoid exposing sensitive data in diagnostics.

Package Angular SSR, API, Worker, PostgreSQL, RabbitMQ, Redis, and object storage with Docker Compose for the Linux target. Persist PostgreSQL, RabbitMQ, and object-storage data. Run migrations as an explicit deployment step rather than racing API and Worker startup migrations. Automate appropriate checks with GitHub Actions.

Before a demonstration deployment, verify backup and restore for both database and media. Develop deployment instructions and operational tooling with the application. Security, correctness, reliability, tests, monitoring, and deployment preparation are ongoing requirements rather than a final hardening phase.

## Delivery Order

Deliver functional stages in this order:

| Stage | Scope |
| --- | --- |
| 1 | Accounts, roles, and Email; initial Identity and Notifications modules and supporting infrastructure. |
| 2 | Complete news workflow, including published corrections, revision history, Media, public SSR pages, and basic news search. |
| 3 | Citizen suggestions, assignment, conversion into articles, and 72-hour deletion. |
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

The current specification is [Stage 1: Accounts, Roles, and Email](stages/STAGE_1.md). Its owner interview is complete, and the agreed rules, permissions, API/module contracts, transaction boundaries, and acceptance criteria are recorded there. Steps 2 and 3 implement the foundation, registration, Email confirmation, durable delivery, and the login/profile/logout dependencies needed for profile-only resend. Independent standards and specification reviews are complete. Recovery, profile editing, administration, delivery inspection/replay, and final stage acceptance remain tracked in [GitHub Project 3](https://github.com/users/Pikatoise/projects/3). The stage remains open for those workflows and owner review. Agree any newly discovered business decisions with the owner before implementing the affected workflow.
