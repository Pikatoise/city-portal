# Issue 13: Public news discovery

## Goal and observable behavior

Deliver the production SSR homepage and detailed news list using only current public revisions. Include Russian word-form search, combined category/tag/city-date filters, deterministic numbered pages, related news, editorial featured selection, current author credit, and safe public metadata. Preserve the article/alias/withdrawal behavior delivered in issue 12.

## Decisions and design

- VALUE.md, CORE.md, STAGE_2.md, NEWS.md, and UI_STYLE.md govern behavior and presentation.
- On 2026-11-16 the owner confirmed that editors choose two distinct homepage categories and their order. Administrator has the same effective editorial permission. Selection belongs to News; broader portal settings remain outside this task.
- Discovery joins Articles to PublishedRevisionId and filters current visibility before returning results. Retained and working revisions never become search results.
- PostgreSQL generated revision columns expose title/summary/body search, taxonomy filters and safe cover placements. Russian tsvector weighting is title A, summary B, body C. GIN search/tag indexes, category index and a partial publication-order index support database filtering and bounded reads.
- Default order is first publication descending then article ID ascending. Search adds descending rank before those tie-breakers. Related news orders shared-tag matches before additional same-category matches, newest first within each group, excluding self and duplicates, capped at five.
- DateOnly bounds represent inclusive city-local dates. Convert the start and exclusive following-day start to UTC using the installed city zone; reject malformed/reversed/unrepresentable input. Public UI pages contain 20 items; API defaults to 20 and caps at 100.
- Current Identity display names are fetched through an explicit bounded batch operation. Summaries omit private account fields, body, history, working revision state and unpublished SEO.
- Curation uses Identity's existing authorized transaction scope and lock ordering. Versioned editorial operations enforce capacity and audit changes atomically. Withdrawal removes featured selection and current public reads exclude the article immediately.
- Homepage categories retain their selected order and may be empty. Newly selected categories must be distinct and active. Later archival does not break existing selected blocks or published associations. Before initial configuration, the homepage shows featured/latest news without category blocks; no categories are chosen automatically.
- Public reads use generated TypeScript operations, omit credentials and disable transfer/browser/shared response caching. Staff writes use generated contracts and CSRF. Related-result failure leaves the main article available.
- Public layout follows the accepted visual language, with actual data, unavailable-image placeholders, keyboard carousel controls, 380ms settling, 650ms interruptible category scrolling, and 180ms content entrance.

## Initial state and execution

Branch: prototype/stage-2-news-review. Baseline: 4ec91bbd107d12c7be1ea93eb7231a8c6637b84c, synchronized with origin when work began. Pre-existing untracked .agents/ and skills-lock.json are preserved and excluded from this delivery.

The owner initially requested GPT coordination with DeepSeek implementation through Orca agent opencode2. Configured selection and actual session metadata both identified opencode/deepseek-v4.1-flash-free. GPT independently verified the session row and assistant-message model metadata in OpenCode's managed runtime SQLite database, read-only. Password-protected session export was unavailable; no credentials or service settings were changed for verification.

Orca ownership history:

| Task | Run / Task / Dispatch | Outcome |
| --- | --- | --- |
| T0 model verification | run_64fa747e2587 / task_c4594ed35af9 / ctx_2badfbdc32fc | Accepted; no product edits. |
| T1 initial implementation | same Run / task_4f194fc63896 / ctx_9943aca0c798 | Interrupted by provider free-usage quota after partial code edits. |

The owner then explicitly asked GPT to continue itself. Guarded worker-stop confirmed state stopped and processAction closed_agent_terminal before GPT edited product files. No replacement model or automatic quota retries were used. The pinned Orca executable was the installed ORCA_CLI_COMMAND; its orchestration and recovery guides governed worker management. Historical dispatch specifications and raw runtime reports remain ignored local artifacts.

## Tasks and review criteria

| ID | Outcome | Dependencies | Owned areas | Status |
| --- | --- | --- | --- | --- |
| T0 | Verify actual DeepSeek session before any worker edits | None | Read-only runtime metadata | Accepted |
| T1 | Current-publication list, Russian search, filters and migration | T0 | News discovery/model/migration, public controller, focused integration tests | Accepted |
| T2 | Related news and public-only taxonomy | T1 | News discovery, public controller, focused integration tests | Accepted |
| T3 | Atomic featured capacity and ordered category configuration | T2 | News curation/audit, editorial controller/contracts, integration tests | Accepted |
| T4 | Generated client, SSR list and related article integration | T3 | Generated API, public Angular components and routes | Accepted |
| T5 | Real homepage, category/search navigation and staff curation UI | T4 | Public components, shared navigation, editorial publications UI | Accepted |
| T6 | Regression checks, HTTPS desktop/mobile, documentation and review | T5 | Focused tests and affected specifications | Accepted |

## Validation and review evidence

See NEWS.md for the final requirement-to-test matrix, exact command outcomes, browser evidence and limitations. The early discovery/curation run passed 12 backend cases. These are included in the final regression and are not additive test totals.

Independent Spec review found missing scroll interruption, image-failure state leaking across reused carousel slides, and missing featured author credit. Standards review also found metadata wrapping. All were corrected; focused re-review confirmed the corrections. Tests cover image failure reuse, scroll interruption and cleanup, and current author credit.

The first full backend run exposed an outdated migration-count assertion and a test-only race between independently hosted API factories sharing the process-wide upload slot. Update the explicit migration expectation and serialize the two upload-using test classes in one xUnit collection; retain production limits and dedicated concurrency checks.

## Completion and limitations

All required implementation tasks are accepted. Final regression passed 163 backend cases with parsed TRX (zero failed/skipped) and 90 frontend cases. Release/SSR builds, formatting, runtime OpenAPI/generated-client consistency, explicit/repeat migrations and final HTTPS desktop/mobile/alias/withdrawal checks passed. Issue 14 owns cross-workflow final acceptance, representative capacity measurements, fresh backup/restore, failure/recovery demonstrations and owner review. Stage 2 stays open. No production deployment or new push is implied.


Final review found no remaining blocker after the documented corrections. Raw browser/results artifacts and the test research/plan/status files remain in ignored local storage. The implementation workflow records this delivery as a local commit on the current branch; no new push or deployment was requested.
