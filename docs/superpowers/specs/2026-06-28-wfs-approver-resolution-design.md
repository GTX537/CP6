# WFS 引擎深化 · 审批人解析高级策略设计 Spec

> 版本 **v1.0**(2026-06-28) · 分支 `feat/wfs-approver-resolve`(off `main` f90a138) · 隔离 worktree `D:/CP6-wfs-approver`
> 上游：[[2026-06-26-wfs-runtime-kernel-design]](token 内核 L0)、[[2026-06-26-wfs-form-inbox-unified-design]](OA 信箱 umbrella，**本 spec 闭其 §1.5.3 四缺口**)、[[2026-06-28-wfs-serial-signing-design]](串簽，已上 main，本 spec 与之正交，复用其 planner/forecast/设计器接缝)。
> 本 spec 在**已完成的 token 内核 + OA 电子表单信箱(A/B/C/C′/D-1) + 串簽(全上 main)**之上，扩 `IApproverResolver` 的解析能力。**不碰引擎执行态/串簽档·轮机制**。

---

## §0 范围(做什么 / 不做什么)

**做(闭 umbrella §1.5.3 全四缺口):**
1. **③ 表单字段指定审批人(Delta 02)** = 新策略 `FormField`：审批人来自实例表单某字段值(存 `UserId` GUID，多值→会签组)。
2. **②a 角色+条件(Delta 18/20)** = 给任意审批人规则配可选 `When`(门控，对表单 vars 求值，决定本规则是否产审批人) + `Filter`(候选过滤，对候选人属性求值，筛掉不合格者)。两者皆复用既有 `ExpressionEvaluator`。
3. **②b Menu 数据驱动(Delta 17)** = 新策略 `DataMap`：审批人由「表单某字段值 → 查映射表」决定。新建 `Wf_ApproverMap` 映射表 + 维护页。
4. **① JSON 组(Delta 15)** = 新策略 `Group`：一组**混源**成员规则(直属/部门/角色/指定/表单字段/数据映射任意组合)各自解析后**去重扁平合并**为一组审批人，整组按节点/档的 `Countersign`(all/any/veto)计票（GroupSubmit=一个会签单元）。
5. **核心接缝**：把实例表单数据 `inst.VarsJson` 注入 `ApproverResolveContext`（②③①ⅰ 全依赖此）。
6. **全栈贯通**：4 处解析调用点接线 + 设计器面板配新策略 + `Wf_ApproverMap` 维护页 + `DynamicForm` `user` 字段升级真选择器 + forecast 精度提升 + i18n 五语 + gstack QA。

**不做(YAGNI / 留 roadmap):**
- **嵌套子组会签**(子组各自会签后再组级聚合)——本期 `Group` 仅**扁平合并**，成员均为叶规则，复用现有 `(Inst,Node,Token,StageIndex,StageRound)` 计票维度，**不新增子计票维度/子 token**。
- **`Wf_ApproverMap` 多层级/通配/区间匹配**——本期仅 `(MapKey, MatchValue)` 精确等值匹配。
- **候选过滤跨用户聚合**(如"取部门人数最多的角色")——`Filter` 仅逐候选谓词。
- **`dept` 字段控件升级**——本期仅升级 `user` 字段类型为真选择器；`dept` 维持现状(纯文本，另起)。
- **表达式语言扩展**——`ExpressionEvaluator` 语法/函数集不动，仅**喂入更丰富的变量字典**(starter.* / user.*)。
- **新审批节点类型**(服务任务/WebAPI/JOB/子流程)——umbrella §9，另起 spec。

---

## §1 现状锚点(实读真行号，落码前仍建议复核)

> 工作树 `D:/CP6-wfs-approver` @ `feat/wfs-approver-resolve`(== `main` f90a138)。

| 主题 | 文件:行 | 现状要点 |
|---|---|---|
| 审批人策略枚举 | `CP6.Core/Services/Wf/IApproverResolver.cs:6-46` | `ApproverStrategy` 5 枚举(DirectManager/DeptLeader/Role/Specified/Starter，L6-18)；`ApproverRule(Strategy,Levels,RoleId,SpecifiedUserId)`(L21)；`ApproverResolveContext{ StarterUserId }`(L24-27，**仅发起人**)；`ApproverResolveResult{ ApproverIds, Resolved, UnresolvedReason }` + `Ok()/Unres()`(L33-41)；`ResolveAsync`(L45)。 |
| 审批人解析实现 | `ApproverResolver.cs:12-79` | 构造注入 `CP6Context _db`(L14-15)；`ResolveAsync` switch 分发(L17-27)；`DirectManagerAsync` 沿 `ManagerId` 上溯第 N 级单人(L30-48)；`DeptLeaderAsync` 沿 `Sys_Dept.ParentId` 取首个有效 `LeaderId`(L51-68)；`RoleAsync` 取角色全启用用户(L71-78)。**纯查询、缺位 `Unres` 不抛(OA-D1)**。 |
| 表达式求值器 | `ExpressionEvaluator.cs:17-370` | 静态类。`Evaluate(expr, varsJson|vars)→bool`(L20-28，空表达式=true、任何错→false)；`Compute→object?`(L31-39)；`ParseVars(varsJson)→Dictionary<string,object?>`(L54-74，number→double/string/bool/null)。标识符**允许 `.`**(L144，`user.deptId` 作单一键查 vars)。比较走 Ordinal 字符串/double(L322-344)。**安全失败铁律：未知字段/任何错→Evaluate 返 false**。 |
| 规则构建(节点→rule) | `FlowEngine.cs:342-347` | `BuildRule(FlowNode n)`：`ApproverStrategy` 空→null；`Enum.TryParse` 失败→null；否则 `new ApproverRule(strat, n.ApproverLevels, n.ApproverRoleId, n.ApproverUserId)`。 |
| 单档审批 handler | `NodeHandlers/ApprovalNodeHandler.cs:12-66` | 单档分支(L17-58)：`BuildRule(node)`(L21)→`ResolveAsync(rule, new ApproverResolveContext{ StarterUserId=inst.StarterId })`(L24，**未传 vars**)→缺位 `Suspend`(L25)→建 task。多档分支(L62-65)：`Planner.BuildAsync`→冻结 `StagePlanJson`→`EnterStageAsync(…,0)`。 |
| 串簽档展开 | `ApprovalStagePlanner.cs:10-78` | `BuildAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node)`(L10，**已收 inst**)。单档兼容用 `BuildRule`(L17)；fixed 档 `new ApproverRule(strat, st.ApproverLevels, st.ApproverRoleId, st.ApproverUserId)`(L72)；managerChain probe `ResolveAsync(…, new ApproverResolveContext{ StarterUserId=inst.StarterId })`(L38-40，**未传 vars**)。 |
| 串簽进档解析 | `FlowEngine.Serial.cs:8-12` | `EnterStageAsync(inst, schema, node, token, plan, k)`：`ResolveAsync(stage.Rule, new ApproverResolveContext{ StarterUserId=inst.StarterId })`(L12，**未传 vars，须补**)。 |
| forecast | `Services/Oa/ForecastService.cs:18-89` | `ForecastAsync(flowKey, varsJson, starterId, fromNodeId?)`(L18)；approval 分支 `_planner.BuildAsync(new Wf_FlowInstance{ StarterId=starterId }, schema, node)`(L58，**未带 varsJson**)；`ResolveRuleNamesAsync(rule, starterId)`(L79-89) `new ApproverResolveContext{ StarterUserId=starterId }`(L83，**未传 vars**)。 |
| Role→CC 解析 | `FlowEngine.ReadModel.cs:103` | `ResolveAsync(new ApproverRule(ApproverStrategy.Role,null,rid,null), …)` 解析抄送角色。无需 vars，传 null 即可。 |
| 节点/档 DSL | `FlowSchema.cs:16-94` | `FlowNode`(L16-62)：`ApproverStrategy/Levels/RoleId/UserId`(L27-30)、`Countersign`(L33)、`Stages?`(L61)。`ApprovalStage`(L82-94)：`Name/Code/Kind/ApproverStrategy/ApproverLevels/ApproverRoleId/ApproverUserId/Countersign/MaxLevels`。`ApprovalStageKinds`(L77)/`CountersignModes`(L79)。 |
| schema 校验器 | `FlowSchemaValidator.cs:4-55` | `KnownStrategies`(L6，现 5 个)；approval 节点校验单档 `ApproverStrategy ∈ KnownStrategies`(L27，违→E-WF-010)；档校验(L38)。 |
| 设计器 model | `cp6.web/src/views/oa/designer/designerModel.ts` | `SchemaNode`(approverStrategy/Levels/RoleId/UserId/countersign/stages…)；`schemaToGraph`/`graphToSchema` 互逆；`validateClient` 客户端镜像。 |
| 设计器面板 | `cp6.web/src/views/oa/designer/NodePropertyPanel.vue` | 单档审批属性(L174-252，5 策略下拉 + DirectManager/Role/Specified 条件控件)；串簽档面板(L318-392，每档同款 5 策略)。`searchUsers`(L101)/`loadRoles`(L119) 远程搜复用。 |
| 动态表单 | `cp6.web/src/views/wf/DynamicForm.vue` | `f.type` 映射 element-plus 控件(L9-63)；`isText` 含 `['input','user','dept']`(L110) **但 `user`/`dept` 当前走 `v-else` 纯文本** `el-input`(L63)。`FormFieldDef`/`FieldMask` from `@/types/wf/wf`(L72)。 |
| 数据字典(参考，不复用) | `Sys_DictType.cs` / `Sys_DictData.cs` | `TypeCode/Value/Label` 键→显示文本，**全局无租户、无审批人列** → 不适合做映射源，故新建 `Wf_ApproverMap`。 |
| 用户实体字段 | `Sys_User.cs` | `UserName`(L15)/`NickName`(L30)/`RoleId int?`(L35)/`Enable bool`(L40)/`DeptId Guid?`(L45)/`ManagerId Guid?`(L48)/`Email`(L53)。**候选/发起人命名空间取此。** |

**关键既有错误码**：`E-WF-006`(流程缺失)/`E-WF-009`(身份码重复)/`E-WF-010`(schema 结构非法)/`E-WF-011`(串簽档配置非法)/`E-WF-012`(退回目标非法)/`E-WF-013`(审批人缺失→Suspend)。

---

## §2 数据模型(DB 净 1 表 + 1 索引；schema 字段进 SchemaJson 无 DB 列；上下文/规则纯内存)

### §2.1 `ApproverStrategy` 枚举(`IApproverResolver.cs`，加 3 枚举)

```csharp
public enum ApproverStrategy
{
    DirectManager, DeptLeader, Role, Specified, Starter,   // 现有 5，不动
    FormField,   // ③ 表单字段指定：VarsJson[FieldName] → UserId(s)
    DataMap,     // ②b 数据驱动：VarsJson[FieldName] 匹配值 → 查 Wf_ApproverMap(MapKey)
    Group,       // ① JSON 组：Members 各自解析 → 去重扁平合并
}
```

### §2.2 `ApproverRule`(运行期，递归 record，**保持 4 定位参数向后兼容**)

```csharp
/// <summary>审批人规则。现有 4 定位参数不变(既有 new ApproverRule(strat,levels,roleId,userId) 全继续编译)；
/// 新增 init 可选成员承载高级策略配置。</summary>
public record ApproverRule(ApproverStrategy Strategy, int? Levels, int? RoleId, Guid? SpecifiedUserId)
{
    /// <summary>FormField:取审批人的字段名;DataMap:取匹配值的字段名。</summary>
    public string? FieldName { get; init; }
    /// <summary>DataMap:命名映射(Wf_ApproverMap.MapKey)。</summary>
    public string? MapKey { get; init; }
    /// <summary>门控(②a):对表单 vars 求值,假则本规则不产审批人。</summary>
    public string? When { get; init; }
    /// <summary>候选过滤(②a):对每个候选人属性求值,留通过者。</summary>
    public string? Filter { get; init; }
    /// <summary>Group(①):成员规则(扁平,均为叶规则)。</summary>
    public IReadOnlyList<ApproverRule>? Members { get; init; }
}
```

### §2.3 `ApproverResolveContext`(加 `VarsJson`，可选)

```csharp
public class ApproverResolveContext
{
    public Guid StarterUserId { get; set; }
    /// <summary>实例表单数据 JSON(②③① 求值/取字段用)。null=无表单上下文(如 Role→CC 解析)。</summary>
    public string? VarsJson { get; set; }
}
```
> 可选 → 所有既有 `new ApproverResolveContext{ StarterUserId=… }` 继续编译（仅不带 vars 的高级策略会 `Unres`）。

### §2.4 `Wf_ApproverMap`(新实体，`BaseTenantEntity`，DB 1 表 + 1 索引)

```csharp
/// <summary>审批人映射表(②b Menu 数据驱动)。一条=某命名映射下,某匹配值对应一个审批目标(用户或角色)。
/// 同 (MapKey,MatchValue) 可多行(多审批人,合并为会签组)。</summary>
public class Wf_ApproverMap : BaseTenantEntity
{
    [MaxLength(100)] public string MapKey { get; set; } = "";       // 命名映射(如 "costCenterApprover")
    [MaxLength(200)] public string MatchValue { get; set; } = "";    // 匹配值(对应表单字段值)
    public Guid? ApproverUserId { get; set; }                        // 审批用户(二选一)
    public int? ApproverRoleId { get; set; }                         // 审批角色(二选一,展开为全员)
    public int OrderNo { get; set; }
    public bool Enable { get; set; } = true;
}
```
- DbSet 注册 + 索引 `IX_Wf_ApproverMap_Lookup = (TenantId, MapKey, MatchValue)`。
- 迁移 `WfsApproverMap`：Up 仅建 1 表 + 1 索引，**零回填，零 Space 污染**。
- 租户隔离：`BaseTenantEntity` 走 `CP6Context` 全局查询过滤器(解析/forecast/维护页全自动租户内)。

### §2.5 设计期承载(方案 X：节点/档扁平字段，仅 Group 用嵌套 Members)

`FlowSchema.cs` 新增 `ApproverSpec`(设计期叶规则镜像) + 节点/档加可选字段：

```csharp
/// <summary>审批人设计期叶规则(JSON 组成员用)。字段对齐 ApproverRule 的叶子部分,无 Members(扁平合并)。</summary>
public class ApproverSpec
{
    public string? Strategy { get; set; }    // DirectManager/DeptLeader/Role/Specified/Starter/FormField/DataMap
    public int? ApproverLevels { get; set; }
    public int? ApproverRoleId { get; set; }
    public Guid? ApproverUserId { get; set; }
    public string? FieldName { get; set; }   // FormField/DataMap
    public string? MapKey { get; set; }      // DataMap
    public string? When { get; set; }
    public string? Filter { get; set; }
}
```
`FlowNode` 新增(均可选，缺省=不启用)：`string? ApproverFieldName` / `string? ApproverMapKey` / `string? ApproverWhen` / `string? ApproverFilter` / `List<ApproverSpec>? ApproverMembers`。
`ApprovalStage` 新增同上 5 字段。
> `FlowNode.ApproverStrategy`/`ApproverLevels`/`ApproverRoleId`/`ApproverUserId` 既有 4 字段**保持**：策略=FormField 时读 `ApproverFieldName`；=DataMap 时读 `ApproverMapKey`+`ApproverFieldName`；=Group 时读 `ApproverMembers`；`When/Filter` 对任意策略生效。

---

## §3 规则构建(设计期 → 运行期 ApproverRule)

`FlowEngine.BuildRule(FlowNode n)`(扩，向后兼容)：

```csharp
internal static ApproverRule? BuildRule(FlowNode n)
{
    if (string.IsNullOrWhiteSpace(n.ApproverStrategy)) return null;
    if (!Enum.TryParse<ApproverStrategy>(n.ApproverStrategy, true, out var strat)) return null;
    return new ApproverRule(strat, n.ApproverLevels, n.ApproverRoleId, n.ApproverUserId)
    {
        FieldName = n.ApproverFieldName,
        MapKey    = n.ApproverMapKey,
        When      = n.ApproverWhen,
        Filter    = n.ApproverFilter,
        Members   = n.ApproverMembers?.Select(MapSpec).ToList(),
    };
}
// MapSpec(ApproverSpec) → ApproverRule(叶,无 Members)
```
> **字节等价**：旧 def 无新字段 → 全 null → `record` 与今天的 `new ApproverRule(strat,levels,roleId,userId)` 等价(C# record 相等性按全成员，但**运行行为**仅看解析路径，新成员 null 走原路径)。`ApprovalStagePlanner` fixed 档同样从 `ApprovalStage` 扩字段构建（L72 处加 init 成员）。

---

## §4 解析器实现(`ApproverResolver.ResolveAsync`，纯查询不抛，保 OA-D1)

### §4.1 主流程(门控 → 分发 → 候选过滤)

```
ResolveAsync(rule, ctx):
  1. 若 rule.When 非空 且 Evaluate(When, ctx.VarsJson)==false → return Unres("条件不满足")   // ②a 门控
  2. 按 rule.Strategy 分发解析出 candidateIds:
       现有 5 策略 → 原实现(DirectManager/DeptLeader/Role/Specified/Starter)
       FormField   → §4.2
       DataMap     → §4.3
       Group       → §4.4(递归;Group 的 When 已在步1处理,Members 各自再过 ResolveAsync 含其自身 When)
  3. 若 candidateIds 为空 → return Unres(分策略原因)
  4. 若 rule.Filter 非空 → §4.5 候选过滤;过滤后空 → Unres("无候选人满足过滤条件")
  5. return Ok(distinct candidateIds)
```

### §4.2 `FormField`(③)

```csharp
// rule.FieldName 必填;读 VarsJson 取值,支持单 GUID 字符串 或 GUID 数组(多选→会签组)
var vars = ExpressionEvaluator.ParseVars(ctx.VarsJson);   // 注:数组值需另解析,见下
// VarsJson 里 user 多选字段是 JSON 数组 → ParseVars 当前对数组返 null;
// FormField 解析直接用 JsonDocument 读 ctx.VarsJson[FieldName],分 String/Array 两路;
// 逐个 Guid.TryParse;过滤出确实存在且 Enable 的 Sys_User → Ok;无 → Unres("表单字段未指定有效审批人")
```
> **实现细节**：`ParseVars` 把数组值降为 null（`ExpressionEvaluator.cs:69` `_ => null`），故 FormField **不走 ParseVars**，单独用 `JsonDocument.Parse(ctx.VarsJson)` 读 `FieldName` 节点，`JsonValueKind.Array` 逐元素、`String` 单值，`Guid.TryParse` 收集，再查 `Sys_Users.Where(Enable && ids.Contains(Id))`。缺字段/无效 GUID/查无人 → `Unres`。

### §4.3 `DataMap`(②b)

```csharp
// rule.MapKey + rule.FieldName 必填;取匹配值
var matchValue = <从 ctx.VarsJson 读 FieldName 的标量值,ToString()>;
var rows = await _db.Set<Wf_ApproverMap>()
    .Where(m => m.MapKey == rule.MapKey && m.MatchValue == matchValue && m.Enable)
    .ToListAsync();   // 租户过滤器自动生效
if (rows.Count == 0) return Unres($"映射 {MapKey}/{matchValue} 无审批人");
var ids = new List<Guid>();
ids.AddRange(rows.Where(r => r.ApproverUserId is Guid).Select(r => r.ApproverUserId!.Value));
foreach (var rid in rows.Where(r => r.ApproverRoleId is int).Select(r => r.ApproverRoleId!.Value).Distinct())
    ids.AddRange((await RoleAsync(new ApproverRule(Role,null,rid,null))).ApproverIds);   // 角色展开复用
// 校验存在+Enable → Ok(distinct);无 → Unres
```

### §4.4 `Group`(①，扁平合并)

```csharp
// rule.Members 必填(≥1);每个成员递归 ResolveAsync(member, ctx)
var ids = new List<Guid>();
foreach (var m in rule.Members ?? [])
{
    var r = await ResolveAsync(m, ctx);   // 成员自身 When/Filter 在此生效
    if (r.Resolved) ids.AddRange(r.ApproverIds);   // 未解析成员静默不贡献(组仍可由他成员成立)
}
return ids.Count > 0 ? Ok(distinct ids) : Unres("JSON 组无任何成员解析出审批人");
```
> 合并后整组为一组审批人，由 handler 按节点/档 `Countersign`(all/any/veto) 建会签 task（**复用现 `(Inst,Node,Token,StageIndex,StageRound)` 计票，零新维度**）。

### §4.5 候选过滤 `Filter`(②a)

```csharp
// 载入候选 Sys_User 行,逐候选建变量字典求值,留 Evaluate==true 者
var users = await _db.Sys_Users.Where(u => candidateIds.Contains(u.Id)).ToListAsync();
var formVars = ExpressionEvaluator.ParseVars(ctx.VarsJson);
var starter = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == ctx.StarterUserId);
var kept = users.Where(u => {
    var vars = new Dictionary<string,object?>(formVars);   // 表单字段裸名
    AddNamespace(vars, "starter", starter);   // starter.deptId / starter.managerId / starter.roleId / starter.userName / starter.enable
    AddNamespace(vars, "user", u);             // user.id / user.deptId / user.managerId / user.roleId / user.userName / user.enable
    return ExpressionEvaluator.Evaluate(rule.Filter, vars);   // 未知字段/任何错→false→排除(安全失败)
}).Select(u => u.Id).ToList();
```

### §4.6 变量命名空间(写进 spec，落码遵守)

| 命名空间 | 键 | 来源 | 类型(求值器内) |
|---|---|---|---|
| 表单字段 | 裸名(`amount`/`deptType`…) | `VarsJson` 顶层标量 | double/string/bool(同既有条件边) |
| 发起人(When+Filter) | `starter.deptId`/`starter.managerId`/`starter.roleId`/`starter.userName`/`starter.enable` | `Sys_User`(StarterUserId) | GUID→串(小写"D")/int→double/string/bool |
| 候选人(仅 Filter) | `user.id`/`user.deptId`/`user.managerId`/`user.roleId`/`user.userName`/`user.enable` | 逐候选 `Sys_User` | 同上 |

> `user.`/`starter.` 为**保留前缀**；表单字段若与之同名则前缀键优先(文档化)。GUID 串化保证 `user.deptId == starter.deptId` 走 Ordinal 相等(`ExpressionEvaluator.cs:341`)。

---

## §5 调用点接线(接缝落地，4 处 + 校验)

| 调用点 | 文件:行 | 改动 |
|---|---|---|
| 单档 handler | `ApprovalNodeHandler.cs:24` | `ResolveAsync(rule, new ApproverResolveContext{ StarterUserId=inst.StarterId, VarsJson=inst.VarsJson })` |
| 串簽进档 | `FlowEngine.Serial.cs:12` | 同上加 `VarsJson=inst.VarsJson`(**当前缺，须补**) |
| 档展开 planner | `ApprovalStagePlanner.cs:38-40` | managerChain probe context 加 `VarsJson=inst.VarsJson`；fixed 档构 `ApproverRule` 加 init 成员(§3)；BuildAsync 已收 `inst` |
| forecast | `ForecastService.cs:58,83` | `_planner.BuildAsync(new Wf_FlowInstance{ StarterId=starterId, VarsJson=varsJson }, …)`；`ResolveRuleNamesAsync` 签名加 `varsJson`，context 带 `VarsJson=varsJson` |
| Role→CC | `FlowEngine.ReadModel.cs:103` | 无需 vars，`VarsJson` 留 null(行为不变) |

**`FlowSchemaValidator`(加 E-WF-014)**：approval 节点/档校验扩 `KnownStrategies` 含新 3 策略；并按策略校配置完整性：`FormField`→`ApproverFieldName` 非空；`DataMap`→`ApproverMapKey`+`ApproverFieldName` 非空；`Group`→`ApproverMembers` ≥1 且每成员合法；非法→`E-WF-014`(高级审批人配置非法)。`When/Filter` 仅做非空时的存在性，不在后端预编译(求值期安全失败兜底)。

---

## §6 设计器(NodePropertyPanel + 档面板 + designerModel)

- **策略下拉**(单档 L176-182 + 档 L320-326)加 3 项：表单字段指定 / 数据映射 / 混合组。
- **FormField**：`ApproverFieldName` = 下拉选「绑定表单中 `type==='user'` 的字段」(读所选 `Wf_FormDef.SchemaJson` 字段列表；取不到时降级文本输入)。
- **DataMap**：`ApproverMapKey` = 选已有 MapKey(调维护页 API list distinct keys) + `ApproverFieldName` = 选匹配字段。
- **Group**：`ApproverMembers` = 可增删/排序的成员行，每行=迷你叶编辑器(策略 + 对应参数，复用 `searchUsers`/`loadRoles`)。
- **When/Filter**：进階段加两表达式输入框 + 可用变量提示(tooltip 列 starter.*/user.* + 表单字段)。
- `designerModel.ts`：`SchemaNode` + 档类型加新字段；`schemaToGraph`/`graphToSchema` round-trip；`validateClient` 镜像 E-WF-014 规则(Group 空/FormField 缺字段/DataMap 缺键)。
- 节点小卡(`ApprovalNode.vue`)可选显示策略徽标(非必须)。

---

## §7 `Wf_ApproverMap` 维护页 + `IApproverMapService` + `DynamicForm` 控件升级

- **`IApproverMapService`**(CRUD)：list(按 MapKey 过滤/分页)、distinctKeys、create/update/delete。校验：`(MapKey,MatchValue)` 同租户内 + 同目标重复 → `E-WF-015`；`ApproverUserId`/`ApproverRoleId` 双空 → `E-WF-015`。
- **控制器** `ApproverMapController`(照 `InboxController` 模式：`LocalizedControllerBase`/`Ok2`/`ICurrentPermissionContext`)。
- **维护视图** `ApproverMapView.vue`(OA 管理菜单)：MapKey 选择/新建 + 行表格(匹配值/审批人或角色/启用)CRUD，审批人/角色复用远程搜。
- **菜单**：OA 组下新增叶(MenuId 取下一空位，幂等守卫块外，授 RoleId=1)。
- **`DynamicForm` `user` 字段升级**(`DynamicForm.vue`)：`f.type==='user'` 分支改为 `el-select`(filterable + remote + 可 `f.multiple` 多选) 远程搜 `userApi`，存 `UserId` GUID(多选存 GUID[])。`isText` 列表移除 `user`(它不再是纯文本)；readonly mask 时显示昵称只读。`dept` 维持现状。

---

## §8 Forecast 精度提升(§1.5.2)

接 vars 后，`FormInitiate` 填單预览/`FormDetail` 预计段能**前解析** FormField(草稿已填该字段)、DataMap(匹配值已知)、含 When/Filter 的条件审批人 → 能解析的关卡显具体人，不能的(如审批中途才产生的字段)显关卡名占位。Group 显合并后的人名清单。`ResolveRuleNamesAsync` 已 try/catch 安全失败兜底。

---

## §9 错误码

| 码 | 含义 | 触发处 |
|---|---|---|
| **E-WF-014**(新) | 高级审批人配置非法(Group 空 / FormField 缺字段 / DataMap 缺 MapKey 或字段) | `FlowSchemaValidator` + `DesignerService` 保存校验 + 前端 `validateClient` 镜像 |
| **E-WF-015**(新) | 审批人映射非法(`(MapKey,MatchValue)` 重复 / 审批目标双空) | `IApproverMapService` create/update |
| E-WF-013(既有) | 审批人缺失 → Suspend | 运行期解析 `Unres` 时引擎挂起(本期新策略缺位统一走此，不静默跳过) |

> 运行期解析缺位**仍返 `Unres`**(不抛、不加新码)，由 handler 决定 `Suspend(E-WF-013)`(单档/档)——与串簽 §3.5 一致。

---

## §10 向后兼容铁律

1. `ApproverResolveContext.VarsJson` 可选 → 既有所有构造点编译通过。
2. `ApproverRule` 保留 4 定位参数 → 既有所有 `new ApproverRule(…)` 编译通过；新成员 null → 走原解析路径。
3. 节点/档无新字段(旧 def)→ `BuildRule`/planner 全 null → **与今天逐字等价**。
4. `ExpressionEvaluator` 不改 → 既有条件边/表单规则零影响。
5. **硬闸**：每 Task `dotnet test --filter Wf` 既有 Wf 测试零改照绿；整支 diff 零 Space 污染。

---

## §11 测试矩阵(TDD)

**解析器单测**：FormField(单值/数组多值/缺字段/无效 GUID/查无人)；DataMap(命中单/多行/角色展开/未命中/租户隔离)；Group(合并去重/部分成员缺位仍成立/全空 Unres/混源 直属+角色+字段)；When(真采用/假 Unres/无 vars)；Filter(候选通过/排除/全排除 Unres/user.deptId==starter.deptId 同部门过滤)；变量命名空间(starter.*/user.* 串化比较)。
**兼容**：5 既有策略 + 无 vars context 行为不变；`BuildRule` 全 null 路径。
**接线**：forecast 带 vars 解析 FormField 显具体人；串簽档 FormField 进档解析。
**校验**：`FlowSchemaValidator` E-WF-014 各分支；`validateClient` 镜像。
**服务**：`IApproverMapService` CRUD + E-WF-015 重复/双空 + 租户隔离。
**gstack QA**(隔离库 CP6DB_OA)：设计含 4 策略的流程 → 维护页种映射 → 填單(含 user 选择器选审批人) → 验各关卡指派正确人 + forecast 显具体人 + When 真假分流 + Filter 同部门筛选 + DataMap 按成本中心路由。

---

## §12 分期(5 波，仿串簽节奏；subagent-driven TDD，每 Task 全新 general-purpose[sonnet]→diff 复核零 Space→本地 commit 不 push→`--filter Wf` 硬闸)

- **P-A 引擎内核**：§2 枚举/ApproverRule/ApproverResolveContext/Wf_ApproverMap(实体+DbSet+索引+迁移) + §4 解析器新 3 策略 + When + Filter + 变量命名空间 + `IApproverMapService` CRUD + 单测。
- **P-B 接线 + 校验**：§3 BuildRule/planner 扩 + §5 四调用点传 VarsJson + `FlowSchemaValidator` E-WF-014 + forecast 精度。
- **P-C 设计器**：§6 面板新策略编辑 + designerModel round-trip + validateClient。
- **P-D 维护页 + 表单控件**：§7 ApproverMapController/View + 菜单 + DynamicForm `user` 选择器升级。
- **P-E i18n 五语 + gstack QA**：§10/§11 词条 + harness + 真浏览器 7 剧本。

---

## §13 决策汇总(brainstorming 2026-06-28 锁定，落码勿翻)

- **D1 范围** = 闭 §1.5.3 全四缺口(③ FormField / ②a When+Filter / ②b DataMap / ① Group)。
- **D2 核心接缝** = `inst.VarsJson` 注入 `ApproverResolveContext`(可选字段)。
- **D3 ② Menu 数据源** = 新建 `Wf_ApproverMap`(非复用 Sys_DictData)。
- **D4 ③ 字段值** = 存 `UserId`(GUID)+ 升级 `DynamicForm` `user` 控件为真选择器。
- **D5 ① JSON 组语义** = 扁平合并(复用现 TokenId 会签，**无新计票维度/无子组会签**)。
- **D6 ②a 条件** = 门控(When，表单 vars) **+** 候选过滤(Filter，候选人属性)**两者都做**。
- **D7 schema 承载** = 方案 X(节点/档保留扁平叶字段 + 新增标量 + Group 用 Members 嵌套；**非**全嵌套 Approver 重构，对串簽面板零扰动)。
- **D8 交付** = 全栈 + gstack QA(沿 OA 一贯)。
- **D9 不做** = 嵌套子组会签 / 映射多层级通配 / dept 控件升级 / 表达式语言扩展 / 新节点类型(§0)。
