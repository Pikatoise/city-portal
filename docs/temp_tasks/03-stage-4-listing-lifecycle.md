# Published revisions and listing lifecycle

## Dependencies

[First publication and public detail](../stages/STAGE_4.md#first-publication-and-public-detail).

## Outcome

Owners manage published listings from My Advertisements. Edits go through
review while the approved revision remains visible within its publication
window. Owners can renew unchanged listings and recover eligible hidden
listings through the agreed rules.

## Decisions to confirm

Agree what renewal does while a revision is pending and how it interacts with
an approval. Define the owner actions after withdrawal, expiry, a direct
listing block, or an account unblock. Agree listing deletion, history, and
photo retention rules before applying them.

## Acceptance criteria

- [ ] An owner can prepare and submit corrections. The public page keeps the
  approved text, photos, and contacts until another revision is approved or the
  current one expires or becomes hidden.
- [ ] An unchanged active or expired listing can be renewed for seven days
  from renewal without another review. Expiry leaves a listing visible to its
  owner and removes it from every public read at the deadline.
- [ ] Owner withdrawal, deletion, and manual relisting follow the agreed state
  rules. An account unblock never automatically restores listings. A directly
  blocked listing stays hidden until its correction is approved.
- [ ] Existing approved photo references survive working-version edits.
  Cleanup releases unreferenced photos according to the agreed retention rules.
- [ ] Use PostgreSQL to verify version conflicts and both commit orders for an
  account block racing with renewal or staff approval of changes. Verify that
  blocked accounts cannot restore public visibility in either path.
