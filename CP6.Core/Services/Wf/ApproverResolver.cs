using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 审批人解析器（OA 章01）。消费 PUB B0 组织模型：
/// Sys_User(ManagerId/DeptId/RoleId/Enable) + Sys_Dept(ParentId/LeaderId/Enable)。
/// 全程纯查询、缺位返回原因不抛异常 —— 由流程引擎决定挂起待指派（OA-D1）。
/// </summary>
public class ApproverResolver : IApproverResolver
{
    private readonly CP6Context _db;
    public ApproverResolver(CP6Context db) => _db = db;

    public Task<ApproverResolveResult> ResolveAsync(ApproverRule rule, ApproverResolveContext ctx) => rule.Strategy switch
    {
        ApproverStrategy.DirectManager => DirectManagerAsync(rule, ctx),
        ApproverStrategy.DeptLeader    => DeptLeaderAsync(ctx),
        ApproverStrategy.Role          => RoleAsync(rule),
        ApproverStrategy.Specified     => Task.FromResult(rule.SpecifiedUserId is Guid u
                                            ? ApproverResolveResult.Ok(u)
                                            : ApproverResolveResult.Unres("未指定审批人")),
        ApproverStrategy.Starter       => Task.FromResult(ApproverResolveResult.Ok(ctx.StarterUserId)),
        ApproverStrategy.FormField     => FormFieldAsync(rule, ctx),
        ApproverStrategy.DataMap       => DataMapAsync(rule, ctx),
        _ => Task.FromResult(ApproverResolveResult.Unres("未知审批人策略")),
    };

    /// <summary>沿 ManagerId 上溯 Levels 级；链短于 N 时取能到达的链顶；无任何上级 → 缺位。</summary>
    private async Task<ApproverResolveResult> DirectManagerAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        var levels = rule.Levels is int l && l >= 1 ? l : 1;
        var current = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == ctx.StarterUserId);
        if (current == null) return ApproverResolveResult.Unres("发起人不存在");

        Sys_User? manager = null;
        for (int i = 0; i < levels; i++)
        {
            if (current.ManagerId is not Guid mid) break;
            var next = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == mid && u.Enable);
            if (next == null) break;
            manager = next;
            current = next;
        }
        return manager == null
            ? ApproverResolveResult.Unres("发起人无直属上级，需人工指派")
            : ApproverResolveResult.Ok(manager.Id);
    }

    /// <summary>从发起人部门沿 ParentId 上溯，取首个有效 LeaderId 对应的启用用户。</summary>
    private async Task<ApproverResolveResult> DeptLeaderAsync(ApproverResolveContext ctx)
    {
        var user = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == ctx.StarterUserId);
        if (user?.DeptId is not Guid did) return ApproverResolveResult.Unres("发起人无部门");

        var dept = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == did && d.Enable);
        while (dept != null)
        {
            if (dept.LeaderId is Guid lid)
            {
                var leader = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == lid && u.Enable);
                if (leader != null) return ApproverResolveResult.Ok(leader.Id);
            }
            if (dept.ParentId is not Guid pid) break;
            dept = await _db.Sys_Depts.FirstOrDefaultAsync(d => d.Id == pid && d.Enable);
        }
        return ApproverResolveResult.Unres("沿部门树未找到有效负责人，需人工指派");
    }

    /// <summary>指定角色下的全部启用用户（停用排除）。</summary>
    private async Task<ApproverResolveResult> RoleAsync(ApproverRule rule)
    {
        if (rule.RoleId is not int rid) return ApproverResolveResult.Unres("未指定角色");
        var ids = await _db.Sys_Users.Where(u => u.RoleId == rid && u.Enable).Select(u => u.Id).ToListAsync();
        return ids.Count > 0
            ? ApproverResolveResult.Ok(ids.ToArray())
            : ApproverResolveResult.Unres($"角色 {rid} 下无启用用户");
    }

    /// <summary>③:从 VarsJson 读 FieldName 取 UserId(单值或数组);过滤存在且启用的用户。</summary>
    private async Task<ApproverResolveResult> FormFieldAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        if (string.IsNullOrWhiteSpace(rule.FieldName)) return ApproverResolveResult.Unres("未配置表单字段名");
        var ids = ReadGuidsFromField(ctx.VarsJson, rule.FieldName);
        if (ids.Count == 0) return ApproverResolveResult.Unres("表单字段未指定有效审批人");
        var valid = await _db.Sys_Users.Where(u => ids.Contains(u.Id) && u.Enable).Select(u => u.Id).ToListAsync();
        return valid.Count > 0 ? ApproverResolveResult.Ok(valid.ToArray()) : ApproverResolveResult.Unres("表单字段指定的用户无效或停用");
    }

    /// <summary>从 VarsJson 读字段(String 单值 / Array 多值),逐个 Guid.TryParse。
    /// 注:不走 ExpressionEvaluator.ParseVars(它把数组降为 null)。</summary>
    private static List<Guid> ReadGuidsFromField(string? varsJson, string fieldName)
    {
        var result = new List<Guid>();
        if (string.IsNullOrWhiteSpace(varsJson)) return result;
        try
        {
            using var doc = JsonDocument.Parse(varsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            if (!doc.RootElement.TryGetProperty(fieldName, out var el)) return result;
            void TryAdd(JsonElement v) { if (v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g)) result.Add(g); }
            if (el.ValueKind == JsonValueKind.Array) foreach (var item in el.EnumerateArray()) TryAdd(item);
            else TryAdd(el);
        }
        catch { /* 解析失败 → 空 */ }
        return result;
    }

    /// <summary>②b:取 FieldName 标量值 → 查 Wf_ApproverMap(MapKey+MatchValue+Enable);收用户 + 展开角色。</summary>
    private async Task<ApproverResolveResult> DataMapAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        if (string.IsNullOrWhiteSpace(rule.MapKey) || string.IsNullOrWhiteSpace(rule.FieldName))
            return ApproverResolveResult.Unres("未配置映射键或匹配字段");
        var matchValue = ReadScalarString(ctx.VarsJson, rule.FieldName);
        if (matchValue is null) return ApproverResolveResult.Unres("表单匹配字段为空");

        var rows = await _db.Wf_ApproverMaps
            .Where(m => m.MapKey == rule.MapKey && m.MatchValue == matchValue && m.Enable).ToListAsync();
        if (rows.Count == 0) return ApproverResolveResult.Unres($"映射 {rule.MapKey}/{matchValue} 无审批人");

        var ids = new List<Guid>();
        ids.AddRange(rows.Where(r => r.ApproverUserId is Guid).Select(r => r.ApproverUserId!.Value));
        foreach (var rid in rows.Where(r => r.ApproverRoleId is int).Select(r => r.ApproverRoleId!.Value).Distinct())
        {
            var roleRes = await RoleAsync(new ApproverRule(ApproverStrategy.Role, null, rid, null));
            if (roleRes.Resolved) ids.AddRange(roleRes.ApproverIds);
        }
        var valid = await _db.Sys_Users.Where(u => ids.Contains(u.Id) && u.Enable).Select(u => u.Id).ToListAsync();
        return valid.Count > 0 ? ApproverResolveResult.Ok(valid.Distinct().ToArray()) : ApproverResolveResult.Unres("映射审批人无效或停用");
    }

    /// <summary>从 VarsJson 读字段标量值的字符串形式(数字/字符串/布尔);数组/对象/缺失→null。</summary>
    private static string? ReadScalarString(string? varsJson, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(varsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(varsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(fieldName, out var el)) return null;
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };
        }
        catch { return null; }
    }
}
