namespace CP6.Core.Utilities;

/// <summary>
/// 業務イベント通知メッセージ。
///
/// 操作ログ（高スループット・append-only → Kafka）とは別物で、
/// 「人が気づくべき業務イベント/アラート」を表す低頻度・確実配信向けのペイロード。
/// RabbitMQ で運ぶのに適する（per-message ack・ルーティング・リトライ・dead-letter）。
/// </summary>
public class BusinessNotification
{
    /// <summary>イベント種別（例: OutboundShipped / StockTakeDiff / InboundReceived / LowStock）。</summary>
    public string EventType { get; set; } = "";

    /// <summary>重要度（Info / Warning / Error）。</summary>
    public string Level { get; set; } = "Info";

    /// <summary>通知タイトル（一覧表示用の短文）。</summary>
    public string Title { get; set; } = "";

    /// <summary>通知本文。</summary>
    public string Message { get; set; } = "";

    /// <summary>発生元モジュール（例: WMS / MES / ERP）。</summary>
    public string? Source { get; set; }

    /// <summary>関連伝票番号など（例: 出荷番号・棚卸番号）。</summary>
    public string? RefNo { get; set; }

    /// <summary>発生日時。</summary>
    public DateTime CreateDate { get; set; } = DateTime.Now;
}

/// <summary>
/// 業務イベント通知の発行口。RabbitMQ 実装（<see cref="RabbitMQService"/>）が担う。
///
/// 設計意図：操作ログは Kafka 専任、業務通知/アラートは RabbitMQ — 各 MW を
/// 「機能適性」で使い分ける。通知は確実に届けたい/ルーティングしたい低頻度イベント。
/// </summary>
public interface INotificationPublisher
{
    /// <summary>RabbitMQ 接続済みか。未接続なら呼び出し側は best-effort でスキップ。</summary>
    bool IsConnected { get; }

    /// <summary>業務イベント通知をキューへ発行する（best-effort、業務処理は止めない）。</summary>
    Task PublishNotificationAsync(BusinessNotification notification);
}
