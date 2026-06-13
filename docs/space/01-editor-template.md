# Space 01 · 空间建模编辑器框架 + 模板化生成 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | SPACE-01 编辑器框架 + 模板化生成 |
| 所属模块 | Space 空间数字底座 · Part 1（P1） |
| 里程碑 | **P1**（建模主入口；编码引擎 03 / 渲染 05 / 发布 04 的上游） |
| 技术栈 | Vue3 + TypeScript + **Konva.js（2D 画布）** + Pinia / .NET8 Web API + EF Core |
| 命名空间 | `cp6.web/src/views/space/editor`、`cp6.web/src/space-editor`（Konva 封装）/ `CP6.Core/Services/Space` |
| 落地决策 | D1 2D 俯视建模 / D3 参数化盒体 / D7 采纳态反向建模入口 / 模板化生成为主·受控自由布局为辅 |
| 依赖 | [00 数据模型](./00-data-model.md)（读写 Site/Floor/Zone/Aisle/Rack/Location 几何） |

> **题眼**：本章是建模的**主入口与框架**——在 **2D 俯视平面图**上把仓库结构搭出来。核心生产力来自**模板化批量生成**：选一个货架/库区模板、刷一片区域，引擎按 00 章参数一次性建出**货架 + 库位阵列**（草稿态、暂无编码）。手工微调（拖拽/旋转/打点）属**受控自由布局**，在 [02 章](./02-free-layout.md) 详述；本章只负责"框架 + 批量生成 + 草稿保存 + 场景复制 + 采纳反向建模入口"。**记住一句**：01 造几何，03 给编码，04 发 WMS——本章产出的库位 `LocationCode` 一律为空，留给编码引擎。

---

## 目录
- 第1章 功能概述与定位（与 02 的边界）
- 第2章 编辑器整体架构（Konva 画布层 + Pinia 状态 + 与 00 的数据绑定）
- 第3章 2D 俯视画布（坐标映射 / 底图描图 / 网格 / 缩放平移 / 图层）
- 第4章 模板库与模板参数（Space_Template.Params JSON）
- 第5章 模板化批量生成（货架模板 / 库区模板 → 货架 + 库位阵列）
- 第6章 草稿态与保存（脏标记 / 整层保存 / 并发）
- 第7章 场景复制与 JSON 导入导出（多客户复制）
- 第8章 D7 采纳态反向建模入口（待绑定列表 → 绑既有冻结码）
- 第9章 API 接口设计
- 第10章 消息一览
- 第11章 集成与依赖
- 自检

---

## 第1章 功能概述与定位

**目的**：提供 Space 的空间建模编辑器框架，让用户在 2D 俯视平面图上把仓库结构（库区/巷道/货架/库位）搭出来，以**模板化批量生成**为主力手段。

**本章范围（01）：**
- 编辑器整体架构（前端 Konva 画布 + 状态管理 + 与 00 数据模型读写）。
- 2D 俯视画布基础设施：坐标映射、底图描图、网格、缩放/平移、图层。
- 模板库管理（货架/库区模板）+ 模板参数 JSON 规范。
- **模板化批量生成**：选模板 → 落区域 → 一次建出货架 + 库位阵列（草稿态、空码）。
- 草稿态与整层保存、场景复制 / JSON 导入导出、**D7 采纳态反向建模入口**。

**不含（划清边界）：**
| 能力 | 去哪章 |
|---|---|
| 拖拽/旋转/打点/框选/捕捉对齐/撤销重做/碰撞提示（受控自由布局**交互**） | [02 章](./02-free-layout.md) |
| 库位编码生成/重排/规则 | [03 章](./03-code-engine.md) |
| 发布给 WMS / 采纳导入的对账落库 | [04 章](./04-publish-contract.md) |
| 3D 渲染 | [05](./05-viewer-core.md)/[06](./06-camera-pick.md) |

> **01 与 02 的分工**：01 = "**生成**几何的框架与批量手段"（一次刷出一片）；02 = "**手工调整**几何的交互"（拖一个、转一个）。二者共用同一 Konva 画布与 Pinia 场景状态，但 01 管"从无到有成批建"、02 管"已有的精修"。

---

## 第2章 编辑器整体架构

### 2.1 前端分层
```
cp6.web/src/views/space/editor/
  FloorEditor.vue           编辑器页面外壳（工具栏 + 画布 + 属性面板 + 模板库侧栏）
  panels/                   属性面板（选中对象的字段编辑，复用 00 字段定义）
cp6.web/src/space-editor/   ← 与视图解耦的画布引擎封装（可独立测试）
  SceneStage.ts             Konva.Stage 封装：图层、缩放/平移、坐标映射
  layers/                   ZoneLayer / AisleLayer / RackLayer / MarkerLayer / UnderlayLayer / GridLayer
  generate/                 模板生成器（纯函数：模板参数 → 货架/库位草稿对象）
  io/                       场景导入导出（JSON 序列化/反序列化 + ID 重映射）
  store useEditorStore      Pinia：当前 Floor 场景对象图 + 脏标记 + 选中集
```

### 2.2 为什么用 Konva（2D 画布）而非 SVG / 直接 Three.js
- **Konva**：canvas 渲染、内建图形/事件/拖拽/变换器（Transformer），千级图元（一层几百货架）流畅，正合 2D 俯视建模（D1）。
- 不用 SVG：图元一多（库位级别）DOM 数量爆炸、卡顿。
- 不用 Three.js 做编辑：3D 拾取/对齐/深度判断重（D1 决策已排除直接 3D 操作）；3D 只读浏览在 05/06。

### 2.3 场景对象图（前端内存模型，镜像 00）
```ts
interface EditorScene {
  floor: FloorVO                 // 00 Space_Floor（含底图比例尺/偏移）
  zones: ZoneVO[]                // 00 Space_Zone（Polygon）
  aisles: AisleVO[]              // 00 Space_Aisle（Polygon + Centerline）
  racks: RackVO[]                // 00 Space_Rack（X/Y/RotationZ + 模板参数）
  locations: LocationVO[]        // 00 Space_Location（草稿态，LocationCode 多为 null）
  markers: MarkerVO[]            // 00 Space_Marker
}
```
> 库位数量大（万级），编辑器内存里**库位以"按货架懒展开"持有**：默认只持有货架，选中/需要时才展开其库位 VO，避免一次性建万级 Konva 节点（渲染细节见第3章图层与 05 章实例化）。

---

## 第3章 2D 俯视画布

### 3.1 坐标映射（floor 局部 mm ↔ 屏幕 px）
- 数据是 floor 局部系、单位 mm、Z-up（00 章）；2D 俯视画布只用 **X/Y 平面**（俯视，Z 不参与平面布局，仅在属性里显示层）。
- 映射：`screenX = (worldX - panX) * zoom`，`screenY = (worldY - panY) * zoom`。`zoom` = px/mm。
- **Y 轴方向**：世界系 Y 向"上"（北），屏幕 Y 向下；画布做一次 Y 翻转，保证"图上往上 = 世界 +Y"，与俯视直觉一致。

### 3.2 底图描图（消费 00 的 UnderlayScale/Offset）
```
底图贴图位置/尺寸：
  imgWorldX = Floor.UnderlayOffsetX
  imgWorldY = Floor.UnderlayOffsetY
  imgWorldW = imgPixelW * Floor.UnderlayScale   // mm/px → 真实 mm 宽
  imgWorldH = imgPixelH * Floor.UnderlayScale
→ 再经 3.1 映射到屏幕；底图作为最底图层（UnderlayLayer），半透明、不可选中、仅描图参照
```
> 底图是"描图参照"不是数据：用户照着平面图勾库区多边形、摆货架。00 章的 `UnderlayScale`(mm/px)+`Offset` 让光栅图按真实尺寸贴正，描出来的坐标才准。**标定流程**：上传底图 → 在图上量一段已知真实长度的线（如一跨柱距）→ 反算 `UnderlayScale`。

### 3.3 网格与捕捉基线
- 网格层 GridLayer：按可配步长（默认 100mm/1000mm 两级）画参考线，随 zoom 自适应疏密。
- 捕捉（snap）的**交互**在 02 章；本章只提供网格基线与 `snapStep` 配置入口。

### 3.4 图层与可见性
| 图层 | 内容 | 可选中 | 说明 |
|---|---|---|---|
| UnderlayLayer | 底图 | 否 | 最底，半透明 |
| GridLayer | 网格 | 否 | 参考线 |
| ZoneLayer | 库区多边形 | 是 | 按 ZoneType 着色 |
| AisleLayer | 巷道面 + 中心线 | 是 | 中心线虚线 |
| RackLayer | 货架（俯视矩形，含 RotationZ）| 是 | 主操作对象 |
| MarkerLayer | 打点标注 | 是 | 02 章交互 |

> 库位**不单独画在 2D 画布**（万级图元会拖垮）：俯视下货架矩形内用简单网格线/计数表达"这架有 6×4 格"即可；单个库位的可视化在 3D（05/06）。选中货架时属性面板显示其库位阵列摘要。

---

## 第4章 模板库与模板参数

`Space_Template`（00 章 4.7）。`TemplateType`：1 货架 / 2 库区。`Params` 为 JSON。

### 4.1 货架模板 Params（TemplateType=1）
```jsonc
{
  "cols": 6,            // 列数（沿货架长度）
  "levels": 4,          // 层数（垂直）
  "depthCount": 1,      // 深度格数（前后排）
  "cellW": 1200,        // 单格宽 mm
  "cellH": 1500,        // 单格高 mm
  "cellD": 1000,        // 单格深 mm
  "defaultRotation": 0  // 默认偏航角
}
```
→ 生成**一个货架** + 其 `cols*levels*depthCount` 个库位（草稿态、空码）。

### 4.2 库区/排架模板 Params（TemplateType=2，批量主力）
描述"一片货架阵列"，一次刷出多排货架 + 可选巷道：
```jsonc
{
  "rackTemplate": { "cols":6,"levels":4,"depthCount":1,"cellW":1200,"cellH":1500,"cellD":1000 },
  "rows": 4,                 // 货架排数
  "racksPerRow": 10,         // 每排货架数
  "rowGap": 2800,            // 排间距 mm（= 巷道宽）
  "rackGap": 100,            // 同排货架间隙 mm
  "aisleBetweenRows": true,  // 排间自动生成巷道（Aisle）+ 中心线
  "orientation": "H"         // 阵列朝向 H 水平 / V 垂直
}
```
→ 生成 `rows*racksPerRow` 个货架 + 各自库位 +（若 `aisleBetweenRows`）排间巷道，全部草稿态。

> **模板是"参数快照"不是"活引用"**：生成后货架/库位独立存在（`Rack.TemplateId` 仅记来源，00 章删模板 SetNull 不影响已生成几何）。改模板不回溯已生成对象——要变只能重新生成或在 02 章手工调。

### 4.3 模板管理画面
左侧模板库列表（按类型分组）+ 右侧参数表单（带实时预览缩略图）。操作：新建/复制/编辑/删除模板。模板**租户级**复用（多客户各自维护，或从系统预置模板库克隆）。

---

## 第5章 模板化批量生成

### 5.1 交互流程
```
选模板（货架/库区）→ 在画布落点（点击=单架；框选矩形=库区阵列铺满）
  → 预览幽灵图形（半透明，未落库）→ 确认
  → 生成器产出草稿对象（Rack[] + Location[]）→ 写入 Pinia 场景 + 标脏
  → 保存时落库（第6章）
```

### 5.2 生成算法（纯函数，`space-editor/generate/`）
```ts
// 货架模板 → 1 货架 + 库位阵列；坐标用 00 §6 公式（角点锚 + 绕角转）
function genRack(tpl: RackTemplate, originX:number, originY:number, rotation:number): {rack:RackVO, locs:LocationVO[]} {
  const rack = { id: newGuid(), x:originX, y:originY, z:0, rotationZ:rotation,
                 cols:tpl.cols, levels:tpl.levels, depthCount:tpl.depthCount,
                 cellW:tpl.cellW, cellH:tpl.cellH, cellD:tpl.cellD, templateId:tpl.id }
  const locs:LocationVO[] = []
  for (let c=1;c<=tpl.cols;c++)
    for (let l=1;l<=tpl.levels;l++)
      for (let d=1;d<=tpl.depthCount;d++) {
        const [ax,ay,az] = computeAbs(rack, c, l, d)          // 00 §6.1 公式
        locs.push({ id:newGuid(), rackId:rack.id, floorId:scene.floor.id,
                    col:c, level:l, depth:d, absX:ax, absY:ay, absZ:az,
                    sizeW:tpl.cellW, sizeH:tpl.cellH, sizeD:tpl.cellD,
                    locationCode:null, codeOrigin:1, placed:true, status:0, version:0 })
      }
  return { rack, locs }
}
```
- 库区模板 = 按 `rows/racksPerRow/rowGap/rackGap` 算每个货架 `originX/Y/rotation`，循环调 `genRack`；`aisleBetweenRows` 则在每两排间生成 `Space_Aisle`（多边形 + 中心线，00 §5）。
- **产出全部草稿态**：`Status=0`、`LocationCode=null`（编码留 03）、`Placed=true`、`CodeOrigin=1`。
- 货架编码 `RackCode`：批量生成时按"排-架"序号给一个**临时建议值**（如 `R01-01`），可在 02 章改；它是 03 编码引擎的"架号段"取值源之一。

### 5.3 生成校验
| 校验 | 处理 |
|---|---|
| 落点超出所属 Zone 多边形 | 警告 W-SPACE-101（允许越界但提示，最终归属按 Zone 选择） |
| 生成的货架与既有货架重叠 | 警告 W-SPACE-102（碰撞提示，02 章可手工挪；不阻断） |
| 阵列规模过大（> 阈值，如一次 > 5000 库位） | 二次确认（防误框选铺满整层卡顿） |
| 必须先选定目标 Zone | 阻断 E-SPACE-101（货架 ZoneId 必填，00 章） |

---

## 第6章 草稿态与保存

### 6.1 草稿与脏标记
- 编辑器对象在 Pinia 中维护，改动打 `dirty` 标记（新增/修改/删除集）。
- 草稿态 = `Location.Status=0`；保存只是把几何落库，**不等于发布**（发布是 04 章独立动作，过冻结闸门才 `Status→1`）。
- 库位 `LocationCode` 在草稿期可为 `null`、可被 03 反复重排（00 章过滤唯一索引支撑）。

### 6.2 整层保存（批量 upsert）
```
POST /api/space/floor/{id}/scene  { zones, aisles, racks, locations, markers, deletes }
  → 事务内：按 dirty 集 upsert/删除；货架位姿变更触发 00 §6.2 RecalcRackLocations
  → 校验 RowVersion（00 乐观锁），冲突返回 E-SPACE-009
  → 返回服务端最新 RowVersion / 生成的 Id 映射
```
- 保存粒度 = 整层场景差量提交（只传 dirty）；大阵列首次保存可分批（每批 N 货架）避免超大请求。
- **临时 Id**：前端生成对象用临时 GUID；保存后以服务端返回为准（前端 GUID 与服务端一致即可直接用，CP6 用 GUID 主键，省去映射）。

### 6.3 并发
多人同时编辑同层：靠 00 章 `RowVersion` 乐观锁，谁先存谁赢，后者收 E-SPACE-009 提示刷新。v1 不做实时协同（OT/CRDT），属 P3+。

---

## 第7章 场景复制与 JSON 导入导出（多客户复制）

商用底座要"一套建好的仓库布局复制给相似客户"。

### 7.1 导出
```
GET /api/space/floor/{id}/export  → SceneExportDto(JSON)
  { meta:{version,exportedFrom}, floor, zones, aisles, racks, templates, locations(可选), markers }
```
- 导出**几何 + 模板**；库位可选（通常导出货架参数即可，库位由导入方重新生成，省体积）。
- **不导出**：TenantId、绝对坐标缓存（导入重算）、LocationCode（导入方重新编码）、发布状态。

### 7.2 导入
```
POST /api/space/site/{id}/import  SceneExportDto
  → 全部 Id 重映射（new GUID），父子引用按映射重连
  → TenantId 注入当前租户；Status 一律置 0 草稿；LocationCode 清空
  → 落 1 个新 Floor + 其下结构；货架按参数 + computeAbs 重建库位
```
- **ID 重映射**是关键：导入即"克隆成新对象"，绝不复用源 Id/编码，避免跨租户/跨场景串号。
- 导入后是**草稿**：客户再按自己的编码规则（03）重新生成库位编码、再发布（04）。

> **为什么导出可不带库位、导入重新生成？** 库位是货架参数的确定性派生（00 §5.3/§6）。导出货架参数 + 模板即可无损重建库位几何；省下万级库位的传输/存储，且强制导入方走自己的编码规则，符合"可配置编码引擎·多客户复制"。

---

## 第8章 D7 采纳态反向建模入口

存量 WMS 客户：编码已存在于 WMS，先导入为"已发布·未放置·无几何"（采纳导入落库在 04 章），本章提供**把几何补上**的反向建模入口。

### 8.1 反向建模流程（与"模板生成新码"并列的第二入口）
```
① 采纳导入（04 章）：现有 WMS 库位编码 → Space_Location（Status=1, Placed=false, CodeOrigin=2, 无几何）
② 编辑器拉"待绑定列表"：GET /api/space/location/unplaced?floorId=（00 §9）
③ 用户在 2D 画布摆货架（第5章模板生成几何，但此货架的库位先不自动建码）
④ 绑定：把货架的格口(col,level,depth) ←→ 待绑定列表里的既有冻结码 配对
     - 自动建议：按"列/层/深顺序"与编码末段顺序对齐，批量预匹配
     - 人工校正：拖拽/点选逐个改绑
⑤ 提交绑定：回填 Location.RackId/FloorId/col/level/depth/AbsXYZ/Size，Placed=true
     —— LocationId 与 LocationCode 不变（00 §7.1），不触发发布（00 §6.2 表）
```

### 8.2 绑定差异可视化（三种 mismatch）
| 差异 | 含义 | 处理 |
|---|---|---|
| 有几何无码 | 货架格口没有可绑的既有码 | 标黄；可转"生成新码"（CodeOrigin=1）或留空待后续 |
| 有码无几何 | 待绑定列表里剩余未绑的码 | 标红；提示补摆货架格口或确认废弃 |
| 数量不匹配 | 货架格数 ≠ 待绑码数 | 汇总提示，逐个核对 |

> 反向建模是采纳客户**几何后补**的独立流程，方向与新建相反（先有码、后有几何）。它**只补几何缓存、不动编码契约**——这正是 00 章 `Placed⊥Status` + `RackId/Code 可空` schema 的用途所在。

---

## 第9章 API 接口设计

路由前缀 `/api/space`。

| 端点 | 方法 | 说明 |
|---|---|---|
| `/template` | GET/POST/PUT/DELETE | 模板 CRUD（货架/库区） |
| `/template/{id}/clone` | POST | 复制模板（系统预置→租户） |
| `/floor/{id}/scene` | GET | 整层场景（00 §9，渲染/编辑共用） |
| `/floor/{id}/scene` | POST | 整层差量保存（第6.2，事务 + 乐观锁） |
| `/floor/{id}/generate` | POST | 模板化批量生成（服务端校验规模 + 落草稿；或前端生成后随 scene 保存） |
| `/floor/{id}/export` | GET | 场景导出 JSON（第7.1） |
| `/site/{id}/import` | POST | 场景导入（ID 重映射，第7.2） |
| `/location/unplaced?floorId=` | GET | 采纳待绑定库位列表（第8，00 §9） |
| `/rack/{id}/bind-codes` | POST | 反向建模：货架格口绑既有冻结码（第8.1⑤） |

> 批量生成可走两种实现：①前端纯函数生成 + 随 `/scene` 保存（推荐，预览即所得）；②服务端 `/generate`（超大阵列时服务端直建省传输）。v1 默认①，②留大场景优化。

---

## 第10章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-SPACE-101 | Error | 请先选择目标库区 | 生成货架未选 Zone（ZoneId 必填） |
| W-SPACE-101 | Warn | 货架落点超出库区范围 | 生成落点在 Zone 多边形外 |
| W-SPACE-102 | Warn | 货架与既有货架重叠 | 生成/移动碰撞 |
| W-SPACE-103 | Warn | 本次将生成 N 个库位，确认继续？ | 阵列规模超阈值 |
| E-SPACE-102 | Error | 底图未标定比例尺，无法按真实尺寸描图 | 用 UnderlayImage 但缺 UnderlayScale |
| E-SPACE-103 | Error | 导入场景版本不兼容 | export meta.version 不匹配 |
| E-SPACE-009 | Error | 数据已被他人修改，请刷新重试 | 保存 RowVersion 冲突（00 章） |

---

## 第11章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 00 数据模型 | 读写 9 表几何；用 §6 computeAbs、§7 状态机、底图比例尺、RowVersion |
| → 02 受控自由布局 | 共用 Konva 画布 + Pinia 场景；02 做拖拽/旋转/打点/捕捉/撤销 |
| → 03 编码引擎 | 本章产草稿库位（空码）；03 给 `LocationCode`、支持重排 |
| → 04 发布契约 | 采纳导入落库在 04；本章提供反向建模绑定入口（unplaced/bind-codes） |
| → 05/06 渲染 | 保存后 `/floor/{id}/scene` 供 3D 渲染 |
| → PUB 权限 | 编辑器操作接 PUB 功能权限（建模/生成/导入需授权）；场景查询接数据权限 |
| 多租户 | 模板/场景按 TenantId 隔离；导入强制重映射 + 注入当前租户 |

---

## 自检
- [ ] 为什么 2D 用 Konva 而非 SVG / 直接 Three.js？库位为什么不画在 2D 画布？
- [ ] 底图描图为什么必须先标定 UnderlayScale？标定怎么做？
- [ ] 货架模板与库区模板各生成什么？批量生成产出的库位是什么状态、有没有编码？
- [ ] 模板是"活引用"还是"参数快照"？改模板会回溯已生成货架吗？
- [ ] 导出为什么可以不带库位、导入重新生成？ID 为什么必须重映射？
- [ ] 采纳态反向建模的五步是什么？绑定只补什么、不动什么？为什么不触发发布？
- [ ] 01 和 02 的分工边界在哪？

---

*实现：新建 `cp6.web/src/space-editor/*`（Konva 封装 + 生成器 + IO）+ `cp6.web/src/views/space/editor/*` + `CP6.Core/Services/Space/{TemplateService,SceneService,SceneIoService}.cs`。配套 xlsx（模板参数样例/生成算法表/画面线框/绑定差异矩阵）见同名 `.xlsx`。*
