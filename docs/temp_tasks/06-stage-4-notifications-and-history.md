# Decision history and workflow notifications

## Dependencies

[Staff review and owner feedback](../stages/STAGE_4.md#staff-review-and-owner-feedback),
[First publication and public detail](../stages/STAGE_4.md#first-publication-and-public-detail),
[03 Published revisions and listing lifecycle](03-stage-4-listing-lifecycle.md), and
[05 Advertisement reports and staff resolution](05-stage-4-advertisement-reports.md).

## Outcome

Owners can find listing decisions and their reasons in My Advertisements.
Authorized staff can review the relevant audit history. Affected users receive
the agreed account and Email notifications through the existing delivery system.

## Decisions to confirm

Agree event types, recipients, and channel defaults. Confirm which history
owners and each staff role may read, and what remains after deletion.

## Acceptance criteria

- [ ] Workflows record significant actions at the time they happen. The
  history views expose the authorized decisions, actor, time, and reason.
  Removing or disabling notifications leaves decision history available.
- [ ] Agreed account and Email notices follow the existing preference and
  retention rules. The business change and its notification work commit
  together through the existing transactional outbox.
- [ ] Broker or Email outages leave committed work available for retry.
  Recovery and duplicate processing do not repeat the business decision or
  create duplicate account notices for the same event.
- [ ] Exercise preferences, authorized history, failure, and recovery with
  real infrastructure and the browser.
