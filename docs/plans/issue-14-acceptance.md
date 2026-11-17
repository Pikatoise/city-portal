# Issue 14: Stage 2 Acceptance

## Scope and completion boundary

Verify the delivered News, Media, Identity and Notifications workflows against
`../stages/STAGE_2.md`. Keep Stage 2 open until the owner reviews the workflows,
transaction/data flow and a failure scenario. Do not specify Stage 3 or deploy.

Baseline: `28c050d` on `prototype/stage-2-news-review`.

## Ordered work

1. Map each Stage 2 acceptance criterion to concrete automated or live evidence.
2. Verify a combined News/Media/Notifications recovery using an actual PostgreSQL
   dump/restore, an isolated object bucket and a separately restored protection
   key directory. Check public/private access, retained history, encrypted SMTP
   configuration, pending image processing and pending workflow Email.
3. Measure the public API and SSR with 10,000 published articles, 100,000 image
   references, 100 categories and 1,000 tags in an isolated database. Capture
   query plans, environment, latency and process memory. Use warm p95 <= 500 ms
   for public API requests and <= 1,000 ms for SSR at eight concurrent readers
   as local engineering acceptance targets; report all errors. These targets
   describe this measurement, not a production SLA.
4. Run real-infrastructure concurrency, authorization, rollback, outage,
   duplicate delivery and restart regressions. Run backend/frontend regression,
   builds, formatting, generated-client/runtime contract and migration checks.
5. Exercise production SSR and desktop/mobile browser flows. Record evidence,
   limitations and the owner's remaining review in an acceptance report.
6. Review the change against the specification and repository standards, fix
   findings and commit the verified work on the current branch.

## Verification record

Backend recovery/upgrade and complete regression passed; frontend checks,
runtime contracts and capacity measurements passed with recorded latency-tail
limitations. Split-role HTTPS/browser verification passed. Specification and
Standards reviews completed; both nonblocking improvements were fixed and
re-reviewed, including a successful mutation-sensitive upgrade regression.
Owner review remains open. Detailed
evidence is in [../STAGE_2_ACCEPTANCE.md](../STAGE_2_ACCEPTANCE.md).
