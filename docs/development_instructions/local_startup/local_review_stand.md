# Local Review Stand

See [prerequisites](prerequisites.md) for the local tools.

Run `python scripts/start-stand.py` from the repository root. Use `--no-build`
to reuse images. The launcher prints the portal (`https://localhost:8443`),
pgAdmin and Mailpit URLs and credentials for five confirmed demo accounts:
RegularUser, Editor, Author, Moderator and Administrator.

`compose.stand.yaml` isolates the `cityportal_stand` database, RabbitMQ vhost
and private Media bucket. The launcher seeds demo categories and tags only
when the respective dictionary is empty. It keeps existing passwords in
ignored `.local/stand/accounts.json`. Service credentials stay in ignored `.env`.

Open pgAdmin at `http://localhost:5050` with `PGADMIN_EMAIL` (default
`pgadmin@example.com`) and `PGADMIN_PASSWORD`. The launcher generates a
password if omitted. Manual Compose starts use `POSTGRES_PASSWORD` as a
fallback. Initial credentials persist in the pgAdmin volume. Connect to
`postgres:5432` as `cityportal` with `POSTGRES_PASSWORD`. Stand accounts are in
**Databases > cityportal_stand > Schemas > identity > Tables > AspNetUsers**.
The `cityportal` database has its own accounts. Public Compose excludes
pgAdmin unless you opt into the loopback-only `local-tools` profile.
