# Stage 1: Accounts, Roles, and Email

## Status and Sources

Status: complete. Implementation, technical verification and owner review are finished. On 2026-11-13 the owner accepted the revised first-run wizard, local-only keyless setup and setup-only page access, and authorized stopping the server, committing, completing GitHub tracking and pushing. Minor visual refinements remain deferred.

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

The read-only endpoint GET `/api/system/info` returns public `name`, `stage`, `city`, `region`, and `timeZone` metadata and has OpenAPI operation ID `getSystemInfo`. Angular uses its generated TypeScript client for connectivity and configured administrative timestamp display. Account workflows and administrative staff screens replace the original placeholders. OpenAPI and Scalar are available only in Development.

Both .NET processes expose `/health/live` and `/health/ready`. Liveness does not depend on external services; readiness requires PostgreSQL connectivity, applied module/messaging/scheduler schemas, and a successful RabbitMQ connection. JSON logs and OpenTelemetry instrumentation are configured, with OTLP export enabled when an endpoint is configured. [DEVELOPMENT.md](../DEVELOPMENT.md) documents Compose, local credentials, HTTPS, migrations, persistent protection keys, trusted proxy configuration, and checks. Step 3 adds Wolverine delivery, SMTP sending, and Quartz retention.

### 3. Deliver Registration and Email Confirmation

Implement registration end to end, including validation, persisted unconfirmed accounts, durable confirmation messages, Worker delivery, confirmation links, the agreed loading/error/sent presentation, and profile-only resend. Verify transaction rollback, infrastructure outages, restarts, and duplicate message processing against real infrastructure.

Completion: a user can register and confirm Email through the interface; pending work survives outages and resumes without repeating account changes.

Implementation dependency: step 3 includes login, logout, and profile reading so the agreed profile-only confirmation resend can be exercised end to end. These operations follow the already agreed step 4 session and protection rules. Password recovery and profile editing remain in step 4; administration remains in step 5. This sequencing does not change the approved behavior or stage scope.

### 4. Deliver Authentication and Password Recovery

Implement login, logout, current-account retrieval, CSRF handling, protected account access, and password recovery in the API and interface. Apply the agreed session, token, abuse-control, and response policies.

Completion: the agreed success and failure scenarios pass, including unauthorized requests, invalid CSRF tokens, and invalid or reused recovery links. Passwords, cookies, and confirmation or recovery links do not appear in ordinary logs.

Implementation: purpose-specific recovery links and quotas reuse the protected Notifications queue. Recovery links expire after one hour, are consumed atomically with password/session changes, and never confirm Email. Password change/reset also invalidates outstanding recovery links. Profile edits enforce confirmation, the current session, and the expected account version. Login, confirmation, profile edits, and password writes coordinate email/account locks in that order to avoid stale account writes. Angular provides anonymous recovery/reset pages, confirmed-profile forms, version-conflict reloads, and separate login after password changes.

### 5. Deliver Administrative Role Management

Implement the agreed initial-administrator setup, account lookup, role assignment and removal, and the administrative interface. Preserve personal access for accounts with staff roles. Record the responsible actor for role changes and enforce effective permissions on subsequent protected requests.

Completion: authorized administrators can perform the agreed operations; other users cannot; removing a role takes effect for an existing authenticated session as required by CORE.md.

Timestamp display follows CORE.md: `City:TimeZone` is deployment configuration with a neutral UTC development default. The public GET `/api/system/info` metadata includes `timeZone`; administration formats stored UTC timestamps in that configured zone. Configuration is read-only over HTTP; no city-settings screen or write permission is introduced. Reject invalid zone configuration at startup. Account lists order by creation time/ID; histories and deliveries show newest first with ID tie-breaking. Invalid pagination or filter values return 400.

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

Implementation tasks #1-#4, final acceptance #5 and first-run setup #6 are complete in [GitHub Project 3](https://github.com/users/Pikatoise/projects/3). The owner accepted the original workflows on 2026-11-12 and the revised wizard on 2026-11-13. Stage 1 is complete. [DEVELOPMENT.md](../DEVELOPMENT.md) records local setup and verification. No Stage 2 specification is included in this change.

If implementation reveals a new business question or requires changing these contracts, agree the affected behavior with the owner and update the owning specifications in the same session.

## Open Business Decisions

None remain for Stage 1. The owner approved the following first-run extension and accepted its delivered result.

### First-Run Setup Contract

New installations configure site name, city, optional region, IANA time zone, the first administrator's Email/display name/password, and SMTP host/port/sender/optional credentials. Email remains the login; the installer-created administrator is immediately confirmed and receives RegularUser and Administrator roles. SMTP supports STARTTLS, TLS on connection, or no TLS for a local receiver. A bounded 15-second SMTP test precedes the coordinated commit of account, bootstrap audit, public settings and protected SMTP configuration. SMTP acceptance is not proof of inbox delivery. Failure leaves setup retryable; concurrent completion has one winner and the completed wizard cannot reopen.

Setup needs no key. The owner restricts access to loopback, using an SSH tunnel for a remote host, and opens network access only after installation. Local Compose explicitly enables Installation:Enabled; the API defaults it to false. Public mode disables completion regardless of client-IP headers and remains unready until the installation marker exists. The public proxy waits for healthy API readiness before publishing ports 80/443. Its SSR allowlist includes only local hostnames and the configured SITE_DOMAIN; wildcard host configuration is rejected. CSRF protection and the ten-attempt/ten-minute IP limit remain enforced. Status exposes installed and canInstall, never credentials.

Before completion, only the wizard application page is available. Node redirects other page requests before SSR/CSR shell delivery; Angular loads shared installation state before rendering and guards every non-setup route. Home/account/staff navigation, linked branding and the normal footer are hidden. Status failures leave other pages closed. Assets and bootstrap API requests remain available. Successful completion updates shared state and restores navigation without a reload; normal login remains a separate action.

Identity owns the installation marker/public defaults beside initial-administrator provisioning. Notifications owns encrypted SMTP configuration and reads it for each delivery without restarting. Contexts share one transaction and setup uses the bootstrap/role advisory lock. Existing account databases are marked installed by migration without changing credentials or data; deployment metadata/mail defaults and CLI bootstrap remain available for them. Public metadata supplies name, city, region and timeZone. PUBLIC_ORIGIN is set before initial setup. Later settings editing, branding uploads, SEO and integration-specific settings remain outside this extension.

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
- Implementation: all Stage 1 workflows and first-run setup are complete and owner-accepted.
- Documentation verification: local setup, explicit EF/Wolverine/Quartz schema deployment, SMTP, persistent keys, trusted proxies, and verification commands are documented. Local credentials, private keys, and build/runtime artifacts are excluded from Git and image build contexts.
- Application verification: 67 backend tests and 39 frontend tests pass. The HTTPS smoke covers account workflows; administration integration tests use actual HTTP, PostgreSQL, RabbitMQ, SMTP, and the bootstrap command in an isolated process. Docker images, explicit/repeat migrations, and live OpenAPI comparison pass. Prior GitHub Actions for `cfe3503` and `74ce081` passed; current local evidence is below.
- Independent review: separate agents reviewed specification compliance and coding standards after implementation. Review found and resolved stale confirmation fragments, malformed-link presentation, missing-delivery loading after retention, and session revocation between authentication and profile lookup. Tests now prove the actual HTTP outage path and await duplicate handling rather than relying on a short delay. Both reviewers confirmed their findings resolved.
- Limitations: local browser checks used a scoped certificate exception with separate CA/hostname-verified HTTPS checks; system trust is unchanged. SMTP acknowledgement followed by database failure can still duplicate external Email, as agreed. Real-provider TLS and public DNS/certificate issuance remain deployment checks. Minor design refinements are deferred.
- Owner completion review: complete, with explicit acceptance of the revised wizard and page isolation on 2026-11-13.

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

### Registration and Confirmation Verification (2026-11-12)

The API coordinates Identity and Notifications using the same database connection and transaction. The Notifications EF Outbox stores a message containing only delivery ID and attempt number. The recoverable recipient/token payload is protected with shared persistent Data Protection keys; Identity stores a token verifier. The Worker uses RabbitMQ durable Inbox delivery, rechecks link validity before SMTP, and persists attempt outcomes and bounded retries. Quartz uses its explicit PostgreSQL schema migration for daily completed-delivery retention. Ordinary startup does not provision database schemas.

Confirmation links use URL fragments, which do not reach proxy request logs. The CSR page clears the fragment from history, observes replacement fragments, rejects malformed links, and requires an explicit POST action. Profile state follows only the latest confirmation request. Missing retained delivery records produce Error/resend rather than indefinite loading.

| Check | Result |
| --- | --- |
| `dotnet test CityPortal.slnx --configuration Release --no-restore --logger "trx;LogFileName=final-stage-one.trx" --results-directory .local/test-results` | Passed: 47, Failed: 0, Skipped: 0. Real PostgreSQL, RabbitMQ, and controllable SMTP; TRX counters verified. |
| `dotnet format CityPortal.slnx --verify-no-changes --no-restore` | Passed. Release API/Worker image publication and test compilation also pass build-time style checks. |
| `npm.cmd test -- --watch=false`, `npm.cmd run build`, `npm.cmd run format:check`, `npm.cmd run api:check` in `frontend` | Passed: 23 frontend tests, production build, formatting, and generated-client consistency. |
| `docker compose build`, explicit `api --migrate`, Compose startup | Passed. Existing local data retained; API and Worker readiness healthy. Frontend image rebuilt after the confirmation-page review fixes. |
| `python scripts/check-registration.py --ca .local/cityportal-root.crt` | Passed through the actual HTTPS origin: CSRF rejection, separate registration/login, secure session cookie, Mailpit HTML/plain-text mail, GET without confirmation, POST confirmation, informational repeated confirmation, profile, logout. |
| `python scripts/check-openapi.py frontend/openapi.json .local/final-openapi.json` | Passed against the running API. JSON-only body schemas and required CSRF headers generate the account client. |
| Independent review | Standards and specification reviewers confirmed their findings resolved. Owner completion review remains pending. |

| Requirement | Evidence |
| --- | --- |
| Field boundaries, Unicode scalars/NFC, exact ASCII password policy | `Registration_WithAllowedNameAndPassword_AcceptsExactValues`, `Registration_WithDecomposedLetters_NormalizesBeforeScalarLengthValidation`, `Registration_WithInvalidName_ReportsDisplayNameError`, `Registration_WithInvalidPassword_ReportsPasswordError`, `Registration_EmailLengthBoundary_IsEnforced`. |
| CSRF, normalized uniqueness, separate login, profile, confirmation/reopen, logout | `RegisterConfirmAndLogout_WithCsrfAndCookie_EnforcesAccountLifecycleAsync`. |
| Malformed requests, alternate route casing and spoofed proxy headers cannot bypass limits | `RegistrationRateLimit_WithMalformedAndAlternateCaseRoutes_CountsAcrossRequestsAsync`. |
| Atomic account/role/link/delivery/Outbox rollback | `Registration_WhenDeliveryInsertFails_RollsBackAccountRoleLinkAndOutboxAsync`, `Queue_WhenTransactionRollsBack_DiscardsDeliveryAndWolverineEnvelopeAsync`. |
| Actual HTTP registration/resend succeeds while broker unavailable | `RegistrationAndResend_WhenBrokerIsUnavailable_ReturnSuccessWithDurableWorkAsync`. |
| Replacement rollback, initial-send allowance, resend bounds, invalidation and expiry | `Resend_WithReplacementQuotaAndExpiry_PreservesOnlyLatestLinkAsync`. |
| One-time concurrent confirmation | `Confirmation_WhenSubmittedConcurrently_ConsumesOnceAndReturnsInformationalReplayAsync`. |
| Persisted lockout, absolute sessions, logout and concurrent quota enforcement | `Login_WithLockoutAbsoluteExpiryAndLogout_ValidatesPersistedSessionsAsync`, `RequestQuota_WhenContextsCompete_AllowsExactlyTheConfiguredCountAsync`. |
| Session revocation after authentication returns 401 on both profile reads | `ProfileReads_WhenSessionIsRevokedAfterAuthentication_ReturnUnauthorizedAsync`. |
| Protected payload, identifier-only envelope, broker dispatch/recovery, duplicate handler and Worker restart | `CommittedOutbox_WhenBrokerAndWorkerRecover_DeliversOnceAndSurvivesRestartAsync`. |
| Bounded transient retries and eventual success; permanent/exhausted failure | `Delivery_WhenTransientFailureOccurs_PersistsBoundedRetriesAndThenSucceedsAsync`, `Delivery_WhenFailuresArePermanentOrExhausted_StopsAutomaticAttemptsAsync`. |
| Expired/invalid links skipped before SMTP | `Delivery_WhenLinkIsExpiredOrInvalid_SkipsBeforeSmtpAsync`. |
| Resolved-only 30-day retention and post-cleanup profile projection | `Retention_WhenCompletedRecordsReachThirtyDays_DeletesOnlyOlderResolvedWorkAsync`, `GetLatest_WhenRetainedDeliveryHasBeenCleanedUp_ProjectsErrorInsteadOfPendingAsync`. |
| Invalid-link UI and same-route replacement with stale in-flight response | `does not submit an invalid confirmation link with %s`, `uses a replacement link on the same route and ignores an older in-flight confirmation`. |
| Explicit action, fresh CSRF/JSON requests and separate login | `removes the confirmation secret from history and requires an explicit POST action`, `fetches fresh CSRF protection across consecutive identity writes`, `creates an account with the exact password and asks for separate login without claiming mail sent`. |
| Latest-request delivery UI, resend, errors and authentication | `shows profile loading, failed mail and later SMTP acceptance without exposing internal queue states`, `does not let an older profile response overwrite the latest requested mail status`, `resends only from the profile and treats persisted work as loading until SMTP accepts it`, `keeps failed login feedback generic and clears the password`. |

### Password Recovery and Profile Editing Verification (2026-11-12)

| Check | Result |
| --- | --- |
| `dotnet test CityPortal.slnx --configuration Release --no-restore --logger "trx;LogFileName=stage-one-recovery-final.trx" --results-directory .local/test-results` | Passed: 54, Failed: 0, Skipped: 0; TRX counters verified. Real PostgreSQL, RabbitMQ, and SMTP. |
| `dotnet format CityPortal.slnx --verify-no-changes --no-restore` | Passed; Release compilation passes build-time style checks. |
| `npm.cmd test -- --watch=false`, `npm.cmd run build`, `npm.cmd run format:check`, `npm.cmd run api:check` | Passed: 31 frontend tests, production SSR build, formatting, and generated client consistency. |
| Docker API/Worker/frontend images and explicit migration | Passed; existing data retained and repeat migration succeeds. Existing links default to confirmation purpose. |
| `python scripts/check-registration.py --ca .local/cityportal-root.crt` | Passed through HTTPS and Mailpit: confirmation, version-checked name edit/stale rejection, one-time recovery, old-password rejection, change-password, revocation, separate login, logout. |
| Independent standards/specification review | Found and fixed undocumented null assumptions and login/profile-version concurrency. Both reviewers confirmed resolution. |
| Embedded browser | Recovery and reset pages render; reset fragment is removed before submission. Full HTTPS browser acceptance remains pending due to local CA rejection. |

| Requirement | Evidence |
| --- | --- |
| Confirmed-only edits, authorization, current password, version conflict, CSRF, all-session revocation and separate login | `ProfileEdits_WithVersionAndCurrentPassword_EnforcePermissionsAndRevokeEverySessionAsync`. |
| Purpose isolation, replacement, exact expiry, invalid tokens, one-time concurrent reset, preservation of unconfirmed status | `Recovery_WithReplacementExpiryAndConcurrentConsumption_PreservesConfirmationAndHasOneWinnerAsync`. |
| Unknown/suppressed 202 equivalence, independent five-per-hour quotas, shared IP quota and route casing | `Recovery_ForUnknownAndSuppressedAccounts_ReturnsSameResponseAndSeparatesMailQuotasAsync`. |
| Queue rollback preserves previous link; failed password write rolls back consumption and session revocation | `Recovery_WhenQueueWriteFails_RollsBackReplacementAndPasswordResetFailuresPreserveLinkAsync`. |
| First-writer version protection and post-authentication session recheck | `ProfileEdits_WhenConcurrent_KeepFirstVersionAndRejectStaleOrRevokedWritersAsync`. |
| Login and profile writes share the email/account lock order | `DisplayName_WhenLoginCompetes_SerializesBeforeCredentialSessionWriteAsync`. |
| Russian multipart recovery mail, recovery route and stated lifetime | `RecoveryMail_WhenDelivered_UsesRecoveryPageAndRussianMultipartTemplateAsync`. |
| Angular generic recovery response, explicit reset, secret/history cleanup, invalid/expired links, conflict reload and password completion | Eight cases in `frontend/src/app/account/recovery.spec.ts`; existing 23 frontend cases also pass. |

### Administration and Email Replay Verification (2026-11-12)

The Identity administration operation serializes bootstrap and role writes with an administration lock, then coordinates account locks with existing login/profile/password operations. Version checks, effective acting-user permissions, last-administrator protection and role audit commit together. Notifications owns replay metadata, locking and audit; the Identity coordinator verifies current administrator access and shares its database transaction with replay/Outbox writes. Replay resets `RetryCount`, while lifetime `AttemptCount` stays monotonic. The migration initializes existing retry budgets from their lifetime counts. Replay history has no cascading delivery reference and survives retention.

| Check | Result |
| --- | --- |
| `dotnet test CityPortal.slnx --configuration Release --no-restore --logger "trx;LogFileName=stage-one-administration-final.trx" --results-directory .local/test-results` | Passed: 67, Failed: 0, Skipped: 0; TRX counters checked. |
| `dotnet format CityPortal.slnx --verify-no-changes --no-restore` | Passed; Release compilation enforces configured style diagnostics. |
| Frontend tests, production build, formatting, generated client | 39 tests pass; SSR build, Prettier, and API drift checks pass. |
| Docker API/Worker/frontend images, explicit migrations and repeat | Passed; existing local data retained. |
| Certificate-verified HTTPS account smoke and live OpenAPI | Passed; new admin account route returns 401 for an anonymous request. |
| Review | Transaction/lock ordering, immutable audit ownership, secret-free DTOs, replay generation and frontend authorization/conflicts reviewed inline. Final independent/owner review remains in #5. |

| Requirement | Evidence |
| --- | --- |
| Bootstrap requires an existing confirmed account, refuses repeats, records system actor and exposes no default password | `Bootstrap_WithMissingUnconfirmedOrExistingAdministrator_RefusesAndAuditsOnlySuccessfulSetupAsync` invokes the actual server command in a separate process and checks exit codes. |
| Concurrent bootstrap creates one administrator | `Bootstrap_WhenConcurrent_ProvisionsExactlyOneInitialAdministratorAsync`. |
| Search/filter/page/detail contracts, current versions, preserved RegularUser, multi-role writes, CSRF, confirmed administrator access and retained actor history | `AdministrationHttp_WithSearchRolesVersionsAndCsrf_EnforcesMetadataContractsAndPermissionsAsync`. |
| Concurrent self-removal preserves the last administrator; existing cookies lose administrative access while keeping personal access | `Roles_WhenAdministratorsRemoveThemselvesConcurrently_PreserveLastAdministratorAndRecheckExistingSessionsAsync`. |
| Role membership, account version and audit roll back together | `RoleWrite_WhenAuditFails_RollsBackMembershipAndVersionAsync`. |
| Versioned replay queues the same link once, retains actor and latest-confirmation pointer, and hides mail secrets | `ReplayHttp_WithConcurrentRequests_QueuesSameLinkOnceAndAuditsWithoutExposingSecretsAsync`. |
| Expired/invalidated links, terminal deliveries, and failed audit cannot produce partial replay changes | Four `Replay_WhenInvalidOrAuditFails_DoesNotQueueOrRecordPartialChangesAsync` cases. |
| Replay starts a fresh retry budget, rejects old attempt envelopes, resumes SMTP, and retains audit after delivery cleanup | `Replay_WhenSmtpRecovers_RestartsRetryBudgetKeepsAttemptNumbersAndRetainsAuditAsync`. |
| Configured city-zone metadata, including Windows-to-IANA normalization | Two `SystemInfo_WithConfiguredCityTimeZone_ReturnsBrowserCompatibleIdentifierAsync` cases. |
| Angular filters, versioned writes, conflict reload, self-removal, replay/error presentation, and city-zone time display | Eight cases in `frontend/src/app/staff/administration.spec.ts`; all prior frontend cases also pass. |

### Final Stage Acceptance (2026-11-13, Issue #5)

The original foundation/account workflows and administrator delivery were verified, then the owner reviewed and accepted the final wizard and navigation changes. Technical evidence for setup and its subsequent revisions follows below. The newly added in-flight role revocation case and the staff SSR correction were also verified before completion.

| Requirement | Evidence |
| --- | --- |
| Revocation defeats an already-started role write without target changes or audit | Roles_WhenRevocationWins_RejectsAlreadyStartedWriteWithoutChangingTargetAsync; deterministic PostgreSQL blocking relationships, 403 and unchanged roles/version/history. |
| Staff pages use browser rendering and cannot enter shared SSR HTML | staff/** client route and check-registration.py shell checks against real HTTPS; pre-fix staff SSR was reproduced and corrected. |
| Account lifecycle, durable broker/Worker recovery, application restart and administration | Real HTTPS browser and Mailpit checks exercised registration during outage, recovery delivery, explicit/consumed confirmation, profile edits, recovery/password changes, revoked sessions, retained cookies after restart, administrator roles/history and SMTP replay. |
| Stage completion | The owner explicitly confirmed everything works and authorized stopping servers, committing, closing tracking and pushing on 2026-11-13. |

### Original First-Run Setup Verification (2026-11-13, Issue #6)

This is historical evidence for the original key-based version. The keyless and setup-only revisions below supersede its key/header and page-access requirements.

The owner approved Email login, immediate access for the first administrator without Email confirmation, and SMTP configuration in the wizard. The new setup follows the contract above. The API, Worker and Angular container images build; explicit migrations and normal startup succeed on the retained local installation. Existing data is preserved and its wizard stays closed. New public metadata includes city and region; the generated client and live contract match. CI now generates an installer key and runs the actual first-run workflow before the account smoke on its empty database. The same `check-registration.py --install` path passed locally against another empty database through CA-verified HTTPS, including the complete subsequent account lifecycle.

| Requirement | Evidence |
| --- | --- |
| Pending setup discloses only installed state, requires key/CSRF and blocks ordinary registration | `Setup_WhenPending_RequiresKeyCsrfAndExplicitCompletionAsync`; no SMTP connection or installation/account/audit/mail-config rows are created on rejected requests. |
| Concurrent completion creates one confirmed administrator and one installation; public and protected defaults persist | `Setup_WhenConcurrent_CreatesOneActiveAdministratorAndPersistsPublicAndProtectedDefaultsAsync`; one 204, one 409, one SMTP message, normal login/admin access, public city/name/zone and ciphertext excluding raw SMTP password/server. |
| SMTP failure and failure after Identity writes leave no partial setup; retry succeeds | Two `Setup_WhenSmtpOrStorageFails_RollsBackAllInstallationWritesAndCanRetryAsync` cases, including an actual PostgreSQL rejection trigger on Notifications storage. |
| Invalid time zone, password or site name cannot send SMTP or create partial data | Three `Setup_WithInvalidInput_DoesNotSendMailOrCreateAccountsAsync` cases. |
| Existing account schema upgrade closes setup without changing account credentials | `SetupMigration_WithExistingAccounts_PreservesAccountsAndClosesWizardAsync`; re-applies the actual migration and verifies normal login and deployment fallback fields. |
| Worker reads persisted SMTP server, public origin and brand across a new host | `Delivery_WithStoredSmtpConfiguration_UsesPersistedServerAndBrandAcrossNewHostAsync`; SMTP reaches the configured receiver rather than its different fallback receiver, and decoded MIME contains the persisted origin/name. |
| Installed UI hides the form; completion sends key only in a header, fresh CSRF and correct implicit-TLS flags, then updates branding | `Setup_WhenInstalled_HidesCredentialsAndCompletionForm`, `Setup_OnSuccess_UsesKeyHeaderFreshCsrfTlsAndUpdatesBranding`. |
| SMTP failure keeps inputs and retry requests get new CSRF; a concurrent winner closes the form | `Setup_OnSmtpFailure_RetainsInputsAndAllowsRetryWithFreshCsrf`, `Setup_WhenAnotherInstallerWins_ClosesWizardAndClearsCredentials`. |
| Actual HTTPS browser installation, SMTP failure/retry, immediate administrator login and public SSR branding | Local `.local/setup-acceptance/results.json`: `FreshInstallation_RedirectsToWizard`, `SmtpFailure_LeavesInstallationPendingAndRetainsInputs`, `Setup_ClosesPermanentlyAndRendersPersistedPublicBranding`, `FirstAdministrator_CanLoginImmediatelyWithoutEmailConfirmation`. |
| Already-running Worker uses new SMTP/branding; mobile page stays within viewport and has no unhandled errors | `RunningWorker_UsesWizardSmtpAndBrandingWithoutRestart`, `Browser_NoUnhandledErrorsOrMobilePageOverflow`; Mailpit receives a real recovery message with the configured brand and public HTTPS link origin. Mobile screenshot inspected. |

Verification results: the complete backend run passed 76 of 77 tests; the only failure was an assertion against raw MIME-encoded text. That assertion now decodes MIME and its focused rerun passes 1/1. TRX counters were parsed; no production behavior was changed for that repair. The current 77 distinct backend cases have passing evidence across those runs. All 43 frontend cases pass. Production SSR build, generated-client drift, Prettier, solution formatting, a clean Release build with zero warnings/errors, live OpenAPI comparison, and CA/hostname-verified HTTPS shell checks pass. Workflow YAML syntax/format was checked with Prettier. These are local results; no new remote CI result is claimed. The independent CA-verified setup/account smoke passed with `--install --smtp-host 127.0.0.1` against the isolated host API; CI uses the Compose default `mailpit`.

Independent backend review checked setup authorization, atomicity, concurrency, protected configuration and legacy behavior without finding a confirmed defect. The reviewer requested direct rollback/concurrency/migration evidence, now provided above. A second reviewer stopped at its usage limit; frontend/standards inspection was completed inline and through browser acceptance. No review result is claimed for that stopped run.

Browser acceptance used a separate empty PostgreSQL database and isolated application processes, with a browser-context certificate exception plus separate CA-verified HTTPS checks. Existing local accounts/volumes were retained. Tests used local SMTP without TLS; real-provider credentials and TLS connectivity remain deployment checks. Temporary credentials, keys, logs and screenshots remain ignored local artifacts. The original workflows remain owner-accepted; acceptance of the new wizard is still pending in #5 before stage closure. The temporary test processes and proxy were stopped after verification; the normal seven-service Compose site remains running.

### Keyless Local Setup Verification (2026-11-13)

The owner replaced the installation key with a local-first deployment workflow. The key field, header, environment option and CI key generation are removed. Local Compose enables setup and publishes only loopback ports. The API defaults setup to disabled. The separate `compose.public.yaml` disables setup, uses Production API/Worker settings and waits for API readiness before starting Caddy with public HTTPS on ports 80/443. The public configuration has been validated but was not applied to this machine.

| Requirement | Evidence |
| --- | --- |
| Local setup works without a key and still requires CSRF | `Setup_WhenPending_RequiresCsrfAndExplicitCompletionAsync`; concurrent completion and SMTP/storage rollback cases also run without any key header. |
| Public mode rejects setup regardless of forwarded client-IP claims | `Setup_InPublicMode_RejectsCompletionRegardlessOfForwardedLoopbackAddressAsync`: 403, no SMTP connection and no installation/account/audit/mail-settings writes. |
| Public mode cannot become ready before installation; liveness remains independent | Two `Setup_InPublicMode_BecomesReadyOnlyAfterInstallationWhileLivenessStaysHealthyAsync` cases: pending 503, installed 200, liveness 200 in both. |
| Public-mode UI has no setup form; enabled local form has no key field/header | `Setup_InPublicMode_HidesInstallationForm`, `Setup_OnSuccess_UsesFreshCsrfWithoutInstallationKeyAndUpdatesBranding`. |
| Existing installation and infrastructure paths remain valid | All 14 selected `InstallationTests` and `InfrastructureFoundationTests` cases pass; TRX counters parsed. |
| Frontend workflows remain valid | All 44 frontend tests pass; production SSR build, Prettier and generated API drift checks pass. |
| Local/public networking configuration is correctly separated | Parsed effective Compose configurations: every local published port binds 127.0.0.1; public proxy ports replace 8443 with only 80/443, API setup is false and proxy dependency is service_healthy. Caddy public configuration validates in a container with networking disabled. |
| Explicit public hostname works without allowing arbitrary hosts | Real built Node SSR request with configured Host returns 200 and SSR HTML; an unconfigured Host returns 400. No public socket was opened for this test. |
| Owner's live review instance needs no key and retains HTTPS CSRF | `https://localhost:8444/setup` renders the keyless form; status is installed=false/canInstall=true. CA-verified HTTPS request and real Edge rendering pass. Host API binds 127.0.0.1; host frontend now honors HOST and defaults to loopback. |

Solution formatting and whitespace checks pass. These are local checks; no public ports were opened and no new remote CI result is claimed. Existing databases and owner review data are retained. The main-site workflow and review instance remain local. Owner review of the revised wizard remains in #5 before stage closure.

### Setup-Only Page Access Verification (2026-11-13)

The owner required the wizard to be the only accessible application page during first-run setup. The home, personal-account and staff navigation links, linked logo and normal footer are hidden until setup completes. Static resources and the bootstrap API calls remain available. The implementation replaces the old one-time browser redirect with a shared installation state, a child-route guard covering every non-setup Angular route, and Node page-request gating before SSR/CSR shell delivery. Status errors leave other pages closed.

| Requirement | Evidence |
| --- | --- |
| Hide normal navigation/linked branding during setup and restore it after completion | `PendingSetup_HidesNavigationAndLogoLinkUntilCompletion`; the successful setup component test also verifies shared state becomes installed. |
| Direct in-app navigation cannot activate other application pages | `PendingSetup_RedirectsEveryNormalRouteBeforeItsPageCanRender` uses the actual route tree and exercises home, registration, recovery, profile, staff and unknown routes. |
| Unknown/failed installation status exposes neither other pages nor navigation | `FailedStatus_KeepsOtherPagesAndNavigationClosed`. |
| Completed installation allows normal account routes | `CompletedSetup_AllowsLoginPageWithoutRedirectingToWizard`. |
| Server-side isolation works without client JavaScript | Built Node server checks against a controlled HTTP status service: pending page requests return 302 to `/setup`, completed state restores SSR/home navigation and account shells, unavailable state returns 503 without login content. The wizard shell remains available for bootstrap/retry. |
| Actual owner-review instance remains pending and shows only the wizard | Real Edge and CA-verified HTTPS checks at 8444 confirm direct home/login/registration/staff redirects and absence of navigation, linked logo and footer. Owner review database was not completed or reset. |

All 48 frontend tests pass. Production SSR build, formatting, generated-client drift and workflow YAML checks pass. The smoke helper now initializes a fresh test site before expecting normal SSR/private shells and checks redirect isolation when used against a pending site; CI follows the initial redirect to the wizard shell. The main frontend image was rebuilt, and the review instance restarted on the same database. These are local checks, with no new remote CI run claimed. Owner review remains in #5.

### Owner Completion Review
Completed on 2026-11-13. The owner confirmed that the revised wizard works, accepted the setup-only page access and hidden navigation, and explicitly authorized stopping the local servers, committing all changes, completing task tracking and pushing. Stage 1 is closed. Prior pending-review and local-only evidence entries above are historical snapshots; this completion record supersedes their status. Minor design refinements remain deferred. No Stage 2 specification is included in this change.

On 2026-11-12 the owner accepted the currently delivered result, deferred minor design adjustments, and added the first-run setup requirement. This acceptance applies to the verified account/administration workflows above; it does not accept an unimplemented setup wizard or close the extended stage. Disposable local review credentials remain in `.local/browser/accounts.json`. Normal interactive browser access still requires accepting the local certificate warning or the documented manual trust setup.

The learning review should cover these concrete flows:

1. Registration stores the account, link and protected delivery intent in one coordinated database transaction. PostgreSQL rollback removes all of them; broker downtime leaves committed Outbox work recoverable.
2. The Worker sends Email after commit. Sent means SMTP acceptance. A crash after SMTP acceptance but before database acknowledgement can duplicate Email; one-time links and duplicate handling prevent repeating the account change.
3. Role writes serialize the effective-permission check, last-administrator protection, versioned membership write and retained actor history. The new in-flight revocation test demonstrates why checking permissions only before waiting would be insufficient.
4. Password change/reset revokes all persisted sessions. Logout revokes the current session. Client-side staff visibility accompanies API authorization; staff data is excluded from SSR and shared caches.

Owner acceptance is recorded above from the explicit review response, rather than inferred from technical tests.

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
| 2026-11-12 | Implemented and independently reviewed registration and Email confirmation. | Delivered step 3 with login/profile/logout dependencies, protected durable Email, distributed limits, persistent keys, explicit message/scheduler migrations, and recorded verification. Recovery/profile editing remain step 4; administration and delivery replay remain step 5. No new business rules. |
| 2026-11-12 | Implemented and independently reviewed password recovery and profile editing. | Delivered step 4, purpose-specific link persistence, atomic password/session changes, coordinated account-write locks, generated API client, Angular screens, and verification evidence. Implementation tasks #1-#3 are complete; stage/owner acceptance stays in #5. |
| 2026-11-13 | Implemented and verified first-run setup. | Owner approved Email login, an immediately active first administrator and SMTP in the wizard. Added atomic setup/defaults, protected mail settings, legacy migration, public branding and direct browser/infrastructure evidence. VALUE.md, CORE.md and DEVELOPMENT.md are synchronized; owner review of this extension remains in #5. |
| 2026-11-12 | Implemented administrator setup, role management, and Email inspection/replay. | Delivered step 5 with serialized role invariants, retained audit, versioned replay/Outbox coordination, administrative screens, and configured city-zone timestamp display. Implementation tasks #1-#4 are complete; final independent review, browser acceptance and owner review remain in #5. |

For subsequent changes, record the changed requirement or decision, its reason, and the affected specification sections. Update those sections in the same work session so the specification describes the current agreement.
