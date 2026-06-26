using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CP6.Core.Services.Platform;

/// <summary>
/// GDPR purge（多租户合规 #5 块③ T7，R6）整租户物理删除的拓扑工具。
/// <para><see cref="GetOwnerEntityTypes"/>：R6 统一判式——所有<b>有 TenantId 列</b>且非 <c>Sys_Tenant</c> 的实体
/// （自动纳入 <c>BaseTenantEntity</c> 子类 ∪ 手加 TenantId 列的 <c>Sys_OperLog</c>）。</para>
/// <para><see cref="BuildDeleteOrder"/>：Kahn 拓扑排序得 <b>leaf-first 删除顺序</b>（先删子表后删父表，
/// 满足外键约束）；自引用环（如 <c>Wf_*</c> 父子）经 <c>cycleNodes</c> 返回，调用方先 null 其父 FK 再删。</para>
/// </summary>
public static class TenantPurgeTopology
{
    /// <summary>R6 统一判式：有 <c>Guid</c> 型 <c>TenantId</c> 列、且非共享表 <c>Sys_Tenant</c> 的实体。
    /// <para>显式要求 <c>TenantId</c> 为 <see cref="Guid"/>（含可空）——排除 <c>Sys_Lang.TenantId</c>（<c>int?</c> 语言覆盖位，
    /// 非真正的租户拥有行，类型不符且 GDPR 上不应按租户清除）。</para></summary>
    private static bool IsTenantOwned(IEntityType t)
    {
        if (t.ClrType == typeof(Sys_Tenant)) return false;
        var prop = t.FindProperty("TenantId");
        if (prop == null) return false;
        var clr = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
        return clr == typeof(Guid);
    }

    /// <summary>
    /// 拥有 TenantId 的实体类型集合（R6 统一判式，自动纳入 Sys_OperLog；排除共享表 Sys_Tenant 自身）。
    /// </summary>
    public static List<Type> GetOwnerEntityTypes(IModel model)
    {
        return model.GetEntityTypes()
            .Where(IsTenantOwned)
            .Select(t => t.ClrType)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Kahn 拓扑排序构造 leaf-first 删除顺序。
    /// <para><b>图建模</b>：节点 = owner 实体类型；对每条 FK <c>child → principal(parent)</c>（仅当父亦在 owner 集），
    /// 记一条依赖 <c>parent 依赖于 child</c>（即父必须在子之后删）。等价地：以"待删除前置数"为入度，
    /// 子表对父表贡献入度 → 入度为 0 的先出栈 = 叶子（无子表依赖它）先删。</para>
    /// <para><b>实现</b>：indegree[parent] += 1 for each (child→parent) 跨 owner 边（忽略自环、忽略指向非 owner 的边）。
    /// 反复取 indegree==0 的节点入结果、并对其"作为某些父的子"的出边减父入度。结果即 leaf-first 删除序。</para>
    /// <para><b>环</b>：自引用 FK（principal==declaring）天然记入 cycleNodes（其 ParentId 类列须先 null）；
    /// 拓扑收敛后仍有残留（多表互引环）的节点亦并入 cycleNodes 供调用方先打断。</para>
    /// </summary>
    public static (List<Type> order, List<Type> cycleNodes) BuildDeleteOrder(IModel model)
    {
        var entityTypes = model.GetEntityTypes()
            .Where(IsTenantOwned)
            .ToList();

        var owners = entityTypes.Select(t => t.ClrType).Distinct().ToList();
        var ownerSet = new HashSet<Type>(owners);

        // 自引用环：含指向自身的 FK 的类型（其父 FK 列须在删除前先 null）。
        var cycleNodes = new HashSet<Type>();

        // 入度（= 该类型作为"父"被多少跨 owner 边指向）+ 子→父邻接（用于减度）。
        var indegree = owners.ToDictionary(t => t, _ => 0);
        // child → 其依赖的父集合（去重，避免同对多 FK 重复计度）。
        var childToParents = owners.ToDictionary(t => t, _ => new HashSet<Type>());

        foreach (var et in entityTypes)
        {
            var childClr = et.ClrType;
            foreach (var fk in et.GetForeignKeys())
            {
                var parentClr = fk.PrincipalEntityType.ClrType;
                if (parentClr == childClr)
                {
                    // 自引用 FK（如 Wf_* 的 ParentId）→ 标记环节点，不计入正常拓扑度。
                    cycleNodes.Add(childClr);
                    continue;
                }
                if (!ownerSet.Contains(parentClr)) continue;        // 指向非 owner（如指向 Sys_Tenant/共享主数据）忽略
                if (childToParents[childClr].Add(parentClr))         // 去重边
                    indegree[parentClr]++;
            }
        }

        // Kahn：反复取入度 0（无子表依赖它的叶子）→ 出栈 → 对其各父减度。
        var queue = new Queue<Type>(owners.Where(t => indegree[t] == 0));
        var order = new List<Type>();
        var processed = new HashSet<Type>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!processed.Add(node)) continue;
            order.Add(node);
            foreach (var parent in childToParents[node])
            {
                indegree[parent]--;
                if (indegree[parent] == 0) queue.Enqueue(parent);
            }
        }

        // 拓扑未覆盖（残留入度>0）= 处于多表互引环中：并入 cycleNodes，并追加到删除序尾部
        // （调用方对 cycleNodes 先 null 其父 FK，故此处补入序仍可被 ExecuteDelete 删除）。
        foreach (var t in owners)
        {
            if (!processed.Contains(t))
            {
                cycleNodes.Add(t);
                order.Add(t);
            }
        }

        return (order, cycleNodes.ToList());
    }
}
