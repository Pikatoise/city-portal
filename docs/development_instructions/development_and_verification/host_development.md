# Host Development and Checks

Check the [prerequisites](../local_startup/prerequisites.md) and [configure local credentials](../local_startup/local_credentials.md) before running the host processes.

Keep PostgreSQL, RabbitMQ, and Mailpit running in Compose. Set
`ConnectionStrings__Database` and `ConnectionStrings__Messaging` in the current
process or .NET user secrets, using `localhost` and the `.env` credentials.
The API uses port 5080 for local development. Never commit real connection strings.
For host API/Worker development, set `Security__DataProtectionKeysPath` to the same
ignored local directory in both processes, for example `.local/protection-keys`
resolved to an absolute path. Also set `Mail__SmtpHost=localhost`,
`Mail__SmtpPort=1025`, and `Mail__UseTls=false` for host Worker access to Mailpit.
Keep `Mail__PublicOrigin=https://localhost:8443` when testing links through Compose.

```powershell
dotnet restore CityPortal.slnx
dotnet build CityPortal.slnx --configuration Release --no-restore
dotnet format CityPortal.slnx --verify-no-changes --no-restore
dotnet test CityPortal.slnx --configuration Release --no-build
dotnet run --project src/CityPortal.Api
```

In a separate terminal:

```powershell
cd frontend
npm.cmd ci
npm.cmd start
```

The frontend development server proxies `/api/` to `http://localhost:5080`.
Use the Compose HTTPS origin for cookie-authentication acceptance checks.
The generated client workflow and frontend test commands live in the frontend
package scripts. CI runs backend format/build/tests, frontend build/tests, and
the Compose image/migration/HTTPS smoke path on Linux.
The frontend CI job installs locked dependencies with lifecycle scripts disabled.
[CI and verification tooling changes](../../OPTIMIZATIONS.md#ci-and-verification-tooling)
record the SonarCloud follow-up and its verification.
