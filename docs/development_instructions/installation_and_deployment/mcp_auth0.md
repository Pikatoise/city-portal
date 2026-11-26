# MCP access with Auth0

Use a test tenant and an HTTPS CityPortal origin when verifying MCP access.
The resource identifier is `PUBLIC_ORIGIN` followed by `/api/mcp`, with no
trailing slash. The issuer is the Auth0 tenant URL, including its trailing
slash. CityPortal checks access tokens intended for the MCP resource. Auth0
accounts for this integration are separate from CityPortal accounts.

## Auth0 setup

1. Enable the tenant's [Resource Parameter Compatibility Profile](https://support.auth0.com/center/s/article/mcp-audience-error-with-auth0).
   Create an Auth0 API with the exact CityPortal resource identifier and
   the RS256 signing algorithm. Use the default Auth0 access token profile,
   which carries the application ID in the `azp` claim.
2. Add the API permission `news:read` and enable RBAC. Assign the permission
   through an Auth0 role to the intended test reader. Leave self-registration
   disabled on the connection for the application.
3. Create a Native application for MCP Inspector with Token Endpoint
   Authentication Method set to None. Enable the Authorization Code grant
   and use PKCE (S256). Register Inspector's exact loopback callback from
   its OAuth settings. For the Inspector web client, the default callback
   is `http://localhost:6274/oauth/callback`. Register the same loopback
   origin in Auth0 Allowed Origins (CORS) if Inspector calls the Auth0
   token endpoint directly from the browser.
4. Set `MCP_AUTHORITY` to the Auth0 tenant issuer and `MCP_CLIENT_ID` to the
   preregistered application's client ID before starting the API. Compose
   maps them to `Mcp:Authority` and `Mcp:AllowedClientIds:0`. The resource
   server needs the client ID and the Auth0 discovery metadata.
5. Sign in to CityPortal as an administrator and enable MCP. The public
   HTTPS proxy forwards discovery at
   `/.well-known/oauth-protected-resource/api/mcp` and protocol requests at
   `/api/mcp` to the API.

## Verify the connection

In [MCP Inspector](https://github.com/modelcontextprotocol/inspector), select
Streamable HTTP, set the exact CityPortal resource URL and enter the
preregistered client ID in OAuth settings. Request `news:read` and sign in
as a reader assigned that permission. Open Tools, list the three tools,
run `list_latest_news`, and pass a returned `address` to `get_article`.
Use `search_news` with a short search term.

Use Inspector's local proxy for the protocol request. CityPortal accepts
requests without an Origin header or with its own configured origin.
If the deployment's trusted proxy forwards another origin, configure an exact
`Mcp:AllowedOrigins` entry on the API, such as
`Mcp__AllowedOrigins__0=http://localhost:6274`.

Check that anonymous requests to the enabled MCP endpoint return 401 with
`resource_metadata` in the Bearer challenge. A token for another resource
returns 401. A token for the correct resource from another application or
without `news:read` returns 403. Disabling MCP in CityPortal takes effect on
the next request. Changes to an Auth0 user assignment take effect when Auth0
issues another token or when the existing token expires.
