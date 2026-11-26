# Stage 5: Article Comments and Moderation

## Status and sources

The owner approved the workflows on 2026-11-25 with a 500-character comment
limit. The owner confirmed the workflows on the stand and approved closing
Stage 5 on 2026-11-26 after the final navigation fixes. All eight code review
findings have been addressed.
[VALUE.md](../VALUE.md#article-comments) defines the approved product behavior.
[CORE.md](../CORE.md#comments) defines the technical direction.
[Stage 4](STAGE_4.md#article-and-advertisement-reports) records the existing
reporting contract that this stage extends.
Track the stage in [issue #16](https://github.com/Pikatoise/city-portal/issues/16)
in [GitHub Project 3](https://github.com/users/Pikatoise/projects/3).

## Goal and scope

Visitors can read and post comments beneath a public article. They can report
a comment, and staff can act on it. The workflow covers the public article
page, account-owned deletion, and staff moderation and decision history.

## Approved workflows and permissions

| Actor | Behavior |
| --- | --- |
| Anonymous visitor | Read, post, and report comments. Anonymous posts have no owner deletion operation. |
| Signed-in user | Read, post, and report comments. Delete comments owned by the acting account. An account block restricts content writes under the existing account rules. |
| Moderator or Administrator | Hide any comment. Staff actions require confirmed Email, an active account, and current permissions. |
| Author or Editor | Use ordinary comment actions. These roles grant no comment moderation permissions. |

Valid comments appear immediately in a single flat list under the article.
Comments have no replies, nesting, or edit operation.

## Workflows and contracts

### Public comments

- Show plain text, a display name, and the creation time. Use the account's
  current display name for signed-in authors and a fixed anonymous label for
  anonymous authors. Accept 1 to 500 characters after trimming.
- Allow unblocked signed-in users to post regardless of Email confirmation.
  Use the shared antiforgery flow for both kinds of visitor and start with
  five posting attempts per minute per IP and per signed-in account. Keep
  IP addresses out of comment records, report history, and application logs
  emitted by this workflow.
- Use newest-first pagination, 20 comments per page. Continue from the last
  creation time and comment ID via an opaque cursor so earlier inserts or
  removals do not shift the next page. Public responses
  contain the comment ID, display name, body, and UTC creation time. The API
  derives deletion and direct moderation controls from the current session.
  SSR reads anonymously and the browser loads its own permission flags.
  Keep viewer-specific state out of shared caches and SSR transfer state.
- Bind comments to the stable article ID. Article withdrawal closes public
  reads, posting, and new reports. Still-visible comments reappear on
  republication. Owner deletion and review of pending reports remain
  available while the article is withdrawn.
- The account's My Comments page lists the owner's undeleted comments,
  including comments hidden by staff and comments on withdrawn articles.
  It shows the text, creation time, and a link to the article's current public
  URL. When the article is unavailable, render the link disabled and show
  “Статья скрыта”. Blocked owners retain read access.
- Blocking an account prevents its comment writes while leaving its existing
  comments subject to individual moderation.

### Reports and moderation

- Add a separate comment queue to the existing Reports workflow. Group
  reports by comment and show the reported text and article context. Use
  the existing report limits. Reasons are spam, insults, hate
  speech, personal data, and other. A report leaves the comment visible.
- Confirmed moderators and administrators can dismiss the reports they
  reviewed or hide the comment with a trimmed reason of 1 to 500 characters.
  Offer a direct hide action on visible comments for the same roles.
- The staff preview includes up to 20 oldest pending reports, one page of up
  to 20 resolved reports, and one page of up to 20 decisions. A decision
  covers only the reviewed pending batch. Staff can page through resolved
  reports and decisions independently.
- Keep report and decision history available to staff after hiding or owner
  deletion. Retain the reported snapshot in Reports. Later reports stay
  pending and can still be dismissed if the comment disappeared during
  review. Direct hides appear in history and leave existing reports pending.
  The stage has no restore action, account notification, or Email notification.

### API

All mutations require CSRF. Use Problem Details, current authorization
checks, and the established report preview sequence and stale-decision
behavior.

| Route | Contract |
| --- | --- |
| GET /api/news/articles/{articleId}/comments | Read up to 20 visible comments. Pass the returned `nextCursor` as `cursor` for the next page. Return 400 for an invalid cursor and 404 when the article is unavailable. |
| POST /api/news/articles/{articleId}/comments | Create a comment with a plain-text body and return 201 with the created comment. Return 400 for invalid text, 403 for a blocked account, 404 for an unavailable article, or 429 when rate limited. |
| GET /api/account/comments | Read up to 20 undeleted comments owned by the signed-in account. Include the account's current deletion permission and an `articleUrl` for each item. The URL is null when the article is unavailable publicly. |
| DELETE /api/account/comments/{id} | Delete the acting account's own comment. Return 204 on success, 403 for a blocked account, or 404 if the comment is unavailable to the actor. |
| POST /api/reports/comments/{id} | Submit a report using the existing reason and optional text contract. Require a currently public comment. |
| GET /api/staff/reports/comments and GET /api/staff/reports/comments/{id} | List pending subjects or history and open a bounded review preview. Use `resolvedPage` and `decisionPage` to page through history. |
| POST /api/staff/reports/comments/{id}/decide | Dismiss reports through the reviewed sequence or hide the target and record the decision in the same transaction. |
| POST /api/staff/comments/{id}/hide | Hide a visible comment with a reason and record the actor and time. |

## Tools and module boundaries

- Use .NET 10 ASP.NET Core controllers, a Comments module with its own EF
  Core DbContext and PostgreSQL schema, and explicit operations for News
  publication state and Reports decisions. News owns article visibility.
  Comments owns the comment lifecycle. Reports owns reports, their snapshots,
  and moderation decisions, including direct hides.
- Coordinate the final article visibility check with comment creation and
  report submission so that a committed withdrawal prevents later writes.
  Those writes share the Identity administration lock. Withdrawal, owner
  deletion, and moderation take it exclusively. Public reads use one
  PostgreSQL snapshot for article visibility and comment rows. Commit a
  moderation decision and the associated comment hide together.
- Signed-in comment writes use the Identity Email and account locks before
  they recheck the session. If a password change commits first, the write
  rejects the revoked session. A write that obtains the locks first can
  commit before the password change. Logout still rejects subsequent
  requests and allows an already authorized write to finish.
- Use Angular SSR and the generated OpenAPI TypeScript client for the public
  article page. Use browser-rendered staff pages for moderation. Follow the
  existing UI conventions.
- Verify with xUnit and PostgreSQL Testcontainers, Angular checks, and HTTPS
  browser scenarios. Run the existing GitHub Actions verification and check
  migration from the Stage 4 schema.

## Delivery order

1. Implement and verify public posting, article visibility, and owner deletion.
2. Extend Reports and implement and verify comment moderation and history.
3. Verify the database upgrade and concurrent writes. Review public posting,
   moderation and one failure scenario with the owner before closing the stage.

## Acceptance criteria

- An anonymous and a signed-in visitor can post on a public article and see
  the comment immediately. The page renders a flat, bounded list and
  displays markup submitted in a comment as plain text.
- Account ownership, staff roles, CSRF protection, and posting limits are
  enforced at the API. A blocked account or forged owner ID cannot delete
  another account's comment. Authors and editors alone cannot hide one.
- A withdrawn article exposes no comments or comment writes through public
  routes. After republication, only comments allowed by the agreed lifecycle
  return. Verify both commit orders for withdrawal racing with comment
  creation or a report.
- A report enters one queue item and leaves the comment visible. Dismissal
  covers the reviewed reports. Hiding removes the comment from public reads
  and records the decision and its reason. Inject a failure before commit to
  verify that the hide and report decision roll back together.
- Verify the agreed behavior when owner deletion or new reports race with
  moderation. Staff can retrieve retained history without exposing it publicly.
- Existing articles and report queues still work after the Stage 4 database
  upgrade. The owner reviews the browser workflows and one failure scenario.

## Open decisions

No product decisions remain open.

## Implementation and verification

The API, Angular article comments, and staff queue are implemented.

- All 227 .NET tests passed. A separate run passed all 14 focused PostgreSQL
  integration cases. They covered the
  500-character boundary, CSRF, rate limiting, ownership, withdrawal and
  republication, both commit orders for posting or reporting against a
  withdrawal, atomic report moderation rollback, and direct hide history.
- On 2026-11-25 the live HTTPS Edge check covered anonymous posting and
  reporting, plain-text rendering and SSR, posting with an unconfirmed
  account, owner deletion, hiding from the report queue, direct hiding,
  and retained history. The article fit 390 and 1440 pixel viewports.
  An owner deletion made a prior hide preview stale. Staff could still
  see the reported text and dismiss the pending report.
- The Reports upgrade applied to the Stage 4 Reports schema with an existing
  article report and decision. It preserved both records and created the
  Comments schema. Both EF models match their migrations.
- The Angular build and all 104 Angular tests passed. The generated client
  matches the live OpenAPI contract. The .NET format check and Prettier on
  changed frontend files passed. The project-wide Prettier check flagged
  30 existing files with Windows CRLF endings. All 30 passed with the LF
  endings stored in Git.

## Stand check and code review

On 2026-11-26 the owner confirmed the comment workflows worked on the local
stand. The stand was stopped with its data retained. The code review compared
the working tree with Stage 4 closure commit `0a0ce73`.

Specification findings:

- Owner deletion works through the API during article withdrawal. The
  browser offers deletion only for comments still visible on an article,
  leaving the owner without a browser action after withdrawal or hiding.
- Offset pagination can repeat or skip comments when an earlier page
  changes between requests.
- Report-review validation checks the raw hide reason length before
  trimming, while direct hide checks the trimmed length.

Engineering findings:

- The staff preview loads all reports and decisions for a target. The new
  comment queue inherits this unbounded response from Stage 4.
- The comment write contract should define behavior when password changes
  revoke sessions during a request. Comment writes use the role lock,
  while password changes use Email and account locks.
- Primary comment actions use the default secondary button treatment.
- Some integration test reads use null suppression in place of explicit
  assertions about response bodies.
- The comment-specific exception for staff reviewing their own content is
  repeated across the report queue, preview, and decision queries.

Local verification is recorded above. GitHub Actions verification remains
pending for these working-tree changes.

## Code review follow-up

On 2026-11-26, the private My Comments page made owner deletion reachable after
withdrawal and hiding. Public comment paging now uses a cursor. Report-driven
hiding normalizes the reason before request validation, and the staff preview
uses separate bounded pages for reports and decisions. Signed-in comment
writes recheck the session under the Identity locks. Primary actions use the
shared button style, test reads assert that response bodies exist, and the
report queue, preview, and decision queries share one eligibility predicate.

All 17 focused .NET integration cases and all 105 Angular tests passed.
The .NET cases include owner access during withdrawal and hiding, cursor
continuation after insertion and hiding, the 500-character trimmed hide
reason, report batch boundaries, and password change winning a concurrent
comment write. The Angular account test exercises owner deletion without
opening the article. The Angular build and live OpenAPI verification passed.
The updated migrations applied to an isolated PostgreSQL database.

The local HTTPS review stand applied the owner-comment index migration to its
retained database. API and Worker readiness and the live OpenAPI comparison
passed. The stand has a dedicated published article and a comment owned by
the demo regular user for the withdrawal and deletion check.

The full Release .NET run passed 229 of 230 tests with no skips. One readiness
case returned HTTP 503 immediately after PostgreSQL resumed while the stand
images were building. Both readiness cases passed in a focused repeat after
the image builds finished.

## Owner acceptance

On 2026-11-26, the owner confirmed that article withdrawal hid the public
article and that the comment owner could delete their comment from My
Comments. The owner requested two final navigation fixes and approved stage
closure after they were addressed.

- The profile links to `/staff/publications` for eligible editors and
  administrators. The Angular account test checks the rendered link and its
  absence for a regular user or an editor with unconfirmed Email.
- My Comments shows an active link for a currently public article and a
  disabled link with “Статья скрыта” for a withdrawn article. Angular tests
  checked both renderings and deletion. PostgreSQL integration tests checked
  the URL before withdrawal, during withdrawal, and after republication.
  Hiding just the comment leaves the public article link available.
- All 17 focused .NET integration cases and all 109 Angular tests passed.
  The Angular production build, .NET format check, live OpenAPI comparison,
  and HTTPS smoke check of the public article link passed.

The stage is closed for product acceptance. GitHub Actions verification of
the final working-tree changes is pending publication of those changes.
