# Configure Local Credentials

Copy `.env.example` to `.env`. Set `POSTGRES_PASSWORD` and `RABBITMQ_PASSWORD`
to different random values containing only letters, digits, hyphens, or underscores.
Random hexadecimal values are suitable for both passwords. PostgreSQL credentials
form part of an Npgsql connection string, and RabbitMQ credentials form part of an
AMQP URI; the restricted local alphabet avoids quoting and URI-escaping ambiguity.
`.env` is ignored by Git. Do not reuse these development credentials for a deployment.

```powershell
Copy-Item .env.example .env
```

The Compose stack deliberately requires nonempty passwords. PostgreSQL and
RabbitMQ initialize credentials only on the first creation of their data volumes.
Changing `.env` later does not change an existing database user's password.
The local PostgreSQL connection disables GSS encryption because this development
stack uses password authentication without Kerberos.

Set separate random `MEDIA_ACCESS_KEY` and `MEDIA_SECRET_KEY` values in the
ignored `.env` file. [Compose](../../../compose.yaml) pins the local RustFS image,
persists objects in `media-data`, and binds port 9000 to loopback. It supplies
API and Worker with the Media endpoint, bucket and credentials. For external
storage, provision a private bucket and use HTTPS. Allow HTTP only for trusted
local development.

## References

- [Npgsql GSS encryption configuration](https://www.npgsql.org/doc/security.html).
