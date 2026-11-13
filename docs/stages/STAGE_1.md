# Stage 1: Accounts, Roles, and Email

## Status and Sources

Complete and owner-accepted on 2026-11-13, including the keyless first-run wizard and setup-only page access. No open business decisions remain. Minor visual refinements are deferred.

[VALUE.md](../VALUE.md) defines product behavior; [CORE.md](../CORE.md) defines architecture and delivery order. This document retains the Stage 1 contracts and acceptance record. Operational commands belong in [DEVELOPMENT.md](../DEVELOPMENT.md).

Implementation and acceptance tasks #1-#6 are complete in [GitHub Project 3](https://github.com/users/Pikatoise/projects/3) for [Pikatoise/city-portal](https://github.com/Pikatoise/city-portal).

## Goal

Deliver working account registration, authentication, Email confirmation, password recovery, and administrative role management through the API and Angular interface. Establish the Identity and Notifications modules and durable Email delivery so later workflows can use authenticated accounts and reliable background processing.

## Scope

Delivered: application foundation, first-run installation, registration, cookie authentication, Email confirmation, password recovery, profile editing, administrator account/role management, retained role history, and durable Email inspection/replay.

News, Media, citizen suggestions, classifieds, comments, and optional notification settings belong to later stages. Account blocking is delivered with advertisement hiding in Stage 4. Confirmation and recovery remain mandatory service messages, independent of future notification switches.

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

Redis and S3-compatible storage are deferred until an owning workflow needs them. Quartz.NET runs delivery retention; content scheduling is outside this stage.

## Implementation Steps

All steps are complete. The first-run wizard was added to the stage after the account workflows.

| Order | Delivered result |
| --- | --- |
| 1 | Agreed workflows, permissions, validation, security limits, and contracts. |
| 2 | API, Worker, Angular SSR host, module migrations, generated client, HTTPS Compose, health/telemetry, and CI checks. |
| 3 | Registration, durable confirmation delivery, explicit link consumption, and profile-only resend; login/profile/logout enabled this workflow. |
| 4 | Recovery, confirmed-profile editing, version conflicts, and password/session revocation. |
| 5 | Legacy administrator bootstrap, account lookup, serialized role changes/history, and audited Email replay. |
| 6 | Keyless setup wizard, atomic site/admin/SMTP persistence, existing-database migration, and setup-only page access. |
| 7 | Infrastructure and browser acceptance, independent review, and owner acceptance. |

### Foundation Contracts

`CityPortal.slnx` contains API, Worker, Identity, Notifications, and integration-test projects. Module DbContexts remain internal; explicit public operations register services and apply owned migrations. The API `--migrate` command applies module, Wolverine, and Quartz schemas. Normal startup never migrates.

GET `/api/system/info` returns public `name`, `stage`, `city`, `region`, and `timeZone` metadata with operation ID `getSystemInfo`. OpenAPI and Scalar are Development-only. Account and staff pages use browser rendering and must not expose private data through shared SSR HTML or caches.

Both .NET processes expose `/health/live` and `/health/ready`. Liveness is dependency-independent. Readiness checks PostgreSQL, required schemas, and RabbitMQ; public-mode API readiness additionally requires completed installation. Structured logs and OpenTelemetry are configured; OTLP export requires a configured endpoint.

Timestamps are stored in UTC and displayed in the configured city time zone. Reject invalid configuration at startup. Legacy deployment configuration defaults to UTC and normalizes supported Windows zone identifiers to IANA. Stage 1 exposes no general settings-editing endpoint or screen.

C# conventions and their enforcement follow [CODE_STYLE.md](../CODE_STYLE.md) and `.editorconfig`, including nullable references, build-time diagnostics, and the CI formatting check.

## Registration and Access Rules

- Email is unique without regard to letter case. Trim leading and trailing whitespace before validation and lookup. If the address is already registered, tell the visitor and offer login or password recovery.
- Display names contain 3-20 characters after trimming leading and trailing whitespace. Display names need not be unique; internal account IDs identify users.
- Display names allow Unicode letters and digits only. Spaces, hyphens, underscores, and other characters are rejected after the agreed outer-whitespace trimming.
- Email addresses contain at most 254 characters after trimming. Validate address syntax; Email confirmation verifies control of the address.
- Passwords contain 8-30 characters from printable ASCII U+0021 through U+007E. Allowed characters are Latin letters A-Z/a-z, digits 0-9, and ASCII punctuation. At least one punctuation character is required; letters and digits are optional. Spaces and non-ASCII characters are rejected. Do not trim or otherwise transform passwords.
- Every registered account receives the Regular User role. Staff roles are added to it.
- Unconfirmed accounts can log in, view their own profile, request another confirmation Email, and log out. Content creation and staff operations require confirmed Email.

## Links, Sessions, and Login Protection

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

## Administration and Profile Scope

- Fresh installations create the first administrator through the setup wizard. Existing installations without an administrator use the server command below; it has no default password and operates only while no administrator exists.
- Administrators can grant and revoke Author, Editor, Moderator, and Administrator roles, including their own roles. The last administrator cannot lose the Administrator role. Preserve Regular User on every account. Enforce the last-administrator invariant under concurrent changes.
- The profile shows Email, display name, confirmation status, and roles. Confirmed accounts can change their display name and password. Email changes, account deletion, and additional settings are outside stage 1.
- Role labels in the profile use Russian, user-facing names. Keep stable role identifiers in backend authorization and API contracts; map them to localized labels in the interface. The exact approved labels are maintained in VALUE.md, under Personal Account and Notifications.
- The administrator user list supports search by Email and display name, filters by role and Email confirmation, pagination, account details, and role changes.
- Role-change history records the actor, target account, timestamp, and roles added or removed. Administrators view it in the user's account details. Retain these records without automatic deletion.

## Email Delivery and Operations

- Send through configurable SMTP using MailKit. Use Mailpit for local development and mail inspection.
- Templates are in Russian and include HTML and plain-text versions, the configured portal name, intended action, link, and link lifetime.
- After a transient send failure, retry after 1, 5, 15, 30, and 60 minutes respectively, measured from the preceding failed attempt. After these five retries, retain the failure for investigation. Skip expired or invalidated links instead of sending them.
- A permanent SMTP rejection stops automatic send attempts immediately. Retain the cause for administrators and show the agreed error/resend presentation to the user.
- Account sending limits are separate for confirmation and recovery. The initial send and user-requested replacements consume the relevant limit; automatic delivery retries do not. Per-IP Email limits remain shared across both purposes.
- The profile shows the delivery result of the latest requested confirmation Email only. Outcomes for older messages cannot overwrite that result.
- Administrators can inspect queued deliveries and failures and replay delivery of a still-valid message. Record replay actions in administrative history.
- User-facing loading, error, and sent presentation follows [VALUE.md](../VALUE.md#registration-and-access). Internal queue states must not appear as additional user-visible labels.
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

Author, Editor, and Moderator do not grant administrator operations. Regular User is retained on every account. Legacy bootstrap administration is a server operation. Fresh installation uses the one-time setup endpoints below.

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
| GET `/installation/status` | None | 200, `installed` and `canInstall`, without credentials. |
| POST `/installation/complete` | Agreed site, administrator, and SMTP settings | 204, installation committed; separate login required. |
| GET `/system/info` | None | 200, public site metadata. |
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

List contracts default to page 1 and 20 items, cap pageSize at 100, and bound account-search queries to 254 characters. Account lists order by creation time/ID; histories and deliveries show newest first with ID tie-breaking. Invalid pagination or filters return 400. The generated OpenAPI document and TypeScript client must reflect these contracts.

Use Problem Details with `status`, `title`, `code`, `traceId`, and field `errors` for validation. Use HTTP 400 for validation/CSRF/invalid or expired links; 401 for missing/expired sessions or generic invalid login; 403 for insufficient role or confirmation; 404 for unavailable admin resources; 409 for version conflicts, duplicate Email, or last-administrator protection. Directly address invalid and expired confirmation links without a resend control on that page. Account login lockout and public request limits use 429 with `Retry-After` and a wait time. Password recovery conceals account-level suppression behind the generic 202; an IP limit can return 429 uniformly for known and unknown addresses.

Apply rolling request windows using server UTC; enforce limits atomically across API instances. Count IP registration/login/Email requests even if they fail validation. Count account sending allowance only when a new link is committed; keep confirmation and recovery counters separate. Unknown recovery addresses follow the same response shape and do not send mail. Automatic delivery retries do not consume account or IP user-request quotas. Accept client IP information only from configured trusted proxies.

## Module Contracts and Transaction Boundaries

Identity owns accounts, credentials, roles, sessions, link validity, and role history. Notifications owns delivery metadata, attempts, retry scheduling, and replay history. Future optional channel settings remain a Notifications responsibility but are outside this stage's UI.

- Registration synchronously creates the account, assigns Regular User, creates a confirmation link, and persists the corresponding delivery intent/Outbox message in one PostgreSQL transaction.
- Expose explicit module operations for queuing account mail, checking link validity before send, reading the latest confirmation delivery projection, and querying/replaying administrative deliveries. Expose DTOs rather than another module's DbContext or tables.
- The broker envelope carries only the delivery ID and attempt number. Notifications stores recipient, account/link references, purpose, timestamps, and a protected recoverable payload; Identity stores the token verifier. Exclude secrets from logs, traces, admin lists, and routine error inspection. API and Worker share persistent Data Protection keys managed outside source control.
- Coordinate participating DbContexts through the shared DbConnection/DbTransaction specified in CORE.md. Verify this with the real PostgreSQL provider and Wolverine Outbox before relying on it. Broker/provider failure cannot undo a committed local transaction.
- User resend atomically enforces quota, invalidates the previous same-purpose link, creates a new link, and persists its delivery work. Rollback restores the old valid link if the replacement transaction fails. A duplicate handler cannot mint another link.
- Confirmation consumes a valid link and confirms the account in one transaction. Reset consumes the recovery link, updates the password, and revokes all sessions atomically. Concurrent consumption has one winner; reopening a successfully consumed confirmation link is informational.
- Role writes, version checks, acting-user permission rechecks, last-administrator protection, and history commit atomically. Serialize the relevant administrator membership changes so concurrent requests cannot remove the last administrator or bypass a committed revocation. Self-removal is evaluated against permissions held before that write.
- SMTP acceptance and database acknowledgement cannot form one transaction; retain the acknowledged possibility of duplicate Email after a crash. Use message IDs and Inbox processing to avoid duplicate durable business effects. Recheck link validity immediately before the send; an in-flight old Email can still arrive after replacement but its link must fail validation.
- Replays operate on still-valid Failed deliveries, reject concurrent/terminal replay conflicts, reuse the same link, reset the bounded retry budget, and record the administrator atomically with Outbox work. Lifetime attempt numbers remain monotonic so old envelopes cannot become current. Replays do not create replacement links or overwrite the latest-confirmation pointer; replay history survives delivery cleanup.
- Retention cleanup is durable background work in the Worker. Quartz.NET uses persistent PostgreSQL scheduling for this job; it does not introduce scheduled content publication.

## Legacy Administrator Command

Provide a server-side bootstrap command that accepts the Email of an existing confirmed account and promotes it to Administrator while retaining Regular User. It receives no default password and exposes no public endpoint. In one coordinated transaction, verify no administrator exists, serialize bootstrap attempts, grant the role, and record a system/bootstrap actor and target in history. Refuse unconfirmed/missing accounts and refuse subsequent runs while any administrator exists. This uses the normal registration/confirmation flow before bootstrap and avoids storing a bootstrap password.

## First-Run Setup Contract

New installations configure site name, city, optional region, IANA time zone, the first administrator's Email/display name/password, and SMTP host/port/sender/optional credentials. Email remains the login; the installer-created administrator is immediately confirmed and receives RegularUser and Administrator roles. SMTP supports STARTTLS, TLS on connection, or no TLS for a local receiver. A bounded 15-second SMTP test precedes the coordinated commit of account, bootstrap audit, public settings and protected SMTP configuration. SMTP acceptance is not proof of inbox delivery. Failure leaves setup retryable; concurrent completion has one winner and the completed wizard cannot reopen.

Setup needs no key. The owner restricts access to loopback, using an SSH tunnel for a remote host, and opens network access only after installation. Local Compose explicitly enables Installation:Enabled; the API defaults it to false. Public mode disables completion regardless of client-IP headers and remains unready until the installation marker exists. The public proxy waits for healthy API readiness before publishing ports 80/443. Its SSR allowlist includes only local hostnames and the configured SITE_DOMAIN; wildcard host configuration is rejected. CSRF protection and the ten-attempt/ten-minute IP limit remain enforced. Status exposes installed and canInstall, never credentials.

Before completion, only the wizard application page is available. Node redirects other page requests before SSR/CSR shell delivery; Angular loads shared installation state before rendering and guards every non-setup route. Home/account/staff navigation, linked branding and the normal footer are hidden. Status failures leave other pages closed. Assets and bootstrap API requests remain available. Successful completion updates shared state and restores navigation without a reload; normal login remains a separate action.

Identity owns the installation marker/public defaults beside initial-administrator provisioning. Notifications owns encrypted SMTP configuration and reads it for each delivery without restarting. Contexts share one transaction and setup uses the bootstrap/role advisory lock. Existing account databases are marked installed by migration without changing credentials or data; deployment metadata/mail defaults and CLI bootstrap remain available for them. Public metadata supplies name, city, region and timeZone. PUBLIC_ORIGIN is set before initial setup. Later settings editing, branding uploads, SEO and integration-specific settings remain outside this extension.

## Acceptance Criteria

All criteria were accepted at stage closure:

- Account and administration workflows work through Angular and the API under the permissions and validation rules above.
- CSRF, current effective permissions, link expiry/replacement/one-time use, session revocation, lockout, and account/IP limits hold under invalid and concurrent requests.
- Identity changes and outgoing work commit or roll back together. Broker/Worker/SMTP recovery preserves work; duplicate processing preserves business state, with the documented possibility of duplicate external Email.
- Role changes preserve Regular User, the last administrator, record versions, and retained actor history. Revoked actors cannot commit protected writes after revocation wins the coordinated check.
- Fresh setup has one winner, rolls back on SMTP/storage failure, persists protected settings, closes permanently, and isolates all other pages until completion. Existing accounts survive upgrade.
- Explicit/repeat migrations, formatting, builds, generated-client consistency, infrastructure checks, and real HTTPS browser scenarios cover the delivered workflows.
- The owner reviewed the result; remaining limitations are recorded below.

## Verification and Review

This is a summary of the recorded 2026-11-11 through 2026-11-13 verification, not a new test run. Superseded installer-key behavior and intermediate pending-review statuses are omitted.

| Area | Recorded evidence |
| --- | --- |
| Validation, CSRF, uniqueness, confirmation, and sessions | Integration tests cover exact field boundaries, NFC/scalar names, case-insensitive Email, HTTP lifecycle, persisted lockout/expiry, quota contention, and revoked-session profile reads. |
| Transaction rollback and outages | `Registration_WhenDeliveryInsertFails_RollsBackAccountRoleLinkAndOutboxAsync`, `RegistrationAndResend_WhenBrokerIsUnavailable_ReturnSuccessWithDurableWorkAsync`, and `CommittedOutbox_WhenBrokerAndWorkerRecover_DeliversOnceAndSurvivesRestartAsync`. |
| Recovery and profile concurrency | `Recovery_WithReplacementExpiryAndConcurrentConsumption_PreservesConfirmationAndHasOneWinnerAsync`, `ProfileEdits_WithVersionAndCurrentPassword_EnforcePermissionsAndRevokeEverySessionAsync`, and rollback/conflicting-writer cases. |
| Email retries, expiry, and retention | Integration cases cover transient/permanent/exhausted failures, pre-send link checks, duplicate handling, 30-day resolved-record cleanup, and latest-confirmation projection after cleanup. |
| Bootstrap and role invariants | Separate-process bootstrap tests; concurrent last-administrator protection; atomic membership/version/audit rollback; `Roles_WhenRevocationWins_RejectsAlreadyStartedWriteWithoutChangingTargetAsync`. |
| Replay | Concurrent HTTP replay, invalid-link/audit rollback, fresh retry budget, rejection of old envelopes, and replay history retained after delivery cleanup. |
| Setup persistence | Concurrent completion, SMTP/storage rollback and retry, input validation before SMTP, legacy migration preserving credentials, and Worker use of persisted SMTP/branding across hosts. |
| Keyless/public-mode isolation | `Setup_WhenPending_RequiresCsrfAndExplicitCompletionAsync`, public-mode completion rejection despite forwarded loopback claims, readiness before/after installation, and effective local/public Compose configuration checks. |
| Setup-only pages | Angular route/navigation tests and built Node checks cover pending redirects, restored normal pages after completion, and closed access on status failure. Real Edge and CA-verified HTTPS checks confirm isolation. |
| Browser acceptance | Real HTTPS and Mailpit flows cover account lifecycle, outage/recovery, password and profile changes, session revocation/restart, administrator history/replay, wizard SMTP failure/retry, immediate administrator login, branding, and mobile overflow/error checks. |
| Build and contracts | Release build, solution/frontend formatting, production SSR build, generated-client drift, live OpenAPI comparison, Docker images, explicit/repeat migrations, and workflow syntax checks passed. |

The setup backend run passed 76 of 77 cases; the remaining MIME assertion was corrected to decode the message and its focused rerun passed. The later keyless revision passed all 14 selected installation/foundation cases. The final setup-only frontend run passed all 48 cases. These results come from separate runs; no single complete backend rerun after every revision or new remote CI result is claimed.

Independent specification and coding-style reviews resolved findings in link handling, retained-delivery projection, session rechecks, and concurrent account writes. Setup backend review found no confirmed defect; a second setup reviewer reached its usage limit, and frontend inspection was completed inline and through browser acceptance.

### Owner Acceptance and Limitations

The owner accepted the account/administration workflows on 2026-11-12 and the revised wizard and page isolation on 2026-11-13. Stage 1 is closed. No Stage 2 specification has been agreed yet.

- Minor visual refinements are deferred.
- Browser checks used a scoped local-certificate exception with separate CA/hostname-verified HTTPS checks; system trust was unchanged.
- SMTP acceptance followed by a database acknowledgement failure can duplicate external Email.
- Real-provider SMTP/TLS, public DNS/certificate issuance, and public deployment remain deployment checks.

Startup, migration, smoke-check commands, and certificate handling are maintained in [DEVELOPMENT.md](../DEVELOPMENT.md). Git history retains the implementation chronology and earlier verification details.
