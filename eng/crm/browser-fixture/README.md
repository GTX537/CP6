# Real CORE browser fixture

This helper creates a new `CP6OidcTest_<GUID>` SQL Server database with the current
complete CORE EF schema and OIDC grant tables. It seeds real tenant, department,
BCrypt user, role, menu, action and data-scope rows. It does not mint a session,
start an issuer, inject cookies or replace password authentication.

Provide a JSON settings file outside version control:

```json
{
  "coreOrigin": "https://localhost:17443",
  "crmCallbackUri": "https://crm.dev.localhost:17444/crm/auth/callback",
  "crmPostLogoutUri": "https://crm.dev.localhost:17444/crm/logged-out",
  "organizationId": "4f082b33-c930-4aa8-8107-8597d2c2e5cc",
  "organizationSlug": "crm-browser",
  "region": "local"
}
```

Use the callback and logout paths actually registered by the CRM BFF, and its
isolated organization identifier. All three URLs must use HTTPS loopback hosts
or reserved `*.dev.localhost` hosts (resolved to loopback by the test browser).
Using `localhost` for CORE and `crm.dev.localhost` for CRM exercises a cross-site
redirect while matching the SANs in a locally trusted development certificate.
Set `CP6_OIDC_TEST_SQL` to the test SQL instance (the supplied database name is
ignored), then run from the CORE repository:

```powershell
dotnet run --project eng/crm/browser-fixture -- <settings.json> <ignored-output-parent>
```

The generated directory contains `browser-fixture.json` (five login passwords,
user/department IDs and the BFF client secret) and `appsettings.Local.json`
(connection, temporary signing keys and real auth configuration). Keep the output
outside version control and do not publish these files as test evidence. The
helper prints only the generated database name and directory.

Launch the actual `CP6.WebApi.dll` with `ASPNETCORE_ENVIRONMENT=Development`,
`--contentRoot <generated-directory>` and a loopback `--urls` address. Remove any
inherited configuration overrides that would select another database or issuer.
Use an HTTPS loopback reverse proxy to serve the existing Vue application and
forward `/api`, `/connect` and `/.well-known` to this API. Trust the temporary
certificate locally, and register the generated issuer/client secret with the
real CRM BFF. Complete login in Vue using a generated password before following
the actual authorization-code/PKCE flow.

`supervisor` has organization scope, lead query/add/edit/assign/view-pii and site
query/configure. `owner` has own scope and lead query/add/edit/view-pii in a child
department. `otherowner` has the same actions in another department. `queryonly`
has own scope and query. `masked` has own scope and query/edit without view-pii.
No refresh tokens, browser families or grants exist before real login.
Tenant expiry is two days; the helper always creates a fresh database and does
not reuse or alter an existing database. Shut down the fixture processes and
remove only the generated database and directory after testing.

This fresh model bootstrap is integration test data, not migration, provisioning
or production acceptance. Forward-upgrade compatibility has separate real SQL
tests in `CP6.Oidc.IntegrationTests`.
