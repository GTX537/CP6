using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.DatabaseCompatibility.Testing;
using CP6.Entity.DomainModels.Wf;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace CP6.DatabaseCompatibility.ApplicationProbe;

internal static partial class AppProbe
{
    private static async Task SignalRVerificationAsync(OwnedTestDatabase owned, ProbeArguments options, SafeReport report, CancellationToken ct)
    {
        var adminPassword = SecretEnvironment("CP6_COMPAT_ADMIN_PASSWORD");
        var ordinaryUsername = SecretEnvironment("CP6_COMPAT_ORDINARY_USERNAME");
        var ordinaryPassword = SecretEnvironment("CP6_COMPAT_ORDINARY_PASSWORD");
        string tenantCode;
        Guid ordinaryId;
        await using (var db = Core(owned, options.Tenant))
        {
            tenantCode = await db.Sys_Tenants.Where(x => x.Id == options.Tenant && x.Enable).Select(x => x.TenantCode).SingleAsync(ct);
            Require(await db.Sys_Users.AnyAsync(x => x.Id == options.User && x.UserName == "admin" && x.Enable, ct),
                "signalr-admin-physical-user-matches");
            ordinaryId = await db.Sys_Users.Where(x => x.UserName == ordinaryUsername && x.Enable).Select(x => x.Id).SingleAsync(ct);
            Require(ordinaryId != options.User, "signalr-distinct-physical-users");
        }
        var adminCookies = new CookieContainer();
        var ordinaryCookies = new CookieContainer();
        await LoginAsync(options.BaseUri!, adminCookies, "admin", adminPassword, tenantCode, ct);
        await LoginAsync(options.BaseUri!, ordinaryCookies, ordinaryUsername, ordinaryPassword, tenantCode, ct);
        report.Counts["LoginProfiles"] = 2;
        report.Assertions.Add(new("signalr-two-http-users-authenticated", true, ExpectedCount: 2, ActualCount: 2));
        report.Inputs.Add(AssemblyInput(typeof(HubConnection)));
        var adminEvents = new ConcurrentQueue<NotificationReceipt>();
        var ordinaryEvents = new ConcurrentQueue<NotificationReceipt>();
        HubConnection? admin = null;
        HubConnection? ordinary = null;
        IDisposable? adminSubscription = null;
        IDisposable? ordinarySubscription = null;
        try
        {
            admin = SignalRConnection(options.BaseUri!, adminCookies);
            ordinary = SignalRConnection(options.BaseUri!, ordinaryCookies);
            adminSubscription = admin.On<JsonElement>("WfNotification", payload => adminEvents.Enqueue(Receive(payload)));
            ordinarySubscription = ordinary.On<JsonElement>("WfNotification", payload => ordinaryEvents.Enqueue(Receive(payload)));
            await admin.StartAsync(ct);
            await ordinary.StartAsync(ct);
            Require(admin.State == HubConnectionState.Connected && ordinary.State == HubConnectionState.Connected,
                "signalr-two-websocket-clients-connected");
            report.Counts["SignalRClients"] = 2;
            report.Assertions.Add(new("signalr-two-websocket-clients-connected", true, ExpectedCount: 2, ActualCount: 2));
            var target = await CreateSignalRNotificationAsync(owned, options, ct);
            target = await ObserveSignalRDispatchAsync(owned, options, target.Id, adminEvents, ct);

            // Persisted status is saved only at the end of a production dispatch batch. A new marker
            // inserted after observing it cannot belong to that already captured candidate list.
            // Repeat twice to prove two actual subsequent batches, without treating sleeps as cycles.
            var markerEvidence = new List<object>();
            var markerIds = new List<Guid>();
            for (var batch = 1; batch <= 2; batch++)
            {
                Require(admin.State == HubConnectionState.Connected && ordinary.State == HubConnectionState.Connected,
                    "signalr-clients-connected-during-observation");
                var markerOptions = options with { EventKey = options.EventKey + "-cycle-" + batch };
                var marker = await CreateSignalRNotificationAsync(owned, markerOptions, ct);
                marker = await ObserveSignalRDispatchAsync(owned, markerOptions, marker.Id, adminEvents, ct);
                markerIds.Add(marker.Id);
                markerEvidence.Add(new { Batch = batch, NotificationIdSha256 = Hash(marker.Id.ToString("D")),
                    StateSha256 = ObjectHash(marker), PayloadSha256 = adminEvents.Single(x => x.NotificationId == marker.Id).PayloadSha256 });
            }
            var replay = await ReplayAsync(owned, options, ct);
            var adminTarget = adminEvents.Where(x => x.NotificationId == target.Id).ToArray();
            var ordinaryTarget = ordinaryEvents.Where(x => x.NotificationId == target.Id).ToArray();
            var adminMarkers = adminEvents.Count(x => markerIds.Contains(x.NotificationId));
            var ordinaryMarkers = ordinaryEvents.Count(x => markerIds.Contains(x.NotificationId));
            var originalHash = ObjectHash(replay.Before);
            var replayedHash = ObjectHash(replay.After);
            NotificationReport(replay.After, options, report);
            report.Counts["AdminTargetEvents"] = adminTarget.Length;
            report.Counts["OrdinaryTargetEvents"] = ordinaryTarget.Length;
            report.Counts["SignalRMarkerRows"] = markerIds.Count;
            report.Counts["AdminMarkerEvents"] = adminMarkers;
            report.Counts["OrdinaryMarkerEvents"] = ordinaryMarkers;
            report.Counts["CompletedFollowupWorkerBatches"] = markerEvidence.Count;
            report.Hashes["TargetPayloadSha256"] = adminTarget.Length == 1 ? adminTarget[0].PayloadSha256 : Hash("no-single-target-payload");
            report.Hashes["FollowupBatchEvidenceSha256"] = ObjectHash(markerEvidence);
            report.Hashes["OriginalNotificationStateSha256"] = originalHash;
            report.Hashes["ReplayedNotificationStateSha256"] = replayedHash;
            var exact = adminTarget.Length == 1 && adminTarget[0].Valid && adminTarget[0].UserId == options.User
                && adminTarget[0].Type == WfNotificationType.TodoCreated && Dispatched(replay.After);
            report.Assertions.Add(new("signalr-target-delivery-exact", exact, ExpectedCount: 1, ActualCount: adminTarget.Length));
            report.Assertions.Add(new("signalr-other-user-excluded", ordinaryTarget.Length == 0 && ordinaryMarkers == 0,
                ExpectedCount: 0, ActualCount: ordinaryTarget.Length + ordinaryMarkers));
            report.Assertions.Add(new("signalr-worker-two-subsequent-batches-completed", markerEvidence.Count == 2 && adminMarkers == 2,
                ExpectedCount: 2, ActualCount: adminMarkers));
            report.Assertions.Add(new("signalr-target-not-repeated", adminTarget.Length == 1,
                ExpectedCount: 1, ActualCount: adminTarget.Length));
            report.Assertions.Add(new("signalr-production-replay-kept-row-and-state", replay.Written == 0
                && replay.Before.Id == replay.After.Id && originalHash == replayedHash, originalHash, replayedHash));
            Require(admin.State == HubConnectionState.Connected && ordinary.State == HubConnectionState.Connected,
                "signalr-clients-connected-through-final-observation");
            Require(report.Assertions.All(x => x.Passed), "signalr-exact-user-routing-and-persistence");
        }
        finally
        {
            adminSubscription?.Dispose();
            ordinarySubscription?.Dispose();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var connections = new[] { admin, ordinary }.Where(x => x is not null).Cast<HubConnection>().ToArray();
            try { await Task.WhenAll(connections.Select(x => x.StopAsync(cleanup.Token))); }
            finally { await Task.WhenAll(connections.Select(x => x.DisposeAsync().AsTask())); }
            var stopped = connections.Length == 2 && connections.All(x => x.State == HubConnectionState.Disconnected);
            report.Assertions.Add(new("signalr-clients-stopped", stopped, ExpectedCount: 2,
                ActualCount: connections.Count(x => x.State == HubConnectionState.Disconnected)));
            Require(stopped, "signalr-client-lifecycle-completed");
        }
    }

    private static string SecretEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Require(!string.IsNullOrWhiteSpace(value), "signalr-private-credentials-required");
        return value;
    }

    private static async Task LoginAsync(Uri baseUri, CookieContainer cookies, string username, string password,
        string tenantCode, CancellationToken ct)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = true, CookieContainer = cookies };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var response = await client.PostAsJsonAsync(new Uri(baseUri, "api/auth/login"),
            new { UserName = username, Password = password, TenantCode = tenantCode }, ct);
        Require(response.StatusCode == HttpStatusCode.OK, "signalr-http-login-success");
        using var profile = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        Require(profile.RootElement.TryGetProperty("userName", out var name) && name.GetString() == username,
            "signalr-http-profile-matches-user");
        Require(cookies.GetCookies(baseUri).Cast<Cookie>().Any(x => x.Name == "cp6_at" && !string.IsNullOrEmpty(x.Value)),
            "signalr-auth-cookie-issued");
    }

    private static HubConnection SignalRConnection(Uri baseUri, CookieContainer cookies) => new HubConnectionBuilder()
        .WithUrl(new Uri(baseUri, "hubs/notify"), options =>
        {
            options.Cookies = cookies;
            options.Transports = HttpTransportType.WebSockets;
            // An empty WebProxy bypasses every destination instead of inheriting the system proxy.
            options.Proxy = new WebProxy();
            options.HttpMessageHandlerFactory = handler =>
            {
                if (handler is not HttpClientHandler direct)
                    throw new ProbeFailure("signalr-direct-http-handler-required");
                direct.UseProxy = false;
                direct.AllowAutoRedirect = false;
                return direct;
            };
            options.WebSocketConfiguration = socket => socket.Proxy = new WebProxy();
        }).Build();

    private static NotificationReceipt Receive(JsonElement payload)
    {
        var valid = payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("notificationId", out var id) && id.TryGetGuid(out _)
            && payload.TryGetProperty("userId", out var user) && user.TryGetGuid(out _)
            && payload.TryGetProperty("type", out var type) && type.TryGetInt32(out _);
        return valid ? new(true, payload.GetProperty("notificationId").GetGuid(), payload.GetProperty("userId").GetGuid(),
            payload.GetProperty("type").GetInt32(), Hash(payload.GetRawText())) : new(false, Guid.Empty, Guid.Empty, 0, Hash(payload.GetRawText()));
    }

    private static async Task<Wf_Notification> CreateSignalRNotificationAsync(OwnedTestDatabase owned, ProbeArguments options, CancellationToken ct)
    {
        await using (var db = Core(owned, options.Tenant))
        {
            Require(!await db.Wf_Notifications.AnyAsync(x => x.EventKey == options.EventKey, ct), "signalr-event-key-new");
            await new NotificationService(db).CreateOutboxAsync(options.User, WfNotificationType.TodoCreated,
                "WP6 real WebSocket notification", "Task-owned transport observation", null, null, null,
                options.EventKey!, inAppRequested: true, emailRequested: false);
            await owned.VerifyAsync(ct);
            Require(await db.SaveChangesAsync(ct) == 1, "signalr-production-service-committed-one-row");
        }
        var row = await NotificationAsync(owned, options, ct);
        Require(row is not null, "signalr-one-owned-notification-visible");
        await RequireOneEventRowAsync(owned, options, ct);
        return row;
    }

    private static async Task<Wf_Notification> ObserveSignalRDispatchAsync(OwnedTestDatabase owned, ProbeArguments options,
        Guid id, ConcurrentQueue<NotificationReceipt> received, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await NotificationAsync(owned, options, ct);
            Require(row is not null && row.Id == id, "signalr-persisted-id-stable");
            var messages = received.Where(x => x.NotificationId == id).ToArray();
            Require(messages.Length <= 1, "signalr-no-duplicate-observed");
            if (Dispatched(row) && messages.Length == 1)
            {
                Require(messages[0].Valid && messages[0].UserId == options.User && messages[0].Type == WfNotificationType.TodoCreated,
                    "signalr-delivery-payload-matches-row");
                return row;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(150), ct);
        }
        throw new ProbeFailure("signalr-native-worker-delivery-timeout");
    }

    private sealed record NotificationReceipt(bool Valid, Guid NotificationId, Guid UserId, int Type, string PayloadSha256);
}
