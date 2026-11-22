# First-Run Setup

Follow [Compose startup](../local_startup/compose_startup.md) to build, migrate and start an installation.

After migrations, open `https://localhost:8443/setup` with the base Compose
stack. It enables setup and binds published ports to loopback. To install on
a remote server, keep that binding and connect through an SSH tunnel:

```powershell
ssh -L 8443:127.0.0.1:8443 user@server
```

Before completing setup, set `PUBLIC_ORIGIN` to the final HTTPS origin for Email
links. Local development defaults to `https://localhost:8443`. Enter the site
and administrator details and SMTP settings. For Compose Mailpit, use
`mailpit:1025`, no TLS and a fictional sender such as `noreply@cityportal.test`.
For an external provider, use its TLS settings (commonly STARTTLS on 587 or
TLS on connection on 465). Fix any reported SMTP or database failure and retry.
After setup succeeds, sign in normally. See the
[setup contract](../../stages/STAGE_1.md#first-run-setup-contract) for fields,
access gating and legacy migration behavior.
