namespace CP6.Entity;

/// <summary>
/// 字段级审计 opt-in 空标记接口（#4 字段审计 T1）。
/// 实体实现本接口即被 CP6Context.SaveChanges 写入管道纳入字段级 before/after 变更捕获。
/// 不实现则完全不参与审计（默认不审计，按需开启）。本身不映射任何列。
/// </summary>
public interface IAuditable
{
}
