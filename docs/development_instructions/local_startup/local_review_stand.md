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

## Telegram and OpenAI stubs

Run `python scripts/start-stand.py --integration-stubs` to start the stand with
local provider substitutes. The override supplies fixture credentials. Use this
mode with demo articles and a fixture channel such as `@cityportal_demo_stub`.

In `/staff/integrations`, save the fixture channel and enable Telegram and AI
suggestions. In `/staff/publications`, open a published article, generate a
suggestion, edit it, and confirm the send. The OpenAI stub builds its draft from
the public title and summary. The Worker delivers the approved text using the
usual queue and records the Telegram message ID returned by the stub.

Open `http://localhost:8090/posts` to compare the delivered text and article URL
against the approved version. The app builds the standard `t.me` link from the
fixture channel and message ID. The matching post exists in the local list.
The stand keeps that list in the `integration-stub-posts` Compose volume.

Before switching the stand back to real providers, let the Worker finish test
sends and disable Telegram and AI in `/staff/integrations`. Stop the stub service
with `docker compose -f compose.yaml -f compose.stand.yaml -f compose.integration-stubs.yaml stop integration-stubs`,
then start the stand without `--integration-stubs`.
