# Space 03 · 可配置库位编码引擎 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | SPACE-03 可配置编码引擎 |
| 所属模块 | Space 空间数字底座 · Part 1（P1） |
| 里程碑 | **P1**（编码生产；上游 [01](./01-editor-template.md)/[02](./02-free-layout.md) 产几何，下游 [04](./04-publish-contract.md) 发布给 WMS） |
| 技术栈 | Vue3 + TypeScript（规则编辑器 + 实时预览）/ .NET8 Web API + EF Core（生成/重排/校验） |
| 命名空间 | `cp6.web/src/views/space/code-rule` / `CP6.Core/Services/Space/CodeEngineService.cs` |
| 落地决策 | D4 发布即冻结（**冻编码不冻几何**）/ D7 采纳态绑既有冻结码（CodeOrigin=2 不重编）/ Aisle 条件段（变长路径）/ **作用域规则必须产出租户全局唯一码** |
| 依赖 | [00 数据模型](./00-data-model.md)（`Space_CodeRule` 表壳 §4.8、`Location.{LocationCode,CodeOrigin,Status,Placed}` §4.6、过滤唯一索引、状态机 §7）、[01](./01-editor-template.md)/[02](./02-free-layout.md)（已建好的几何与层级链） |

> **题眼**：01/02 把几何骨架搭好后，库位的 `LocationCode` 一律为空（`CodeOrigin=1` 草稿）。本章是**把"几何层级"翻译成"业务编码"的引擎**——用户配一套**分段规则**（区-巷-架-层-位，每段定义名称/位数/分隔符/起始/步长/取值源），引擎沿每个库位的**层级链**遍历求值、拼成编码、写回库位。三件硬约束贯穿全章：① **租户全局唯一**（库位编码是发给 WMS 的 join key，分段必须含足够上层段，否则跨库区撞码、被唯一索引拦下）；② **Aisle 条件段**（无巷道库区跳过该段 → 变长路径，但仍须唯一）；③ **发布即冻结**（D4：草稿期编码可反复重排，一经 04 发布终生不改；冻编码**不**冻几何，几何仍可在 02 调）。**记住一句**：03 只生产 `LocationCode`（写 Status=0 草稿库位），冻结发生在 04；采纳码（CodeOrigin=2）是外部既有真相，03 **跳过不碰**。

---

## 目录
- 第1章 功能概述与定位（与 01/02/04 的边界）
- 第2章 编码规则模型 Space_CodeRule（作用域 + 优先级 + IsDefault）
- 第3章 分段定义 Segments JSON 规范（取值源 / 位数 / 分隔符 / 起始步长 / 条件段）
- 第4章 层级遍历生成算法（Floor→Zone→[Aisle]→Rack→col/level/depth）
- 第5章 Aisle 条件段与变长路径
- 第6章 **租户全局唯一保证**（规则完备性静态预检 + 生成后唯一校验）
- 第7章 草稿批量重排（避开唯一索引中途违约）
- 第8章 实时预览（规则编辑器）
- 第9章 与 D7 采纳态、D4 冻结闸门的关系
- 第10章 API 接口设计
- 第11章 消息一览
- 第12章 集成与依赖
- 自检

---

## 第1章 功能概述与定位

**目的**：提供一套**可配置**的库位编码引擎，把 01/02 建好的空间层级几何，按租户自定义规则翻译成**租户内全局唯一**的库位编码，写回 `Space_Location.LocationCode`，供 04 发布给 WMS。

**本章范围（03）：**
- 编码规则 `Space_CodeRule` 的语义：作用域（租户/楼层/库区）、优先级解析、默认规则。
- 分段定义 `Segments` JSON 规范：每段的取值源、位数补零、分隔符、起始值、步长、大小写、条件。
- 层级遍历生成：对作用域内每个草稿库位，沿层级链求各段值 → 拼接 → 写回。
- Aisle 条件段（无巷道库区跳过）与由此产生的**变长路径**。
- **租户全局唯一**：规则完备性静态预检 + 生成后 `(TenantId, LocationCode)` 唯一校验。
- 草稿批量重排（整体重新生成、交换编码不撞唯一索引）。
- 规则编辑器实时预览。

**不含（划清边界）：**
| 能力 | 去哪章 |
|---|---|
| 建几何 / 模板生成 / 草稿保存 | [01 章](./01-editor-template.md) |
| 拖拽/旋转/对齐等几何精修 | [02 章](./02-free-layout.md) |
| **发布给 WMS / 发布即冻结的状态翻转 / 采纳导入落库 / 对账** | [04 章](./04-publish-contract.md) |
| 3D 渲染 / 按编码定位 | [05](./05-viewer-core.md)/[06](./06-camera-pick.md) |

> **03 与 04 的分工**：03 = "**生产**编码"（把空码草稿写上码，草稿期可反复重排）；04 = "**冻结并发布**编码"（Status 0→1，之后码终生不变，推给 WMS）。冻结这个动作属 04；03 只负责让发布前的草稿编码**正确、唯一、可重排**。03 提供"发布前编码预检"给 04 当闸门入口（第9章）。

> **03 与 D7 采纳的分工**：采纳库位 `CodeOrigin=2`、`Status=1 已发布`、码是外部既有冻结码（00 §7.1）。03 的生成/重排**只作用于 `CodeOrigin=1` 且 `Status=0` 的草稿库位**，**永不触碰采纳码**。反向建模（01 §8/02）只补几何不改码——03 在此只提供"有几何无码的格口可选转生成新码（CodeOrigin=1）"这一条旁路。

---

## 第2章 编码规则模型 Space_CodeRule

表壳已在 00 §4.8 定义（`RuleName / ScopeType / ScopeId / Segments / IsDefault`）。本章定其**语义**。

### 2.1 作用域 ScopeType
| ScopeType | 含义 | ScopeId | 用途 |
|---|---|---|---|
| 0 租户默认 | 全租户兜底规则 | null | 没配楼层/库区规则时用它 |
| 1 楼层 | 某 Floor 专用 | FloorId | 一层一套编码体系 |
| 2 库区 | 某 Zone 专用 | ZoneId | 同层不同库区不同编码（如冷库 vs 常温） |

### 2.2 规则解析优先级（就近覆盖）
对某库位生成编码时，按**最具体优先**选规则：
```
该库位所属 Zone 的库区规则(ScopeType=2, ScopeId=ZoneId)
  ↓ 无 → 该库位所属 Floor 的楼层规则(ScopeType=1, ScopeId=FloorId)
  ↓ 无 → 租户默认规则(ScopeType=0, IsDefault=true)
  ↓ 无 → E-SPACE-301 未配置编码规则
```
- 同作用域多条规则时取 `IsDefault=true` 那条；无默认且多条 → E-SPACE-302（规则歧义，需指定默认）。
- 一次"整层生成"可能命中**多套规则**（不同库区各自的库区规则）——这正是要害：必须保证拼出来的码**跨规则、跨库区仍租户全局唯一**（第6章）。

### 2.3 默认规则 IsDefault
- 每个作用域至多一条 `IsDefault=true`；设新默认自动清旧默认（同 ScopeType+ScopeId 内互斥）。
- 租户初始化时种一条 `ScopeType=0, IsDefault=true` 的兜底规则（如 `Z段-A段-R段-L段-C段`），保证开箱即可生成。

---

## 第3章 分段定义 Segments JSON 规范

`Space_CodeRule.Segments` 是一个 JSON 数组，**顺序即编码从左到右的段序**。

### 3.1 单段结构
```jsonc
{
  "key":   "rack",        // 段标识（唯一，见 3.2 取值源）
  "name":  "货架号",       // 显示名（预览/校验用）
  "source":"rack-seq",    // ★取值源类型（3.2）
  "width": 2,             // 补零宽度（0 = 不补，原样）
  "pad":   "0",           // 补齐字符，默认 '0'
  "start": 1,             // 序号类起始值（source 为 *-seq 时生效）
  "step":  1,             // 序号步长
  "sep":   "-",           // ★本段【后】的分隔符（最后一段 sep 通常为空）
  "upper": true,          // 字母是否大写（取 code 字段时）
  "fixedValue": "",       // source=fixed 时的固定文本
  "optional": false       // ★是否条件段（true=对应层级缺失时整段含其 sep 一并跳过，第5章）
}
```

### 3.2 取值源 source 一览
| source | 取值 | 说明 |
|---|---|---|
| `fixed` | `fixedValue` 固定文本 | 如仓库代号 `WH` |
| `site-code` | 所属 Site.Code | 站点编码段 |
| `floor-level` | 所属 Floor.Level | 楼层号（按 width 补零） |
| `zone-code` | 所属 Zone.Code | 库区编码（用户在 Zone 上维护的业务码） |
| `zone-seq` | 库区在层内的序号 | 按 Zone 排序生成 1,2,3…（start/step/width） |
| `aisle-code` | 所属 Aisle.Code | **条件段**：无巷道时跳过（第5章） |
| `aisle-seq` | 巷道在库区内序号 | **条件段** |
| `rack-code` | 所属 Rack.RackCode | 货架业务码（01 §5.2 批量生成给的"排-架"建议值，可在 02 改） |
| `rack-seq` | 货架在其父（Aisle 或 Zone）内序号 | 序号类 |
| `col` | 库位列索引 Col | 00 §4.6 三轴索引之一 |
| `level` | 库位层索引 Level | 垂直层 |
| `depth` | 库位深索引 Depth | 前后排 |

> **取值源分两类**：①**码源**（`*-code`/`fixed`/`floor-level`）取对象上已有的字段值；②**序号源**（`*-seq`/`col`/`level`/`depth`）按位置算序号，受 `start/step/width` 控制。序号源保证"位置→码"确定性可复算（重排幂等）。

### 3.3 拼接规则
```
code = seg1.render() + seg1.sep + seg2.render() + seg2.sep + ... + segN.render()
seg.render():
  raw = resolveSource(seg, locationContext)     // 取值源求值
  if seg.source 是序号源: raw = start + (index-1)*step   // 算序号
  s = String(raw); if seg.upper: s = s.toUpperCase()
  if seg.width>0: s = pad(s, seg.width, seg.pad)         // 左补
  return s
```
> 例：`[{key:zone,source:zone-code} - {key:rack,source:rack-seq,width:2} - {key:level,source:level,width:2} - {key:col,source:col,width:2}]` → 库区 `A`、第 3 货架、2 层、5 列 → `A-03-02-05`。

---

## 第4章 层级遍历生成算法

### 4.1 输入与上下文
对作用域内每个**草稿库位**（`Status=0 且 CodeOrigin=1`），构造其**层级上下文** `LocationContext`：
```ts
interface LocationContext {
  site: SiteVO; floor: FloorVO; zone: ZoneVO;
  aisle: AisleVO | null;       // ★可空 → 触发条件段（第5章）
  rack: RackVO;
  col: number; level: number; depth: number;
  // 序号源所需：该对象在其父集合内的序号（生成前一次性算好，见 4.3）
  zoneSeq?: number; aisleSeq?: number; rackSeq?: number;
}
```

### 4.2 主流程（服务端 CodeEngineService）
```
GenerateCodes(scope):
  1. 选规则集：作用域内每个 Zone 按 §2.2 优先级各自解析出生效规则（可能多套）
  2. 预检规则完备性（第6章静态预检）→ 不过则 E-SPACE-303 终止，不写库
  3. 拉草稿库位（Status=0 ∧ CodeOrigin=1），按层级排序，算各级序号（4.3）
  4. 事务内：
     a. 重排前置空（第7章）：把这批库位 LocationCode 全置 NULL
     b. 逐库位：选其 Zone 的规则 → 按 §3.3 拼 code → 暂存
     c. 生成后唯一校验（第6章）：批内去重 + 与库内既有非空码比对
        冲突 → 整事务回滚 E-SPACE-304，报告冲突清单
     d. 无冲突 → 批量 UPDATE 写回 LocationCode
  5. 返回：生成条数 / 命中规则数 / 样例前 N 条
```

### 4.3 序号的确定性
- 序号源（`zone-seq/aisle-seq/rack-seq/col/level/depth`）必须**确定性可复算**：序号 = 对象按固定排序键（如 `Zone.Code` 或 `Rack.{X,Y}` 几何顺序）的位次。
- 几何变了（02 挪了货架）→ 序号可能变 → 重排会改草稿码——**这正是草稿期允许重排的原因**（D4：发布前码不冻）。一经发布，码冻结，后续几何调整不再改码（00 §6.2 表 / D4）。
- `col/level/depth` 直接取库位索引（00 §4.6），不随货架位姿变。

### 4.4 只写草稿、不翻状态
- 生成只写 `LocationCode`（与必要时的 `CodeOrigin=1`），**不改 `Status`、不发布**。Status 0→1 的翻转是 04 的发布动作。
- 已发布库位（Status≥1）若混在作用域里：**跳过**，绝不改其码（试图改 → E-SPACE-004，00 §6 已定）。

---

## 第5章 Aisle 条件段与变长路径

### 5.1 为什么变长
00 决策：`Rack.ZoneId` 必填、`AisleId` 可空——**无巷道库区**（如平面堆垛区）的货架直接挂 Zone，没有 Aisle。于是编码路径**变长**：有巷道库区 `区-巷-架-层-位`，无巷道库区 `区-架-层-位`（跳过巷道段）。

### 5.2 条件段跳过规则
- `source` 为 `aisle-code`/`aisle-seq` 且 `optional:true` 的段，在 `LocationContext.aisle == null` 时**整段连同其 `sep` 一并跳过**（不留空段、不留多余分隔符）。
```
有巷道：A - 02 - 03 - 02 - 05      （区A-巷02-架03-层02-列05）
无巷道：A -      03 - 02 - 05  →  A-03-02-05   （巷段及其 sep 整体消失）
```
- 条件段**必须**标 `optional:true`；若忘标而 aisle 缺失 → E-SPACE-305（条件段未声明 optional，无法跳过）。

### 5.3 变长下的唯一性陷阱（与第6章联动）
> **要害**：跳过巷道段后，无巷道区的 `区-架-层-位` 与有巷道区的某个 `区-巷-架-层-位` **绝不能拼出相同串**。这要求 **rack-seq 在 Zone 范围内唯一**（而非仅在 Aisle 内），或保留足以区分的上层段。第6章把它做成**静态预检**：若规则含条件巷道段，则 `rack` 段的序号范围必须按 **Zone 级**编号（覆盖该 Zone 下所有巷道的货架），否则预检 E-SPACE-303 拦下。

---

## 第6章 租户全局唯一保证（核心约束）

> 库位编码是发给 WMS 的 **join key**，`Location` 上有租户级过滤唯一索引 `UX_Space_Location_Tenant_Code`（00 §4.6）。**作用域规则（楼层/库区级）也必须产出租户全局唯一码**——这是 00 章遗留、03 必须落实的下游待办②。两道防线：

### 6.1 静态预检（生成前，不写库）
规则保存时 + 生成前各跑一次，**纯规则结构分析**：
| 检查 | 判定 | 失败 |
|---|---|---|
| 含足够上层段 | 分段是否包含能区分到 Zone 的段（`zone-code`/`zone-seq`，或 `site+floor` 组合）。库区级规则尤其要含 zone 标识 | E-SPACE-303 规则不足以全局唯一 |
| 条件段下 rack 编号粒度 | 若含 optional 巷道段，rack-seq 必须 Zone 级编号（第5.3） | E-SPACE-303 |
| 段完备 | 至少含到库位粒度（`col/level/depth` 或能定位单格的组合） | E-SPACE-306 规则未到库位粒度 |
| 多规则一致性 | 一次生成命中多套库区规则时，各规则产出的码空间不重叠（靠各自含 zone 段保证） | E-SPACE-303 |

> **设计原则**：宁可生成前静态拦下（给出"规则缺 zone 段"的明确指引），也不要等生成后撞唯一索引才报错。静态预检让"可配置"不等于"可配出坏规则"。

### 6.2 生成后唯一校验（写库前，事务内）
即使静态预检过，仍在事务内做**值级**兜底：
```
candidates = 本批生成的所有 (locationId, code)
1. 批内自检：code 去重，重复 → E-SPACE-304（附冲突库位对）
2. 库内比对：SELECT 已存在的非空 LocationCode（同 TenantId，排除本批 id）
              与 candidates 交集非空 → E-SPACE-304
3. 全过 → 批量写；任一冲突 → 整事务回滚，零写入
```
- 依赖 00 的过滤唯一索引做最后一道 DB 兜底（并发下两请求同时生成时，后写者撞 `UX_..._Code` → E-SPACE-009/304）。

---

## 第7章 草稿批量重排（避开唯一索引中途违约）

### 7.1 为什么需要"先置 NULL"
草稿期重排会**交换编码**（如插入一排货架后整体重编，A 的码给了 B）。SQL Server 唯一索引**无延迟校验**：UPDATE 过程中若先把 B 改成 A 的现值，瞬时两行同码即违约。00 §4.6 已为此把编码设计成**可空 + 过滤唯一索引**（`WHERE LocationCode IS NOT NULL`）。

### 7.2 两阶段重排
```
事务内：
  阶段1  UPDATE Space_Location SET LocationCode = NULL
         WHERE TenantId=@t AND Status=0 AND CodeOrigin=1 AND <作用域>
         —— 全置空，过滤唯一索引此刻不约束 NULL，无中途违约
  阶段2  按第4章算新码，批量 UPDATE 赋值（赋值前已过第6章唯一校验）
  提交
```
- 重排**只动草稿引擎码**（`Status=0 ∧ CodeOrigin=1`）：已发布码冻结、采纳码外部既有，二者都不在 WHERE 内，天然豁免。
- 失败回滚：阶段2 任一冲突 → 整事务回滚，库位码回到重排前（要么全空、要么旧值，取决于回滚点；实务上整事务回滚即恢复原值）。

### 7.3 重排 vs 增量生成
| 场景 | 动作 |
|---|---|
| 首次生成（全空码草稿） | 直接第4章生成（阶段1 的 NULL 化是 no-op） |
| 加了货架后局部补码 | 可选"仅空码"模式：只对 `LocationCode IS NULL` 的草稿生成，不动已有草稿码（不重排，省扰动） |
| 规则改了/要整体重编 | 全量重排（阶段1+2） |

> 提供**两种模式**：`fill-empty`（只补空码，稳定不扰动既有草稿码）与 `rebuild`（全量重排）。默认 `fill-empty`；改规则后引导用户 `rebuild`。

---

## 第8章 实时预览（规则编辑器）

### 8.1 预览即所得
规则编辑器（`code-rule` 视图）改任一段，**即时**渲染：
- **结构示意**：`[区A]-[巷02]-[架03]-[层02]-[列05]` 各段彩色块 + 段名。
- **样例编码**：用选定 Floor 的**真实层级样本**（取该层前 N 个库位上下文）算出前 N 条真实编码；无数据时用合成样例。
- **变长示意**：同时展示"有巷道"与"无巷道"两条路径的成码（第5章），让用户直观看到条件段跳过效果。
- **预检红灯**：静态预检（第6.1）实时跑，缺 zone 段/未到库位粒度即在编辑器顶部亮红条提示，未过不让保存规则。

### 8.2 预览接口
```
POST /api/space/code-rule/preview  { segments, scopeType, scopeId?, floorId? }
  → { structure:[...段], samples:["A-03-02-05", ...], variableLen:{withAisle, withoutAisle}, precheck:{ok, errors[]} }
```
- 预览**不写库**（纯计算）；用真实样本时只读取上下文、不改库位。

---

## 第9章 与 D7 采纳态、D4 冻结闸门的关系

### 9.1 D7 采纳态：03 跳过外部码
- 采纳库位：`CodeOrigin=2`、`Status=1`、码是外部既有冻结码（00 §7.1）。
- 03 的生成/重排 WHERE 恒含 `CodeOrigin=1 ∧ Status=0`，**永不命中采纳码**。
- 反向建模（01 §8/02）给采纳库位补几何、绑码——那是**绑既有码**不是生成码，不经 03 引擎。
- 唯一旁路：反向建模时若货架格口"有几何无码"（待绑列表里没有对应外部码），用户可选**让 03 给这个格口生成新码**（CodeOrigin=1，走第4章），与采纳码并存（仍受第6章全局唯一约束）。

### 9.2 D4 冻结闸门：03 提供发布前预检
- 冻结动作（Status 0→1）属 04。03 提供**发布前编码预检**当闸门入口：
```
GET /api/space/floor/{id}/code-precheck
  → { emptyCodeCount,        // 仍为空码的草稿库位数（必须为 0 才能发布）
      duplicateGroups,       // 同码冲突组（必须为空）
      precheckErrors,        // 第6.1 规则完备性问题
      unplacedDraftCount }   // 有码但 Placed=false 的草稿（异常态，提示）
```
- 04 发布前调它：`emptyCodeCount>0` 或有重复 → 阻断发布（E-SPACE-307），引导回 03 先补码/重排。
- **冻编码不冻几何**（D4）：发布后码冻结，但 02 仍可调几何（货架位姿变 → 00 §6.2 重算库位坐标，**码不变**）。03 此后对已发布库位**只读不写**。

---

## 第10章 API 接口设计

路由前缀 `/api/space`。

| 端点 | 方法 | 说明 |
|---|---|---|
| `/code-rule` | GET/POST/PUT/DELETE | 编码规则 CRUD（含 Segments JSON + Scope + IsDefault） |
| `/code-rule/preview` | POST | 实时预览（结构/样例/变长/预检，第8.2，不写库） |
| `/code-rule/{id}/set-default` | POST | 设为作用域默认（互斥清旧默认，§2.3） |
| `/floor/{id}/generate-codes` | POST | 按规则生成/重排库位编码 `{ mode: 'fill-empty'｜'rebuild', scope?:ZoneId }`（第4/7章，事务+唯一校验） |
| `/floor/{id}/code-precheck` | GET | 发布前编码预检（第9.2，供 04 当闸门） |
| `/location/{id}/gen-code` | POST | 单格口生成新码（反向建模旁路，§9.1，CodeOrigin=1） |

> `generate-codes` 是 03 的主入口：默认 `fill-empty`（只补空码、不扰动既有草稿码）；改规则后用 `rebuild`（两阶段全量重排）。两者都只作用 `Status=0 ∧ CodeOrigin=1`，事务内过第6章唯一校验才落库。

---

## 第11章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-SPACE-301 | Error | 未配置编码规则 | 作用域链上无任何可用规则（§2.2） |
| E-SPACE-302 | Error | 编码规则存在歧义，请指定默认规则 | 同作用域多条规则且无 IsDefault |
| E-SPACE-303 | Error | 规则不足以保证租户内唯一（缺库区/上层段） | 静态预检失败（第6.1） |
| E-SPACE-304 | Error | 生成的库位编码存在重复 | 生成后唯一校验冲突（第6.2），整事务回滚 |
| E-SPACE-305 | Error | 巷道段未声明为条件段(optional) | 含 aisle 段但库区无巷道且段未标 optional（§5.2） |
| E-SPACE-306 | Error | 规则未细到库位粒度 | 分段不含 col/level/depth 等可定位单格的段（第6.1） |
| E-SPACE-307 | Error | 存在空码或重复码，无法发布 | 04 发布前 code-precheck 不过（§9.2） |
| E-SPACE-004 | Error | 已发布库位编码不可修改 | 试图改 Status≥1 的 LocationCode（00 §6 复用） |
| E-SPACE-009 | Error | 数据已被他人修改，请刷新重试 | 生成/重排 RowVersion 或唯一索引并发冲突（00 章） |
| I-SPACE-301 | Info | 已生成/重排 N 条库位编码（命中 M 套规则） | 生成成功 |

---

## 第12章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 00 数据模型 | 读 `Space_CodeRule`(§4.8) + 库位层级链；写 `Location.{LocationCode,CodeOrigin}`；靠过滤唯一索引 + 状态机(§7) + RowVersion |
| ← 01 编辑器框架 | 消费 01 产的空码草稿库位（CodeOrigin=1）+ 层级几何；RackCode 作 rack-code 取值源 |
| ← 02 受控自由布局 | 02 调正几何后再生成/重排，序号源(rack-seq 等)按规整后的几何顺序算 |
| → 04 发布契约 | 提供 code-precheck 当冻结闸门入口；发布(Status0→1)后码冻结，03 对其只读；CodeOrigin 供 04 对账 |
| → 05/06 渲染定位 | LocationCode 是 06"按编码定位"的 key |
| → WMS（经 04） | LocationCode = 发给 WMS 的 join key，全局唯一是 WMS 关联库位的前提 |
| → PUB 权限 | 规则配置/生成/重排接 PUB 功能权限；规则与库位查询接数据权限；规则按 TenantId 隔离 |

---

## 自检
- [ ] 编码规则作用域有哪三级？解析优先级怎么走？一次整层生成为什么可能命中多套规则？
- [ ] Segments 每段有哪些字段？取值源分哪两类、各自如何受 start/step/width 影响？
- [ ] Aisle 条件段怎么跳过（连 sep 一起）？变长路径下唯一性陷阱是什么、第6章怎么静态拦？
- [ ] "租户全局唯一"为什么是硬约束？静态预检与生成后校验各拦什么？为什么宁可生成前拦？
- [ ] 草稿批量重排为什么必须"先置 NULL 再赋值"？这跟 00 的可空编码+过滤唯一索引怎么配合？
- [ ] 03 只作用于哪种库位（Status/CodeOrigin）？为什么永不碰采纳码和已发布码？
- [ ] D4 冻结发生在哪一章？03 给 04 提供什么闸门？"冻编码不冻几何"对 03 意味着什么？
- [ ] fill-empty 与 rebuild 两种模式各用在什么场景？

---

*实现：新建 `CP6.Core/Services/Space/CodeEngineService.cs`（规则解析 + 层级遍历生成 + 两阶段重排 + 静态/值级唯一校验）+ `cp6.web/src/views/space/code-rule/*`（分段编辑器 + 实时预览）。配套 xlsx（Segments 字段表 / 取值源一览 / 变长路径成码示例 / 唯一性预检矩阵 / 重排两阶段时序）见同名 `.xlsx`。*
