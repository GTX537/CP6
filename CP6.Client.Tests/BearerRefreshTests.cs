using System.Net;
using System.Net.Http.Json;
using CP6.Client.Api;
using CP6.Client.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CP6.Client.Tests;

public sealed class BearerRefreshTests
{
    [Fact]
    public async Task Concurrent_401s_Share_One_Refresh()
    {
        var primary = new FakeServerHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRefreshTokenStore, MemoryTokenStore>();
        services.AddCp6ClientCore(new ClientOptions
        {
            ApiBaseAddress = new Uri("https://cp6.test/"),
            Platform = "windows",
            Context = new ClientContext
            {
                ClientKind = "Windows",
                DeviceId = "test-device",
                AppVersion = "1.0.0",
            },
        });
        services.AddHttpClient(ClientServiceCollectionExtensions.RawClient)
            .ConfigurePrimaryHttpMessageHandler(() => primary);
        services.AddHttpClient(ClientServiceCollectionExtensions.AuthenticatedClient)
            .ConfigurePrimaryHttpMessageHandler(() => primary);

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ClientAccessGate>().Update(
            new ClientUpgradeDecision { BusinessAllowed = true });
        var sessions = provider.GetRequiredService<IClientSessionService>();
        await sessions.AdoptAsync(new TokenSession
        {
            AccessToken = "old-access",
            RefreshToken = "refresh-1",
            Profile = new ClientProfile { UserName = "operator" },
        });

        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ClientServiceCollectionExtensions.AuthenticatedClient);
        var calls = Enumerable.Range(0, 8).Select(_ => client.GetAsync("api/protected"));
        var responses = await Task.WhenAll(calls);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, primary.RefreshCalls);
    }

    private sealed class MemoryTokenStore : IRefreshTokenStore
    {
        private string? _value;
        public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult(_value);
        public Task WriteAsync(string refreshToken, CancellationToken ct = default)
        {
            _value = refreshToken;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken ct = default)
        {
            _value = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeServerHandler : HttpMessageHandler
    {
        private int _refreshCalls;
        public int RefreshCalls => _refreshCalls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/client-auth/refresh")
            {
                Interlocked.Increment(ref _refreshCalls);
                await Task.Delay(25, cancellationToken);
                return Json(new TokenSession
                {
                    AccessToken = "new-access",
                    RefreshToken = "refresh-2",
                    Profile = new ClientProfile { UserName = "operator" },
                });
            }

            return request.Headers.Authorization?.Parameter == "new-access"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value),
        };
    }
}
