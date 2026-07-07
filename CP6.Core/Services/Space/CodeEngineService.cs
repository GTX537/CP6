using CP6.Core.EFDbContext;
using CP6.WebApi.Localization;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CP6.Core.Services.Space;

/// <summary>
/// 可配置库位编码引擎实现（ch03）。
///
/// v1.1 多租户约定（全文遵守）：
///   · 构造只注入 CP6Context，不注入任何租户上下文。
///   · 查询不写 .Where(x => x.TenantId == ...)——CP6Context 全局过滤自动按当前租户隔离。
///   · 创建实体不写 TenantId = ...——SaveChanges 写入盖章自动补当前租户。
/// </summary>
public class CodeEngineService : ICodeEngineService
{
    private readonly CP6Context _db;

    /// <summary>构造（v1.1：只注入 CP6Context；租户隔离由全局过滤 + SaveChanges 盖章自动处理）。</summary>
    public CodeEngineService(CP6Context db) => _db = db;

    // ══════════════════════════════════════════════════════════════════════
    // CodeRule CRUD（ch03 §2.1）
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<List<Space_CodeRule>> ListRulesAsync() =>
        await _db.Space_CodeRules
            .OrderBy(r => r.ScopeType).ThenBy(r => r.RuleName)
            .ToListAsync();

    /// <inheritdoc/>
    public async Task<Guid> CreateRuleAsync(CodeRuleDto d, string? user)
    {
        // IsDefault 同作用域互斥：若新建为默认规则，把同作用域其他规则 IsDefault 置 false（同批 SaveChanges）
        if (d.IsDefault)
            await ClearDefaultAsync(d.ScopeType, d.ScopeId, excludeId: null);

        var e = new Space_CodeRule
        {
            Id         = Guid.NewGuid(),
            RuleName   = d.RuleName,
            ScopeType  = d.ScopeType,
            ScopeId    = d.ScopeId,
            Segments   = JsonSerializer.Serialize(d.Segments),
            IsDefault  = d.IsDefault,
            Creator    = user,
            CreateDate = DateTime.Now
        };
        _db.Space_CodeRules.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    /// <inheritdoc/>
    public async Task UpdateRuleAsync(Guid id, CodeRuleDto d, string? user)
    {
        var e = await _db.Space_CodeRules.FirstOrDefaultAsync(r => r.Id == id)
                ?? throw new BizException("E-SPACE-301");

        // 改为默认时，把同作用域其他规则 IsDefault 置 false（排除自身，同批 SaveChanges）
        if (d.IsDefault && !e.IsDefault)
            await ClearDefaultAsync(d.ScopeType, d.ScopeId, excludeId: id);

        e.RuleName   = d.RuleName;
        e.ScopeType  = d.ScopeType;
        e.ScopeId    = d.ScopeId;
        e.Segments   = JsonSerializer.Serialize(d.Segments);
        e.IsDefault  = d.IsDefault;
        e.Modifier   = user;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task DeleteRuleAsync(Guid id)
    {
        var e = await _db.Space_CodeRules.FirstOrDefaultAsync(r => r.Id == id)
                ?? throw new BizException("E-SPACE-301");
        _db.Space_CodeRules.Remove(e);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 将同作用域（ScopeType + ScopeId）的其他规则 IsDefault 置 false（不立即 SaveChanges，由调用方统一提交）。
    /// </summary>
    private async Task ClearDefaultAsync(int scopeType, Guid? scopeId, Guid? excludeId)
    {
        var others = await _db.Space_CodeRules
            .Where(r => r.ScopeType == scopeType
                     && r.ScopeId  == scopeId
                     && r.IsDefault
                     && (excludeId == null || r.Id != excludeId))
            .ToListAsync();
        foreach (var r in others)
            r.IsDefault = false;
    }

    // ══════════════════════════════════════════════════════════════════════
    // GenerateAsync — 批量生成（ch03 §4.2）
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<List<string>> GenerateAsync(Guid floorId, string mode, Guid? scopeZoneId)
    {
        // ── 1. 拉层级数据 ──────────────────────────────────────────────

        var zones = await _db.Space_Zones
            .Where(z => z.FloorId == floorId && (scopeZoneId == null || z.Id == scopeZoneId))
            .ToListAsync();
        var zoneIds = zones.Select(z => z.Id).ToList();

        var racks  = await _db.Space_Racks .Where(r => zoneIds.Contains(r.ZoneId)).ToListAsync();
        var aisles = await _db.Space_Aisles.Where(a => zoneIds.Contains(a.ZoneId)).ToListAsync();

        // Floor + Site（用于 site-code / floor-level 取值源）
        var floorEntity = await _db.Space_Floors.FirstOrDefaultAsync(f => f.Id == floorId);
        Space_Site? siteEntity = null;
        if (floorEntity != null)
            siteEntity = await _db.Space_Sites.FirstOrDefaultAsync(s => s.Id == floorEntity.SiteId);

        // ── 2. 草稿库位（Status=0 ∧ CodeOrigin=1 ∧ 已落位）────────────

        var rackIds = racks.Select(r => r.Id).ToList();
        var drafts  = await _db.Space_Locations
            .Where(l => l.Status     == 0
                     && l.CodeOrigin == 1
                     && l.Placed
                     && l.RackId != null
                     && rackIds.Contains(l.RackId!.Value))
            .ToListAsync();

        // ── 3. 规则集（全量拉取，按 Zone 就近匹配） ───────────────────

        var rules = await _db.Space_CodeRules.ToListAsync();

        // ── 4. 序号字典 ────────────────────────────────────────────────

        // zone-seq：按楼层内 ZoneCode 字典序编号（1-based）
        var zoneSeq = zones
            .OrderBy(z => z.ZoneCode)
            .Select((z, i) => (z.Id, Seq: i + 1))
            .ToDictionary(x => x.Id, x => x.Seq);

        // ★ rack-seq：按 Zone 分组，每 Zone 内按 (X, Y) 几何序编号（保变长唯一，ch03 §5.3）
        //   关键：即使「有巷道货架」与「无巷道货架」混在同一 Zone，rack-seq 在 Zone 范围内唯一，
        //   从而无巷道路径（跳过 aisle 段）仍能与有巷道路径区分码值。
        var rackSeq = racks
            .GroupBy(r => r.ZoneId)
            .SelectMany(g => g.OrderBy(r => r.X).ThenBy(r => r.Y).Select((r, i) => (r.Id, Seq: i + 1)))
            .ToDictionary(x => x.Id, x => x.Seq);

        var rackById  = racks .ToDictionary(r => r.Id);
        var zoneById  = zones .ToDictionary(z => z.Id);
        var aisleById = aisles.ToDictionary(a => a.Id);

        // ── 5. 每 Zone 命中规则做静态预检 ────────────────────────────

        foreach (var z in zones)
        {
            var rule = PickRule(rules, z.Id, floorId);
            var segs = DeserializeSegs(rule.Segments);
            var errs = CodePrecheck.Validate(segs);
            if (errs.Count > 0)
                throw new BizException(errs[0]);  // E-303/305/306
        }

        // ── 6. 组装候选码 ──────────────────────────────────────────────

        var candidates = new List<(Space_Location Loc, string Code)>();
        foreach (var l in drafts)
        {
            // fill-empty 模式：跳过已有码的库位，保留既有码不动
            if (mode == "fill-empty" && l.LocationCode != null)
                continue;

            var rack  = rackById[l.RackId!.Value];
            var zone  = zoneById[rack.ZoneId];
            Space_Aisle? aisle = rack.AisleId.HasValue
                && aisleById.TryGetValue(rack.AisleId.Value, out var ai) ? ai : null;

            var rule = PickRule(rules, zone.Id, floorId);
            var segs = DeserializeSegs(rule.Segments);

            var code = Assemble(segs, siteEntity, floorEntity, zone, aisle, rack,
                l.Col ?? 1, l.Level ?? 1, l.Depth ?? 1, zoneSeq, rackSeq);
            candidates.Add((l, code));
        }

        // ── 7. 值级唯一校验（ch03 §6.2）——批内重复 + 与库内既有码冲突 ──

        // ① 批内重复检查
        var dupInBatch = candidates
            .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        if (dupInBatch.Count > 0)
            throw new BizException("E-SPACE-304");

        // ② 与库内既有非空码比对（全局过滤已按当前租户隔离）
        var idsInBatch = candidates.Select(c => c.Loc.Id).ToHashSet();
        // 拉现有库存码（排除本批自身，避免 rebuild 时自身被排斥）
        var existingCodes = await _db.Space_Locations
            .Where(l => l.LocationCode != null && !idsInBatch.Contains(l.Id))
            .Select(l => l.LocationCode!)
            .ToListAsync();
        var existingSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (candidates.Any(c => existingSet.Contains(c.Code)))
            throw new BizException("E-SPACE-304");

        // ── 8. 两阶段写回（ch03 §7.2）─────────────────────────────────

        if (mode == "rebuild")
        {
            // 阶段一：所有目标草稿 LocationCode 置 NULL。
            // 必须先 SaveChanges，防止 SQL Server 过滤唯一索引
            // (TenantId, LocationCode) WHERE LocationCode IS NOT NULL
            // 在 A↔B 交换时中途违约。InMemory 不触约束，仍须实现（真库 D-9 会测）。
            foreach (var l in drafts)
                l.LocationCode = null;
            await _db.SaveChangesAsync();
        }

        // 阶段二：赋新码
        foreach (var (loc, code) in candidates)
            loc.LocationCode = code;
        await _db.SaveChangesAsync();

        return candidates.Select(c => c.Code).ToList();
    }

    // ══════════════════════════════════════════════════════════════════════
    // PreviewAsync — 实时预览（ch03 §8）
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<CodePreviewResp> PreviewAsync(CodePreviewReq req)
    {
        var resp = new CodePreviewResp();

        // 静态预检
        resp.Precheck.Errors = CodePrecheck.Validate(req.Segments);
        resp.Precheck.Ok     = resp.Precheck.Errors.Count == 0;

        // 段结构列表
        resp.Structure = req.Segments
            .Select(s => (object)new { s.Key, s.Name, s.Source, s.Optional })
            .ToList();

        // 合成虚拟层级数据（无真实 DB 数据时用于样例生成）
        // zone="A", aisle="A02", rack="R03"(rackSeq=3), col=5, level=2, depth=1
        var synthZone  = new Space_Zone  { Id = Guid.NewGuid(), ZoneCode  = "A" };
        var synthAisle = new Space_Aisle { Id = Guid.NewGuid(), AisleCode = "A02" };
        var synthRack  = new Space_Rack  { Id = Guid.NewGuid(), RackCode  = "R03", X = 0, Y = 0 };

        var zSeq = new Dictionary<Guid, int> { [synthZone.Id] = 1 };
        var rSeq = new Dictionary<Guid, int> { [synthRack.Id] = 3 };

        // 有巷道样例（aisle = synthAisle）
        resp.VariableLen.WithAisle =
            Assemble(req.Segments, null, null, synthZone, synthAisle, synthRack, 5, 2, 1, zSeq, rSeq);
        // 无巷道样例（aisle = null，optional 段跳过）
        resp.VariableLen.WithoutAisle =
            Assemble(req.Segments, null, null, synthZone, null, synthRack, 5, 2, 1, zSeq, rSeq);

        resp.Samples.Add(resp.VariableLen.WithAisle);
        // 若两路不同，补充无巷道样例（体现变长效果）
        if (!string.Equals(resp.VariableLen.WithAisle, resp.VariableLen.WithoutAisle, StringComparison.OrdinalIgnoreCase))
            resp.Samples.Add(resp.VariableLen.WithoutAisle);

        await Task.CompletedTask;
        return resp;
    }

    // ══════════════════════════════════════════════════════════════════════
    // PrecheckAsync — 发布前编码预检（ch03 §9.2）
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<CodePrecheckResp> PrecheckAsync(Guid floorId, Guid? zoneId = null)
    {
        var resp = new CodePrecheckResp();

        // 拉 floor（或指定库区）内全部草稿库位——库区归属经 Rack.ZoneId 推导
        var locQuery = _db.Space_Locations.Where(l => l.FloorId == floorId && l.Status == 0);
        if (zoneId != null)
        {
            var rackIds = await _db.Space_Racks.Where(r => r.ZoneId == zoneId).Select(r => r.Id).ToListAsync();
            locQuery = locQuery.Where(l => l.RackId != null && rackIds.Contains(l.RackId.Value));
        }
        var locs = await locQuery.ToListAsync();

        resp.EmptyCodeCount = locs.Count(l => l.LocationCode == null);

        resp.DuplicateGroups = locs
            .Where(l => l.LocationCode != null)
            .GroupBy(l => l.LocationCode!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Select(x => x.Id).ToList())
            .ToList();

        resp.UnplacedDraftCount = locs.Count(l => l.LocationCode != null && !l.Placed);

        // 规则完备性：对 floor（或指定库区）跑静态预检，汇总错误码（去重）
        var zoneQuery = _db.Space_Zones.Where(z => z.FloorId == floorId);
        if (zoneId != null) zoneQuery = zoneQuery.Where(z => z.Id == zoneId);
        var zones = await zoneQuery.ToListAsync();
        var rules = await _db.Space_CodeRules.ToListAsync();
        var precheckErrs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var z in zones)
        {
            try
            {
                var rule = PickRule(rules, z.Id, floorId);
                var segs = DeserializeSegs(rule.Segments);
                foreach (var err in CodePrecheck.Validate(segs))
                    precheckErrs.Add(err);
            }
            catch (BizException ex)
            {
                // E-SPACE-301 (无规则) / E-SPACE-302 (多规则无默认)
                precheckErrs.Add(ex.Code);
            }
        }

        resp.PrecheckErrors = precheckErrs.ToList();
        return resp;
    }

    // ══════════════════════════════════════════════════════════════════════
    // GenSingleAsync — 单格生成（ch03 §10，最小实现）
    // ══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<string> GenSingleAsync(Guid locationId)
    {
        // TODO: rackSeq 在大规则集下应取 Zone 级完整排序；此处取 1 作最小实现（计划 §10 补全）
        var loc = await _db.Space_Locations.FirstOrDefaultAsync(l => l.Id == locationId)
                  ?? throw new BizException("E-SPACE-301");

        if (loc.RackId == null || !loc.Placed)
            throw new BizException("E-SPACE-301");

        var rack = await _db.Space_Racks.FirstOrDefaultAsync(r => r.Id == loc.RackId)
                   ?? throw new BizException("E-SPACE-301");
        var zone = await _db.Space_Zones.FirstOrDefaultAsync(z => z.Id == rack.ZoneId)
                   ?? throw new BizException("E-SPACE-301");
        Space_Aisle? aisle = rack.AisleId.HasValue
            ? await _db.Space_Aisles.FirstOrDefaultAsync(a => a.Id == rack.AisleId)
            : null;

        var floor = await _db.Space_Floors.FirstOrDefaultAsync(f => f.Id == rack.FloorId);
        var site  = floor != null
            ? await _db.Space_Sites.FirstOrDefaultAsync(s => s.Id == floor.SiteId)
            : null;

        var rules   = await _db.Space_CodeRules.ToListAsync();
        var rule    = PickRule(rules, zone.Id, rack.FloorId);
        var segs    = DeserializeSegs(rule.Segments);
        var preErrs = CodePrecheck.Validate(segs);
        if (preErrs.Count > 0)
            throw new BizException(preErrs[0]);

        // 简化序号字典（单格场景；完整 Zone 级排序见计划 §10）
        var zoneSeq = new Dictionary<Guid, int> { [zone.Id] = 1 };
        var rackSeq = new Dictionary<Guid, int> { [rack.Id] = 1 };

        var code = Assemble(segs, site, floor, zone, aisle, rack,
            loc.Col ?? 1, loc.Level ?? 1, loc.Depth ?? 1, zoneSeq, rackSeq);

        loc.LocationCode = code;
        await _db.SaveChangesAsync();
        return code;
    }

    // ══════════════════════════════════════════════════════════════════════
    // 私有算法辅助
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 规则优先级解析（ch03 §2.2）。
    /// 作用域：库区(2) > 楼层(1) > 租户默认(0)；同层多条时 IsDefault 优先；
    /// 仍多条且无默认 → E-SPACE-302；无任何规则 → E-SPACE-301。
    /// </summary>
    private static Space_CodeRule PickRule(List<Space_CodeRule> rules, Guid zoneId, Guid floorId)
    {
        // 按作用域从高到低依次尝试
        var hit = rules.Where(r => r.ScopeType == 2 && r.ScopeId == zoneId).ToList();
        if (hit.Count == 0)
            hit = rules.Where(r => r.ScopeType == 1 && r.ScopeId == floorId).ToList();
        if (hit.Count == 0)
            hit = rules.Where(r => r.ScopeType == 0).ToList();
        if (hit.Count == 0)
            throw new BizException("E-SPACE-301");

        // IsDefault 优先；否则仅一条直接用；多条无默认 → 歧义错误
        var def = hit.FirstOrDefault(r => r.IsDefault);
        if (def != null) return def;
        if (hit.Count == 1) return hit[0];
        throw new BizException("E-SPACE-302");
    }

    /// <summary>
    /// 拼装单个库位编码（ch03 §5）。
    ///
    /// 巷道条件段（aisle-code/aisle-seq，Optional=true）：
    ///   aisle==null 时 continue 跳过整段，包括其 Sep，实现变长拼接。
    ///
    /// 末尾分隔符处理：各段依次 Append(Render) + Append(Sep)，末段 Sep 常设 ""；
    ///   若末段有非空 Sep，用 TrimEnd 清理遗留分隔符保证格式整洁。
    /// </summary>
    private static string Assemble(
        List<CodeSegmentDef> segs,
        Space_Site?  site,
        Space_Floor? floor,
        Space_Zone   zone,
        Space_Aisle? aisle,
        Space_Rack   rack,
        int col, int level, int depth,
        Dictionary<Guid, int> zoneSeq,
        Dictionary<Guid, int> rackSeq)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var seg in segs)
        {
            // 巷道条件段：source 为 aisle-code/aisle-seq，标 optional，且当前货架无巷道
            bool isAisleSeg = seg.Source == "aisle-code" || seg.Source == "aisle-seq";
            if (isAisleSeg && aisle == null && seg.Optional)
                continue;   // 跳过整段及其 Sep（continue 使两个 Append 均不执行）

            var input = Resolve(seg, site, floor, zone, aisle, rack,
                col, level, depth, zoneSeq, rackSeq);
            sb.Append(CodeSegment.Render(seg, input));
            sb.Append(seg.Sep);
        }

        // 去掉末尾遗留分隔符（末段 Sep 通常设 ""，TrimEnd 作保险处理）
        return sb.ToString().TrimEnd('-', '_', '.', '/', ' ');
    }

    /// <summary>按 source 映射 SegInput（ch03 §3 取值表）。</summary>
    private static SegInput Resolve(
        CodeSegmentDef seg,
        Space_Site?  site,
        Space_Floor? floor,
        Space_Zone   zone,
        Space_Aisle? aisle,
        Space_Rack   rack,
        int col, int level, int depth,
        Dictionary<Guid, int> zoneSeq,
        Dictionary<Guid, int> rackSeq)
        => seg.Source switch
        {
            "fixed"       => new SegInput { RawCode  = seg.FixedValue },
            "site-code"   => new SegInput { RawCode  = site?.SiteCode },
            "floor-level" => new SegInput { RawCode  = (floor?.Level ?? 0).ToString() },
            "zone-code"   => new SegInput { RawCode  = zone.ZoneCode },
            "zone-seq"    => new SegInput { SeqIndex = zoneSeq.GetValueOrDefault(zone.Id, 1) },
            "aisle-code"  => new SegInput { RawCode  = aisle?.AisleCode },
            "aisle-seq"   => new SegInput { SeqIndex = 1 },       // 简化：巷道内序号（后续可按巷道分组扩展）
            "rack-code"   => new SegInput { RawCode  = rack.RackCode },
            "rack-seq"    => new SegInput { SeqIndex = rackSeq.GetValueOrDefault(rack.Id, 1) },
            "col"         => new SegInput { SeqIndex = col },
            "level"       => new SegInput { SeqIndex = level },
            "depth"       => new SegInput { SeqIndex = depth },
            _             => new SegInput { RawCode  = "" }
        };

    /// <summary>反序列化 Segments JSON；失败回退空列表。</summary>
    private static List<CodeSegmentDef> DeserializeSegs(string json) =>
        JsonSerializer.Deserialize<List<CodeSegmentDef>>(json) ?? new();
}
