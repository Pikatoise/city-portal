# Backup and Restore

## Back up the installation

1. Stop API writes and the Worker. Keep writers stopped until all parts of the
   backup are captured, or use a separately designed consistent snapshot scheme.
2. Take a custom-format PostgreSQL dump. Export processed objects using
   authenticated S3 operations with full listing pagination. A live
   object-store directory copy cannot serve as a portable backup.
3. Save the Data Protection keys and private deployment configuration with
   the database dump and object export as one recovery set. Restrict access
   like production credentials and keep the backup outside source control.

## Restore and verify

1. Restore PostgreSQL and processed objects into a new database and private
   bucket. Restore the Data Protection keys to a separate directory or volume
   before processing protected payloads.
2. Configure the restored API and Worker for those resources and a separate
   broker vhost. Apply explicit migrations before starting the application.
   See [Compose startup](../local_startup/compose_startup.md).
3. Check [service readiness](../development_and_verification/services_and_diagnostics.md),
   public and private image access, retained history, SMTP decryption, and
   pending work. Verify that the Worker resumes Processing rows from staged
   payloads and retries Deleting rows.
4. Keep the original environment until the restored installation has been
   reviewed.

See the [Stage 2 recovery evidence](../../stages/STAGE_2.md#combined-recovery)
and [Stage 4 recovery evidence](../../stages/STAGE_4.md#upgrade-and-recovery-verification)
for the scenarios verified on isolated fixtures.
