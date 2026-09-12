using CP6.Core.EFDbContext;
using System.Data;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Common;

/// <summary>
/// 全社統一採番ヘルパ。
/// 採番形式：機能コード(3) + 年(4) + 月(2) + 自増(4) = 13 桁（例：EMC2026050001）。
/// 自増は機能コードごとに永不重置（グローバル累計）。
/// </summary>
/// <remarks>
/// カウンタの加算は呼び出し側の SaveChanges と同一トランザクションで確定するため、
/// 採番→実体登録が原子的になる（従来の MAX(...)+1 と同等以上の整合性）。
/// 呼び出し側は本メソッド後に必ず SaveChangesAsync を行うこと。
/// SQL Server ORD allocation is immediately serialized across ordinary, backorder and CRM writers.
/// It joins an existing transaction; callers without one reserve a number independently (gaps are possible).
/// </remarks>
public static class DocNumber
{
    /// <summary>機能コード別の次番号を採番する。戻り値：(13桁番号, 自増値)。</summary>
    public static async Task<(string No, int Seq)> NextAsync(CP6Context db, string funcCode, DateTime? date = null)
    {
        var code = funcCode.ToUpperInvariant();
        if (code == "ORD" && db.Database.IsSqlServer())
            return await NextOrderAsync(db, date);
        // 同一 DbContext 内で複数回採番する場合（例：FSC 一括発行のループ）、
        // 未保存の追加済みカウンタ行を DB クエリは拾えないため、まず Local を確認する。
        var row = db.DocSequences.Local.FirstOrDefault(x => x.FuncCode == code)
                  ?? await db.DocSequences.FirstOrDefaultAsync(x => x.FuncCode == code);
        if (row == null)
        {
            row = new DocSequence { FuncCode = code, LastSeq = 0, CreateDate = DateTime.Now };
            db.DocSequences.Add(row);
        }
        row.LastSeq += 1;
        var d = date ?? DateTime.Today;
        return ($"{code}{d:yyyyMM}{row.LastSeq:D4}", row.LastSeq);
    }

    private static async Task<(string No, int Seq)> NextOrderAsync(CP6Context db, DateTime? date)
    {
        if (db.ChangeTracker.Entries<DocSequence>().Any(e => e.Entity.FuncCode == "ORD" &&
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("ORD_SEQUENCE_HAS_PENDING_MANUAL_CHANGE");
        await using var ownedTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            DECLARE @numbers TABLE (Number int NOT NULL);
            UPDATE dbo.T_DocSequence WITH (UPDLOCK,HOLDLOCK)
            SET LastSeq=LastSeq+1 OUTPUT inserted.LastSeq INTO @numbers WHERE FuncCode=N'ORD';
            IF @@ROWCOUNT=0
                INSERT dbo.T_DocSequence (Id,FuncCode,LastSeq,CreateDate)
                OUTPUT inserted.LastSeq INTO @numbers VALUES (@id,N'ORD',1,@now);
            SELECT Number FROM @numbers;
            """;
        var id = command.CreateParameter(); id.ParameterName = "@id"; id.Value = Guid.NewGuid(); command.Parameters.Add(id);
        var now = command.CreateParameter(); now.ParameterName = "@now"; now.Value = DateTime.UtcNow; command.Parameters.Add(now);
        var value = await command.ExecuteScalarAsync();
        var sequence = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        if (ownedTransaction is not null) await ownedTransaction.CommitAsync();
        return ($"ORD{date ?? DateTime.Today:yyyyMM}{sequence:D4}", sequence);
    }
}
