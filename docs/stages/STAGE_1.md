# Stage 1: Accounts, Roles, and Email

## Status and Sources

Status: stage 1 business decisions agreed with the owner; step 2 application foundation implemented, automatically verified, and independently reviewed. Steps 3-6 and owner completion review remain pending.

Product requirements come from [VALUE.md](../VALUE.md), and technical requirements come from [CORE.md](../CORE.md). Follow the stage workflow in [AGENTS.md](../../AGENTS.md#stage-workflow). This document describes stage 1 only. Business rules below were agreed through the owner interview; concrete API and storage conventions translate those rules into an implementation plan.

## Goal

Deliver working account registration, authentication, Email confirmation, password recovery, and administrative role management through the API and Angular interface. Establish the Identity and Notifications modules and durable Email delivery so later workflows can use authenticated accounts and reliable background processing.

## Scope and Approved Behavior

- Register with Email, password, and a separate display name. Staff register as ordinary users before receiving staff roles.
- Support login, logout, and access to the current account through cookie authentication and CSRF protection.
- Confirm Email through a link. Later content workflows must require confirmed Email before creating advertisements, citizen suggestions, or articles.
- Recover passwords through a one-time Email link.
- Support Regular User, Author, Editor, Moderator, and Administrator roles on unified accounts with multiple roles. Administrators assign author, editor, and moderator roles.
- Enforce current effective permissions in the API. A committed role removal must affect protected requests that begin afterwards, even with an existing cookie.
- Persist outgoing Email work with the initiating business transaction. Registration can succeed with an unconfirmed account while RabbitMQ or the Email provider is unavailable, provided PostgreSQL and the local transaction are available. User-facing delivery states and queue presentation are specified below.
- Resume queued delivery after recovery. Duplicate message handling must not repeat business changes; external Email delivery can occasionally repeat after a failure.
- Confirmation and password-reset messages are mandatory service messages, independent of optional notification switches.
- Include the account and administrative screens needed for these workflows and history for significant role-management actions.

News, Media, citizen suggestions, classifieds, comments, and their notification events belong to later stages. Account and advertisement blocking are delivered together in stage 4; stage 1 authorization must remain compatible with the blocking requirements in CORE.md. Optional notification settings are outside stage 1; the agreed profile scope is recorded below.

## Tools and Infrastructure

| Area | Use in this stage |
| --- | --- |
| Backend | C#, .NET 10, ASP.NET Core Web API Controllers, ASP.NET Core Identity. |
| Data | PostgreSQL and EF Core; separate Identity and Notifications ownership, schemas, DbContexts, and migrations. |
| Frontend | One Angular application; account and administrative areas render in the browser behind the same HTTPS origin as `/api/`. Prepare the application for public SSR in stage 2. |
| Background work | Separate .NET Worker, Wolverine, RabbitMQ, EF Core Transactional Outbox/Inbox integration; Quartz.NET with PostgreSQL persistence for the delivery-retention job. |
| Email | MailKit SMTP sender, configurable SMTP server and sender identity, and Mailpit for local mail capture and inspection. |
| API contract | Explicit DTOs, consistent errors, OpenAPI, Scalar, generated TypeScript client. |
| Verification | xUnit, Testcontainers with PostgreSQL, actual RabbitMQ integration checks, and selected Angular/API end-to-end scenarios. |
| Operations | Docker Compose, GitHub Actions, Linux-compatible packaging, structured logs, OpenTelemetry, and health checks. |

Pin compatible stable dependencies when creating the solution. Redis and S3-compatible storage remain selected project infrastructure; introduce their feature-specific use when a workflow requires it. Quartz.NET serves the concrete delivery-retention job in this stage. This stage does not introduce content scheduling, caching, or media workflows.

## Implementation Steps

### 1. Agree on Account Workflows

Use the agreed rules, permission matrix, state models, and contracts below to prepare workflow tasks. Discuss any newly discovered business decision with the owner and update this specification before implementing the affected behavior.

Completion: the affected workflow has no unresolved decision that changes its behavior, permissions, or acceptance criteria. Record the agreement here before coding that workflow.

### 2. Establish the Application Foundation

Create the API, Worker, Angular application, Identity and Notifications module projects, and the required test projects. Start with one main project per module and extract contracts only when dependency direction requires them. Configure local PostgreSQL, RabbitMQ, and Email development infrastructure, one-origin routing, migrations, API documentation, and TypeScript client generation.

Establish automated build and test checks, health checks, structured logs, and secret configuration. Document local startup and explicit migration execution.

Apply [CODE_STYLE.md](../CODE_STYLE.md) and the root `.editorconfig`. Enable nullable reference types and build-time style diagnostics for the .NET projects, and add a solution formatting check to CI. Verify the selected rules against real project code; review conventions without built-in enforcement manually.

Completion: the application starts through the documented local procedure, migrations initialize an empty database, and the initial automated checks pass.

Foundation implementation: `CityPortal.slnx` contains API, Worker, Identity, Notifications, and integration-test projects. Module DbContexts remain internal. Public module operations register services and apply owned migrations. The API `--migrate` command is an explicit deployment step; ordinary startup never migrates. Notifications initially owns its schema and migration history, with delivery records and handlers introduced in step 3.

The read-only foundation endpoint GET `/api/system/info` returns `{ "name": "CityPortal", "stage": 1 }` and has OpenAPI operation ID `getSystemInfo`. Angular uses its generated TypeScript client to display API connectivity. The temporary account and staff routes explain that their workflows are not available. No account or staff data is exposed. OpenAPI and Scalar are available only in Development.

Both .NET processes expose `/health/live` and `/health/ready`. Liveness does not depend on external services; readiness requires PostgreSQL connectivity, applied module migrations, and a successful RabbitMQ connection. JSON logs and OpenTelemetry instrumentation are configured, with OTLP export enabled when an endpoint is configured. [DEVELOPMENT.md](../DEVELOPMENT.md) documents Compose, local credentials, HTTPS, migrations, and checks. Wolverine delivery, SMTP sending, Quartz retention, persistent protection keys, and authenticated proxy handling remain work for the account workflows.

### 3. Deliver Registration and Email Confirmation

Implement registration end to end, including validation, persisted unconfirmed accounts, durable confirmation messages, Worker delivery, confirmation links, the agreed loading/error/sent presentation, and profile-only resend. Verify transaction rollback, infrastructure outages, restarts, and duplicate message processing against real infrastructure.

Completion: a user can register and confirm Email through the interface; pending work survives outages and resumes without repeating account changes.

### 4. Deliver Authentication and Password Recovery

Implement login, logout, current-account retrieval, CSRF handling, protected account access, and password recovery in the API and interface. Apply the agreed session, token, abuse-control, and response policies.

Completion: the agreed success and failure scenarios pass, including unauthorized requests, invalid CSRF tokens, and invalid or reused recovery links. Passwords, cookies, and confirmation or recovery links do not appear in ordinary logs.

### 5. Deliver Administrative Role Management

Implement the agreed initial-administrator setup, account lookup, role assignment and removal, and the administrative interface. Preserve personal access for accounts with staff roles. Record the responsible actor for role changes and enforce effective permissions on subsequent protected requests.

Completion: authorized administrators can perform the agreed operations; other users cannot; removing a role takes effect for an existing authenticated session as required by CORE.md.

### 6. Verify and Review the Stage

Run the complete stage acceptance scenarios, including browser flows and background outage/recovery cases. Verify persistent data and message recovery after restart, migration execution, and the deployment procedure relevant to this stage. Record results, limitations, and remaining work here and in GitHub tracking.

Review data flow, transaction boundaries, authorization, and a changed requirement or failure scenario with the owner.

Completion: all agreed acceptance criteria pass, required documentation matches the implementation, and the owner has reviewed the result. Then mark the stage complete and describe stage 2.

## Agreed Registration and Access Rules

- Email is unique without regard to letter case. Trim leading and trailing whitespace before validation and lookup. If the address is already registered, tell the visitor and offer login or password recovery.
- Display names contain 3-20 characters after trimming leading and trailing whitespace. Display names need not be unique; internal account IDs identify users.
- Display names allow Unicode letters and digits only. Spaces, hyphens, underscores, and other characters are rejected after the agreed outer-whitespace trimming.
- Email addresses contain at most 254 characters after trimming. Validate address syntax; Email confirmation verifies control of the address.
- Passwords contain 8-30 characters from printable ASCII U+0021 through U+007E. Allowed characters are Latin letters A-Z/a-z, digits 0-9, and ASCII punctuation. At least one punctuation character is required; letters and digits are optional. Spaces and non-ASCII characters are rejected. Do not trim or otherwise transform passwords.
- Every registered account receives the Regular User role. Staff roles are added to it.
- Unconfirmed accounts can log in, view their own profile, request another confirmation Email, and log out. Content creation and staff operations require confirmed Email.

## Agreed Links, Sessions, and Login Protection

- Email confirmation links expire after 24 hours. Confirmation resend requests are limited to one per 60 seconds and at most five per hour per account.
- Password recovery links expire after one hour and can be used only once. Recovery requests return the same response regardless of whether the Email address is registered. Apply the same sending limits as confirmation: one per 60 seconds and at most five per hour per account. Requests for unknown addresses must preserve the agreed non-disclosing response behavior.
- A normal login uses a session cookie, intended to end when the browser closes, with a server-enforced maximum lifetime of 12 hours. Browser session restoration can preserve session cookies; closing a browser is not a guaranteed server-side revocation event.
- An explicit Remember Me option uses a persistent cookie with a fixed 30-day lifetime. Do not extend the lifetime automatically.
- Changing or resetting a password invalidates all existing sessions, including the current one. The user must log in again.
- After five failed login attempts for an account, prevent further login attempts for 15 minutes. This temporary login lockout is separate from moderation blocking.
- A successful login resets the account's failed-attempt counter. After the 15-minute login lockout expires, new attempts start with a cleared counter. Unknown Email and incorrect password return the same login message.
- Additional per-IP request limits are five registration requests per hour, 15 login requests per minute, and ten Email requests per hour across confirmation and recovery. Account-level limits apply in addition. Rate-limit feedback includes the wait time; recovery responses must still not reveal account existence.

### Link Replacement and Failure Behavior

- A new confirmation or recovery link invalidates the previous link for the same purpose. Reprocessing the same message does not create a new link.
- Link lifetimes start at creation. Before sending a queued message, skip links that have already expired or been invalidated. The user can request another link through the permitted interface.
- Reopening an already-used confirmation link reports that Email is already confirmed.
- An invalid or expired confirmation link reports the corresponding condition. Requesting a new confirmation link is available only in the authenticated profile, not on the link-result page.
- Used, invalidated, or expired recovery links cannot reset a password. Password recovery and requests for replacement recovery links remain available without login, including from the invalid/expired recovery-link result page. The profile-only resend restriction applies exclusively to Email confirmation.
- Changing a password in the profile requires the current password.
- Concurrent sessions on multiple devices are allowed. Logout invalidates only the current session; password change or reset invalidates all sessions.

### Concurrent Changes and Authorization

Use the explicit record version required by CORE.md. A conflicting update preserves the first committed change and returns HTTP 409 so the interface can reload current data. Recheck the acting administrator's effective permissions before committing a role change; an already-started request must not use revoked permissions to commit. Implement the permission recheck and write with transaction coordination so concurrent role revocation cannot bypass this rule.

## Agreed Administration and Profile Scope

- Provision the first administrator through a separate server command with no default password in source code. The command operates only while no administrator exists.
- Administrators can grant and revoke Author, Editor, Moderator, and Administrator roles, including their own roles. The last administrator cannot lose the Administrator role. Preserve Regular User on every account. Enforce the last-administrator invariant under concurrent changes.
- The profile shows Email, display name, confirmation status, and roles. Confirmed accounts can change their display name and password. Email changes, account deletion, and additional settings are outside stage 1.
- Role labels in the profile use Russian, user-facing names. Keep stable role identifiers in backend authorization and API contracts; map them to localized labels in the interface. The exact approved labels are maintained in VALUE.md, under Personal Account and Notifications.
- The administrator user list supports search by Email and display name, filters by role and Email confirmation, pagination, account details, and role changes.
- Role-change history records the actor, target account, timestamp, and roles added or removed. Administrators view it in the user's account details. Retain these records without automatic deletion.

## Agreed Email Delivery and Operations

- Send through configurable SMTP using MailKit. Use Mailpit for local development and mail inspection.
- Templates are in Russian and include HTML and plain-text versions, the configured portal name, intended action, link, and link lifetime.
- After a transient send failure, retry after 1, 5, 15, 30, and 60 minutes respectively, measured from the preceding failed attempt. After these five retries, retain the failure for investigation. Skip expired or invalidated links instead of sending them.
- A permanent SMTP rejection stops automatic send attempts immediately. Retain the cause for administrators and show the agreed error/resend presentation to the user.
- Account sending limits are separate for confirmation and recovery. The initial send and user-requested replacements consume the relevant limit; automatic delivery retries do not. Per-IP Email limits remain shared across both purposes.
- The profile shows the delivery result of the latest requested confirmation Email only. Outcomes for older messages cannot overwrite that result.
- Administrators can inspect queued deliveries and failures and replay delivery of a still-valid message. Record replay actions in administrative history.
- The user sees only two delivery states: Sent, or Error with an invitation to resend. The exact Russian interface labels are maintained in VALUE.md. Sent means the SMTP server accepted the message, not that it reached the inbox. While the first attempt is pending, show a loading indicator without a separate status. If the first attempt fails, show Error/resend while background retries continue. A later successful attempt changes the displayed state to Sent. Do not label a queued message as sent.
- Retain completed delivery records for 30 days. Retain unresolved failures until resolved. Retain administrative action history without automatic deletion.

## Permission Matrix

All HTTP writes require a valid CSRF token, including anonymous authentication operations. Confirmation and recovery links open a frontend page; the page obtains CSRF protection and submits the token through POST. Opening a GET link alone does not change account state.

| Operation | Anonymous visitor | Unconfirmed account | Confirmed account | Confirmed administrator |
| --- | --- | --- | --- | --- |
| Register, login, request/reset forgotten password | Public authentication flow | Same public recovery flow | Same public recovery flow | Same public recovery flow |
| Submit confirmation link | Valid link required | Valid link required | Valid/already-used link outcome | Valid/already-used link outcome |
| Read own profile, confirmation delivery result, logout | Denied | Allowed | Allowed | Allowed |
| Resend confirmation from profile | Denied | Allowed within limits | Already confirmed; no new message | Already confirmed; no new message |
| Change own name/password | Denied | Denied | Allowed | Allowed |
| Search/view other accounts, role history | Denied | Denied | Denied | Allowed |
| Change roles | Denied | Denied | Denied | Allowed with version and last-administrator checks |
| Inspect deliveries, replay valid failed work | Denied | Denied | Denied | Allowed; replay audited |

Author, Editor, and Moderator do not grant administrator operations. Regular User is retained on every account. Bootstrap administration is a server operation, not a public HTTP endpoint.

## State Models and Persistence

- Account confirmation is Unconfirmed or Confirmed. Login lockout is a separate `lockoutUntilUtc` and failure counter, not a moderation block. Staff roles do not bypass confirmation.
- Sessions have a server-verifiable identifier, account association, absolute expiry, and revocation state. Logout revokes the current identifier; password changes revoke all existing identifiers. API requests check current effective roles rather than trusting old cookie claims.
- Link records have an opaque identifier, purpose, account, creation/expiry timestamps, and Active/Consumed/Invalidated state. Expiry is checked against UTC on use, not just by cleanup. Store a verifier rather than a reusable raw token in Identity. New links invalidate the previous active link of the same purpose transactionally.
- A consumed confirmation link can return AlreadyConfirmed when its account is confirmed. Invalidated or malformed tokens do not become valid merely because an account was subsequently confirmed.
- Notifications delivery states are Queued, Sending, RetryScheduled, Sent, Failed, and Skipped. Sent means SMTP acceptance. Failed retains permanent/exhausted errors; Skipped records expired or invalidated links. Attempts record timestamps, sanitized causes, and next-attempt time. Recover abandoned Sending work after Worker restart.
- The profile projects the latest confirmation request as loading before its first attempt completes, Sent after SMTP acceptance, or Error after a failure while retries may continue. Internal scheduling states are not additional user-visible labels.
- Count display-name length in Unicode scalar values after outer trim and NFC normalization; allow Unicode letters and decimal digits. Password length is unambiguous ASCII length and passwords are never normalized.
- Use stable opaque IDs, UTC timestamps, and explicit record versions for mutable account/admin views. The database enforces normalized Email uniqueness.

## API Contract

All routes below are relative to `/api`. JSON uses camelCase. Return explicit DTOs, never EF entities. Account DTOs contain `id`, `email`, `displayName`, `emailConfirmed`, `roles`, and `version`; the profile also includes the latest confirmation delivery projection. Passwords, token verifiers, and mail bodies are never returned in account/admin list DTOs.

| Method and route | Request | Success |
| --- | --- | --- |
| GET `/auth/csrf` | None | 200, CSRF token for the request header. |
| POST `/auth/register` | `email`, `displayName`, `password` | 201, account created and confirmation work persisted; login remains a separate operation. |
| POST `/auth/login` | `email`, `password`, `rememberMe` | 200, account DTO and authentication cookie. |
| POST `/auth/logout` | None | 204, revoke current session and clear cookie. |
| GET `/account` | Authenticated session | 200, own profile DTO. |
| PATCH `/account/display-name` | `displayName`, `version` | 200, updated profile DTO. |
| POST `/account/change-password` | `currentPassword`, `newPassword`, `version` | 204, all sessions revoked; clear current cookie. |
| POST `/account/email-confirmation` | Authenticated unconfirmed session | 202, new confirmation work persisted. |
| GET `/account/email-confirmation` | Authenticated session | 200, latest delivery projection and resend wait time. |
| POST `/auth/confirm-email` | `linkId`, `token` | 200, Confirmed or AlreadyConfirmed result. |
| POST `/auth/forgot-password` | `email` | 202, generic response independent of account existence/account-level throttling. |
| POST `/auth/reset-password` | `linkId`, `token`, `newPassword` | 204, consume link, change password, revoke all sessions. |
| GET `/admin/accounts` | `query`, `role`, `emailConfirmed`, `page`, `pageSize` | 200, paginated account summaries. |
| GET `/admin/accounts/{id}` | Account ID | 200, account details and version. |
| PUT `/admin/accounts/{id}/roles` | Desired complete `roles` set, `version` | 200, updated account; always retain Regular User. |
| GET `/admin/accounts/{id}/role-history` | Pagination | 200, actor/target IDs, timestamp, old/new roles. |
| GET `/admin/email-deliveries` | `state`, `page`, `pageSize` | 200, delivery metadata and sanitized errors. |
| POST `/admin/email-deliveries/{id}/replay` | `version` | 202, requeue valid failed work and record actor. |

List contracts default to page 1 and 20 items, cap pageSize at 100, and bound account-search queries to 254 characters. Use deterministic ordering by creation time and ID. The generated OpenAPI document and TypeScript client must reflect these contracts.

Use Problem Details with `status`, `title`, `code`, `traceId`, and field `errors` for validation. Use HTTP 400 for validation/CSRF/invalid or expired links; 401 for missing/expired sessions or generic invalid login; 403 for insufficient role or confirmation; 404 for unavailable admin resources; 409 for version conflicts, duplicate Email, or last-administrator protection. Directly address invalid and expired confirmation links without a resend control on that page. Account login lockout and public request limits use 429 with `Retry-After` and a wait time. Password recovery conceals account-level suppression behind the generic 202; an IP limit can return 429 uniformly for known and unknown addresses.

Apply rolling request windows using server UTC; enforce limits atomically across API instances. Count IP registration/login/Email requests even if they fail validation. Count account sending allowance only when a new link is committed; keep confirmation and recovery counters separate. Unknown recovery addresses follow the same response shape and do not send mail. Automatic delivery retries do not consume account or IP user-request quotas. Accept client IP information only from configured trusted proxies.

## Module Contracts and Transaction Boundaries

Identity owns accounts, credentials, roles, sessions, link validity, and role history. Notifications owns delivery metadata, attempts, retry scheduling, and replay history. Future optional channel settings remain a Notifications responsibility but are outside this stage's UI.

- Registration synchronously creates the account, assigns Regular User, creates a confirmation link, and persists the corresponding delivery intent/Outbox message in one PostgreSQL transaction.
- Expose explicit module operations for queuing account mail, checking link validity before send, reading the latest confirmation delivery projection, and querying/replaying administrative deliveries. Expose DTOs rather than another module's DbContext or tables.
- The account-mail envelope carries `messageId`, `accountId`, `purpose`, `linkId`, `createdAtUtc`, and `expiresAtUtc`, plus the recipient and protected delivery payload. Keep the link secret recoverable only for delivery through protected persisted data; exclude it from logs, traces, admin lists, and error queues intended for routine inspection. Manage protection keys outside source control and preserve them across restarts.
- Coordinate participating DbContexts through the shared DbConnection/DbTransaction specified in CORE.md. Verify this with the real PostgreSQL provider and Wolverine Outbox before relying on it. Broker/provider failure cannot undo a committed local transaction.
- User resend atomically enforces quota, invalidates the previous same-purpose link, creates a new link, and persists its delivery work. Rollback restores the old valid link if the replacement transaction fails. A duplicate handler cannot mint another link.
- Confirmation consumes a valid link and confirms the account in one transaction. Reset consumes the recovery link, updates the password, and revokes all sessions atomically. Concurrent consumption has one winner; reopening a successfully consumed confirmation link is informational.
- Role writes, version checks, acting-user permission rechecks, last-administrator protection, and history commit atomically. Serialize the relevant administrator membership changes so concurrent requests cannot remove the last administrator or bypass a committed revocation. Self-removal is evaluated against permissions held before that write.
- SMTP acceptance and database acknowledgement cannot form one transaction; retain the acknowledged possibility of duplicate Email after a crash. Use message IDs and Inbox processing to avoid duplicate durable business effects. Recheck link validity immediately before the send; an in-flight old Email can still arrive after replacement but its link must fail validation.
- Replays operate on still-valid Failed deliveries, reject concurrent/terminal replay conflicts, reuse the same link, reset the bounded attempt schedule, and record the administrator. Replays do not create replacement links or overwrite the latest-confirmation pointer.
- Retention cleanup is durable background work in the Worker. Use the selected Quartz.NET/PostgreSQL scheduling infrastructure when implementing this retention job; it does not introduce scheduled content publication.

## Initial Administrator Command

Provide a server-side bootstrap command that accepts the Email of an existing confirmed account and promotes it to Administrator while retaining Regular User. It receives no default password and exposes no public endpoint. In one coordinated transaction, verify no administrator exists, serialize bootstrap attempts, grant the role, and record a system/bootstrap actor and target in history. Refuse unconfirmed/missing accounts and refuse subsequent runs while any administrator exists. This uses the normal registration/confirmation flow before bootstrap and avoids storing a bootstrap password.

## Implementation Backlog and Remaining Setup

The owner interview resolved the stage 1 business questions. Track implementation in the order of steps 2-6 above, splitting each workflow into reviewable tasks with acceptance examples from this document. The repository is [Pikatoise/city-portal](https://github.com/Pikatoise/city-portal), with `main` as its default branch.

Implementation tracking is established in [GitHub Project 3](https://github.com/users/Pikatoise/projects/3): [foundation #1](https://github.com/Pikatoise/city-portal/issues/1), [registration and confirmation #2](https://github.com/Pikatoise/city-portal/issues/2), [authentication and recovery #3](https://github.com/Pikatoise/city-portal/issues/3), [administration #4](https://github.com/Pikatoise/city-portal/issues/4), and [stage acceptance #5](https://github.com/Pikatoise/city-portal/issues/5). Implement these in order; foundation implementation and independent review are complete, with owner review pending. [DEVELOPMENT.md](../DEVELOPMENT.md) records foundation commands and configuration. Document concrete protection-key management, SMTP delivery, authenticated proxy handling, and scheduling integration with their account workflows. These are engineering setup tasks, not unresolved approval of the business rules.

If implementation reveals a new business question or requires changing these contracts, agree the affected behavior with the owner and update the owning specifications in the same session.

## Open Business Decisions

None remain from the stage 1 interview. Remaining engineering setup is listed above. The stage itself is not complete until application acceptance checks and the owner's completion review pass.

## Stage Acceptance Criteria

- Registration, confirmation, login, logout, recovery, and agreed role-management operations work through the Angular interface and API.
- API authorization rejects unauthorized access; CSRF protection rejects invalid state-changing requests; current role removal takes effect on subsequent protected requests.
- The agreed unconfirmed-account restrictions are enforced and exposed accurately in the interface.
- Recovery links are one-time; invalid and expired links follow the agreed behavior.
- Registration enforces the agreed field lengths and password character rules; Email lookup ignores case and display names may repeat.
- Display names accept only letters and digits and reject internal spaces, hyphens, and underscores; Email addresses are limited to 254 characters. Per-IP limits enforce five registrations/hour, 15 login requests/minute, and ten Email requests/hour alongside account-level limits. Recovery and login responses preserve the agreed account-existence protections.
- Confirmation links expire after 24 hours and recovery links after one hour; sending limits enforce the agreed 60-second and five-per-hour bounds.
- Issuing a new link invalidates the previous one for the same purpose; duplicate processing does not generate a replacement. Expired or invalidated queued links are skipped before sending. Invalid/expired confirmation pages report the condition, and confirmation resend is available only in the profile.
- Already-used confirmation links report an already-confirmed account. Profile password changes require the current password; concurrent sessions are allowed, and logout revokes only the current session.
- Normal sessions expire within 12 hours and Remember Me sessions within a fixed 30 days. Password change or reset revokes all existing sessions. Five failed login attempts trigger a 15-minute account login lockout.
- Account creation and outgoing confirmation work commit or roll back together. RabbitMQ or Email outages leave committed work recoverable; the interface follows the agreed delivery presentation without claiming an unsent message was sent.
- Worker and broker recovery resumes durable work. Duplicate messages do not repeat business changes; bounded retries and exhausted-delivery handling are verified.
- Permanent SMTP failures stop automatic attempts immediately. Account limits are separate per purpose and include initial/user-requested mail; automatic retries do not consume user quotas. Only the latest requested confirmation message controls its profile status.
- Administrative changes retain the responsible actor. Sensitive authentication data does not enter ordinary logs.
- The first-administrator command refuses to run when an administrator exists. Concurrent role changes cannot remove the last administrator or any account's Regular User role.
- Confirmed users can change their own display name and password. The profile shows the agreed fields and Russian role labels; administrators can search/filter accounts and inspect retained role-change history.
- Migrations, automated tests, browser checks, health checks, delivery diagnostics, and local/deployment instructions cover the delivered workflows. Record actual commands and results after implementation.
- C# follows CODE_STYLE.md; solution formatting and configured build-time style checks pass. Review also covers single-statement braces, method signature layout, conventional constructors, Async naming, and cancellation propagation where tools do not enforce them.
- The owner has reviewed the delivered workflows and their explanation; every remaining limitation is recorded before closing the stage.

## Progress and Verification

### Concrete Acceptance Examples

| Scenario | Required result |
| --- | --- |
| Register a 2-character or 21-character display name; use an internal space, hyphen, or underscore. | Validation rejects it; a 3-20-character name made of letters/digits is accepted and may duplicate another display name. |
| Use a 7-character or 31-character password, a space, a non-ASCII character, or no punctuation. | Validation rejects it. An 8-30-character printable ASCII password with punctuation passes the password policy. |
| Register an existing Email using different case or outer whitespace. | The normalized uniqueness check rejects the duplicate and offers login/recovery. |
| Use an unconfirmed session for profile reading versus profile editing/admin operations. | Reading and confirmation resend work; name/password editing and staff operations are forbidden. |
| Send a sixth account confirmation request within an hour, or request again before 60 seconds. | No new link is created; return the agreed wait information. Recovery has a separate account allowance but shares the ten/hour IP Email limit. |
| Make a sixteenth login request within a minute from one IP, or an eleventh mail request within an hour. | The shared IP limiter rejects the request with wait information regardless of account existence. |
| Request recovery for known and unknown Emails, including an account-specific suppressed send. | The public response remains generic and equivalent; only a permitted known-account request creates delivery work. |
| Replace a valid link, then deliver or open the old one. | Old queued delivery is skipped if detected before send, and the invalidated link cannot change account state. |
| Open a confirmation link after 24 hours or a recovery link after one hour. | Expired result; confirmation resend exists only in the profile, while recovery remains publicly available. |
| Reset a password twice with the same link, including concurrent submissions. | Exactly one password change succeeds; all pre-existing sessions are revoked. |
| Remove another administrator's role while their role-change request is in flight. | That request cannot commit after the revocation wins the coordinated authorization check. |
| Concurrently remove the last two administrators or bootstrap twice. | At least one administrator remains; at most one bootstrap succeeds. |
| Fail a registration transaction after account insertion but before delivery intent persistence. | Neither the account nor its outgoing work commits. |
| Stop RabbitMQ/Worker or the SMTP receiver after a valid registration. | The account and durable mail intent remain; delivery resumes after recovery and the interface follows loading/error/sent rules. |
| Fail SMTP transiently, reject permanently, or complete an older request after a replacement. | Use bounded retries for transient failure, stop immediately for permanent failure, and retain the latest-request profile status. |
| Replay the same broker message or restart between SMTP acceptance and acknowledgement. | No duplicate account/link business change; a duplicate Email remains possible and is not presented as exactly-once delivery. |
| Clean up completed deliveries after 30 days. | Completed records are eligible for cleanup; unresolved errors and administrative history remain. |

### Current Progress

- Specification: owner interview complete; agreed rules and implementation contracts recorded.
- Implementation: step 2 foundation implemented. Registration, authentication, recovery, role management, and durable Email delivery are not implemented yet. Step 3 is next.
- Documentation verification: foundation setup, explicit migrations, pinned tools, credentials, HTTPS, and operational limits are documented. Outgoing source was checked for credentials and local user paths; local credentials and certificates remain ignored.
- Application verification: 10 backend tests and two frontend tests passed; commands and evidence are recorded below. CI is configured, but no remote Actions run is claimed because these commits have not been pushed.
- Independent review: separate agents reviewed specification compliance and coding standards after implementation. Credential alphabet guidance and the DTO constructor were corrected. Tests now check that neither module migrates at startup and that either module's pending migrations block readiness. Both reviewers confirmed their findings resolved.
- Limitations: visual browser validation could not run because the connected browser/native automation host was unavailable. Automated frontend tests and actual HTTPS SSR/API checks passed. Local browser certificate trust remains a documented manual choice; system trust was not changed. Account security, durable delivery, and scheduling guarantees are not delivered by this foundation.
- Owner completion review: pending.

### Foundation Verification (2026-11-11)

| Check | Result |
| --- | --- |
| `dotnet build CityPortal.slnx --configuration Release --no-restore --no-incremental` | Passed with zero warnings/errors before review; incremental Release build after DTO/test corrections also passed with zero warnings/errors. |
| `dotnet format CityPortal.slnx --verify-no-changes --no-restore` | Passed. Build-time style checks also caught generated migration namespace/unused-using violations during implementation; those files were formatted before completion. |
| `dotnet test tests/CityPortal.IntegrationTests/CityPortal.IntegrationTests.csproj --configuration Release` | Initial project discovery and execution verified; final solution command below verifies the reviewed test set. |
| `dotnet test CityPortal.slnx --configuration Release --no-build` | Passed: 10, Failed: 0, Skipped: 0. Real PostgreSQL and RabbitMQ run through Testcontainers. |
| `npm.cmd run build`, `npm.cmd run format:check`, `npm.cmd run api:check` in `frontend` | Passed. Angular production SSR build, source formatting, and generated-client consistency. |
| `npm.cmd test -- --watch=false` in `frontend` | Passed: 2, Failed: 0. Generated-client connection success, failed request, and retry behavior. |
| `docker compose config --quiet`, `docker compose build`, `docker compose run --rm --no-deps api --migrate` | Passed. Explicit migrations initialize empty PostgreSQL schemas; repeat execution preserves existing data. |
| Compose readiness, HTTPS, and contract smoke | All seven containers run. API and Worker readiness return 200. HTTPS API and SSR homepage return 200 with CA and hostname verification; `/account` returns `Cache-Control: no-store`. Live OpenAPI matches `frontend/openapi.json` using `scripts/check-openapi.py`. |
| RabbitMQ outage and recovery | Readiness returns 503 during the outage while liveness returns 200; readiness returns 200 after AMQP recovery. No account/delivery outage guarantee is inferred. |
| Workflow syntax | `actionlint` passed for `.github/workflows/ci.yml`; CI runs build/format/tests, client drift checks, explicit migrations, and HTTPS smoke on Linux. Remote execution remains pending push. |

| Requirement | Evidence |
| --- | --- |
| Public metadata and development-only documentation | `SystemInfo_ReturnsOnlyPublicApplicationMetadataAsync`, `OpenApi_InDevelopment_DescribesSystemInfoContractAsync`, `ApiDocumentation_InProduction_IsUnavailableAsync`. |
| Dependency-independent liveness and structured errors | `Liveness_WhenDependenciesAreUnavailable_RemainsHealthyAsync`, `Readiness_WhenDependenciesAreUnavailable_ReturnsServiceUnavailableAsync`, `UnknownApiRoute_ReturnsProblemDetailsAsync`. |
| Explicit, repeatable module migrations | `ModuleMigrations_OnEmptyDatabase_CreateOwnedSchemasAndPreserveDataOnRepeatAsync`. |
| Both module migration gates and dependency failure/recovery | `Readiness_WhenDependenciesFailAndRecover_TracksAvailabilityWhileLivenessRemainsHealthyAsync` runs with both migration orders. |
| Angular connection loading, success, failure, and retry | `shows successful connection after the generated API request completes`, `allows a failed connection to be retried successfully`. |

Update this section as work proceeds. Include the completed step, verification evidence, unresolved limitations, and review outcome.

## Specification Changes

| Date | Change | Reason and affected specifications |
| --- | --- | --- |
| 2026-11-11 | Created the stage 1 specification and sequential stage workflow. | Owner requested one specification per stage, written before implementation; AGENTS.md and CORE.md record the workflow. |
| 2026-11-11 | Agreed registration and unconfirmed-account access rules. | Owner approved case-insensitive unique Email, duplicate-address guidance, non-unique display names, initial Regular User role, and restricted access before confirmation; set display names to 3-20 characters and passwords to 8-30 characters without spaces or Unicode, with at least one special character. |
| 2026-11-11 | Agreed exact password characters, link lifetimes, sending limits, session lifetimes, session revocation, and login lockout. | Owner confirmed printable ASCII passwords with mandatory punctuation, 24-hour confirmation links, one-hour one-time recovery links, one send per 60 seconds and five per hour, 12-hour normal sessions, fixed 30-day Remember Me sessions, all-session revocation after password change/reset, and a 15-minute login lockout after five failures. |
| 2026-11-11 | Agreed administrator provisioning, role management, profile editing, account lookup, and retained role history. | Owner approved the proposed scope and requested Russian role labels in the profile; VALUE.md records the user-facing behavior. |
| 2026-11-11 | Agreed link replacement, delivery expiry checks, link-result behavior, current-password verification, and concurrent sessions. | Owner restricted replacement confirmation-link requests to the profile and clarified that password recovery remains available without login. |
| 2026-11-11 | Agreed Email tooling, templates, retry delays, administrator replay, and delivery retention. | Owner limited user-facing states to Sent and Error/resend, then approved loading during the initial attempt and Error/resend after a failed attempt while retries continue. VALUE.md records the revised interface requirement. |
| 2026-11-11 | Agreed remaining field constraints, IP limits, login counter handling, and concurrent-change behavior. | Owner selected letter/digit-only display names, 15 login requests/minute/IP and ten Email requests/hour/IP; approved the other proposed rules, including five registrations/hour/IP, 254-character Email, non-disclosing login failures, conflict responses, and pre-commit authorization rechecks. |
| 2026-11-11 | Closed the owner interview and documented implementation contracts. | Owner approved permanent-failure handling, separate account mail limits including initial sends, automatic retry exclusions, and latest-confirmation status. Added API, permission, state, transaction, bootstrap, and verification contracts; synchronized relevant baseline documents. |
| 2026-11-11 | Added the owner-reviewed C# style baseline and stage checks. | Created .editorconfig and CODE_STYLE.md from the edited sample, including omitted braces for single statements and readable single-line signatures. AGENTS.md and CORE.md link the conventions; stage 1 scaffolding must add build/CI enforcement. |
| 2026-11-11 | Implemented and independently reviewed the application foundation. | Added API/Worker, Angular SSR host with CSR account/staff placeholders, module migrations, generated client, real infrastructure checks, Compose HTTPS, local setup, and CI. Recorded actual verification and the browser automation limitation; remaining account workflows and owner review keep stage 1 open. |

For subsequent changes, record the changed requirement or decision, its reason, and the affected specification sections. Update those sections in the same work session so the specification describes the current agreement.
