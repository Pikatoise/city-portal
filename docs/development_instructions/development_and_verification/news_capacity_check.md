# Reproduce the Capacity Check

Build the Release API and production Angular SSR. Start local Compose
infrastructure with an ignored `.env`, then run:

```powershell
python scripts/check-news-capacity.py
```

Each fresh run creates an isolated database and broker vhost. The helper
stops its API/SSR processes on exit and leaves the fixtures, results, query
plans and keys under ignored `.local/issue14/capacity_<uuid>/` for inspection.
Use `--serve` to keep the processes up until interrupted and `--reuse` with a
directory created by the helper. Follow the
[workspace cleanup rules](../../../AGENTS.md#workspace-cleanup) once done. See the
[capacity results](../../OPTIMIZATIONS.md#capacity-results) for fixture size and
recorded measurements.
