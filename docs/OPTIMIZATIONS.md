# CityPortal Optimizations and Refactoring

Record completed optimizations, refactors and related engineering improvements
here. Each entry identifies the affected area, the reason for the change,
the implementation and the verification or limits of the evidence.
Product and architecture contracts remain in [VALUE](VALUE.md), [CORE](CORE.md)
and the stage specifications. Operating procedures are in
[DEVELOPMENT](DEVELOPMENT.md).

## Draft write concurrency

Completed on 2026-11-19 in `4a626c3`. The previous exclusive role lock
serialized draft writes across unrelated authors. The targeted change lets
independent ordinary drafts progress together.

The administration advisory lock coordinates setup, bootstrap, role changes
and writes using `AccountAuthorization`. Those writes take it before the
normalized Email and actor account locks, recheck the current session and
role, and then acquire the workflow resource locks. Password and profile
changes take Email and account locks in that order. Registration and login lock the Email.
Logout revokes the session record, which subsequent authorization checks
read. Role changes hold the exclusive lock through their dependent News and
Citizen Submissions updates. The commit releases transaction locks before
post-commit notification flushing.

Private draft saves share the administration lock with other draft saves.
They hold one article row and replace one working revision's Media references,
with image IDs locked in sorted order. The other workflows using this lock
keep exclusive mode. An approved save can refresh multiple revisions. It
releases the shared transaction before making changes, starts an exclusive
transaction, and validates the actor, ownership and version again.

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

The PostgreSQL regression for unrelated authors observed one save waiting
on an article row. Before the change the second save waited behind it. With
the shared lock, the second save committed while the first row remained locked.
The role-revocation regression verified both commit orders. A revocation
that committed first denied the waiting save. A save that committed first
updated the draft once, and subsequent requests lost author access.

The full Release .NET run passed 191 tests with zero failures or skips.
It also exercised last-administrator protection, session revocation,
approved saves, notification rollback and the Stage 3 role-removal race.
Solution formatting verification passed.

## News discovery queries

Completed on 2026-11-17 in `1f67b7c`.

The initial capacity check exposed slow broad search and taxonomy reads. The
`DiscoveryReadPerformance` News migration adds a partial index on the current
public `PublishedRevisionId` and selects `MAIN` storage for the generated
search vector. Recomputing existing generated values applies the physical
storage choice to existing revisions without changing text or article versions.
The upgrade regression verifies preserved content/search, the physical policy,
the index and repeat application.

The upgrade regression checks a large vector under the preceding EXTENDED policy
and verifies its PostgreSQL row version changes during migration. Removing the
recomputation UPDATE failed the assertion; restoring it passed the focused test.
Specification and standards reviews resolved their findings. Failed helper commands
retain stdout/stderr before checking their exit code.

An isolated comparison measured the representative tag-page query at 62.428 ms
before the index and 4.078 ms after it. A representative broad ranked search
measured 471.552 ms before the storage change and 20.571 ms after it. These are
single-query diagnostics, separate from HTTP percentiles.

`SET STORAGE` sets the policy for future writes. See the
[PostgreSQL 18 ALTER TABLE reference](https://www.postgresql.org/docs/18/sql-altertable.html).
The update takes normal PostgreSQL DDL and write locks. A migration failure
rolls back, and a later explicit run may retry it. Follow the
[upgrade procedure](DEVELOPMENT.md#stage-2-upgrade-and-recovery) for writer
shutdown, backup and disk headroom. The change used the existing PostgreSQL
search path and the existing no-store response policy.

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

### Reproduce the capacity check

Build the Release API and production Angular SSR, then run the capacity check.

```powershell
python scripts/check-news-capacity.py
```

The helper requires the local Compose infrastructure and ignored `.env`. Each
fresh run creates a unique isolated database and broker vhost. It leaves those
fixtures and ignored results, plans and keys for inspection. It stops its
API/SSR processes on exit. `--serve`
keeps them available for local review; interrupt it to stop. `--reuse` accepts
only a previously created `.local/issue14/capacity_<uuid>` directory.

The baseline contains 10,000 public articles with roughly 3,800 body characters,
100,000 image-reference rows, 100 categories and 1,000 tags. Reference rows
exercise metadata and query volume. Recovery and live Media checks cover
object delivery functionally. This workload uses ordinary article sizes and
short measurement runs.

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
