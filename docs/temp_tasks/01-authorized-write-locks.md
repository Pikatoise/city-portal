# Measure and narrow authorized write locking

**Status** · Existing workflow implemented. Stage 4 cases deferred to that stage.

The [optimization log](../OPTIMIZATIONS.md#draft-write-concurrency) records
the lock order, before/after measurements and PostgreSQL regression.
The [Stage 4 direction](../CORE.md#classifieds-and-moderation) records the
required commit-order cases for blocking versus publication or renewal.

## Problem

At baseline,
[AccountAuthorization](../../src/Modules/CityPortal.Identity/AccountAuthorization.cs)
acquired the same exclusive PostgreSQL transaction lock for authorized writes
across accounts. Article autosave, editorial actions, template moderation and
staff suggestion actions could queue behind one another. The lock coordinates
with [AccountAdministration](../../src/Modules/CityPortal.Identity/AccountAdministration.cs)
so a request rechecks its authorization after a concurrent role change.

## Implementation plan

1. Map lock acquisition and release for authorized writes, account/session
   changes, role changes and bootstrap. Record the global, Email, account and
   resource locks and identify which checks rely on each one. Include the
   role-change callbacks and the scope of their work inside the transaction.
2. On isolated PostgreSQL, establish a baseline with article autosave and
   suggestion or editorial writes for distinct accounts and resources. Repeat
   with same-account writes and with a concurrent role change. Record lock
   wait, request p50/p95 and throughput with the workload and environment.
3. If unrelated writers spend meaningful time waiting, try a shared transaction
   lock for routine authorized writes and keep the exclusive lock for role
   changes and bootstrap. Retain the per-account and resource locks, preserve
   acquisition order and recheck authorization after waiting. Check exclusive
   wait time under sustained writes. If the baseline shows little cost, record
   the result and keep the existing lock.
4. Carry the lock order and the results into the Stage 4 specification once it
   exists. Design account blocking and advertisement hiding as one transaction.
   Specify how a block coordinates with approval/publication and renewal for
   the listing owner, including writes performed by staff. Implement those
   scenarios with Stage 4.

## Acceptance criteria

- The baseline and any before/after comparison use the same workload and report
  lock wait separately from total request latency.
- If locking changes, a PostgreSQL integration test shows two unrelated writes
  can make progress independently. Tests exercise both orderings of role
  revocation versus an authorized write, and preserve same-account conflict
  handling, session revocation, last-administrator protection and rollback.
- The Stage 4 specification requires a winning block to prevent publication or
  renewal. If publication or renewal wins first, a subsequent successful block
  hides the listing in both public catalog and direct reads. Failure to hide
  listings rolls back the block. These cases become Stage 4 acceptance tests.

## Dependencies

The baseline and comparison isolated this change from the Media reference
task. The Stage 4 scenarios depend on the agreed Stage 4 specification and an
implemented Classifieds workflow.

Related contracts are in [CORE](../CORE.md#transactions-concurrency-and-module-interaction),
[Stage 2](../stages/STAGE_2.md#api-transactions-and-module-contracts) and
[Account Blocking](../VALUE.md#account-blocking).
