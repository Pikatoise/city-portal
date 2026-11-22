# Legacy Administrator Bootstrap and Staff Screens

For a migrated installation without an administrator, register and confirm the
intended account before running:

```powershell
docker compose run --rm --no-deps api --bootstrap-admin resident@example.test
```

Use the intended account's Email. The command exits with code 1 if an
administrator already exists. See [Stage 1](../../stages/STAGE_1.md) for bootstrap,
role and delivery contracts.

| Route | Purpose |
| --- | --- |
| `/staff/accounts` | Search/filter accounts, manage roles and inspect role history. |
| `/staff/email-deliveries` | Inspect sanitized failures and replay eligible failed deliveries. |
| `/account/forgot-password` | Request recovery and inspect the message in Mailpit. |
| `/account` | Check confirmation and edit display name or password. |

Set `CITY_TIME_ZONE` in Compose or `City__TimeZone` for a host API to
configure the legacy fallback. Use an IANA identifier such as `Europe/Moscow`.
UTC is the development default. The API normalizes supported Windows
identifiers to IANA and rejects invalid values at startup. New installations
use the persisted wizard setting.
