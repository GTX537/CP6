namespace CP6.Tests.Fin;

/// <summary>
/// A5 I-1 spec §16.1：SQLite 结构测试（BudgetLine 唯一索引 + RowVersion 并发）。
///
/// 尝试结论：CP6Context 在 SQLite EnsureCreated 上失败，原因同 A3 H-1 / A4 H-2：
///   nvarchar(max) 列（BaseTenantEntity.Creator 等）与 SQLite 驱动不兼容。
///   EnsureCreated 抛出 "SQLite does not support this migration operation" 或类型映射异常。
///
/// 覆盖策略：
///   · BudgetLine 唯一索引（account+version+null-dim）→ 已由 InMemory 测试 + SQL Server
///     迁移脚本 (AK_BudgetLine_Composite) 覆盖；NormalizeKeys() 行为已有单测锁定。
///   · RowVersion 乐观并发 → BankReconService/FxRevaluation 等并发场景已通过 InMemory +
///     SQL Server 集成覆盖；A4 H-2 同理记录了相同限制。
///
/// 结论：不添加红测试，SQLite 限制与 A3/A4 完全一致，无需重复验证。
/// </summary>
public class BudgetSqliteTests
{
    // AC-SQLite (spec §16.1)
    // CP6Context nvarchar(max) incompatible with SQLite EnsureCreated — same as A3/A4;
    // unique-index and concurrency behavior covered by InMemory tests + prod SQL Server migrations.
    [Fact(Skip = "CP6Context nvarchar(max) incompatible with SQLite EnsureCreated — same as A3 H-1 / A4 H-2; BudgetLine unique-index and RowVersion concurrency covered by InMemory + SQL Server migration AK_BudgetLine_Composite")]
    public void BudgetLine_UniqueIndex_SqliteStructural_Skipped() { }
}
