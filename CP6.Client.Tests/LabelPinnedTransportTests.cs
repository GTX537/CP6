using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CP6.Client.Api;
using CP6.Client.Core;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Client.Tests;

public sealed class LabelPinnedTransportTests
{
    [Fact]
    public async Task A_different_actual_actor_cannot_send_a_pinned_request()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Adopt(Token(fixture.Identity.TenantId, Guid.NewGuid()));
        var error = await Assert.ThrowsAsync<LabelGatewayRecoveryException>(() => fixture.SendPinned());
        Assert.Equal("RecoveryScopeChanged", error.Code);
        Assert.Empty(fixture.Server.BusinessRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Safe_refresh_keeps_the_original_scope_and_rejects_an_actor_change(bool changeActor)
    {
        await using var fixture = await Fixture.Create();
        fixture.Server.FirstUnauthorized = true;
        fixture.Server.RefreshedToken = Token(fixture.Identity.TenantId,
            changeActor ? Guid.NewGuid() : fixture.Identity.ActorId, serial: 2);
        if (changeActor)
        {
            var error = await Assert.ThrowsAsync<LabelGatewayRecoveryException>(() => fixture.SendPinned());
            Assert.Equal("RecoveryScopeChanged", error.Code);
            Assert.Single(fixture.Server.BusinessRequests);
        }
        else
        {
            using var response = await fixture.SendPinned();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, fixture.Server.BusinessRequests.Count);
            Assert.Equal(fixture.Server.RefreshedToken, fixture.Server.BusinessRequests[1].Token);
        }
        Assert.Equal(1, fixture.Server.RefreshCalls);
        Assert.All(fixture.Server.BusinessRequests, request => Assert.True(request.Pinned));
    }

    [Fact]
    public async Task Endpoint_changes_cannot_retarget_recovery_but_still_apply_to_ordinary_requests()
    {
        await using var fixture = await Fixture.Create();
        fixture.Options.ApiBaseAddress = new Uri("https://next-cp6.test/");
        var error = await Assert.ThrowsAsync<LabelGatewayRecoveryException>(() => fixture.SendPinned());
        Assert.Equal("RecoveryScopeChanged", error.Code);
        Assert.Empty(fixture.Server.BusinessRequests);
        using var ordinary = await fixture.Client.GetAsync("api/ordinary");
        Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
        Assert.Equal("next-cp6.test", Assert.Single(fixture.Server.BusinessRequests).Host);
    }

    [Theory]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public async Task Actual_DI_captures_the_session_partition_and_uses_the_durable_gateway(string actorClaim)
    {
        await using var fixture = await Fixture.Create(actorClaim);
        await fixture.Provider.GetRequiredService<LabelGatewayService>().ProcessOnceAsync();
        var request = Assert.Single(fixture.Server.BusinessRequests);
        Assert.True(request.Pinned);
        Assert.Equal(fixture.Identity.ScopeKey,
            fixture.Provider.GetRequiredService<ILabelGatewayContext>().Capture().ScopeKey);
        Assert.IsType<FileLabelGatewayJournal>(fixture.Provider.GetRequiredService<ILabelGatewayJournal>());
        Assert.Equal(0, fixture.Printer.Calls);
    }

    private static string Token(Guid tenant, Guid actor, string actorClaim = "sub", int serial = 1)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        { ["tenant_id"] = tenant, [actorClaim] = actor, ["serial"] = serial });
        return "e30." + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".test-signature";
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ServiceProvider Provider;
        public required ClientOptions Options;
        public required LabelGatewayIdentity Identity;
        public required Server Server;
        public required Printer Printer;
        public required HttpClient Client;

        public static async Task<Fixture> Create(string actorClaim = "sub")
        {
            var identity = new LabelGatewayIdentity(new Uri("https://cp6-pinned.test/"), Guid.NewGuid(), Guid.NewGuid(), "GW-PINNED");
            var options = new ClientOptions
            {
                ApiBaseAddress = identity.Source, Platform = "Windows",
                Context = new() { ClientKind = "Windows", DeviceId = identity.DeviceId },
                LabelJournalDirectory = Path.Combine(Environment.GetEnvironmentVariable("CP6_LABEL_CLIENT_ROOT")
                    ?? Path.Combine(Path.GetTempPath(), "CP6.LabelGatewayTests"), Guid.NewGuid().ToString("N"))
            };
            var server = new Server(); var printer = new Printer();
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton<IRefreshTokenStore, MemoryTokens>();
            services.AddSingleton<ILabelPrinter>(printer);
            services.AddCp6ClientCore(options);
            services.AddHttpClient(ClientServiceCollectionExtensions.RawClient).ConfigurePrimaryHttpMessageHandler(() => server);
            services.AddHttpClient(ClientServiceCollectionExtensions.AuthenticatedClient).ConfigurePrimaryHttpMessageHandler(() => server);
            var provider = services.BuildServiceProvider();
            provider.GetRequiredService<ClientAccessGate>().Update(new() { BusinessAllowed = true });
            var result = new Fixture { Provider = provider, Options = options, Identity = identity, Server = server,
                Printer = printer, Client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientServiceCollectionExtensions.AuthenticatedClient) };
            await result.Adopt(Token(identity.TenantId, identity.ActorId, actorClaim));
            return result;
        }

        public Task Adopt(string token) => Provider.GetRequiredService<IClientSessionService>().AdoptAsync(
            new() { AccessToken = token, RefreshToken = "refresh-test", Profile = new() { UserName = "operator" } });

        public Task<HttpResponseMessage> SendPinned()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Identity.Source, "api/pinned"));
            request.Options.Set(ClientRequestScope.Key, new(Identity.Source, Identity.TenantId, Identity.ActorId, Identity.DeviceId));
            return Client.SendAsync(request);
        }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class MemoryTokens : IRefreshTokenStore
    {
        private string? _value;
        public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult(_value);
        public Task WriteAsync(string value, CancellationToken ct = default) { _value = value; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken ct = default) { _value = null; return Task.CompletedTask; }
    }

    private sealed class Printer : ILabelPrinter
    {
        public int Calls;
        public Task PrintAsync(LabelJob job, CancellationToken ct = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class Server : HttpMessageHandler
    {
        public bool FirstUnauthorized;
        public int RefreshCalls;
        public string? RefreshedToken;
        public List<(string Host, string? Token, bool Pinned)> BusinessRequests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/client-auth/refresh")
            {
                RefreshCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(
                    new TokenSession { AccessToken = RefreshedToken!, RefreshToken = "next-refresh", Profile = new() { UserName = "operator" } }) });
            }
            BusinessRequests.Add((request.RequestUri.Host, request.Headers.Authorization?.Parameter,
                request.Options.TryGetValue(ClientRequestScope.Key, out _)));
            if (FirstUnauthorized && BusinessRequests.Count == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new PagedResult<LabelJob> { Items = Array.Empty<LabelJob>(), Total = 0, Page = 1, PageSize = 200 }) });
        }
    }
}
