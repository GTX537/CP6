using CP6.Entity.DTOs;

namespace CP6.Core.Services;

/// <summary>
/// 仕掛チェック（MSBBPA050 第10章）— 訂正/削除時の生産予定状況検証。
/// </summary>
/// <remarks>
/// 本来は mcframe7 の `mcf.受注明細` / `品目オーダ情報` を参照するが、
/// 現環境では mcframe7 が無いため、Stub 実装で常に Level=0 を返す。
/// Phase 3 で実テーブル参照に差し替え可能なよう、インターフェース化。
/// </remarks>
public interface IWipCheckService
{
    /// <summary>
    /// 製品 CD に対する 3 段階仕掛チェックを実行。
    /// Level 0 = 問題なし（mcframe7 連携無し時もここを返す）
    /// Level 1 = 警告（生産予定未確定）— ユーザ確認後継続可
    /// Level 2 = エラー（生産予定確定済み）— 操作ブロック
    /// Level 3 = エラー（指図済み）— 操作ブロック
    /// </summary>
    Task<WipCheckResultDto> CheckAsync(string productCd);
}

/// <summary>
/// mcframe7 が存在しない環境向け Stub。常に Level=0（問題なし）を返す。
/// </summary>
public class NoOpWipCheckService : IWipCheckService
{
    public Task<WipCheckResultDto> CheckAsync(string productCd)
    {
        return Task.FromResult(new WipCheckResultDto
        {
            Level = 0,
            Message = "mcframe7 連携無し（仕掛チェックをスキップしました）"
        });
    }
}
