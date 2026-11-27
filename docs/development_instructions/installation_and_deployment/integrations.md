# Configure Integrations

Use the normal [migration and startup procedure](../local_startup/compose_startup.md)
when upgrading from Stage 5. The migration creates the `integrations` schema.
Include it in the regular database backup. An existing installation starts with
all integration switches off.

For local review without Telegram or OpenAI credentials, see the
[stand with provider stubs](../local_startup/local_review_stand.md#telegram-and-openai-stubs).

Open `/staff/integrations` as an administrator with confirmed Email. Enable each
integration when its deployment configuration is ready. Set `PUBLIC_ORIGIN` to
the public HTTPS origin before publishing links through RSS, Telegram, or MCP.

- **RSS.** Enable it in the administration page and open `/rss.xml` through the
  reverse proxy. Check that item links open the corresponding articles.
- **Weather.** Enter latitude and longitude in decimal degrees. Enable the widget
  and allow up to 30 minutes for Quartz to fetch an observation. Check the
  timestamp in administration and the result on the homepage. The open-access
  Open-Meteo endpoint is for the noncommercial demo.
- **Telegram.** Create a test bot and grant it permission to post in a test
  public channel. Set `TELEGRAM_BOT_TOKEN` in the deployment environment and
  recreate API and Worker containers. Save the channel's `@handle` and enable
  sending. Open a published article in `/staff/publications`, confirm the
  announcement, and verify the returned post link. If the result is uncertain,
  inspect the channel before attaching a post, authorizing another attempt, or
  closing the delivery. Resolve outstanding sends before changing the channel
  handle in Telegram.
- **AI.** Set `OPENAI_API_KEY` for the API container and recreate it. Enable AI and
  request a suggestion from the Telegram announcement form. Check the draft
  and edit it before confirming a test send.
- **MCP.** Set up [Auth0 and MCP Inspector](mcp_auth0.md), then enable the endpoint.

Keep the tokens out of command output, screenshots and issue reports. To
disable a provider, switch it off in administration. Already sent Telegram
posts remain under channel management. Worker and API logs report sanitized
provider outcomes. The admin page shows pending Telegram deliveries and those
requiring attention.
