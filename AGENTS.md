# CityPortal

## Project Purpose

CityPortal is a portfolio and learning project focused on professional .NET backend development and understanding a complete system. It serves one configurable city per deployment and uses a fictional city for development and demonstrations.

News is the main feature, complemented by moderated classifieds, citizen story suggestions, flat article comments, personal accounts, and editorial and administrative workspaces. Unified accounts support multiple roles: Regular User, Author, Editor, Moderator, and Administrator.

## Sources of Truth

- Read [docs/VALUE.md](docs/VALUE.md) before defining product behavior, permissions, content lifecycles, or scope.
- Read [docs/CORE.md](docs/CORE.md) before architectural changes, implementation planning, or infrastructure work.
- Before writing or reviewing C#, read [docs/CODE_STYLE.md](docs/CODE_STYLE.md) and follow the root `.editorconfig`. The owner's reviewed sample establishes the conventions, including omitted braces for single-statement control-flow bodies.
- These documents define the approved direction. Agree unresolved workflow details with the owner before implementation; new business rules and features require confirmation.

## Technical Direction

- Build a modular monolith using C# and .NET 10, ASP.NET Core Web API Controllers, and one Angular application. Public pages use SSR; personal and staff areas render in the browser.
- Run Angular SSR, the API, and a separate .NET Worker behind one HTTPS origin, with the API at `/api/`.
- Use PostgreSQL and EF Core. Each module owns its models, schema, DbContext, and migrations, and exposes explicit operations to other modules.
- Use ASP.NET Core Identity with cookie authentication, CSRF protection, and API-enforced authorization based on roles, ownership, and content state.
- Use Redis, Wolverine with RabbitMQ and transactional Outbox/Inbox, Quartz.NET, and S3-compatible media storage for the relevant workflows.
- Use OpenAPI and Scalar, a generated TypeScript API client, xUnit and Testcontainers, OpenTelemetry, Docker Compose, GitHub Actions, and Linux hosting.

## Implementation Priorities

Preserve published revisions while changes await approval. Enforce current visibility and expiry rules in public reads. Account blocking and hiding the account's advertisements must be atomic. Citizen suggestions expire after 72 hours; advertisements remain public for seven days unless renewed. Durable background delivery must tolerate outages and duplicate messages.

Deliver in order: accounts, roles, and Email; news and Media; citizen suggestions; classifieds and moderation; comments; Telegram, weather, and RSS. Apply security, tests, observability, and deployment preparation throughout. Keep the implementation maintainable and avoid speculative abstractions or infrastructure.

The owner defines scenarios and key decisions; AI implements agreed behavior and explains it. Specify the relevant workflow, permissions, contracts, and acceptance criteria before coding. Complete and review each workflow before moving to the next module. Track work in GitHub Issues and Projects.

## Stage Workflow

1. Before stage work, read its specification in `docs/stages/STAGE_X.md`, where `X` is the delivery stage number from CORE.md. Start with [Stage 1](docs/stages/STAGE_1.md).
2. Describe only the current stage. Create the next stage specification after the current stage is implemented, verified, and reviewed with the owner.
3. Each specification must contain the goal, scope, tools, ordered implementation steps, relevant workflows, permissions, contracts, acceptance criteria, open decisions, and implementation status.
4. Resolve open decisions with the owner before implementing the affected workflow. Implement the agreed specification and record progress and verification evidence in it.
5. When work changes an agreed requirement, contract, or technical decision, update every affected specification in the same work session. Update VALUE.md for product changes and CORE.md for architecture or delivery changes; keep stage specifications consistent with both. Confirm new business rules and features with the owner before implementing them.
6. Close the stage only when its acceptance criteria pass and the owner has reviewed the result. Record remaining limitations and the review outcome before describing the next stage. Keep GitHub Issues and Projects aligned with the stage specification.

## Communication

Respond to the owner in Russian, briefly and directly. Write code, comments, and documentation in English. Before the first reply in a session, read the `plain-writing` skill and apply it to written communication.
