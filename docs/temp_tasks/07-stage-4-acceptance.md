# Stage 4 acceptance and owner review

## Dependencies

[03 Published revisions and listing lifecycle](03-stage-4-listing-lifecycle.md),
[04 Public catalog, search, and homepage](04-stage-4-public-discovery.md), and
[06 Decision history and workflow notifications](06-stage-4-notifications-and-history.md),
including their prerequisites.

## Outcome

Verify the complete Stage 4 workflow and record the owner's review against the
[stage specification](../stages/STAGE_4.md).

## Acceptance criteria

- [ ] Verify the supported upgrade and repeat migrations on durable storage.
  Check PostgreSQL and Media backup and restore for the new data and references.
- [ ] Through the live HTTPS application, exercise owner draft, photo upload,
  submission, staff review, public discovery, revision, renewal, blocking,
  return to the report queue after a new report, and delivery recovery.
- [ ] Reconcile the acceptance criteria with automated and browser evidence,
  including exact expiry, atomic blocking, concurrent actions, role access,
  and public visibility. Verify health, generated client consistency, and CI
  checks. Record any limitations and clean up task-owned test resources.
- [ ] Walk the owner through the workflow, transaction boundaries, and one
  failure scenario. Record the review outcome and align the GitHub tracker
  before closing Stage 4.
- [ ] Commit completed work and verification. After moving durable results to
  the stage specification, remove the completed temporary task files.
