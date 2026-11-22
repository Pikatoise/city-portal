# Prerequisites

- Docker Desktop with Linux containers and Docker Compose 2.24.4 or later.
- For host development, the .NET SDK pinned by `global.json` and Node.js 26.8.1.
- Git. No cloud Email provider or public domain is required for local development.

Run guide commands from the repository root unless the guide changes directory. In Windows PowerShell, use
`npm.cmd` instead of `npm` when the execution policy blocks `npm.ps1`.

To use an installed project-local .NET SDK in the current PowerShell session:

```powershell
$env:DOTNET_ROOT = Join-Path $PWD '.local/dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
```

Keep local SDK binaries, credentials, reports and logs out of Git.
