using CP6.Core.Services.Integration;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.Services;

/// <summary>
/// <see cref="ISpaceNotifier"/> の SignalR 実装。
///
/// <see cref="SpaceHub"/> の全クライアントへ "LocationPublished" を配信する（低頻・全播、グループ無し）。
/// 照 <see cref="SignalRWmsNotifier"/>。
///
/// ★契約通り例外を投げない：try/catch で吞み、ログのみ ── プッシュ失敗は業務トランザクションに
/// 一切影響させない（サービス層は commit 後に呼ぶため、ここで落ちても既に確定済み）。
/// </summary>
public class SignalRSpaceNotifier : ISpaceNotifier
{
    private readonly IHubContext<SpaceHub> _hub;
    private readonly ILogger<SignalRSpaceNotifier> _logger;

    public SignalRSpaceNotifier(IHubContext<SpaceHub> hub, ILogger<SignalRSpaceNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyLocationPublishedAsync(string batchNo, int count, string status)
    {
        try
        {
            await _hub.Clients.All.SendAsync("LocationPublished", new { batchNo, count, status });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Space 発布プッシュに失敗（旁路、無視）: {BatchNo}", batchNo);
        }
    }
}
