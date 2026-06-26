# WFS 工作流引擎 × 电子表单信箱 — 统一总设计（umbrella）

> **源/背景**：2026-06-26 brainstorming 共识。用户决定把两条在途线 —— **WFS 通用工作流运行时内核**（`Wf_FlowToken` token 化引擎，已写 spec `2026-06-26-wfs-runtime-kernel-design.md` 定稿）与 **电子表单信箱**（仿台达 Delta 电子表单系统的 OA 前端外壳）—— **合并重新统规**为一个分层程序。
>
> **设计依据**（逆向真实、不编造）：
> - 用户线框图 `docs/oa/电子表单信箱 线框图（草稿备份）.html`（Design 导出，已解码文案/结构）；
> - **台达 Delta 真实上线系统 6 张实拍照片** `docs/oa/电子表单信箱/*.jpg`（未處理/在途/已處理/暫存/填單/表單查詢/設定 七大区实况）；
> - **台达 Delta WFS 流程设计器 4 张实拍照片** `docs/oa/WFS/*.jpg`（流程狀態編輯/路徑信息維護/Sign Records 簽核履歷弹窗实况，State+Path 状态机模型）+ 离线版流程编辑器 `docs/oa/WFS/流程编辑器-离线版.html`；
> - 现有 `Wf` 引擎实读（`CP6.Core/Services/Wf/` 22 服务 + `CP6.Entity/DomainModels/Wf/` 8 实体 + `cp6.web/src/views/wf/` 16 视图，631+ 测试）。
>
> **本文定位 = umbrella 总设计**：统领三层架构、统一数据模型、内核↔履历的写入钩子、信箱应用设计、分阶段交付。**已写好的 WFS 内核 spec 不推倒重写**，作 L0 引用；本文新增 L1 读模型 + L2 信箱应用，并定义二者与 L0 的接缝。各阶段落码时各自走 writing-plans → TDD。

---

## §0 决策总账（brainstorming 已确认）

| # | 决策 | 选择 |
|---|---|---|
| C1 | 信箱与现有 wf 前端关系 | **统一新外壳，旧视图退役**（TodoCenter/MyApplications/FlowTrace 内容吸收，旧路由重定向） |
| C2 | 代理身份切换深度 | **完整主动 act-as**（会话级切为他人身份处理其收件箱，动作以"代理人(本人)"双记可追溯） |
| C3 | P1 信箱新能力 | 已读/未读 · 仪表盘聚合 · 抄送 CC · 草稿箱 **四项全要** |
| C4 | 移动端 | **P1 桌面端，移动端后置 P2** |
| C5 | P1 边界（真实系统多出项） | **转交（引擎新动作）· 填單表单库（分类+收藏）· 表單查詢（高级搜索）三项全进 P1** |
| C6 | 左侧文件夹命名 | **对齐 Delta 原名**（未處理/在途/已處理/暫存 + 填單/表單查詢/設定） |
| C7 | `Wf_FlowData` 语义 | **每关卡表单快照**（每人签核那一刻表单的样子，与 `Wf_FormData` 字段快照 / `VarsJson` 流程变量区分） |
| C8 | 信箱步骤精度 vs WFS token 内核 | **两者合并重新统规**（token=运行时实时位置；FlowFormTo=传签履历；二者互补一体两面） |
| C9 | 统规文档结构 | **Umbrella 总设计统领 + 保留内核 spec 作 L0**（只补一节"读模型写入钩子"；信箱另起应用 spec；不丢已锁 10 决策，不破 631 兼容） |

**实现层补充决策（本文推荐，待用户审 spec 时确认）**：

| # | 决策 | 取舍理由 |
|---|---|---|
| R1 | act-as 用**租户内轻量会话态**（前端 sessionStorage 态机 + 每动作服务端校验 active 授权），**不上 SaaS 平台超管那套 jti 黑名单/带外重武器** | 代理是租户内常规协作，非跨租户越权；轻量即可，但履历双记照做 |
| R2 | 草稿 = `Wf_FlowInstance.Status=Draft` 态（有实例、无 token、未进流程），**不另起草稿表** | 统一"我的申请/在途"查询；提交即原地 `SpawnToken` 进流程 |
| R3 | 旧视图**重定向保留**，不直接删 | 平滑过渡，降回归风险 |
| R4 | `Wf_FlowFormTo` 与 `Wf_FlowHistory` **并存分工**（履历台账 vs 纯追加事件日志），不合并 | `FlowHistory` 是 631 测试 + 合规依赖项，不动；台账是信箱主读模型 |

---

## §1 三层架构总览

```
┌─ L2  电子表单信箱（应用层）── 仿 Delta + 用户线框图 ──────────────────────────┐
│  左侧文件夹：未處理(待審核|CC) · 在途 · 已處理(月份|全部|我的|CC) · 暫存       │
│  ‖ 填單(分类表单库+☆收藏+常用) · 表單查詢(多条件) · 設定(代理人+显示偏好)      │
│  ‖ 顶部头像(代理切换+「代理中」标记) · 仪表盘(数字卡片+趋势,用户增强)          │
│  详情：左读右签(左只读表单 / 右传签时间线) + 底部操作条(批准/退回/加签/转交)   │
└───────────────────────────────────── 读 ↑ ─────────────────────────────────────┘
┌─ L1  读模型（履历层）── 用户提出，本文设计 ─────────────────────────────────┐
│  Wf_FlowFormTo  传签履历台账（步骤序/关卡码Tocode/应处理人/实处理人/onBehalfOf │
│                 /状态/送签·处理时刻/TokenId）→ 状态列·时间线·已處理多行·应vs实  │
│  Wf_FlowData    每关卡表单快照（InstanceId+TokenId+StepSeq+NodeId+DataJson）   │
│  Wf_FlowCc      抄送（Instance+Recipient+IsRead+ReadAt）                       │
└──────────────────────── token 推进时写入（correct-by-construction）↑ ──────────┘
┌─ L0  token 运行时内核（引擎层）── 已写 spec，作 Layer0 引用 ─────────────────┐
│  Wf_FlowToken(多活动节点) + INodeHandler 插件 + spawn/advance/consume 三原语   │
│  + parallelSplit/parallelJoin 网关 + RowVersion 乐观并发 + 631 兼容硬闸        │
└─────────────────────────────────────────────────────────────────────────────┘
```

**为何这样合最干净**：履历/快照不是事后 join 拼出来的，而是**引擎每推进一个 token 就落一行**。`Wf_FlowFormTo` 带 `TokenId` 维度后，并行多分支的"进行到哪一步"天然精确——每条 token 一串履历，会签每人一行，与内核 §7.2「会签计票按 `(InstanceId, NodeId, TokenId)` 隔离」同口径。这正是把信箱步骤精度和 token 内核合并的根本理由（C8）。

**职责边界**：

| 层 | 唯一职责 | 真相源 |
|---|---|---|
| L0 token | 流程**现在在哪**（活的执行点，支持并行/会签） | `Wf_FlowToken` 集合 |
| L1 履历 | 流程**走过哪些步、谁签的、何时**（给人看的轨迹） | `Wf_FlowFormTo`（台账）+ `Wf_FlowHistory`（事件日志，不动） |
| L2 信箱 | **怎么呈现与操作**（收件箱体验/代理/批量/填单/查询） | 读 L1，写动作经 L0 引擎 |

---

## §1.5 WFS 设计器实证（Delta State+Path 状态机模型）— spec 修正依据

> 据 `docs/oa/WFS/*.jpg`（Delta 流程设计器 + Sign Records 弹窗）实读。这批图**验证了读模型设计、并修正若干信箱与引擎细节**。

### §1.5.1 Delta = State（状态）+ Path（路徑）状态机

- **状态 StateCode**：整数编号节点（`1=填單 / 2=申請人確認 / 10=申請人直屬主管審核 / 21=地區財稅主管審核 / 30=呼叫ERP重試中 / 31=填單人確認 / 1000=流程結束 / 1001=表單退回 / 1500=表單取消`）。每状态有：状态名/英文名/语言资源ID、**流程接鈕（A傳送/C取消…）**、是否停用。
- **路徑 Path**：状态→状态的转移（`開始StateCode→目標StateCode`），带：路徑名稱/英文名、**路徑類別（條件/無條件）**、**下一步審核人類別**，及三组副作用 tab：**知會人員（CC）/ 執行WebAPI / 執行JOB**。
- **节点类型 palette**：`填單(start) / 表單狀態(approval) / 選單多籤(会签) / 數據回寫(service task) / 流程結束(end) / 表單取消(cancel)`。
- **条件/并行分支**：节点 10 → 21（本地区）或 22（外地区）按条件分流（路徑類別=條件）。

**与 CP6 node+edge 模型同构**：State=FlowNode、Path=FlowEdge。差异仅"审批人/副作用挂在路徑(转移)还是节点"——本设计沿用内核 spec 的 **node 携审批人**（等价），并**把 CC/副作用按路徑(边)配置**（§1.5.4）。整数 StateCode = CP6 的 `NodeCode`（Tocode，§2.2）。**P1 不改 node+edge 内核模型**，仅吸收语义。

### §1.5.2 ★ Sign Records 弹窗 = `Wf_FlowFormTo` 时间线（设计被实证）

图 3「2892-Uniform...Sign Records」弹窗逐条印证 `Wf_FlowFormTo` 渲染：

| 实拍条目 | 对应 FlowFormTo |
|---|---|
| `01/13 10:14 ✏️ Form in — NANCY.FENG` | 提交关卡（SentAt/ExpectedHandler） |
| `01/13 10:16 ✓ Applicant Confirm — NANCY.FENG (Deputy of Varapom)` **Agree** | 已办（Status=Approved、ActualHandler=NANCY、**OnBehalfOf=Varapom 代理签**、HandledAt、Comment） |
| `🔶 Manager Approve — Suwanna` **Current Processor** | 当前关卡（Status=Pending、当前 token 停泊） |
| `Below is forecast process:` Manager Approve→HR Approve→Form End（灰） | **预计流程（computed，非持久行）** |

**两大修正确认**：
1. **代理签核双记**（`OnBehalfOf`）被真实系统证明（"NANCY as Deputy of Varapom"）——§2.2/§4.4 设计正确。
2. **时间线含"预计流程"段**（forecast）：已完成/当前 = `Wf_FlowFormTo` 持久行；**预计 = 从当前状态前推 schema + 解析后续审批人计算得出**（不持久）。→ 新增 `ForecastAsync`（§4.3），FormDetail 时间线与 FormInitiate 提交前预览**共用同一前推算法**。

### §1.5.3 下一步審核人類別（审批人解析策略）— 引擎/组织引擎职责

Delta 路徑配 02~20+ 审批人解析策略。CP6 现有 `IApproverResolver`（实读）支持 **5 策略**：`DirectManager(沿 ManagerId 上溯 N 级) / DeptLeader(部门负责人) / Role / Specified / Starter`，消费 PUB 组织模型（`Sys_User.ManagerId/DeptId/RoleId` + `Sys_Dept.ParentId/LeaderId`）。对照：

| Delta 策略 | CP6 现状 |
|---|---|
| 04 直屬主管(某層) / 05 工号三层 | ✅ `DirectManager(Levels)` |
| 03/08 部门主管/部门签核人 | ✅ `DeptLeader`（部分） |
| 11 指定审核人 / By 角色 | ✅ `Specified` / `Role` |
| 15 JSON 组（P 直属/S 部门混合 + GroupSubmit 会签控制） | ❌ 缺 |
| 17/18/20 Menu 数据驱动 / 角色+条件 | ❌ 缺 |
| 02 表单 PB 字段指定审批人 | ❌ 缺 |

→ **常用策略已具备（覆盖多数真实流程）；高级数据/字段/JSON 组策略归引擎 roadmap**（§9），非信箱 P1 阻塞项。**预计流程（§1.5.2）精度受此约束**：能前解析的关卡显具体人，不能的显关卡名占位。

### §1.5.4 其余修正点（落入对应章节）

- **CC 按路徑配置（知會人員）**：除节点/提交/结束抄送外，**转移(路徑)亦可挂知會人員** → §2.4 抄送来源 + §3 写入钩子补"路徑遍历时落 CC"。
- **详情操作条按状态配置（流程接鈕）**：底部按钮不写死，由当前状态的 `流程接鈕`（A傳送/C取消/批准/退回…）驱动 → §4.2 FormDetail 修正。
- **数据回写/WebAPI/JOB = 服务任务**：`數據回寫`=CP6 BridgeHook/IApprovalCallback 回写；`執行WebAPI/JOB`=服务/定时任务 → 对齐内核 roadmap **WFS P2(服务任务)/P3(定时事件)**（§9）。
- **约定终态码**：`流程結束(end=Approved) / 表單退回(SendBack 退回申请人可重提) / 表單取消(Withdraw/Cancel)` 三态，信箱状态显示须区分（§4.2）。

---

## §2 统一数据模型

### §2.1 L0 引擎层（引用 WFS 内核 spec，本文不重述细节）

见 `2026-06-26-wfs-runtime-kernel-design.md` §2：
- 新表 `Wf_FlowToken`（5 业务字段：InstanceId/NodeId/Status/ParentTokenId/ForkId）；
- `Wf_FlowInstance` 加 `RowVersion`（乐观并发）、`CurrentNode` 语义升级为代表节点；
- `Wf_FlowTask` 加 `TokenId`；
- `FlowSchema.FlowNode.Type` 加 `parallelSplit`/`parallelJoin`。

**本文对 L0 的唯一增量** = §3 读模型写入钩子（建议作为一节追加进内核 spec，或在 Phase A 计划中引用本文 §3）。

### §2.2 L1 读模型 — `Wf_FlowFormTo`（传签履历台账，新表）

`CP6.Entity/DomainModels/Wf/Wf_FlowFormTo.cs`，新建。**关卡级台账**：token 每到一个人工关卡写一行（送签时建、处理时更新）。与 `Wf_FlowHistory`（纯追加事件流，不动）分工互补（R4）。

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 传签履历台账（WFS 读模型）。token 每到一个人工关卡落一行：送签时建（应处理人/送签时刻），
/// 处理时更新（实处理人/onBehalfOf/状态/意见/处理时刻）。带 TokenId → 并行多分支履历各成一串。
/// 与 Wf_FlowHistory（纯追加事件日志，护 631 测试）分工：本表是给人看的关卡台账（信箱主读模型）。
/// </summary>
[Table("Wf_FlowFormTo")]
public class Wf_FlowFormTo : BaseTenantEntity
{
    /// <summary>所属流程实例 → Wf_FlowInstance.Id</summary>
    public Guid InstanceId { get; set; }

    /// <summary>所属令牌 → Wf_FlowToken.Id（并行分支隔离；与 Wf_FlowTask.TokenId 同口径）</summary>
    public Guid? TokenId { get; set; }

    /// <summary>关卡顺序号（本实例内单调递增；并行分支共用序号空间，靠 TokenId 区分支）</summary>
    public int StepSeq { get; set; }

    /// <summary>来源节点 Id（路徑 開始StateCode；提交根关卡为 null）。配 NodeId 还原"从哪步走到哪步"。</summary>
    [MaxLength(100)]
    public string? FromNodeId { get; set; }

    /// <summary>关卡节点 Id（路徑 目標StateCode = FlowNode.Id）</summary>
    [MaxLength(100)]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>关卡码（Tocode = Delta 目標StateCode 整数码/稳定业务码）：状态列/履历显示用（取 FlowNode 上的码或 Id）</summary>
    [MaxLength(100)]
    public string? NodeCode { get; set; }

    /// <summary>关卡名快照（建行时从 schema 复制，改版不影响旧履历）</summary>
    [MaxLength(200)]
    public string? NodeName { get; set; }

    /// <summary>应处理人 → Sys_User.Id（送签时的目标审批人；会签多人则多行）</summary>
    public Guid ExpectedHandlerId { get; set; }

    /// <summary>实处理人 → Sys_User.Id（实际点批准/退回者；代理时=代理人本人）</summary>
    public Guid? ActualHandlerId { get; set; }

    /// <summary>代签：被代理人（act-as 时记"代谁签"；非代理为 null）→ Sys_User.Id</summary>
    public Guid? OnBehalfOfId { get; set; }

    /// <summary>关卡状态：0=待签 1=同意 2=驳回 3=转交 4=加签 5=跳过/会签未轮到 6=作废(驳回连坐)。见 FlowFormToStatus</summary>
    public int Status { get; set; }

    /// <summary>处理意见</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }

    /// <summary>送签时刻（token 进入本关卡、建待办那一刻）</summary>
    public DateTime SentAt { get; set; }

    /// <summary>处理时刻（实处理人办结那一刻；待签为 null）</summary>
    public DateTime? HandledAt { get; set; }
}
```

`FlowFormToStatus`（`CP6.Core/Services/Wf/WfStatus.cs`，对齐家族风格）：
```csharp
public static class FlowFormToStatus
{
    public const int Pending = 0;     // 待签
    public const int Approved = 1;    // 同意
    public const int Rejected = 2;    // 驳回
    public const int Transferred = 3; // 转交（改派他人，本行收尾，受让人另起新行）
    public const int AddSigned = 4;   // 加签（衍生关卡）
    public const int Skipped = 5;     // 跳过 / 会签未轮到
    public const int Voided = 6;      // 作废（驳回 terminate 连坐 / 退回清场）
}
```

### §2.3 L1 读模型 — `Wf_FlowData`（每关卡表单快照，新表）

`CP6.Entity/DomainModels/Wf/Wf_FlowData.cs`，新建。**每关卡一份表单快照**——记录每个人签核那一刻表单长什么样，支持逐步留痕 / 字段变更对比 / 详情页"看历史关卡时还原当时表单"。

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 每关卡表单快照（WFS 读模型）。每到一关、每次办结时存一份当时的表单字段值。
/// 区别：Wf_FormData=整单最新字段快照（一次提交一行）；Wf_FlowInstance.VarsJson=流程变量（条件取值源，会被覆盖）；
/// 本表=按关卡逐步留痕的不可变快照（StepSeq 串起"每步表单变化轨迹"）。
/// </summary>
[Table("Wf_FlowData")]
public class Wf_FlowData : BaseTenantEntity
{
    /// <summary>所属流程实例 → Wf_FlowInstance.Id</summary>
    public Guid InstanceId { get; set; }

    /// <summary>所属令牌 → Wf_FlowToken.Id（并行分支隔离）</summary>
    public Guid? TokenId { get; set; }

    /// <summary>关卡顺序号（与 Wf_FlowFormTo.StepSeq 对齐，可 join 同一关卡的履历与快照）</summary>
    public int StepSeq { get; set; }

    /// <summary>快照所在关卡节点 Id</summary>
    [MaxLength(100)]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>该关卡时刻的表单字段值快照 JSON（不可变留痕）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string DataJson { get; set; } = "{}";
}
```

> **快照粒度（落码决策）**：P1 在**每关卡办结时**存一份（够支撑"看每步表单变化"）；若只需"提交时一份 + 每次改动一份"可缩减，留 Phase A 实现时按测试收敛。`DataJson` 来源 = 办结时 `inst.VarsJson`（与条件流转同源）。

### §2.4 L1 读模型 — `Wf_FlowCc`（抄送，新表）

```csharp
[Table("Wf_FlowCc")]
public class Wf_FlowCc : BaseTenantEntity
{
    public Guid InstanceId { get; set; }              // 被抄送的流程实例
    public Guid RecipientId { get; set; }             // 抄送对象 → Sys_User.Id
    [MaxLength(100)] public string? AtNodeId { get; set; }  // 在哪个关卡产生的抄送（节点抄送/结束抄送）
    public bool IsRead { get; set; }                  // 收件箱"未读"标记
    public DateTime? ReadAt { get; set; }
}
```

**抄送人来源（P1，对齐 Delta 知會人員）**：① 流程节点 schema 配置抄送人（`FlowNode` 上加可选 `CcUsers`/`CcRoles`，由 ApproverResolver 同款解析）；② **路徑(转移)挂知會人員**（`FlowEdge` 上加可选 `CcUsers`/`CcRoles`，token 经该转移时落 CC，§3）；③ 提交时发起人指定抄送；④ 流程结束抄送。四种均写 `Wf_FlowCc` 行。

### §2.5 L2 应用支撑（新表 + 列改）

| 实体 | 改动 | 用途 |
|---|---|---|
| `Wf_FormFavorite`（新表） | `UserId` / `FormKey`（+唯一约束 Tenant+User+FormKey） | 填單☆收藏 / "常用"快捷 |
| `Wf_InboxPref`（新表） | `UserId` / `PrefsJson` | 显示偏好（主旨概要/分页数/隐藏取消单…） |
| `Wf_FlowTask` | +`IsRead` / +`ReadAt` | 未處理"未读"标记（待办项读状态） |
| `Wf_FlowInstance` | +`Draft` 状态值（`FlowInstanceStatus.Draft`） | 暫存（草稿=有实例无 token）R2 |
| `Wf_FormDef` | +`Category` / +`SubCategory` | 填單分类表单库（机能大类→子类） |
| `Wf_FlowDelegate` | **复用**为 act-as 授权依据（现有 Grantor/Delegate/有效期/Scope 字段够用） | 代理身份切换 |

> **读状态归属**：未處理读状态 → `Wf_FlowTask.IsRead`；CC 读状态 → `Wf_FlowCc.IsRead`。**不放 `Wf_FlowFormTo`**（履历是事实轨迹，与"谁读没读"正交）。

### §2.6 DbSet / 索引 / 迁移

- 新 DbSet：`Wf_FlowFormTos` / `Wf_FlowDatas` / `Wf_FlowCcs` / `Wf_FormFavorites` / `Wf_InboxPrefs`（挨现有 Wf DbSet 后加）。
- 关键索引：
  - `Wf_FlowFormTo`：`(InstanceId, StepSeq)`（时间线排序）、`(InstanceId, TokenId)`（分支履历）、`(ExpectedHandlerId, Status)`（"应我处理"反查兜底）；
  - `Wf_FlowData`：`(InstanceId, StepSeq)`；
  - `Wf_FlowCc`：`(RecipientId, IsRead)`（CC 收件箱列表）、`(InstanceId)`；
  - `Wf_FormFavorite`：唯一 `(TenantId, UserId, FormKey)`。
- EF 迁移分阶段：Phase A 迁移 = L1 三表（FlowFormTo/FlowData/Cc）+ Token 内核表（合 WFS 迁移）；Phase B/C 迁移 = FormFavorite/InboxPref + 列改（IsRead/Category/Draft 走 Status 值不需列）。
- InMemory 限制同内核 spec（rowversion 并发用 SqlServer/SQLite provider 测）。

### §2.7 身份编码方案：functionID + flowcode（每表单一专属流程）

> 用户决策（2026-06-26）：流程编辑器以 **functionID** 为核心键；每表单一个 **flowcode**（Delta 式 2887/2889）；**flowcode ↔ functionID 1:1 不可重复**。functionID 取**独立新字段**（不绑死 FormKey/Sys_Menu）。

`Wf_FlowDef` 加两字段（独立于现有 FlowKey/FormKey）：

```csharp
/// <summary>功能码（MSBBPA010 这种程序/功能标准码）。流程编辑器的组织主键 + 业务功能关联键。租户内唯一。</summary>
[MaxLength(50)] public string? FunctionId { get; set; }

/// <summary>流程编号（Delta 式 2887/2889，人面可读）。后续逐表单设定。租户内唯一。</summary>
[MaxLength(50)] public string? FlowCode { get; set; }
```

- **唯一约束（租户内）**：`UQ(TenantId, FunctionId)` + `UQ(TenantId, FlowCode)` + `UQ(TenantId, FormKey)`（1 表单 ↔ 1 流程，§4.8）→ 三约束共同保证 functionID/flowcode **不可重复且 1:1**。
- **可空过渡**：FunctionId/FlowCode 录入前可空（flowcode "后续设"）；编辑器保存时校验唯一、非空。
- **流程编辑器**：按 `FunctionId` 列表/打开/新建流程（核心键，§4.8）；列表显示 `FlowCode` 作人面编号（信箱单号亦可用 FlowCode+流水）。
- **现有键不动**：`FlowKey`（内部稳定键）/`FormKey`（绑定表单 schema 键）保留，避免触动 631 测试与既有数据。

---

## §3 读模型写入钩子（内核 ↔ 履历的接缝）★

> **这是合并的技术核心**：履历/快照不另起服务事后拼，而是**嵌进 token 原语与 handler**，随引擎推进 correct-by-construction 落库。本节即"建议追加进 WFS 内核 spec 的那一节"。所有写入与 token 变更**共享同一 scoped DbContext、同一次 `SaveChanges`**（沿用 OA2-D5 原子铁律，回调/履历/token 一并落或一并回滚）。

| 引擎时机（L0） | 写 L1 履历/快照 |
|---|---|
| `ApprovalNodeHandler.OnEnterAsync` 建 `Wf_FlowTask`（token 进审批节点、送签） | **每个应处理人一行** `Wf_FlowFormTo`：`Status=Pending`、`SentAt=now`、`ExpectedHandlerId`、`TokenId=ctx.Token.Id`、`StepSeq=NextSeq(inst)`、`NodeCode/NodeName` 快照；并存一份 `Wf_FlowData`（当前 `VarsJson` 快照） |
| `ActAsync` 办结（approve/reject） | 更新该 `(InstanceId, NodeId, TokenId, ExpectedHandlerId)` 行：`Status=Approved/Rejected`、`ActualHandlerId=actor`、`OnBehalfOfId=代理时的被代理人`、`HandledAt=now`、`Comment`；办结时再存一份 `Wf_FlowData` 快照 |
| `TransferAsync` 转交（§4.5，引擎新动作） | 原行 `Status=Transferred`、`ActualHandlerId=转出人`、`HandledAt=now`；受让人**另起新行** `Status=Pending`、`ExpectedHandlerId=受让人`、`SentAt=now`、同 `TokenId/NodeId` |
| `AddSignAsync` 加签 | 加签人新行 `Status=Pending`、`AddSigned` 关联；继承原任务 `TokenId`（同内核 §7.3） |
| 会签多人一节点 | 多行（每人一行），计票口径与内核 `(InstanceId, NodeId, TokenId)` 一致；未轮到/被否决的并存行按 `Skipped/Voided` 收尾 |
| `AdvanceToken` 经路徑转移（内核 §4.2） | 新关卡履历行 `FromNodeId=来源节点`；**若该 `FlowEdge` 配了知會人員 → 写 `Wf_FlowCc` 行**（§2.4 来源②） |
| `ActAsync` 驳回 = terminate（内核 §7.2 `CancelAllActiveTokens`） | 全实例 `Pending` 履历行 → `Voided`（连坐收尾，时间线显示"因驳回终止"） |
| `SendBackAsync` 退回（内核 §7.3 清场 + 重建根 token） | 被清的在途关卡履历行 → `Voided`；退回目标节点重建 token 时正常新起 `Pending` 行 |
| `EndNodeHandler` / `FinishIfDrained` 实例通过 | 无新人工关卡；若配了"结束抄送"，写 `Wf_FlowCc` 行 |

**`StepSeq` 规则**：`NextSeq(inst)` = 当前实例 `Max(StepSeq)+1`（含并行分支共享序号空间）。并行两分支各自 token 推进时各取递增号，时间线按 `SentAt` 排序、按 `TokenId` 分支分组显示。

**幂等**：履历写入跟随 token 原语的 Active 守卫与 `ActAsync` 重试（内核 §6）。重试重读后，已写履历行经 `(InstanceId, NodeId, TokenId, ExpectedHandlerId, SentAt)` 去重（或挂 EF 变更跟踪天然合并），不重复落行。

---

## §4 信箱应用设计（L2）

### §4.1 信息架构（对齐 Delta，C6）

**左侧文件夹**（邮箱隐喻）：

```
未處理   待審核 | CC                 ← 待我审批 / 抄送我（标签页，未读标记，勾选批量）
在途     我的在途表单                ← 我发起、流转中（含「處理人」列，勾选）
已處理   ◀年份▶ 1..12月 · 全部|我的|CC  ← 月份选择器 + 三标签
暫存     草稿箱                      ← 改 / 提交 / 删
─────────
填單     分类表单库 + ☆收藏 + 常用    ← 机能大类→子类→表单
表單查詢  多条件高级搜索
設定     代理人設定 + 显示偏好
```

**顶部**：Logo · 表单快速搜索 · 语言切换（五语）· 头像下拉（**代理身份切换** + 切回本人 + 个人资料/代理人设置/退出）；代理中时顶部显「**代理中**」标记。

**仪表盘（用户增强，Delta 无）**：登录首屏数字卡片（待我处理/我发起/本月完成/被退回）+ 近 N 天审批量趋势 + 最近表单列表。

### §4.2 视图清单（`cp6.web/src/views/oa/inbox/`，新建；设计器 `views/oa/designer/`）

| 视图 | 关键交互 | 读模型来源 |
|---|---|---|
| `InboxDashboard` | 数字卡片 + 趋势图 + 最近列表 | InboxService.StatsAsync |
| `InboxPending`（未處理） | `待審核`\|`CC` 标签、未读加粗、勾选→浮出批量条（批准/退回/意见） | `Wf_FlowTask`(待审核) / `Wf_FlowCc`(CC) |
| `InboxRunning`（在途） | 我发起在途单、`處理人`列（当前关卡应处理人）、勾选 | `Wf_FlowInstance`(我发起,Running) + `Wf_FlowFormTo`(当前关卡) |
| `InboxDone`（已處理） | 月份选择器 + `全部`\|`我的`\|`CC` | `Wf_FlowFormTo`(我处理过) / 实例(全部) / `Wf_FlowCc` |
| `InboxDraft`（暫存） | 草稿列表，改/提交/删 | `Wf_FlowInstance`(Status=Draft) |
| `FormCatalog`（填單） | 机能大类→子类→表单卡片、☆收藏、顶部"常用" | `Wf_FormDef`(Category/SubCategory) + `Wf_FormFavorite` |
| `FormQuery`（表單查詢） | 申请人/填单人/处理人/单号/类型/日期/主旨/适配结果 | InboxService.QueryAsync |
| `InboxSettings`（設定） | 代理人設定（增删授权/有效期/范围）+ 显示偏好 | `Wf_FlowDelegate` + `Wf_InboxPref` |
| `FormDetail`（详情/审批页） | **左读右签**（= Delta Sign Records，§1.5.2）：左只读表单（复用 `DynamicForm.vue`，看历史关卡可还原当时快照）/ 右传签时间线：**已完成·当前 = `Wf_FlowFormTo` 持久行**（含代理"代 X 签"），**预计段 = `ForecastAsync` 前推计算**（非持久），分支分组；**底部操作条按当前状态 `流程接鈕` 配置驱动**（默认集：批准/退回/加签/**转交**/意见） | `Wf_FlowFormTo` + `Wf_FlowData` + `ForecastAsync` |
| `FormInitiate`（发起） | 申请人信息自动带入、明细、附件拖拽、**提交前预览审批节点（= `ForecastAsync` 同一前推算法）**、存草稿/提交 | `Wf_FormDef`/`Wf_FlowDef` + `ForecastAsync` |

**复用**：`DynamicForm.vue`（动态表单渲染）、`FlowTrace.vue`（升级为读 `Wf_FlowFormTo` 的时间线）、`ruleEngine.ts`、`fieldMask.ts`。
**退役（R3）**：`TodoCenter.vue`→吸收进 `InboxPending`+`InboxDashboard`；`MyApplications.vue`→吸收进 `InboxRunning`+`InboxDone`；旧路由 302 重定向到信箱。

### §4.3 后端服务（新信箱/设计器服务置 `CP6.Core/Services/Oa`，消费 `Wf` 引擎；引擎动作如 TransferAsync 仍在 `Services/Wf`）

| 服务 | 方法（要点） |
|---|---|
| `IInboxService` / `InboxService` | `PendingAsync`(待审核/CC) · `RunningAsync` · `DoneAsync`(月份/三标签) · `MarkReadAsync`(幂等) · `StatsAsync`(仪表盘计数+趋势) · `QueryAsync`(表單查詢多条件) |
| `IForecastService` / `ForecastService` | `ForecastAsync`：从当前状态（详情）或起点（发起）**前推 schema** —— 逐转移取首真边、并行分叉全展开、调 `IApproverResolver` 预解析后续审批人 → 返回预计关卡序列（能解析显具体人，不能则关卡名占位，§1.5.3）。FormDetail 预计段 + FormInitiate 提交前预览**共用**；遇 join/未知条件按"乐观单链"展示并标注 |
| `IDraftService` / `DraftService` | `SaveDraftAsync` · `UpdateDraftAsync` · `SubmitDraftAsync`(Draft→引擎 `SubmitAsync` 起 token) · `DeleteDraftAsync` |
| `ICcService` / `CcService` | 节点 schema 抄送解析 · 提交/结束抄送 · 写 `Wf_FlowCc` |
| `IDelegateService` / `DelegateService` + `ActAsContext` | `MyGrantsAsync`(我能代理谁/谁能代理我) · `EnterActAsAsync`/`ExitActAsAsync`(切换/切回) · `AssertActiveGrant`(每动作校验) |
| 引擎 `TransferAsync`（新动作，AdvancedFlow） | 见 §4.5 |
| `ApprovalService.ActBatchAsync` | 批量批准/退回，逐条结果（允许部分失败回报） |
| `IFavoriteService` / `IPrefService` | 收藏增删 · 显示偏好读写 |

### §4.4 代理 act-as 机制（C2 + R1）

- **授权** = `Wf_FlowDelegate`（Grantor=委托人/被代理人，Delegate=代理人，有效期，Scope 可限 FlowKey）。設定页维护。
- **切换** = 前端 sessionStorage 态机（仿 SaaS impersonation 但**租户内轻量**，无 jti 黑名单/带外标志）：代理人在头像菜单选"切为 X 身份"→ 前端置 `actingAs=X` 态 → 收件箱查询带 `actingAs` → 看到 X 的未處理。
- **每动作服务端校验**：任何审批/转交动作携带 `actingAs` → 服务端 `AssertActiveGrant(actor=本人, onBehalfOf=X)` 校验存在有效授权（防伪造）；失败抛 `E-WF-0xx`。
- **履历双记**：动作落 `Wf_FlowFormTo.ActualHandlerId=代理人本人` + `OnBehalfOfId=被代理人`（§3）；时间线显示"代理人(代 X)"，可追溯。
- **顶部标记**：`actingAs` 非空时顶部「代理中」横幅 + 一键切回。

### §4.5 转交（引擎新动作，C5）

`AdvancedFlow.TransferAsync(taskId, actorId, toUserId, comment)`：
1. 校验：目标 task `Status==Pending`、actor 为该 task 应处理人（或其代理）、`toUserId` 同租户 → 否则抛 `E-WF-0xx`；
2. 改 `Wf_FlowTask.AssigneeId = toUserId`（保持同 `TokenId/NodeId`，不流转、不计票变化）；
3. 履历：原行 `Transferred`、受让人新起 `Pending` 行（§3）；`AddHistory("transfer", "转交 X→Y")`；
4. `notifier.TodoCreatedAsync(toUserId)` 实时推待办。
> 与"加签"区别：加签 = 增加一个审批人（并存计票）；转交 = 把待办**移交**给他人（原处理人退出该关卡）。

### §4.6 错误码（Wf 现无结构化码，本程序引入 `E-WF-0xx`）

沿用 CP6.Core 范式：服务抛 `InvalidOperationException("E-WF-0xx")` → 控制器 catch 转 BizException → 前端 i18n。

| 码 | 含义 |
|---|---|
| E-WF-001 | act-as 无有效授权（伪造代理） |
| E-WF-002 | 转交目标非同租户 / 非待办任务 |
| E-WF-003 | 草稿越权提交（非本人草稿） |
| E-WF-004 | 批量操作含无效/已办任务（逐条回报，不整体失败） |
| E-WF-005 | 抄送对象解析为空 / 越租户 |
| …（落码时按需续）| |

### §4.7 多租户 / i18n

- 所有新实体 `BaseTenantEntity`（自动租户隔离）；**act-as 严格租户内**（与 SaaS 平台超管跨租户带外严格区分）。
- 五语 i18n（简/繁/英/日/越或现有语种）按 `DbStringLocalizer` 三铁律补；信箱所有文案 + 错误码 + 关卡状态走词条；落码后重建五语快照。

### §4.8 OA 模块 · 每表单一专属流程 · 流程编辑器（设计器）

**OA 模块结构（用户决策 2026-06-26）**：新建 **OA 功能模块**（菜单/产品大组，与 MES/WMS/PUB 平级，对齐模块/菜单分类约定），承载 电子表单信箱 + 流程编辑器 + 工作流引擎；OA 相关后续都归此。
- **代码低 churn**：**引擎代码保留 `Wf` 命名空间**（不动 8 实体/22 服务/631 测）；新信箱 + 设计器**前端置 `cp6.web/src/views/oa/`**、后端新服务置 `CP6.Core/Services/Oa`（消费 Wf 引擎）；"OA" 是产品/菜单面，"Wf" 是工作流技术内核（命名空间 ⊥ 菜单分组）。
- **其他模块共用**：ERP/MES/WMS/Fin/Pur… 任一功能要审表单，**经单一入口 `IApprovalService.SubmitAsync(bizType,…)` 接入**，其 functionID 在流程编辑器编好专属流程即可——编辑器是 OA 提供的中央能力。

> **每表单一专属流程（1 表单 ↔ 1 流程）**：不让一条流程被多张表单共享复用——共享会造成流程归属混乱、改一处牵动多处。身份编码见 **§2.7（functionID + flowcode，1:1 不可重复）**。

**绑定模型（贴合现有，无需新表）**：
- `Wf_FlowDef.FormKey` 本就是"一条流程绑一张表单"（**1:1 天然契合**本决策）；流程库 = **每表单专属流程的集合**（非"一流程多表单"共享库）。
- 业务挂接仍走**单一入口**：`Wf_ApprovalBinding`（BizType→FlowKey，每业务/表单一条启用绑定）+ `IApprovalService.SubmitAsync(bizType,…)`；**约定每业务/表单专属 FlowKey，不挂他表流程**。终态反向回调 `IApprovalCallback` 落地业务。
- **"复用"仅以模板克隆形式（可选、安全口径）**：编辑器里"以现有流程为模板克隆"→ 生成**独立副本（新 FlowKey）**，此后各改各的、互不影响；**不做活引用共享**。
- 信箱填單：选一张表单 → 起它专属流程（FormKey→其 FlowDef）。

**流程编辑器（设计器，你的重设计稿 `流程编辑器-离线版.html` 为目标；基础版随引擎长，W7）**：
- **基础版（覆盖 P1 内核能力，现可做）**：节点拖拽（填單/審批/結束/取消/退回）+ 路徑（排他条件）+ 并行 split/join + **并簽**（会签 all）+ 状态进阶参数（逾時天數/允許退回/逾時提醒/自動跳轉）+ 路徑「知會人員」（CC）+ 下一步审核人类型（现有 5 策略）。输出 `SchemaJson` → `IFlowDefService.SaveDefAsync(flowKey, formKey, schemaJson)`。
- **随引擎长（后续）**：**串簽**（顺签：按序逐个，引擎增量，§9）/ **系統動作**（数据回写=BridgeHook，WFS P2）/ 執行WebAPI（P2）/ 執行JOB（P3）/ 腳本条件。
- **流程管理 UI（轻量，填單能挂流程的前提，早于完整设计器）**：每表单的流程 列表/版本/启用停用 + 表单↔流程 1:1 绑定维护 + 模板克隆。

---

## §5 分阶段交付

| 阶段 | 内容 | 验收标志 | 依赖 |
|---|---|---|---|
| **A 引擎 + 读模型** | WFS token 内核（内核 spec §0~§12）+ §3 读模型写入钩子（FlowFormTo/FlowData/Cc 表 + 嵌 token 原语/handler） | 631 测试不改照绿；并行审批流程 token 跑通；履历/快照随推进正确落库 | 无（底座） |
| **B 信箱核心** | 信箱壳 + 四文件夹（未處理/在途/已處理/暫存）+ CC + 草稿 + 已读 + 仪表盘 + 批量 + 详情左读右签（读 FlowFormTo）+ **流程管理 UI（轻量：每表单流程 列表/版本/启用 + 表单↔流程 1:1 绑定，填單挂流程前提）** | gstack QA 跑通收件箱全流程；旧视图重定向；i18n 五语 | A 的读模型 |
| **C 信箱进阶** | 代理 act-as + 转交 + 填單表单库（分类+收藏）+ 表單查詢 + 設定 | gstack QA 跑代理切换全流程 + 转交 + 填单发起 | B |
| **C′ 流程设计器（基础版）** | 覆盖 P1 内核能力的可视化设计器（节点拖拽 填單/審批/結束/取消/退回 + 路徑 + 并行 split/join + 并簽 + 状态进阶参数 + 路徑知會人員 + 审核人类型）+ 模板克隆，输出 `SchemaJson`→`SaveDefAsync`（§4.8） | 设计器能编出**能跑的真实审批流**（非手写 JSON） | B（可与 C 并行） |
| **D 后续（P2+）** | 移动端响应式 · 在途批量转单 · 通知設定 · 同单多状态多行显示 · 设计器随引擎长（串簽/系統動作/WebAPI/JOB/腳本）· WFS P2~P5（服务任务/定时/子流程/全量设计器） | — | C / C′ |

**依赖图**：`A(引擎+读模型) → B(信箱核心+轻量流程管理) → {C(信箱进阶) ‖ C′(基础版设计器)} → D(后续)`。每阶段一个 writing-plans → subagent TDD 循环；阶段间用户审检查点。

> **流程从哪来（各阶段）**：A/B 期用**手写/种子 `SchemaJson` + 轻量流程管理 UI 绑定**即可让信箱跑起来；C′ 基础版设计器落地后，业务人员可视化编每表单专属流程。

---

## §6 测试与 QA（必跑 skill）

- **TDD 后端单测**（每阶段）：
  - A：内核 token 三原语/并行 happy path/驳回 terminate/嵌套分叉/会签隔离/并发卡死防护/回填种子（内核 spec §10）+ **履历写入**（送签建行/办结更新/会签多行/转交收尾/驳回连坐 Voided/快照逐关卡）。
  - B：CC 可见性（含路徑知會人員）· 已读幂等 · 草稿提交流转（Draft→token）· 仪表盘计数边界 · 批量部分失败逐条回报 · **`ForecastAsync` 前推**（线性显具体人 / 并行展开 / 不可解析关卡名占位 / join 标注）· 时间线三态（完成·当前·预计）渲染数据。
  - C：act-as 授权校验 + 履历双记 · 转交（仅 pending+租户内+履历收尾）· 收藏唯一约束 · 表單查詢多条件过滤。
- **gstack 真浏览器 QA**：A=并行审批流程；B=收件箱四文件夹 + 详情左读右签；C=代理切换全流程 + 转交 + 填单发起。固化 `docs/superpowers/qa/wfs-form-inbox/`。
- **i18n 五语快照**重建（B/C 前端落地后）。
- **基线**：后端 1189 测 / 1 skip → 各阶段 +N 全绿；前端 type-check/vitest/build 绿。

---

## §7 与现有 WFS 内核 spec 的关系（C9）

- 本 umbrella **统领**；`2026-06-26-wfs-runtime-kernel-design.md` 作 **L0 引擎层**，内容不变、不推倒。
- 本文 §3「读模型写入钩子」= 对内核的唯一增量，**建议在 Phase A 实现时作为内核的读模型扩展**（或追加为内核 spec 的一节 §13）。
- 信箱应用（L2，§4）落码时另起应用 spec（Phase B/C 各自 writing-plans）。
- **不丢内核已锁 10 决策（D1~D10），不破 631 兼容硬闸。**

---

## §8 决策总账

| # | 决策 | 落点 |
|---|---|---|
| C1~C9 | 见 §0（brainstorming 确认） | 全文 |
| R1 | act-as 租户内轻量会话态，不上带外重武器 | §4.4 |
| R2 | 草稿 = `FlowInstance.Status=Draft`，不另起表 | §2.5 / §4.3 |
| R3 | 旧视图重定向保留 | §4.2 |
| R4 | `Wf_FlowFormTo` 与 `Wf_FlowHistory` 并存分工 | §2.2 / §3 |
| U1 | 履历/快照随 token 推进写入（correct-by-construction） | §3 |
| U2 | 三层架构：token=位置 / FlowFormTo=履历 / 信箱=呈现 | §1 |
| U3 | 分阶段 A→B→C→D，umbrella 统领、内核 spec 不动 | §5 / §7 |
| W1 | Sign Records 时间线 = FlowFormTo 持久(完成/当前) + `ForecastAsync` 预计(computed)；代理"代 X 签"被真实系统实证 | §1.5.2 / §4.2 / §4.3 |
| W2 | CC 来源加"路徑知會人員"（转移亦可配抄送） | §2.4 / §3 |
| W3 | 详情底部操作条按当前状态 `流程接鈕` 配置驱动，非写死 | §1.5.4 / §4.2 |
| W4 | 审批人解析 P1 用已有 5 策略；高级策略(JSON 组会签/数据/字段驱动)归引擎 roadmap | §1.5.3 / §9 |
| W5 | Delta State+Path 与 CP6 node+edge 同构，P1 不改内核模型仅吸收语义；StateCode=NodeCode(Tocode) | §1.5.1 / §2.2 |
| W6 | **每表单一专属流程（1:1），不共享复用**（避免流程混乱）；"复用"仅模板克隆为独立副本；契合现有 `Wf_FlowDef.FormKey` | §4.8 |
| W7 | 流程编辑器基础版覆盖 P1 内核能力、随引擎(串簽/系統動作/WebAPI/JOB)逐步长；轻量流程管理 UI(1:1 绑定)早于完整设计器 | §4.8 / §5 |
| W8 | 新建 **OA 功能模块**（菜单/产品大组）承载信箱+编辑器+引擎；引擎保留 `Wf` 命名空间(低 churn)，新前端 `views/oa`、新服务 `Services/Oa`；他模块经 `IApprovalService` 共用编辑器 | §4.8 |
| W9 | 身份编码：**functionID**(MSBBPA010,独立字段,编辑器核心键) + **flowcode**(2887,Delta 式编号)，租户内各唯一 + FormKey 唯一 → **1:1 不可重复** | §2.7 |

---

## §9 YAGNI / 后续（D 阶段及以后）

- 移动端响应式（P2）；在途批量转单（設定批量改派）；通知設定（签核完成/超时邮件）；同单多状态多行显示偏好；多地区（CP6 映射多租户，P1 已天然隔离）。
- **审批人解析高级策略（§1.5.3）**：JSON 组混合（P 直属/S 部门 + GroupSubmit 会签控制）/ Menu 数据驱动 / 角色+条件 / 表单字段(PB)指定审批人 —— 扩 `IApproverResolver`，归引擎/组织引擎 roadmap（P1 用已有 5 策略，预计流程精度随之）。
- **串簽（顺签，引擎增量）**：编辑器有"串簽=一组审核人按先后顺序逐个审完"。内核 P1 做的是**并行会签**（all/any/veto，同批并存计票）；串簽是**顺序**语义，需建审批节点链或加 serial 计票模式 —— 归引擎增量（设计器基础版先支持并簽，串簽随引擎落地，W7）。
- **WFS 引擎深化（对齐 Delta 节点类型）**：服务任务 `數據回寫`=CP6 BridgeHook/IApprovalCallback 回写（P2）/ `執行WebAPI`（P2）/ `執行JOB` 定时事件（P3）/ 子流程（P4）/ **Delta 状态机式 BPM 设计器**（P5，状态+路徑画布，参 `流程编辑器-离线版.html`）—— 见内核 spec §11。
- inclusive 包容网关、跨并行块退回、单分支驳回不连坐 —— 内核 hardening。

---

*生成于 2026-06-26。配套：L0 内核 spec `2026-06-26-wfs-runtime-kernel-design.md`；落码位 `CP6.Entity/DomainModels/Wf`、`CP6.Core/Services/Wf`、`cp6.web/src/views/wf/inbox`。*
