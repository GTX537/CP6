using System.Security.Cryptography;
using System.Text;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CP6.Tests.Sys;

public sealed class NativeSsoGrantStoreTests
{
    [Fact]
    public async Task Grant_Is_Bound_To_Pkce_Device_And_Consumed_Once()
    {
        var store = Create();
        var client = Context();
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var request = await store.CreateRequestAsync(
            "cp6-desktop://auth/callback",
            challenge,
            client);
        var code = await store.CompleteAsync(
            request,
            Guid.NewGuid(),
            Guid.NewGuid());

        var grant = await store.ConsumeGrantAsync(code, verifier, client);

        Assert.Equal("cp6-desktop://auth/callback", grant.RedirectUri);
        var replay = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ConsumeGrantAsync(code, verifier, client));
        Assert.Equal("E-SEC-022", replay.Message);
    }

    [Fact]
    public async Task Rejects_Unregistered_Redirect_And_Device_Swap()
    {
        var store = Create();
        var client = Context();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateRequestAsync(
                "https://attacker.example/callback",
                new string('a', 43),
                client));

        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var request = await store.CreateRequestAsync(
            "cp6-desktop://auth/callback",
            challenge,
            client);
        var code = await store.CompleteAsync(
            request,
            Guid.NewGuid(),
            Guid.NewGuid());
        var swapped = Context();
        swapped.DeviceId = "other-device";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ConsumeGrantAsync(code, verifier, swapped));
        Assert.Equal("E-SEC-024", ex.Message);

        var wrongVersion = Context();
        wrongVersion.AppVersion = "1.0.1";
        var versionError =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ConsumeGrantAsync(
                    code,
                    verifier,
                    wrongVersion));
        Assert.Equal("E-SEC-024", versionError.Message);

        var grant = await store.ConsumeGrantAsync(code, verifier, client);
        Assert.Equal(client.DeviceId, grant.Client.DeviceId);
    }

    [Fact]
    public async Task Invalid_Pkce_Does_Not_Burn_Grant()
    {
        var store = Create();
        var client = Context();
        var verifier = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var request = await store.CreateRequestAsync(
            "cp6-desktop://auth/callback",
            challenge,
            client);
        var code = await store.CompleteAsync(
            request,
            Guid.NewGuid(),
            Guid.NewGuid());

        var invalid = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ConsumeGrantAsync(code, "wrong-verifier", client));
        Assert.Equal("E-SEC-024", invalid.Message);

        Assert.NotNull(await store.ConsumeGrantAsync(code, verifier, client));
    }

    [Fact]
    public async Task Concurrent_Exchange_Allows_Exactly_One_Consumer()
    {
        var store = Create();
        var client = Context();
        var verifier = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var request = await store.CreateRequestAsync(
            "cp6-desktop://auth/callback",
            challenge,
            client);
        var code = await store.CompleteAsync(
            request,
            Guid.NewGuid(),
            Guid.NewGuid());

        var exchanges = Enumerable.Range(0, 32)
            .Select(async _ =>
            {
                try
                {
                    await store.ConsumeGrantAsync(code, verifier, client);
                    return true;
                }
                catch (InvalidOperationException ex)
                    when (ex.Message == "E-SEC-022")
                {
                    return false;
                }
            });

        var results = await Task.WhenAll(exchanges);
        Assert.Single(results, succeeded => succeeded);
    }

    [Fact]
    public async Task Concurrent_Callback_Allows_Exactly_One_Grant()
    {
        var store = Create();
        var client = Context();
        var verifier = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var request = await store.CreateRequestAsync(
            "cp6-desktop://auth/callback",
            challenge,
            client);

        var callbacks = Enumerable.Range(0, 32)
            .Select(async _ =>
            {
                try
                {
                    return await store.CompleteAsync(
                        request,
                        Guid.NewGuid(),
                        Guid.NewGuid());
                }
                catch (InvalidOperationException ex)
                    when (ex.Message == "E-SEC-022")
                {
                    return null;
                }
            });

        var grants = await Task.WhenAll(callbacks);
        Assert.Single(grants, code => code is not null);
    }

    private static NativeSsoGrantStore Create()
    {
        return new NativeSsoGrantStore(
            new MemoryNativeSsoGrantCache(),
            Options.Create(new SecurityOptions
        {
            NativeClient = new NativeClientOptions
            {
                AllowedRedirectUris =
                [
                    "cp6-desktop://auth/callback",
                    "cp6-mobile://auth/callback",
                ],
            },
        }));
    }

    private static ClientContextDto Context() => new()
    {
        ClientKind = "Windows",
        DeviceId = "desktop-1",
        AppVersion = "1.0.0",
    };
}
