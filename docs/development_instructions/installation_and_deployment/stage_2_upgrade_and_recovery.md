# Stage 2 Upgrade and Recovery

See [Compose startup](../local_startup/compose_startup.md) for the migration command and [service diagnostics](../development_and_verification/services_and_diagnostics.md) for readiness endpoints.

Before applying `DiscoveryReadPerformance`, stop API writes and the Worker and
take a consistent PostgreSQL/object/key backup. Allow time and disk headroom
for updating all retained revisions. See the
[News read optimization](../../OPTIMIZATIONS.md#news-discovery-queries) for the
migration's changes and verification. The update takes PostgreSQL DDL and
write locks. A failed migration rolls back and can be retried. Apply the
explicit migration twice to verify repeat application. Start API and Worker, then verify
readiness and publication visibility.

Keep PostgreSQL, processed objects, Data Protection keys and private deployment
configuration as one recovery set. Take a custom-format PostgreSQL dump and
export objects using authenticated S3 operations with full listing pagination.
A live object-store directory copy cannot serve as a portable backup.
Restrict backup access like production credentials and
keep it outside source control. Keep writers stopped until all parts are
captured, or use a separately designed consistent snapshot scheme.

Restore into a new database, private bucket and key directory/volume. Configure
the restored API/Worker for these resources and a separate broker vhost. Restore
keys before protected-payload delivery. Apply explicit migrations, start the
application, and verify public/private images, retained history, SMTP decryption
and pending work. Confirm the Worker resumes Processing rows from staged payloads
and retries Deleting rows. Keep the original environment until recovery is
reviewed. See the [combined recovery check](../../stages/STAGE_2.md#combined-recovery).
