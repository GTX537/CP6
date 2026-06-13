# Space 00 · 数据模型与坐标系底座 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | SPACE-00 数据模型与坐标系底座 |
| 所属模块 | Space 空间数字底座 · Part 1（P1 地基） |
| 里程碑 | **P1 地基**（编辑器 / 编码引擎 / 渲染 / 发布 全部以此为真相源） |
| 技术栈 | Vue3 + TypeScript + Three.js / .NET8 Web API + EF Core / SQL Server |
| 命名空间 | `CP6.Entity/DomainModels/Space`、`CP6.Core/Services/Space`、`cp6.web/src/views/space` |
| 落地决策 | D1 2D 建模 / D3 参数化盒体 / D4 GUID 稳定主键·发布冻结 / **D7 采纳态反向建模** / Aisle 条件父级 / 每 Floor 局部坐标系·mm·Z-up·RotationZ 浮点 / Building 简化（Site→Floor 直连） |

> **题眼**：Space 是"空间几何/布局/库位编码"的**唯一真相源**。本章把这份真相落成 **9 张表 + 一套坐标系约定 + 几何 JSON 结构 + 稳定 GUID 身份**。编辑器（01/02）往这写、编码引擎（03）按这生成、渲染（05/06）按这画、发布（04）按这发——全模块从这一章长出来。**记住一句**：几何永远可动（货架可挪），但 `LocationId` 与库位编码一经发布即冻结，join key 永不漂移。
>
> **修订说明（2026-06-12）**：本版修复地基 schema 与 D7 采纳态/草稿编码流的冲突，并补齐外键删除策略、并发控制、FloorId 冗余、底图比例尺、锚点/旋转支点等可落码必需项。详见文末「修订记录」。

---

## 目录
- 第1章 功能概述与定位
- 第2章 坐标系与单位约定（每 Floor 局部 / mm / Z-up / RotationZ / 货架锚点·支点 / Building 简化）
- 第3章 数据模型总览与父子关系（Aisle 条件父级 / 放置维度 / 删除策略）
- 第4章 实体 DDL（9 表 C# + SQL）
- 第5章 几何 JSON 结构（Zone 多边形 / Aisle 面+中心线 / Rack 参数 / Location 索引+绝对坐标缓存）
- 第6章 绝对坐标缓存与重算逻辑
- 第7章 库位稳定身份与生命周期（GUID + Status + 放置态 + Version + CodeOrigin）
- 第8章 字段明细
- 第9章 API 接口设计（主数据骨架）
- 第10章 消息一览
- 第11章 集成与依赖
- 自检

---

## 第1章 功能概述与定位

**目的**：建立 Space 模块的空间几何主数据，作为四个下游的唯一真相源：
1. **编辑器（01/02）**：在 2D 俯视平面图上读写本章的 Site/Floor/Zone/Aisle/Rack/Location 几何；含 D7 采纳态"先摆货架→绑既有冻结码"。
2. **编码引擎（03）**：按层级（Site→Floor→Zone→[Aisle]→Rack→Location）遍历生成库位编码。
3. **渲染（05/06）**：把几何 + 模板参数程序化成 3D 盒体，InstancedMesh 渲染、拾取回库位编码。
4. **发布（04）**：把库位编码 + 层级路径 + 属性发布给 WMS（**不含几何**）。

**范围**：9 张表的数据模型 + 坐标系/单位/旋转/锚点约定 + 几何 JSON 结构 + 绝对坐标缓存重算 + 库位稳定身份与生命周期状态机（含放置维度）。
**不含**：编辑交互（01/02）、编码生成算法（03）、发布契约细节（04）、渲染（05/06）。本章只定**数据真相的形状与不变量**。

---

## 第2章 坐标系与单位约定

落地决策固化，全模块共享，违反即 join/渲染错位。

| 约定 | 取值 | 说明 |
|---|---|---|
| **坐标系归属** | **每 Floor 一个局部坐标系** | 原点取该楼层参考角 `(0,0)`；不同 Floor 各自独立，不共享世界系 |
| **单位** | **毫米 (mm)**，整数友好 | 坐标/尺寸均存 mm；避免浮点累积误差，`Space_Location` 绝对坐标用整数 mm |
| **轴向** | **Z 轴向上**（X/Y 为地面、Z 为高度） | 建筑/仓库惯例；`space-viewer` 内部适配 Three.js 的 Y-up（见 05 章），**数据模型不迁就渲染库** |
| **旋转** | 仅绕 Z 的偏航角 `RotationZ`（度，**浮点**） | 货架在 2D 平面图只做平面旋转；受控自由旋转任意角度，模板生成默认 0/90/180/270 |
| **货架锚点** | `Rack.X/Y` = **货架占地的原点角**（未旋转时的 min-X/min-Y 角） | 格子从该角沿 +localX（列方向）/+localY（深度方向）铺开；占地范围 `[0, Cols·CellW] × [0, DepthCount·CellD]` |
| **旋转支点** | **绕货架原点角 `(Rack.X, Rack.Y)`** 旋转 `RotationZ` | 与第6章公式一致（绕 rack 局部系原点旋转）。编辑器（02）若以"中心旋转"交互，须换算后回填角点 X/Y，保证三方一致 |
| **Rack `Z`** | v1 **恒 0**（落地） | 夹层走独立 Floor；字段保留以备 P3 |
| **层级简化** | **跳过 Building**：`Site → Floor` 直连 | v1 单建筑；多建筑园区留垂直扩展，届时 `Floor` 改挂 `Building`。多建筑客户须注意 Floor 编号不撞号 |

> **为什么每 Floor 一个局部系、而非全站世界系？** 楼层各自建模、各自渲染、各自切换浏览（06 章楼层切换），局部系让每层原点稳定、坐标值小、编辑对齐直观；跨层不需要统一世界坐标（库存叠加按库位编码 join，不靠坐标）。

> **为什么旋转只存一个 `RotationZ`、不用四元数？** v1 货架只在地面平面旋转（D1：2D 建模 + D3：参数化盒体落地）。一个偏航角即可完整描述，存储/编辑/渲染都最简单；真要倾斜货架是 P3 之后的事，届时再扩四元数字段。

> **为什么必须钉死锚点与支点？** 货架位姿被三方消费：编辑器（02）拖拽落点、渲染（05）建盒体、坐标公式（第6章）算库位绝对坐标。三方若对"X/Y 是角还是中心""绕角还是绕中心转"理解不一，库位就会整体偏半个货架。本章定为**角点锚 + 绕角点转**，与第6章公式严格一致。

---

## 第3章 数据模型总览与父子关系

```
Space_Site         站点/仓库      TenantId, SiteCode, SiteName, 地理信息
  └─ Space_Floor   楼层           SiteId, Level, FloorCode, Height, 底图+比例尺(可选)
       └─ Space_Zone   库区        FloorId, ZoneCode, ZoneType, Polygon(多边形)
            ├─ Space_Aisle  巷道(可选)  ZoneId, AisleCode, Polygon(面)+Centerline(中心线)
            └─ Space_Rack   货架        ZoneId(必填), AisleId(可选), FloorId(冗余), TemplateId, X/Y/Z+RotationZ, 列/层/深
                 └─ Space_Location  库位  RackId(可空·见放置维度), FloorId(冗余·可空), LocationCode, 列/层/深索引, 绝对坐标缓存, Status, Placed, CodeOrigin, Version  ← join key

Space_Template     模板（货架/库区参数来源）   TenantId, TemplateType, 参数 JSON
Space_CodeRule     编码规则（本章定表壳，语义见 03）  TenantId, Scope(Floor/Zone), 分段定义 JSON
Space_Marker       打点/标注（受控自由布局标注）  FloorId, X/Y, MarkerType, Text
```

**父子关系（关键不变量）：**

| 子 | 父 | 必填性 | 说明 |
|---|---|---|---|
| Floor | Site | 必填 | Building 简化，Floor 直挂 Site |
| Zone | Floor | 必填 | 一层多区 |
| Aisle | Zone | —（Aisle 自身可有可无） | 巷道是 Zone 下的可选结构 |
| **Rack.ZoneId** | Zone | **必填** | **Zone 恒为 Rack 父级** |
| **Rack.AisleId** | Aisle | **可选** | **有巷道才挂**；无巷道库区（收货区/平铺区）此字段为空 |
| **Location.RackId** | Rack | **可空（见放置维度）** | 已放置库位必有货架；**采纳态未放置库位 RackId 为空** |

> **Aisle 是条件父级（贯穿 03/04）**：发布层级路径 = `Site / Floor / Zone / [Aisle] / Rack / Location`。**有巷道 → Aisle 段出现；无巷道 → 路径跳过 Aisle 段、变短**。这要求：①`Rack.ZoneId` 必填、`AisleId` 可空（本章）；②编码引擎支持"可选段"（03 章）；③发布载荷路径为变长数组（04 章）。**绝不为无巷道库区硬造假巷道**。

### 3.1 放置维度（D7 采纳态的 schema 兑现）

库位有两个**正交**维度，不要混为一谈：

- **生命周期 `Status`**：0 草稿 / 1 已发布 / 2 停用（第7章）。
- **放置态 `Placed`**：库位是否已落到某货架几何上。`Placed=false ⇔ RackId 为空`，此时 `Col/Level/Depth/AbsX/Y/Z/Size*/FloorId` 全部为空（无几何）。**落库不变量（应用层强制）：`Placed == (RackId != null)`**——`Placed` 与 `RackId` 完全等价，二者必须同步，防"Placed=true 却 RackId=null"的脏态漂移。

D7 采纳流程：存量编码导入为 **`Status=1 已发布` + `Placed=false 未放置` + `CodeOrigin=2 采纳`**——有冻结码、无几何。编辑器（01/02）反向建模"先摆货架→把格口绑到既有冻结码"时，回填 `RackId/FloorId/索引/坐标/尺寸`，`Placed` 置 `true`，**编码与 `LocationId` 不变**。
> 因为未放置库位 `FloorId` 为空，整层场景查询（第9章 `/floor/{id}/scene` 按 FloorId 命中）**天然不会带出未放置库位**——它们只在"待绑定列表"里出现，不污染渲染。

### 3.2 删除与引用完整性策略

外键以**应用层校验为主、DB 索引兜底**；删除策略如下（违反即报错，不级联物理删，避免孤儿/误删）：

| 父→子 | 删父策略 | 说明 |
|---|---|---|
| Site→Floor | **Restrict** | 有楼层不能删站点 |
| Floor→Zone/Marker | **Restrict** | 有库区不能删楼层 |
| Zone→Aisle/Rack | **Restrict** | 有巷道/货架不能删库区 |
| Aisle→Rack(引用) | **置空**（SetNull AisleId） | 删巷道→其下货架 `AisleId` 置空（货架仍属 Zone） |
| Rack→Location | **Restrict**（E-SPACE-003） | 有库位不能删货架 |
| Template→Rack(引用) | **置空**（SetNull TemplateId） | 删模板不影响已生成货架几何 |
| Rack→Marker(引用) | **置空**（SetNull RefRackId） | 删货架→锚在其上的标注解除锚定，标注保留（仅 SetNull，不报错） |

> **逻辑删/启用约定**：Site/Zone/Rack 提供 `Enable`（停用而非物理删），保留历史与引用；物理删一律走上表 Restrict。**全表不做软删标记列**（用 `Enable` 表达停用），物理删仅在无子引用时允许。

> **租户隔离机制**：全表 `TenantId`，EF Core 用**全局查询过滤器**（`HasQueryFilter(e => e.TenantId == _ctx.TenantId)`）统一拦截（接入细节见 09 章）。所有唯一索引均含 `TenantId` 前缀。

---

## 第4章 实体 DDL（9 表）

所有表继承 `BaseEntity`（含 `Id`(GUID)/`TenantId`/`CreateTime`/`Creator`/`UpdateTime`/`Updater` 审计字段），全表按 `TenantId` 隔离。**`Space_Rack`、`Space_Location` 额外带 `RowVersion`(乐观并发戳)**——编辑器多人协作 + 货架改尺寸批量重算库位时防丢更新（EF `[Timestamp] byte[] RowVersion` / SQL `ROWVERSION`）。

### 4.1 Space_Site 站点/仓库
```csharp
[Table("Space_Site")]
public class Space_Site : BaseEntity
{
    public string  SiteCode { get; set; } = "";   // 站点编码，租户内唯一
    public string  SiteName { get; set; } = "";
    public string? Address  { get; set; }          // 地址
    public double? Lng      { get; set; }          // 经度（地图定位，可选）
    public double? Lat      { get; set; }          // 纬度
    public bool    Enable   { get; set; } = true;
}
```
```sql
CREATE TABLE Space_Site (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
    SiteCode NVARCHAR(50) NOT NULL, SiteName NVARCHAR(100) NOT NULL,
    Address NVARCHAR(200) NULL, Lng FLOAT NULL, Lat FLOAT NULL, Enable BIT NOT NULL DEFAULT 1,
    CreateTime DATETIME2 NOT NULL, Creator NVARCHAR(50) NULL, UpdateTime DATETIME2 NULL, Updater NVARCHAR(50) NULL
);
CREATE UNIQUE INDEX UX_Space_Site_Tenant_Code ON Space_Site(TenantId, SiteCode);
```

### 4.2 Space_Floor 楼层
```csharp
[Table("Space_Floor")]
public class Space_Floor : BaseEntity
{
    public Guid    SiteId    { get; set; }            // → Space_Site
    public int     Level     { get; set; }            // 层号（1,2,...；地下用负数）
    public string  FloorCode { get; set; } = "";      // 楼层编码，站点内唯一
    public string  FloorName { get; set; } = "";
    public int     Height    { get; set; }            // 层高 mm（渲染层叠用）
    public string? UnderlayImage   { get; set; }      // 底图 URL（可选，描图用，非 CAD 导入）
    public double? UnderlayScale   { get; set; }      // ★底图比例尺：mm / 像素（描图对齐必需）
    public int     UnderlayOffsetX { get; set; }      // ★底图原点相对 floor 原点偏移 mm
    public int     UnderlayOffsetY { get; set; }
    public int     OriginX   { get; set; }            // 局部坐标系原点（通常 0）
    public int     OriginY   { get; set; }
}
```
```sql
CREATE TABLE Space_Floor (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
    SiteId UNIQUEIDENTIFIER NOT NULL, Level INT NOT NULL, FloorCode NVARCHAR(50) NOT NULL,
    FloorName NVARCHAR(100) NOT NULL, Height INT NOT NULL DEFAULT 6000,
    UnderlayImage NVARCHAR(500) NULL, UnderlayScale FLOAT NULL,
    UnderlayOffsetX INT NOT NULL DEFAULT 0, UnderlayOffsetY INT NOT NULL DEFAULT 0,
    OriginX INT NOT NULL DEFAULT 0, OriginY INT NOT NULL DEFAULT 0,
    CreateTime DATETIME2 NOT NULL, Creator NVARCHAR(50) NULL, UpdateTime DATETIME2 NULL, Updater NVARCHAR(50) NULL
);
CREATE UNIQUE INDEX UX_Space_Floor_Site_Code ON Space_Floor(TenantId, SiteId, FloorCode);
CREATE INDEX IX_Space_Floor_Site ON Space_Floor(TenantId, SiteId);
```
> **底图比例尺为什么必需？** 描图建模要把光栅平面图当 1:1 参照。只有 URL 时，编辑器不知道"图上 1 像素 = 现实多少 mm"、也不知道图该贴在 floor 哪个位置。`UnderlayScale`(mm/px) + `UnderlayOffsetX/Y` 让底图能按真实尺寸对齐到 floor 局部系，描图坐标才准。

### 4.3 Space_Zone 库区
```csharp
[Table("Space_Zone")]
public class Space_Zone : BaseEntity
{
    public Guid    FloorId  { get; set; }             // → Space_Floor
    public string  ZoneCode { get; set; } = "";       // 库区编码，楼层内唯一
    public string  ZoneName { get; set; } = "";
    public int     ZoneType { get; set; }             // 1存储 2收货 3发货 4分拣 5通道
    public string  Polygon  { get; set; } = "[]";     // 多边形顶点 JSON（floor 平面 mm），见第5章
    public string? Color    { get; set; }             // 库区底色（编辑/渲染区分用）
    public bool    Enable   { get; set; } = true;     // 停用而非物理删
}
```
```sql
CREATE TABLE Space_Zone (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
    FloorId UNIQUEIDENTIFIER NOT NULL, ZoneCode NVARCHAR(50) NOT NULL, ZoneName NVARCHAR(100) NOT NULL,
    ZoneType INT NOT NULL DEFAULT 1, Polygon NVARCHAR(MAX) NOT NULL DEFAULT '[]', Color NVARCHAR(20) NULL,
    Enable BIT NOT NULL DEFAULT 1,
    CreateTime DATETIME2 NOT NULL, Creator NVARCHAR(50) NULL, UpdateTime DATETIME2 NULL, Updater NVARCHAR(50) NULL
);
CREATE UNIQUE INDEX UX_Space_Zone_Floor_Code ON Space_Zone(TenantId, FloorId, ZoneCode);
CREATE INDEX IX_Space_Zone_Floor ON Space_Zone(TenantId, FloorId);
```

### 4.4 Space_Aisle 巷道（可选）
```csharp
[Table("Space_Aisle")]
public class Space_Aisle : BaseEntity
{
    public Guid    ZoneId     { get; set; }           // → Space_Zone
    public string  AisleCode  { get; set; } = "";     // 巷道编码，库区内唯一
    public string  Polygon    { get; set; } = "[]";   // 巷道地面多边形 JSON
    public string  Centerline { get; set; } = "[]";   // 中心线折线/路径节点 JSON（08 拣货路径消费）
}
```
```sql
CREATE TABLE Space_Aisle (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
    ZoneId UNIQUEIDENTIFIER NOT NULL, AisleCode NVARCHAR(50) NOT NULL,
    Polygon NVARCHAR(MAX) NOT NULL DEFAULT '[]', Centerline NVARCHAR(MAX) NOT NULL DEFAULT '[]',
    CreateTime DATETIME2 NOT NULL, Creator NVARCHAR(50) NULL, UpdateTime DATETIME2 NULL, Updater NVARCHAR(50) NULL
);
CREATE UNIQUE INDEX UX_Space_Aisle_Zone_Code ON Space_Aisle(TenantId, ZoneId, AisleCode);
CREATE INDEX IX_Space_Aisle_Zone ON Space_Aisle(TenantId, ZoneId);
```

### 4.5 Space_Rack 货架
```csharp
[Table("Space_Rack")]
public class Space_Rack : BaseEntity
{
    public Guid    ZoneId     { get; set; }           // ★必填 → Space_Zone
    public Guid?   AisleId    { get; set; }           // ★可选 → Space_Aisle（有巷道才挂）
    public Guid    FloorId    { get; set; }           // ★冗余 → Space_Floor（= Zone.FloorId，建时回填，加速场景/叠加查询）
    public Guid?   TemplateId { get; set; }           // → Space_Template（模板化生成来源）
    public string  RackCode   { get; set; } = "";     // 货架编码（编码引擎用作架号段）
    public int     X          { get; set; }           // 锚点角 局部坐标 mm（第2章：角点锚）
    public int     Y          { get; set; }
    public int     Z          { get; set; }           // v1 恒 0（落地）
    public double  RotationZ  { get; set; }           // 偏航角（度，浮点；绕锚点角旋转）
    public int     Cols       { get; set; }           // 列数（列=沿货架长度方向）
    public int     Levels     { get; set; }           // 层数（垂直）
    public int     DepthCount { get; set; }           // 深度方向格数（前后排，常 1）—— 原 Depth 改名，避免与 Location.Depth 索引混淆
    public int     CellW      { get; set; }           // 单格宽 mm
    public int     CellH      { get; set; }           // 单格高 mm
    public int     CellD      { get; set; }           // 单格深 mm
    public bool    Enable     { get; set; } = true;
    public byte[]? RowVersion { get; set; }           // 乐观并发戳
}
```
```sql
CREATE TABLE Space_Rack (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
    ZoneId UNIQUEIDENTIFIER NOT NULL, AisleId UNIQUEIDENTIFIER NULL, FloorId UNIQUEIDENTIFIER NOT NULL,
    TemplateId UNIQUEIDENTIFIER NULL,
    RackCode NVARCHAR(50) NOT NULL, X INT NOT NULL, Y INT NOT NULL, Z INT NOT NULL DEFAULT 0,
    RotationZ FLOAT NOT NULL DEFAULT 0,
    Cols INT NOT NULL, Levels INT NOT NULL, DepthCount INT NOT NULL DEFAULT 1,
    CellW INT NOT NULL, CellH INT NOT NULL, CellD INT NOT NULL, Enable BIT NOT NULL DEFAULT 1,
    RowVersion ROWVERSION,
    CreateTime DATETIME2 NOT NULL, Creator NVARCHAR(50) NULL, UpdateTime DATETIME2 NULL, Updater NVARCHAR(50) NULL
);
CREATE UNIQUE INDEX UX_Space_Rack_Zone_Code ON Space_Rack(TenantId, ZoneId, RackCode);
CREATE INDEX IX_Space_Rack_Zone  ON Space_Rack(TenantId, ZoneId);
CREATE INDEX IX_Space_Rack_Aisle ON Space_Rack(TenantId, AisleId);
CREATE INDEX IX_Space_Rack_Floor ON Space_Rack(TenantId, FloorId);   -- 整层取架
-- 落库不变量（应用层校验）：Cols≥1, Levels≥1, DepthCount≥1, CellW/H/D>0
```

### 4.6 Space_Location 库位（join key 载体）
```csharp
[Table("Space_Location")]
public class Space_Location : BaseEntity      // Id(GUID) = LocationId 稳定主键（D4）
{
    public Guid?   RackId       { get; set; }         // ★可空：未放置（采纳态 D7）时为空，绑定后回填
    public Guid?   FloorId      { get; set; }         // ★冗余·可空：= Rack.FloorId，加速整层场景/叠加；未放置为空
    public string? LocationCode { get; set; }         // ★库位编码（引擎生成 / 采纳导入）= join key；草稿首生成前可空
    public int     CodeOrigin   { get; set; }         // ★1 引擎生成 / 2 采纳导入（04 对账依据）
    public int?    Col          { get; set; }         // 列索引（1..Cols）；未放置为空
    public int?    Level        { get; set; }         // 层索引（1..Levels）
    public int?    Depth        { get; set; }         // 深索引（1..DepthCount）
    public int?    AbsX         { get; set; }         // ★绝对坐标缓存 mm（随几何变更重算，第6章）；未放置为空
    public int?    AbsY         { get; set; }
    public int?    AbsZ         { get; set; }
    public int?    SizeW        { get; set; }          // 库位尺寸 mm（默认取货架单格，可覆写）；未放置为空
    public int?    SizeH        { get; set; }
    public int?    SizeD        { get; set; }
    public int?    LoadLimit    { get; set; }          // 承重 kg（可选，建模才填）
    public int?    Capacity     { get; set; }          // 容量（可选；07 库容率用）
    public int?    CapacityUom  { get; set; }          // ★容量单位：1托盘 2箱 3件 4体积L（库容率口径）
    public bool    Placed       { get; set; }          // ★是否已放置（= RackId 非空）；放置维度，正交于 Status
    public int     Status       { get; set; }          // 0草稿 1已发布 2停用（第7章）
    public long    Version      { get; set; }          // 发布版本号，按 LocationId 递增（04 章）
    public byte[]? RowVersion   { get; set; }          // 乐观并发戳
}
```
```sql
CREATE TABLE Space_Location (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
    RackId UNIQUEIDENTIFIER NULL, FloorId UNIQUEIDENTIFIER NULL,
    LocationCode NVARCHAR(100) NULL, CodeOrigin INT NOT NULL DEFAULT 1,
    Col INT NULL, Level INT NULL, Depth INT NULL,
    AbsX INT NULL, AbsY INT NULL, AbsZ INT NULL,
    SizeW INT NULL, SizeH INT NULL, SizeD INT NULL,
    LoadLimit INT NULL, Capacity INT NULL, CapacityUom INT NULL,
    Placed BIT NOT NULL DEFAULT 0, Status INT NOT NULL DEFAULT 0, Version BIGINT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION,
    CreateTime DATETIME2 NOT NULL, Creator NVARCHAR(50) NULL, UpdateTime DATETIME2 NULL, Updater NVARCHAR(50) NULL
);
-- ★编码可空 + 过滤唯一索引：草稿期"先建库位、后生成码"阶段 code 为 NULL 不互撞；非空码租户内唯一
CREATE UNIQUE INDEX UX_Space_Location_Tenant_Code ON Space_Location(TenantId, LocationCode) WHERE LocationCode IS NOT NULL;
CREATE INDEX IX_Space_Location_Rack   ON Space_Location(TenantId, RackId);
CREATE INDEX IX_Space_Location_Floor  ON Space_Location(TenantId, FloorId);   -- 整层场景/叠加 join 快路径
CREATE INDEX IX_Space_Location_Status ON Space_Location(TenantId, Status);
```
> **编码为什么可空 + 过滤唯一索引？** 解决两个草稿期真实问题：①模板生成一架库位、编码引擎（03）尚未跑时，库位 code 为 `NULL`，多条不会互撞（非空才唯一）；②03 草稿**批量重排**会交换编码（A↔B），过程态允许先置 `NULL` 再赋值，避开 SQL Server 唯一索引无延迟校验的中途违约。一经发布，code 必非空且终生冻结（第7章）。

> **RackId 为什么可空？** 兑现 D7：采纳态导入的库位"有冻结码、无几何"，此时 `RackId/FloorId/索引/坐标` 全空、`Placed=false`；反向建模绑定货架后回填，`LocationId` 与 `LocationCode` 不变。

### 4.7 Space_Template 模板
```csharp
[Table("Space_Template")]
public class Space_Template : BaseEntity
{
    public string  TemplateCode { get; set; } = "";
    public string  TemplateName { get; set; } = "";
    public int     TemplateType { get; set; }         // 1货架 2库区
    public string  Params       { get; set; } = "{}"; // 参数 JSON（列/层/深/间距/尺寸等），01 章详述
}
```
```sql
CREATE TABLE Space_Template (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
    TemplateCode NVARCHAR(50) NOT NULL, TemplateName NVARCHAR(100) NOT NULL,
    TemplateType INT NOT NULL DEFAULT 1, Params NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    CreateTime DATETIME2 NOT NULL, Creator NVARCHAR(50) NULL, UpdateTime DATETIME2 NULL, Updater NVARCHAR(50) NULL
);
CREATE UNIQUE INDEX UX_Space_Template_Tenant_Code ON Space_Template(TenantId, TemplateCode);
```

### 4.8 Space_CodeRule 编码规则（本章定表壳，语义见 03）
```csharp
[Table("Space_CodeRule")]
public class Space_CodeRule : BaseEntity
{
    public string  RuleName     { get; set; } = "";
    public int     ScopeType    { get; set; }         // ★0 租户默认 / 1 楼层 / 2 库区（粒度比"仅楼层"更现实，语义见 03）
    public Guid?   ScopeId      { get; set; }         // 作用域对象 Id（ScopeType=1→FloorId，=2→ZoneId，=0→null）
    public string  Segments     { get; set; } = "[]"; // 分段定义 JSON（名称/位数/分隔符/起始/步长/取值源），03 章详述
    public bool    IsDefault    { get; set; }
}
```
```sql
CREATE TABLE Space_CodeRule (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
    RuleName NVARCHAR(100) NOT NULL, ScopeType INT NOT NULL DEFAULT 0, ScopeId UNIQUEIDENTIFIER NULL,
    Segments NVARCHAR(MAX) NOT NULL DEFAULT '[]', IsDefault BIT NOT NULL DEFAULT 0,
    CreateTime DATETIME2 NOT NULL, Creator NVARCHAR(50) NULL, UpdateTime DATETIME2 NULL, Updater NVARCHAR(50) NULL
);
CREATE INDEX IX_Space_CodeRule_Scope ON Space_CodeRule(TenantId, ScopeType, ScopeId);
```

### 4.9 Space_Marker 打点/标注
```csharp
[Table("Space_Marker")]
public class Space_Marker : BaseEntity
{
    public Guid    FloorId    { get; set; }           // → Space_Floor
    public int     X          { get; set; }           // 局部坐标 mm
    public int     Y          { get; set; }
    public int     Z          { get; set; }           // 高度 mm（默认 0；预留贴墙/悬挂标注）
    public int     MarkerType { get; set; }           // 1文字 2图标 3区域提示
    public string  Text       { get; set; } = "";
    public Guid?   RefRackId  { get; set; }           // 可选：标注锚到某货架（随架移动，02 章用）
}
```
```sql
CREATE TABLE Space_Marker (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
    FloorId UNIQUEIDENTIFIER NOT NULL, X INT NOT NULL, Y INT NOT NULL, Z INT NOT NULL DEFAULT 0,
    MarkerType INT NOT NULL DEFAULT 1, Text NVARCHAR(200) NOT NULL DEFAULT '', RefRackId UNIQUEIDENTIFIER NULL,
    CreateTime DATETIME2 NOT NULL, Creator NVARCHAR(50) NULL, UpdateTime DATETIME2 NULL, Updater NVARCHAR(50) NULL
);
CREATE INDEX IX_Space_Marker_Floor ON Space_Marker(TenantId, FloorId);
```
> Marker v1 仍偏轻量；图标库/样式/旋转等富标注留 02 章按需扩。`RefRackId` 让"钉在某货架上的标注"能随架移动。

---

## 第5章 几何 JSON 结构

几何用 JSON 列存（SQL Server `NVARCHAR(MAX)`），坐标单位 mm、floor 局部系。

### 5.1 Zone / Aisle 多边形（顶点数组）
```jsonc
// Space_Zone.Polygon / Space_Aisle.Polygon —— 闭合多边形，顺时针，首尾不重复
[ [0,0], [12000,0], [12000,8000], [0,8000] ]   // 单位 mm，[x,y]
```

### 5.2 Aisle 中心线（路径节点）
```jsonc
// Space_Aisle.Centerline —— 折线节点序列（08 章拣货路径沿此走）
[ [600,0], [600,8000] ]    // 一条贯穿巷道的中心线
```

### 5.3 Rack 不存几何顶点，存参数（参数化盒体，D3）
- 货架几何 = 由 `X/Y/Z + RotationZ + Cols/Levels/DepthCount + CellW/CellH/CellD` **程序化推导**（渲染在 05 章，不落顶点 JSON）。
- 这是 D3"纯参数化盒体"的体现：几何由参数生成、零美术素材、InstancedMesh 友好。

### 5.4 Location 不存几何，存索引 + 绝对坐标缓存
- 库位几何位置 = 由所属 Rack 的位姿 + `(Col,Level,Depth)` 索引推导（第6章公式）。
- `AbsX/AbsY/AbsZ` 是**推导结果的缓存**，供拾取（06）与库存叠加 join（07）零计算命中，**随几何变更重算**（第6章）。
- **未放置库位（D7 采纳态）无 Rack，几何字段全为 `NULL`**——它不参与渲染、不进整层场景，直到绑定货架。

> **为什么 Rack/Location 不落顶点、Zone/Aisle 落？** Zone/Aisle 是**自由勾画的多边形**（编辑器里手绘），没有参数公式，只能存顶点。Rack/Location 是**规整阵列**，由模板参数完全决定，存参数比存一堆顶点省、且改模板自动重算——参数化与自由几何各取所需。

---

## 第6章 绝对坐标缓存与重算逻辑

`Space_Location.AbsX/AbsY/AbsZ` = 库位中心在 floor 局部系的绝对坐标，**缓存值**（未放置时为 `NULL`）。

### 6.1 推导公式（货架局部 → floor 局部）
锚点与支点见第2章：`Rack.X/Y` 为货架**原点角**，旋转**绕该角**。库位在货架自身坐标系的偏移从原点角量起：
```
// 库位在货架自身坐标系的偏移（原点角在 X/Y/Z，未旋转时；索引 1..N）
localX = (Col   - 0.5) * CellW            // 沿货架长度方向（列）
localZ = (Level - 0.5) * CellH            // 高度方向（层）→ 落到 floor 的 Z
localY = (Depth - 0.5) * CellD            // 深度方向

// 绕原点角 Z 旋转 RotationZ（度→弧度 θ），再平移到货架锚点
AbsX = Rack.X + (localX*cosθ - localY*sinθ)
AbsY = Rack.Y + (localX*sinθ + localY*cosθ)
AbsZ = Rack.Z + localZ                     // v1 Rack.Z=0
```

### 6.2 触发重算的场景（几何可动、编码不变，D4）
```csharp
// CP6.Core/Services/Space/LocationGeometryService.cs
// 货架位姿/尺寸变更后，重算其下全部已放置库位绝对坐标缓存；LocationId 与 LocationCode 不变
public async Task RecalcRackLocationsAsync(Guid rackId)
{
    var rack = await _db.Space_Racks.FindAsync(rackId);
    var locs = await _db.Space_Locations
                        .Where(l => l.RackId == rackId && l.Placed)
                        .ToListAsync();
    foreach (var l in locs)
        (l.AbsX, l.AbsY, l.AbsZ) = ComputeAbs(rack, l.Col!.Value, l.Level!.Value, l.Depth!.Value); // 仅改坐标缓存
    await _db.SaveChangesAsync();   // RowVersion 兜底并发
    // ★注意：不触发 LocationPublished（载荷不含几何，见 04 章）——几何可动而 join key 不漂移
}
```

| 几何/放置变更 | 重算范围 | 是否发布 WMS |
|---|---|---|
| 货架移动 / 旋转 / 改尺寸 | 该货架下全部库位 AbsX/Y/Z | **否**（纯几何，载荷无几何） |
| 货架增删格子（改 Cols/Levels/DepthCount）| 新增库位补码 / 删除库位停用 | **是**（库位增删/停用，04 章） |
| **采纳态绑定货架（D7：未放置→已放置）** | 回填该库位 RackId/FloorId/索引/坐标/尺寸，`Placed=true` | **否**（编码早已是已发布·冻结，几何回填不改 join key） |
| 库区/巷道多边形调整 | 不影响库位坐标（库位锚在货架） | 否 |

> **这条是 D4 的技术兑现**：现实里货架挪位很常见，几何必须可动；而 WMS 那边压根没有几何（混合分权），所以纯几何编辑根本不需要同步——join key 自然永不漂移。采纳态绑定同理：码先于几何存在，绑几何只补缓存，不动契约。

---

## 第7章 库位稳定身份与生命周期

### 7.1 稳定身份（D4）
- `Space_Location.Id`(GUID) = **`LocationId` 稳定主键**，建库位（或采纳导入）时生成，**终生不变**。
- `LocationCode` 库位编码：草稿期可为 `NULL`、可由引擎反复重排（03 章）；**一经发布即非空且冻结**，之后只增不改。
- `CodeOrigin`：`1` 引擎生成 / `2` 采纳导入——采纳码是外部既有、04 章对账依据。
- 对外（WMS）契约 = `LocationId` + `LocationCode`，二者发布后都不变。

### 7.2 生命周期状态机（`Status`）与放置维度（`Placed`）

`Status`（生命周期）与 `Placed`（是否落到货架几何）**正交**：

```
            ┌─────────┐  发布(04章·过冻结闸门)   ┌──────────┐
   新建 ───▶│ 0 草稿   │ ───────────────────────▶│ 1 已发布  │
  (生成码)   │ Draft   │                          │Published │
            └─────────┘                          └────┬─────┘
                ▲  编码可重排/可空、可删                 │ 停用(D6:前置0库存校验)
                │  几何随意改                            ▼
  采纳导入 ─────┘（CodeOrigin=2，直接入"已发布·未放置"） ┌──────────┐
  Status=1, Placed=false ── 反向建模绑定货架 ──▶ Placed=true │ 2 停用    │
                                              └──────────┘
```

| Status | 编码 | 几何 | 放置 | 发布 | 说明 |
|---|---|---|---|---|---|
| 0 草稿 | 可空/可重排/可删 | 可改 | 通常已放置 | 未发布 | 建模自由区 |
| 1 已发布 | **非空·冻结** | **仍可改**（重算缓存） | 已放置 **或** 采纳待放置 | 已同步 WMS | join key 生效 |
| 2 停用 | 冻结 | 可改 | — | 发停用事件 | 须先经 `IWmsStockQuery` 校验 0 库存（D6/04 章） |

> **`Placed=false` 仅合法存在于"采纳态已发布·待绑定几何"**：有冻结码、无 Rack/坐标，不进整层场景渲染，只在编辑器"待绑定列表"出现。绑定后 `Placed=true`、几何回填。

> **`Version`**：按 `LocationId` 维度递增的发布版本号，每次发布/停用 +1，WMS 可逐库位检测陈旧更新（04 章批量 upsert 用）。本章只定义字段与"按 LocationId 递增"语义。

---

## 第8章 字段明细（关键表）

### 8.1 Space_Rack（货架·维护）
| 字段 | 中文名 | 控件 | 必填 | 说明 |
|---|---|---|---|---|
| zoneId | 所属库区 | 树选 | 是 | Zone 恒为父级 |
| aisleId | 所属巷道 | 下拉 | 否 | 有巷道才选；无巷道留空 |
| floorId | 所属楼层 | 系统 | 是 | = Zone.FloorId，建时回填（冗余加速） |
| templateId | 货架模板 | 下拉 | 否 | 模板化生成来源 |
| rackCode | 货架编码 | 文本 | 是 | 库区内唯一；编码引擎架号段 |
| x / y | 锚点角位置 | 数字(mm) | 是 | 角点锚；2D 拖拽自动填（02 章） |
| rotationZ | 偏航角 | 数字(度) | 否 | 默认 0；绕锚点角；模板 0/90/180/270 |
| cols/levels/depthCount | 列/层/深 | 数字 | 是 | 决定库位阵列 |
| cellW/cellH/cellD | 单格尺寸 | 数字(mm) | 是 | 默认取模板 |

### 8.2 Space_Location（库位·只读为主，几何派生）
| 字段 | 中文名 | 来源 | 说明 |
|---|---|---|---|
| locationCode | 库位编码 | 引擎生成/采纳导入 | join key；草稿可空，发布后非空冻结 |
| codeOrigin | 编码来源 | 生成/采纳 | 1 生成 / 2 采纳（对账用） |
| rackId / floorId | 货架/楼层 | 放置时回填 | 未放置为空 |
| placed | 是否放置 | 系统 | 正交于 status |
| col/level/depth | 列/层/深索引 | 放置时定 | 未放置为空 |
| absX/absY/absZ | 绝对坐标 | **缓存·重算** | 拾取/叠加 join 用；未放置为空 |
| capacity / capacityUom | 容量/单位 | 建模填 | 07 库容率；单位口径必带 |
| status | 状态 | 状态机 | 0草稿/1已发布/2停用 |
| version | 版本号 | 按 LocationId 递增 | 发布检测陈旧 |

> 库位字段控制：`locationCode` 在已发布/停用态**只读**；`absX/Y/Z` 全程系统维护、不可手编；几何通过编辑货架（8.1）或采纳绑定间接驱动。

---

## 第9章 API 接口设计（主数据骨架）

路由前缀 `/api/space`。本章只给主数据 CRUD 骨架；几何编辑细节在 01/02，发布在 04。

| 端点 | 方法 | 说明 |
|---|---|---|
| `/site` | GET/POST/PUT/DELETE | 站点 CRUD（删前校验无楼层） |
| `/floor?siteId=` | GET/POST/PUT/DELETE | 楼层 CRUD（含 UnderlayImage 上传 + 比例尺/偏移） |
| `/zone?floorId=` | GET/POST/PUT/DELETE | 库区 CRUD（含 Polygon） |
| `/aisle?zoneId=` | GET/POST/PUT/DELETE | 巷道 CRUD（可选） |
| `/rack?zoneId=` | GET/POST/PUT/DELETE | 货架 CRUD；改位姿/尺寸触发 `RecalcRackLocationsAsync`；删前校验无库位 |
| `/location?rackId=` | GET | 库位查询（生成在 03、发布在 04） |
| `/location/unplaced?floorId=` | GET | **采纳态待绑定库位列表**（Placed=false 且 Status=1，供反向建模，D7） |
| `/floor/{id}/scene` | GET | **整层场景快照**（Zone/Aisle/Rack/Location/Marker 几何聚合，供 05 渲染一次拉全）；**可选 `?zoneId=`/`?bbox=x,y,w,h` 分块过滤**（v1 可忽略，契约预留供 P3 大场景按需加载） |

> `/floor/{id}/scene` 是渲染入口的关键聚合接口：按 `Location.FloorId` 直取该层全部**已放置**库位（无多跳 join），返回几何 + 绝对坐标缓存，前端 `space-viewer` 据此建场景图（05 章）。按 TenantId + 数据权限（PUB）过滤；未放置库位天然不在其中。`?bbox=`/`?zoneId=` 为分块寻址预留（与 D2/D5"接口可分块、v1 先全量"一致）。

---

## 第10章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-SPACE-001 | Error | 站点/楼层/库区/货架编码已存在 | 各级 Code 作用域内重复 |
| E-SPACE-002 | Error | 货架必须归属库区 | Rack.ZoneId 为空 |
| E-SPACE-003 | Error | 该货架下存在库位，不能删除 | 删货架仍有库位 |
| E-SPACE-004 | Error | 已发布库位编码不可修改 | 改 Status≥1 的 LocationCode |
| E-SPACE-005 | Warn | 库位有库存，不能停用（请先清空） | 停用前置 0 库存校验失败（D6，04 章细化） |
| E-SPACE-006 | Error | 多边形顶点少于 3 个 | Zone/Aisle Polygon 非法 |
| E-SPACE-007 | Error | 上级存在子节点，不能删除 | Site/Floor/Zone 删除时有子引用（删除策略 3.2） |
| E-SPACE-008 | Error | 采纳编码已存在，不能重复导入 | 采纳导入的 LocationCode 与既有非空码冲突 |
| E-SPACE-009 | Error | 数据已被他人修改，请刷新重试 | RowVersion 乐观并发冲突 |

---

## 第11章 集成与依赖

| 关系 | 说明 |
|---|---|
| → 01/02 编辑器 | 读写本章 Zone/Aisle/Rack/Location/Marker 几何；含 D7 采纳态绑定（`/location/unplaced`） |
| → 03 编码引擎 | 按层级（含 Aisle 条件段）遍历生成 LocationCode，写回库位；草稿可空码 + 重排 |
| → 04 发布契约 | 取 LocationCode + 层级路径（变长，跳 Aisle）+ 属性发布；纯几何编辑/绑定不发布；CodeOrigin 供对账 |
| → 05/06 渲染 | `/floor/{id}/scene` 聚合（按 FloorId 快取）+ 绝对坐标缓存供 InstancedMesh/拾取 |
| → 07 叠加 | 库位编码 join WMS 库存；Capacity + CapacityUom 供库容率 |
| → PUB 权限 | 场景查询接 PUB 数据权限；全表 TenantId 隔离（EF 全局查询过滤器） |
| 多租户 | 全表 `TenantId`，按租户隔离（09 章接入） |

---

## 自检
- [ ] 为什么每 Floor 一个局部坐标系、单位用 mm、Z 轴向上？渲染 Y-up 谁来适配？
- [ ] 货架锚点是角还是中心？绕什么旋转？三方（编辑/渲染/公式）怎么保持一致？
- [ ] 旋转为什么只存一个 RotationZ 而非四元数？
- [ ] Aisle 为什么是条件父级？Rack.ZoneId 必填、AisleId 可空对编码/发布意味着什么？
- [ ] Zone/Aisle 为什么存顶点 JSON，而 Rack/Location 不存几何只存参数/索引？
- [ ] 货架挪位时，库位的什么变、什么不变？为什么不触发 LocationPublished？
- [ ] D7 采纳态"有码无几何"在 schema 里怎么表达？RackId/编码为什么可空？Placed 与 Status 怎么正交？
- [ ] 草稿"先建库位后生成码"和"批量重排"为什么需要可空编码 + 过滤唯一索引？
- [ ] 删 Site/Floor/Zone/Rack 各自的策略是什么？AisleId/TemplateId 为什么是置空而非级联？
- [ ] 库位三态（草稿/已发布/停用）各自编码/几何/发布的规则是什么？Version 按什么维度递增？CodeOrigin 干嘛用？
- [ ] FloorId 为什么冗余到 Rack/Location？对场景加载和库存叠加有什么用？
- [ ] 底图为什么必须带比例尺和偏移？RowVersion 解决什么问题？

---

## 修订记录（2026-06-12）

| # | 改动 | 对应评审项 | 落点 |
|---|---|---|---|
| 1 | `Location.RackId` 改可空 + 新增 `Placed` 放置维度（正交于 Status）；采纳态=已发布·未放置·无几何，绑定后回填 | 致命1（RackId vs D7） | 3.1 / 4.6 / 6.2 / 7.2 |
| 2 | `LocationCode` 改可空 + 过滤唯一索引 `WHERE LocationCode IS NOT NULL`；几何索引/坐标随之可空 | 致命2（草稿编码冲突/重排） | 4.6 |
| 3 | 第2章新增"货架锚点=原点角、旋转绕角点"约定，并与第6章公式对齐 | 致命3（锚点/支点未定义） | 第2章 / 6.1 |
| 4 | 新增"删除与引用完整性策略"表（Restrict/SetNull）+ Enable 逻辑停用约定 | 重要4（外键删除策略） | 3.2 / +E-SPACE-007 |
| 5 | `Rack`/`Location` 加 `RowVersion` 乐观并发戳 | 重要5（并发控制） | 4.5 / 4.6 / +E-SPACE-009 |
| 6 | `FloorId` 冗余到 Rack 与 Location，场景/叠加按 FloorId 直取 | 重要6（FloorId 冗余） | 4.5 / 4.6 / 9 / 11 |
| 7 | `Floor` 加 `UnderlayScale/OffsetX/OffsetY`，底图可按真实尺寸对齐描图 | 重要7（底图比例尺） | 4.2 |
| 8 | `Location` 加 `CodeOrigin`（生成/采纳）；状态机补采纳直入"已发布·未放置"入口 | 重要8（采纳来源/状态机） | 4.6 / 7.1 / 7.2 / +E-SPACE-008 |
| 9 | `Capacity` 加 `CapacityUom` 单位口径 | 中等9 | 4.6 / 8.2 |
| 10 | `/floor/{id}/scene` 预留 `?zoneId=/?bbox=` 分块参数；新增 `/location/unplaced` 待绑定接口 | 中等10 | 第9章 |
| 11 | Zone/Rack 补 `Enable` 逻辑停用，统一启用/软删约定 | 中等11 | 3.2 / 4.3 / 4.5 |
| 12 | `CodeRule` 作用域由"仅楼层"扩为 `ScopeType`(租户/楼层/库区)+`ScopeId` | 中等12 | 4.8 |
| 13 | `Rack.Depth` 改名 `DepthCount`，与 `Location.Depth` 索引解歧义 | 小（命名） | 4.5 / 5.3 / 6.2 / 8.1 |
| 14 | `Marker` 加 `Z` 与 `RefRackId`（标注可锚到货架）；注明富标注留 02 | 小（Marker 偏薄） | 4.9 |
| 15 | 注明 EF 全局查询过滤器做租户隔离；补落库不变量（Cols≥1 等） | 小（机制/不变量） | 3.2 / 4.5 |
| 16 | `Marker.RefRackId` 删货架时 SetNull（解除锚定、标注保留） | 合并评审·小 | 3.2 |
| 17 | 补落库不变量 `Placed == (RackId != null)`，防放置态漂移 | 合并评审·小 | 3.1 |

> 骨架与坐标系核心不变；本版只补地基缺口、修自洽性。下游 01–09 章引用本表字段时以本修订版为准。
>
> **下游待办（合并评审残留，写 03/04 时落实，不动 00）**：①〔04〕删巷道若其下有**已发布**库位 → Restrict 或触发 re-publish（避免发布路径元数据陈旧）；②〔03〕作用域编码规则（楼层/库区级）也**必须产出租户全局唯一码**（分段须含足够上层段），否则与 `Location.LocationCode` 租户级唯一索引冲突、生成时校验拦下。
