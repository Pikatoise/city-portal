# Stage 5: Article Comments and Moderation

## Status and sources

The owner approved the workflows on 2026-11-25 with a 500-character comment
limit. The owner confirmed the workflows on the stand and approved closing
Stage 5 on 2026-11-26 after the final navigation fixes. All eight code review
findings have been addressed.
[VALUE.md](../VALUE.md#article-comments) defines the approved product behavior.
[CORE.md](../CORE.md#comments) defines the technical direction.
[Issue #16](https://github.com/Pikatoise/city-portal/issues/16) is closed
and marked Done in [GitHub Project 3](https://github.com/users/Pikatoise/projects/3).

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

The comment queue at `/staff/reports/comments` follows the
[shared reporting contract](../CORE.md#content-reports). Reasons are spam,
insults, hate speech, personal data, and other. The preview shows the
reported text and article context. Reports retains a copy of the comment text
for review after owner deletion.

Moderators and administrators can review reports on any comment, including
their own. They can also hide a visible comment directly with a trimmed
reason of 1 to 500 characters. Direct hides appear in history and leave
existing reports pending. The stage has no restore action, account
notification, or Email notification.

### API

All mutations require CSRF. Use Problem Details and current authorization
checks. The shared contract defines the routes for the `comments` report
kind.

| Route | Contract |
| --- | --- |
| GET /api/news/articles/{articleId}/comments | Read up to 20 visible comments. Pass the returned `nextCursor` as `cursor` for the next page. Return 400 for an invalid cursor and 404 when the article is unavailable. |
| POST /api/news/articles/{articleId}/comments | Create a comment with a plain-text body and return 201 with the created comment. Return 400 for invalid text, 403 for a blocked account, 404 for an unavailable article, or 429 when rate limited. |
| GET /api/account/comments | Read up to 20 undeleted comments owned by the signed-in account. Include the account's current deletion permission and an `articleUrl` for each item. The URL is null when the article is unavailable publicly. |
| DELETE /api/account/comments/{id} | Delete the acting account's own comment. Return 204 on success, 403 for a blocked account, or 404 if the comment is unavailable to the actor. |
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

The API, Angular article comments, My Comments, and staff queue are
implemented. The final local run passed all 17 focused .NET integration
cases and all 109 Angular tests. The Angular production build, .NET
format check, and live OpenAPI comparison also passed.

- **API and concurrency.** PostgreSQL cases covered the 500-character
  boundary, CSRF, rate limiting, ownership, withdrawal and republication,
  and both commit orders for posting or reporting against a withdrawal.
  They verified moderation rollback, direct hide history, cursor
  continuation after insertion or hiding, trimmed hide reasons, bounded
  report batches, and session revocation when a password change wins a
  concurrent comment write.
- **Browser workflows.** The live HTTPS check covered anonymous posting
  and reporting, plain-text SSR, posting with an unconfirmed account,
  owner deletion, both hide paths, and retained history. The article
  fit 390 px and 1440 px viewports. Owner deletion made a prior hide
  preview stale. Staff could still read the retained text and dismiss
  the report.
- **Upgrade.** The migration from the Stage 4 schema preserved an existing
  article report and decision and created the Comments schema. Both EF
  models matched their migrations. The owner-comment index also applied
  to the retained local stand database, and API and Worker readiness passed.
- **Local readiness retry.** A full Release run passed 229 of 230 tests.
  One readiness case returned HTTP 503 just after PostgreSQL resumed
  while stand images were building. Both readiness cases passed when
  rerun after the builds finished.

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
- A live HTTPS smoke check confirmed the public article link.

The stage is closed. The [GitHub Actions run](https://github.com/Pikatoise/city-portal/actions/runs/45806791723)
passed backend, frontend, and container verification on implementation
commit `13f0266`.
