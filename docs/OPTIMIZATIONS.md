# CityPortal Optimizations and Refactoring

Record each change's date, scope, reason, implementation and verification,
including measurement conditions and limits. See [VALUE.md](VALUE.md),
[CORE.md](CORE.md) and the stage specifications for current contracts, and
[DEVELOPMENT.md](DEVELOPMENT.md) for operating procedures.

## Stage 5 comment and report review

2026-11-26. Comment paging uses the existing article, timestamp, and ID index
with a cursor. The My Comments query has a partial index for undeleted
account comments. The staff preview replaces two unbounded result sets with
bounded pending, resolved, and decision queries. Report visibility for staff
is expressed in one predicate shared by list, preview, and decision
operations. See the [Stage 5 verification](stages/STAGE_5.md#implementation-and-verification).

The integration fixture confirmed the response caps of 20 pending reports,
20 resolved reports, and 20 decisions. It also verified that later reports
remain pending. Cursor tests covered inserts and hides before the page
boundary. No latency comparison was measured.

## Angular CSRF transport

2026-11-22. Consolidated CSRF acquisition for 37 Angular write operations
in `ApiWrite`, preserving response bodies, HTTP status, Problem Details and
`Retry-After`. See the [transport contract](CORE.md#identity-and-authorization).

All 103 Angular tests and the production SSR build passed. Tests covered API
root, JSON/multipart writes, token ordering, failure isolation, error metadata
and the browser guard. Regenerated client and live OpenAPI matched the
snapshot. Prettier passed on changed files and flagged 35 other files.

Edge against isolated HTTPS Compose passed setup, login, multipart upload,
suggestion create/edit/claim/import/return, moderation deletion and logout.
Two simultaneous first recovery requests returned 202 after sequential token
requests. Missing and invalid headers returned 400 `invalid_csrf`. An
anonymous write worked after logout, and SSR with a fixture cookie still made
anonymous news reads without requesting a token.

## Suggestion assignment lifecycle

2026-11-22. Claim and return now use named suggestion state transitions.
The News deletion observer reuses return for explicit return, ordinary deletion
and Author role removal, resolving duplicated ownership of timer and version
updates. See the [Stage 3 contract](stages/STAGE_3.md#author-queue-and-manual-deletion).

`DraftDeletionOrAuthorRoleRemoval_ResumesRemainingQueueTimeAsync` checks the
deletion actor, single version increment and 52 hours of remaining queue time.
The publication test checks explicit return, the first-claim timestamp and
published suggestions after role removal.
`Assignment_RollsBackDraftHistoryAndMediaWhenSuggestionWriteFailsAsync`
forces failure after News saves creation or deletion, then checks rollback and
same-version retry. Release build, formatting and all 195 .NET tests passed.

## Media reference visibility

2026-11-19. Reviewer changes previously validated every retained revision
document and replaced its Media references. News now derives audiences from
revision IDs and submission timestamps. Media groups matching audiences in
batches of up to 128 revisions and updates changed flags in place.
See the [Stage 2 contract](stages/STAGE_2.md#api-transactions-and-module-contracts).

### Measured reviewer assignment

Compared the HTTP claim action at `4a626c3` and after the change with
PostgreSQL 18.6, the Release API test host and the private S3 fixture. Each
revision referenced one ready image. The short history had the original
draft and its submitted revision. The long history added 128 retained
submitted revisions. A temporary EF Core command listener counted Media
SQL, including the revision and image locks. A PostgreSQL row trigger
counted committed writes to Media references.

| Revisions | Media SQL statements before / after | Rows deleted / inserted / updated before | Rows deleted / inserted / updated after |
| ---: | ---: | ---: | ---: |
| 2 | 12 / 4 | 2 / 2 / 0 | 0 / 0 / 1 |
| 130 | 780 / 8 | 130 / 130 / 0 | 0 / 0 / 129 |

The new path kept the unsubmitted draft reference unchanged in both fixtures.
The claim updated the submitted references, including the retained ones an
assigned editor can read. This comparison measured SQL and row counts.

The PostgreSQL integration test
`Review_MediaVisibilityUpdatesOnlyChangedReferencesAndRollsBackAsync`
detected the old delete-and-insert path. It checks that claim, takeover and
release each update only the submitted reference. It also forces a News
history failure after Media updates and checks that the previous reviewer
retains access while the replacement reviewer still has none.

The full Release .NET suite passed 192 tests with zero failures or skips.
It covers publication and withdrawal, template blocking and inheritance,
attachment and cleanup races, and imported suggestion images. Solution
formatting verification passed.

## Draft write concurrency

2026-11-19 (`4a626c3`). An exclusive administration lock serialized even
unrelated draft saves. Private draft saves now share it. See the
[authorization and lock policy](CORE.md#transactions-concurrency-and-module-interaction).

### Local comparison

The 2026-11-19 comparison used the same real HTTP handlers in a Release
ASP.NET Core TestServer, isolated PostgreSQL 18.6 on Docker Desktop/WSL2,
pooled Npgsql connections and .NET SDK 10.0.403. The host was a Ryzen 5 5500U
with 12 logical processors. Each run warmed up with 32 saves. Each writer
repeated a versioned save on its own draft with a short body and no images.
The same-account case used one author and eight distinct drafts. In the mixed
case an editor made 16 category changes and an administrator made 16 role
changes on a separate account while the draft saves ran. The baseline used
commit `bcac0a6` with temporary measurement instrumentation. The comparison
run used the shared-lock implementation with the same instrumentation.

| Workload | Saves | Save p50 / p95 before (ms) | Save p50 / p95 after (ms) | Role lock command p50 / p95 before (ms) | Role lock command p50 / p95 after (ms) | Saves/s before / after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| One author, one writer | 24 | 66.59 / 71.18 | 64.64 / 77.91 | 1.82 / 2.09 | 1.69 / 2.13 | 14.9 / 15.2 |
| Eight distinct authors | 192 | 306.29 / 348.73 | 97.75 / 111.22 | 240.33 / 274.42 | 2.33 / 4.79 | 25.8 / 80.5 |
| Eight writers on one account | 96 | 268.77 / 275.41 | 252.34 / 282.43 | 210.89 / 219.13 | 1.56 / 2.11 | 29.8 / 31.1 |
| Eight authors with editorial and role changes | 192 | 317.46 / 343.34 | 135.32 / 173.07 | 264.07 / 289.50 | 47.83 / 81.98 | 25.6 / 59.4 |

The lock-command timer measures acquisition separately from the request and
includes its SQL round trip. The mixed-workload lock column includes the 16
editorial commands alongside the 192 draft saves. For the 16 exclusive role
changes, lock-command p50/p95 was 274.72/293.53 ms before and 84.42/98.65 ms
after. Their complete request p50/p95 was 331.35/440.96 ms before and
146.31/273.67 ms after. Same-account requests still serialize on their actor
locks. These short runs compare the listed workloads. Production latency and
long-running fairness remain unmeasured.

A PostgreSQL regression held one article row locked. The second author's save
waited before the change and committed independently after it. Role-revocation
tests covered both commit orders. Release formatting and all 191 .NET tests
passed, including approved saves, notification rollback and Stage 3 role removal.

## Public article read

Implemented on 2026-11-22. Article SSR previously paged through the public
category and tag lists and dropped related stories when either list request
failed. The [Stage 2 detail contract](stages/STAGE_2.md#public-news-and-discovery)
now supplies the labels for the current published revision. The API queries by
the revision's tag IDs (up to ten), skips the query when there are no IDs, and
restores the stored order. PostgreSQL used the taxonomy primary-key index for
the three queried IDs in the local fixture.

The production Angular build made eight public news requests before the change
and two after it on a loopback fixture with 205 categories and 205 tags, using
100 items per page. Taxonomy list requests fell from six to zero. Active,
renamed, and archived tag links rendered before and after the change. A
separate live API and PostgreSQL fixture with the same public taxonomy counts
confirmed two public news requests for SSR and two for an in-browser
article-to-article navigation in Edge. Both reads made zero taxonomy requests.

With a pending revision in PostgreSQL, SSR rendered only the published body,
tag links, and SEO metadata. After withdrawal, SSR and the direct API returned
404 without article content or article metadata. The SSR fixture also verified
that a related-request failure leaves the article visible. Angular's router test
verified that a late related response from the previous article is discarded.
The live OpenAPI document matched the snapshot, and regenerating the TypeScript
client from that snapshot produced matching files.

The full runs passed 192 .NET tests and 100 Angular tests. The public-read
regression also passed on the final Release build. Production SSR built
successfully.

## News discovery queries

Completed on 2026-11-17 in `1f67b7c`.

The initial capacity check exposed slow broad search and taxonomy reads. The
`DiscoveryReadPerformance` News migration adds a partial index on the current
public `PublishedRevisionId` and selects `MAIN` storage for the generated
search vector. Recomputing existing generated values applies the physical
storage choice to existing revisions without changing text or article versions.
The upgrade regression verifies preserved content/search, the physical policy,
the index and repeat application.

The upgrade regression detected removal of the recomputation UPDATE by checking
the PostgreSQL row version of a large vector originally stored as EXTENDED.
An isolated tag-page query improved from 62.428 ms to 4.078 ms. A broad ranked
search improved from 471.552 ms to 20.571 ms. These timings measure individual
queries. See [Stage 2 upgrade](development_instructions/installation_and_deployment/stage_2_upgrade.md)
for deployment steps.

### Capacity results

The final capacity run reused the same isolated, migrated 10,000-article fixture
after warm-up. Each route received 24 warm-up requests at eight readers, then
80 measured requests at eight readers. All 800 measured requests and 240 warm-up
requests completed without HTTP errors. Public API and SSR responses carried
`Cache-Control: no-store`.

| Route | Warm p50 ms | Warm p95 ms | Maximum measured ms |
| --- | ---: | ---: | ---: |
| Feed | 29.05 | 53.94 | 56.96 |
| Page 500 | 59.82 | 71.35 | 81.69 |
| Broad Russian search | 61.20 | 78.49 | 86.68 |
| Combined category/tag | 17.18 | 19.66 | 29.59 |
| Homepage API | 75.94 | 81.94 | 99.62 |
| Public tags, first 100 | 26.79 | 36.50 | 46.74 |
| Related articles | 28.73 | 35.01 | 47.07 |
| Homepage SSR | 256.37 | 302.44 | 2305.11 |
| List SSR | 637.09 | 875.91 | 2957.98 |
| Article SSR | 571.43 | 743.10 | 785.71 |

The local targets of API p95 <= 500 ms and SSR p95 <= 1,000 ms passed in this
final run. Latency tails remain: warm-up reached 2,791 ms and measured SSR
requests reached 2,958 ms. An earlier fresh post-migration run also exceeded
the tag p95 target (1,195 ms); its repeat on the same fixture measured 36.50 ms.
This short local run leaves production latency and long-run behavior unmeasured. The pre-change broad-search p95 was 1,906 ms
and list/article SSR p95 was 4,624/4,241 ms.

Environment: Windows 11 build 28000, 12 logical CPUs; Docker Desktop 29.9.3 with
12 CPUs and 7,974,043,648 bytes of VM memory; PostgreSQL 18.6 Alpine x86-64;
.NET SDK 10.0.403; Node 26.8.1. Host API/SSR processes used Release/production
builds and loopback HTTP for measurements. HTTPS and browser behavior were
verified separately. Informational SQL logging remained enabled.

After measurement, API/SSR working sets were approximately 231/281 MiB,
compared with 178/92 MiB before measurement. The figures are point-in-time
working sets. Representative final EXPLAIN ANALYZE times
were feed 0.390 ms, search 18.395 ms, image visibility 0.031 ms and tag page
3.221 ms. The helper saves plans with buffers and full environment/latency/memory
JSON in ignored `.local/issue14/capacity_<uuid>/` artifacts during measurement.
The owner-requested cleanup removed local artifacts and disposable fixtures.

See the [capacity procedure](development_instructions/development_and_verification/news_capacity_check.md).
The fixture used 10,000 public articles with roughly 3,800 body characters,
100,000 image-reference rows, 100 categories and 1,000 tags. The load run
exercised image metadata and queries. Recovery and live Media checks covered
object delivery separately.

## CI and verification tooling

Completed on 2026-11-19 in `158e2ab` and `bcac0a6`.

The SonarCloud follow-up excludes the generated client under
`frontend/src/app/api/` from duplication analysis. The OpenAPI refresh and
capacity scripts use loopback addresses and reject redirects. CI checks the
generated client against the snapshot. It also downloads the live contract
into a temporary file and passes it to the comparator after a successful
download. The comparator owns the path to the checked-in snapshot. CI
removes the temporary file on exit.

On Windows, a clean install with scripts disabled passed formatting, client
generation, the production build, and all 99 frontend tests. Local script checks
confirmed TLS certificate and hostname verification, rejection of redirects
from the OpenAPI refresh and capacity scripts, and detection of contract drift.

## Author workspace CSS budget

Completed on 2026-11-16 in `b38ed07`.

The author workspace stylesheet exceeded Angular's 4 kB budget by one byte.
The upload-label margin and input width moved into
[the shared stylesheet](../frontend/src/styles.css) under the author workspace
selector. The author workspace kept the same styling, and the change resolved
the budget warning.

## Angular page initialization

Completed on 2026-11-13 in `8308e12`.

The profile, installation, staff accounts and Email delivery pages moved
their initial loads to Angular's `ngOnInit`. The profile also starts its timer
there and retains destruction cleanup. The setup tests run initialization
through Angular's component lifecycle. See [Stage 1 verification](stages/STAGE_1.md#verification-and-review)
for the recorded account and setup checks.
