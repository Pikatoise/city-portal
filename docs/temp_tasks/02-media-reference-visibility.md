# Update Media visibility without replacing image references

**Status** · Implemented

The [optimization log](../OPTIMIZATIONS.md#media-reference-visibility) records
the visibility rules, SQL comparison and regression evidence.

## Problem

At baseline,
[ReviewOperations](../../src/Modules/CityPortal.News/ReviewOperations.cs)
deserialized and validated the content of every article revision when it
refreshed Media access. Then
[MediaReferences](../../src/Modules/CityPortal.Media/MediaReferences.cs)
deleted and reinserted the reference rows even when only their audience or
publication status changed. The same Media operation handled image selection,
inheritance and updates to existing references.

## Implementation plan

1. Trace the current reference flags for private drafts, submitted revisions,
   the public revision and retained history. Record how review assignment,
   withdrawal, deletion and role removal affect those flags, including author
   visibility after deleting a draft created from a suggestion.
2. Keep the image selection path for actual attachment changes. Preserve
   validation of new selections, ready and owned images, archived template
   inheritance, editor-owned uploads and suggestion-image imports. Keep
   selection and reference changes coordinated with Media cleanup.
3. Add an operation in Media for updating the access state of existing
   references. Have News calculate that state from revision and article
   metadata and pass it in the existing transaction. Update only changed rows
   and preserve the reference keys. Start with bounded batches of revision IDs
   if bulk updates are needed. Keep attachment retention and the
   synchronization used by template decisions and cleanup.
4. Switch the editorial refresh paths to the new operation. Measure SQL
   statements and changed rows for the same transition on articles with short
   and long revision histories. Verify the old path is still used when the
   image set changes.

## Acceptance criteria

- A visibility-only transition updates the affected flags without parsing the
  revision content or deleting and reinserting its image references.
- PostgreSQL and Media integration cases cover private draft access, assigned
  reviewer access and its loss, publication, withdrawal, retained history,
  template block/unblock and archived inheritance. Include a draft with
  imported suggestion images.
- Changing access and the article state commits or rolls back with history and
  any notification work. After a failed change, access still matches the
  previous committed article state.

## Dependencies

Use the existing PostgreSQL and S3-compatible fixtures. Keep this change
separate from task 04 on the suggestion lifecycle so any regression in draft
deletion or Media retention can be traced to its source.
See [Stage 2](../stages/STAGE_2.md#api-transactions-and-module-contracts)
and [Stage 3](../stages/STAGE_3.md#attachments-and-notifications).
