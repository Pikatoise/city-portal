# Public catalog, search, and homepage

## Dependencies

[First publication and public detail](../stages/STAGE_4.md#first-publication-and-public-detail).
Run the combined discovery and lifecycle checks after
[Published revisions and listing lifecycle](../stages/STAGE_4.md#published-revisions-and-listing-lifecycle).

## Outcome

Visitors browse the public classifieds catalog, narrow results with search and
filters, and reach the approved detail page. The homepage displays recent
public advertisements and provides the agreed entry points to browse or create
a listing.

## Decisions to confirm

Agree default sorting, text search fields and ordering, pagination, homepage
size, and which filters apply to the first version's common listing fields.
Confirm whether category-specific attribute configuration and entry are part of
this stage beyond the first version's common fields.

## Acceptance criteria

- [ ] Public catalog and detail navigation work anonymously through Angular
  SSR. An authenticated owner can reach the existing draft workflow from the
  agreed creation entry point.
- [ ] Bounded, consistently ordered results support the agreed category,
  price, and location filters. Search and filters read only the current public
  revision. Category-specific attributes follow the approved first-version scope.
- [ ] The homepage shows only the agreed number of recent public listings.
  Catalog, homepage, direct detail, and photos enforce the same visibility and
  exact expiry conditions.
- [ ] Verify search, filters, pagination, and homepage links through the HTTPS
  browser. Check that hidden, expired, withdrawn, and pending data stays absent,
  including any shared caches used by the workflow.
