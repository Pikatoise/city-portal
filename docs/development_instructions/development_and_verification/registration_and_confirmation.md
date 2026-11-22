# Registration and Confirmation Configuration

Follow [local HTTPS certificate setup](../local_startup/local_https_certificate.md) before running the smoke checks.

Compose configures the local mail sender through the following settings, shared
by API and Worker:

| Configuration | Local value or purpose |
| --- | --- |
| `Mail:PublicOrigin` | `https://localhost:8443`, the origin used in confirmation links. |
| `Mail:PortalName` | `CityPortal`, displayed in Russian HTML and plain-text templates. |
| `Mail:SenderAddress` | `noreply@cityportal.test`, a fictional local sender. |
| `Mail:SmtpHost`, `Mail:SmtpPort` | `mailpit`, `1025`. |
| `Mail:UseTls` | `false` only for the isolated local SMTP receiver; use TLS for deployment. |
| `Mail:Username`, `Mail:Password` | Optional SMTP credentials, supplied outside source control. |
| `Security:DataProtectionKeysPath` | Shared persistent key directory. |
| `Security:TrustedProxies` | Exact trusted proxy IP addresses; untrusted forwarded headers are ignored. |

Register at `/account/register`, sign in at `/account/login` and check
delivery status at `/account`. Open the Mailpit link and click Confirm on the
page. Resend from the authenticated profile. See the
[account contract](../../stages/STAGE_1.md) for confirmation and recovery behavior.

The following HTTPS smoke check creates one disposable account and retains its
mail for inspection. It verifies CSRF rejection, registration, login, SMTP
templates, confirmation, repeated confirmation, profile status, and logout. It
uses the actual registration limit, so repeated runs can return HTTP 429.

```powershell
python scripts/check-registration.py --ca .local/cityportal-root.crt
```

To check public SSR and private staff browser shells without creating another
account or consuming registration quota:

```powershell
python scripts/check-registration.py --ca .local/cityportal-root.crt --shell-only
```

CI runs `check-registration.py --install` against a fresh disposable database in
local setup mode before checking normal account workflows. No key environment
variable is needed. This test mode creates an administrator with random
credentials; use the browser wizard for an installation you intend to keep.
`--smtp-host` is available for this test initialization path; its Compose default
is `mailpit`.

Health probes check dependencies. Check actual mail delivery through Mailpit
locally or the configured SMTP provider in deployment.

## References

- [Mailpit configuration](https://mailpit.axllent.org/docs/configuration/).
