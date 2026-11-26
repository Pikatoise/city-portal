# Stage 2 Upgrade

See [Compose startup](../local_startup/compose_startup.md) for the migration command and [service diagnostics](../development_and_verification/services_and_diagnostics.md) for readiness endpoints.

Before applying `DiscoveryReadPerformance`, follow the
[backup procedure](backup_and_restore.md#back-up-the-installation).
Allow time and disk headroom for updating all retained revisions.
The update takes PostgreSQL DDL and write locks. A failed migration rolls
back and can be retried. Apply the explicit migration twice to verify repeat
application. Start API and Worker, then verify readiness and publication
visibility.

See the [News read optimization](../../OPTIMIZATIONS.md#news-discovery-queries)
for the migration's changes and verification. Use the
[restore procedure](backup_and_restore.md#restore-and-verify) to rehearse
recovery on a separate environment.
