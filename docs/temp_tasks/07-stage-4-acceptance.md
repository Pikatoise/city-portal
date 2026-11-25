# Stage 4 acceptance and owner review

## Dependencies

[Published revisions and listing lifecycle](../stages/STAGE_4.md#published-revisions-and-listing-lifecycle),
[Public catalog and homepage entry point](../stages/STAGE_4.md#public-catalog-and-homepage-entry-point), and
[Decision history and workflow notifications](../stages/STAGE_4.md#decision-history-and-workflow-notifications),
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
