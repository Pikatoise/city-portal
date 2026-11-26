# Stage 6 Acceptance Tasks

Implementation baseline: `1c60bd9`. See the [Stage 6 specification](../stages/STAGE_6.md)
for contracts and completed checks. Track acceptance in [GitHub issue #17](https://github.com/Pikatoise/city-portal/issues/17).

- [ ] Run the full .NET Release suite on the implementation revision and record the
  result. The focused rerun passed all 16 cases, including the three failures
  from the interrupted full run.
- [ ] On an HTTPS test deployment, use a test bot and channel to send an approved
  Telegram announcement from the editorial workspace. Verify that its status and
  post link match the message in the channel.
- [ ] With a test OpenAI API key configured, generate a suggestion for a published
  article. Edit it and confirm that the approved text is what Telegram receives.
- [ ] With a test Auth0 tenant and preregistered MCP Inspector client, complete
  OAuth sign-in and verify that an assigned reader can list, search and open
  published news.
- [ ] Review the results with the owner. Record acceptance in the stage
  specification, update GitHub Issues and Projects, then remove this checklist.

See the [provider setup guide](../development_instructions/installation_and_deployment/integrations.md)
and the [Auth0 setup guide](../development_instructions/installation_and_deployment/mcp_auth0.md).
