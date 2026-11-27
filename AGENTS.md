# CityPortal

CityPortal is a portfolio and learning project for professional .NET backend development.

## Sources of Truth

- **Product behavior, scope, permissions, or content lifecycles.** Read [VALUE.md](docs/VALUE.md).
- **Architecture, implementation planning, or infrastructure.** Read [CORE.md](docs/CORE.md) for technical decisions and delivery order.
- **Local startup, verification, deployment, or recovery.** Use the procedures indexed in [DEVELOPMENT.md](docs/DEVELOPMENT.md).
- **C# implementation or review.** Read [CODE_STYLE.md](docs/CODE_STYLE.md) and follow the root [`.editorconfig`](.editorconfig).
- **UI design or styling.** Read [UI_STYLE.md](docs/UI_STYLE.md).

## Stage Workflow

1. Before working on a stage, open its specification from the delivery order in [CORE.md](docs/CORE.md#delivery-order). Specify the current stage's goal, scope, tools, implementation order, workflows, permissions, contracts, acceptance criteria, open decisions, and status.
2. The owner defines scenarios and key decisions. Agree on the workflow and acceptance criteria before coding. Resolve open decisions and obtain owner approval for new business rules and features before implementing them.
3. Implement and explain the agreed behavior. Include security, tests, observability, and deployment checks as relevant. Complete and review each workflow before moving to the next module. Record progress and verification in the stage specification and track work in GitHub Issues and Projects.
4. In the same session as a requirement, contract, or technical decision changes, update every affected specification. Update [VALUE.md](docs/VALUE.md) for product changes and [CORE.md](docs/CORE.md) for architecture or delivery changes.
5. Close the stage after its acceptance criteria pass and the owner reviews the result. Record the review outcome and remaining limitations, and update GitHub Issues and Projects.
6. Immediately after closing a numbered stage, start a new agent session to review all of `docs/` without waiting for a request. Fix stale and conflicting text, consolidate duplicate rules in their owning documents, and move reusable procedures to the development guides. Preserve approved requirements, acceptance evidence, and limitations. Verify local links and anchors before specifying the next stage.

## Optimization and Refactoring

Read [OPTIMIZATIONS.md](docs/OPTIMIZATIONS.md) before revisiting an optimization or refactor. In the same session as implementation, record its date, scope, reason, change, and verification there. Include before-and-after measurements and their conditions when measured. Keep current contracts and procedures in their owning documents and link to the log for history and evidence.

## Workspace Cleanup

Before finishing, stop task-owned processes and remove temporary files, test fixtures, disposable databases, broker vhosts, and object buckets. Keep diagnostic artifacts while investigating an unresolved failure. Preserve active stand data, credentials, required SDKs, and dependencies.

Record durable verification results in the existing project documentation, then remove completed task specs from `docs/temp_tasks/`.

## Communication

Respond to the owner in Russian, briefly and directly. Write code, comments, and documentation in English. Before the first reply in a session, read the `plain-writing` skill and apply it to written communication.
