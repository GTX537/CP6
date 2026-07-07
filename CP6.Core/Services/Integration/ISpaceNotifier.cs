namespace CP6.Core.Services.Integration;

/// <summary>
/// Space リアルタイム通知契約（発布/停用の SignalR プッシュ）。
///
/// 消費者 Space 側で定義、WebApi 側で SignalR 実装（<c>SignalRSpaceNotifier</c>）を注入。
/// 照 <see cref="IWmsStockQuery"/> のインターフェース注入範式（反射不使用）。
///
/// ★契約：実装は決して例外を投げてはならない（best-effort）。プッシュ失敗は業務トランザクションに
/// 一切影響させない ── 実装層で try/catch し、ログのみ。サービス層は commit 後に直接呼ぶ。
/// </summary>
public interface ISpaceNotifier
{
    /// <summary>
    /// 库位発布/停用の完了を全クライアントへ通知（低頻・全播、グループ無し）。
    /// </summary>
    /// <param name="batchNo">発布バッチ番号（LPUB-yyyyMMdd-nnnn）。</param>
    /// <param name="count">対象库位件数（発布=locs.Count、停用=1）。</param>
    /// <param name="status">結果ステータス（"SUCCESS" 等）。</param>
    Task NotifyLocationPublishedAsync(string batchNo, int count, string status);
}

/// <summary>
/// 空実装（テスト／降級用）。何もしない ── プッシュ経路が無い環境でも業務は素通り。
/// </summary>
public sealed class NoOpSpaceNotifier : ISpaceNotifier
{
    public Task NotifyLocationPublishedAsync(string batchNo, int count, string status)
        => Task.CompletedTask;
}
