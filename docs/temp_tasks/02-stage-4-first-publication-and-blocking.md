# First publication and atomic blocking

## Dependencies

[01 Staff review and owner feedback](01-stage-4-review-queue.md).

## Outcome

A staff approval publishes the first approved revision immediately. Visitors
can open its public detail page and photo carousel. Authorized staff can hide a
listing or block an account, with the visibility change effective at commit.

## Decisions to confirm

Agree the public URL, unavailable-page behavior, and contact display. Confirm
direct listing block permissions, mixed-role account rules, self-block and
last-administrator safeguards, and session behavior after unblocking.

## Acceptance criteria

- [ ] Approval by a moderator or administrator publishes another owner's
  submitted version. The published snapshot and its photos remain independently
  readable for the publication period. The first seven-day window starts at
  publication, and all public reads enforce its exact end.
- [ ] The SSR detail page uses only public fields. It displays up to five
  approved photos in a separate carousel immediately after the title and before
  the description. Listings without photos omit the carousel.
- [ ] A direct listing block requires a reason, records its actor and time,
  and removes public access to the listing and its photos.
- [ ] Authorized account blocking records its reason and actor, denies
  subsequent account actions, and hides every listing in the same PostgreSQL
  transaction. Unblocking preserves hidden listings and direct listing blocks.
  Published news retains its existing visibility.
- [ ] Use a common visibility rule for public details, photo delivery, and
  later catalog reads. Coordinate publication and account blocking before
  enabling the public routes.
- [ ] PostgreSQL tests force rollback during a block and verify both commit
  orders when a block races with first staff publication. Verify direct page
  and photo access before and after a successful block.
