using CP6.WebApi.Services;

namespace CP6.Oidc.IntegrationTests;

[Collection(OidcRelationalCollection.Name)]
public sealed partial class GrantStoreRelationalTests(OidcRelationalFixture fixture)
{
    [Fact]
    public async Task Twenty_four_independent_connections_redeem_exactly_once()
    {
        var grant = new CrmOidcGrant
        {
            Id = Guid.NewGuid(), CodeHash = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken()), ClientId = "CP6.Web",
            RedirectUri = "https://crm.example/signin-oidc", Challenge = CrmOidcCrypto.Challenge(CrmOidcCrypto.RandomToken()),
            Nonce = CrmOidcCrypto.RandomToken(), SubjectId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
            SourceJti = Guid.NewGuid().ToString(), SecurityStamp = new string('A', 64),
            SourceRefreshHash = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken()),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1), AccessExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            SourceExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        };
        await fixture.CreateStore().SaveAsync(grant);

        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ =>
            fixture.CreateStore().ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge)));

        Assert.Single(results, result => result != null);
        Assert.True((await fixture.CreateStore().FindSessionAsync(grant.Id))!.Consumed);
    }
}
