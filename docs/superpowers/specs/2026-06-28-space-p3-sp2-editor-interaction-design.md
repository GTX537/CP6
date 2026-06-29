# Space P3 · SP2 编辑器交互运行态收尾 — 设计规格

- 版本：v1.0
- 日期：2026-06-28
- 分支：`feat/space-p3-hardening`（worktree `D:\CP6-space-backend`，基于已落 main 的 Space 00~08）
- 范围：前端 Konva 2D 编辑器（`cp6.web/src/space-editor/`）交互运行态四项收尾，**零后端改动**
- 承接：[[project_space_p1_impl]] 末「新窗口接力 SP2」；P1 编辑器 01/02（E→I）留下的四个运行态 QA 标记

---

## §0 背景与目标

P1 阶段把 Konva 2D 编辑器（建仓 / 模板生成 / 自由布局 Command 双栈 + SnapEngine + 碰撞 / 绑码）全栈落码，但**四处画布交互只有纯逻辑保证（vue-tsc + vitest），运行态未校准**，在 `RotateTool.ts` / `SelectTool.ts` / `SceneStage.ts` 顶部留有 QA 标记：

| 编号 | 问题 | 根因（已在代码核实） |
| --- | --- | --- |
| ① | 旋转锚点/符号不一致，松手"跳变" | `RotateTool` 用 Konva `Transformer(rotateEnabled)` **绕选区包围盒中心**旋转；而数据模型 + `SceneStage.renderRack`（`rotation:-rotationZ`）+ `RotateRackCmd`（只改 `rotationZ`）**绕货架锚点角 `(rack.x,rack.y)`** 旋转。枢轴不一致 → 拖拽绕中心、`afterCommand` 重渲染瞬间跳回绕锚点 |
| ② | 角度吸附无手感反馈 | `RotateTool.snapAngle`（15° 倍数 / ±3° / Ctrl 关）参数合理，但旋转时无当前角度读数、无"已吸附"提示 |
| ③ | lasso 框选旋转货架偏大误选 | `SelectTool.onMouseUp` 用 `node.getClientRect()` 取**旋转货架的轴对齐包围盒（AABB）**，旋转后 AABB 比真实 OBB 大 → 误选 |
| ④ | 幽灵预览不跟随鼠标 | `SceneStage.showGhost()` 已实现但**从未被调用**；placement mode（`FloorEditor`）只切 CSS 光标，落点前看不到货架占位 |

**目标**：四项全部收口至"运行态可用、手感正确、纯逻辑可测 + 浏览器 QA 验证"。

**关键决策（用户已拍板）**：

- **D1 旋转枢轴 = 几何中心**（手感自然，原地转）。代价：`RotateRackCmd` 须同时回算 `rack.{x,y}`。**渲染 / 碰撞 / 吸附全链继续锚点制**，中心枢轴逻辑只活在 `RotateTool` + `RotateRackCmd` 内，下游无感。
- **D2 lasso = 精确 OBB 相交-触碰**（YAGNI）。口径同现在（overlap 即选），仅把 AABB 换成真实 OBB-SAT；不做方向化 contain/touch。
- **D3 幽灵 = 整阵列外包矩形**（便宜且信息足），随光标跟随 + 按合法性着色。

**非目标**：多选旋转（维持单选）；后端 / 库位几何前端重算（保存时既有 `SceneService` 按 `changedRackIds` 重算）；路径规划做真（属 SP3）。

---

## §1 as-built 锚点（落码前直接引用，不重探查）

坐标与渲染约定（`SceneStage.ts` / `coords.ts`）：

- `view: ViewState = { panX, panY, zoom, height }`；`worldToScreen` 翻 Y（屏幕 Y 向下）；`screenToWorld` 反变换。**worldToScreen 仅 scale + 平移 + Y 翻转，无旋转** → 屏幕轴对齐矩形 ↔ 世界轴对齐矩形（③的数学依据）。
- `renderRack`：`group = Konva.Group({ id, name:'rack', x:origin.x, y:origin.y, rotation:-rack.rotationZ })`，rect 画在 `y:-dPx`（沿 +D 向屏幕上方延伸）。**group 的 position = 锚点 `(rack.x,rack.y)` 的屏幕坐标，旋转绕该 position**。
- 货架尺寸：`W = cols*cellW`（局部 X，mm）、`D = depthCount*cellD`（局部 Y，mm）；锚点 `(rack.x,rack.y)` 是 OBB 原点角。
- `getRackNode(id)`：`layers.rack.findOne('#'+id)`。
- 图层：`underlay/grid/zone/aisle/rack/marker/ghost`。lasso 与幽灵都画在 `ghost` 层。

OBB / 多边形纯逻辑（`interact/collide/CollisionHint.ts`，**直接复用，③ 须 export 三个原语**）：

- `rackCorners(r: RackVO): Vec2[]` —— 返回 OBB 4 角（锚点、+W、+W+D、+D）的世界坐标，绕锚点旋转。
- `project(points, axis): [min,max]` / `separated(a,b): boolean` —— SAT 投影 / 区间分离（当前为模块私有，③ 改 export）。
- `pointInPolygon(px,py,poly): boolean` —— 射线法（④ 合法性判定复用）。
- `Vec2 = { x:number; y:number }`（当前为私有 interface，③ 复用时 export 或在新模块本地声明）。

命令栈（`command/`）：

- `Command = { label; do(ctx); undo(ctx); merge? }`；`EditorContext = { scene, markDirty, markDirtyDelete }`。
- `store.stack.exec(cmd, editorCtx)` / `store.updateUndoRedo()` / `store.buildEditorContext()`。
- `afterCommand()`（`FloorEditor`）统一收尾：render → scanCollisions → applyRackStyles → 徽标 → refreshTransformer。

交互框架（`interact/`）：

- `InteractionManager`：工具状态机 `select|drag|rotate|marker`，`ToolContext = { stage, store, snap, ctrlHeld, afterCommand, transformer }`，持单个共享 `Konva.Transformer`（默认仅虚线选框：rotateEnabled/resizeEnabled false、enabledAnchors []）。
- `ITool`：`onMouseDown/Move/Up/Click/Activate/Deactivate/Escape`。
- `findRackGroup(target)` / `isTransformerNode(node)`（已 export）。
- `SnapEngine`（`interact/snap/SnapEngine.ts`）：`snap(point, {zoom,racks,aisles}) → {x,y,snapped}`，候选=货架边角/巷道中心/网格交点（位置吸附，**与角度吸附无关**）。

放置模式（`views/space/editor/FloorEditor.vue`）：

- `placementMode: ref<boolean>` / `pendingSel: ref<TemplatePanelSelection>` / `selectedZoneId`。
- `onTemplateSelect(sel)`：置 `pendingSel` + `placementMode=true` + `im.setEnabled(false)` + 提示。
- `bindStageClick()`：placement 期间 stage `click` → `screenToWorld(ptr)` → `genZoneArray(template, zoneId, floorId, {...arrayParams, originX, originY, rotation:0})` → push racks/locs/aisles + markDirty + afterCommand + exitPlacementMode；阵列总数 >200 走 `ElMessageBox.confirm`。
- `exitPlacementMode()`：`placementMode=false` + `stageRef.hideGhost()` + `im.setEnabled(true)`。
- `TemplatePanelSelection = { template, arrayParams:{ rows, racksPerRow, ... } }`（详见 `panels/TemplatePanel.vue` / `generate/genZoneArray.ts`）。

---

## §2 ① 旋转：几何中心枢轴 + 自定义旋转手柄

### §2.1 纯逻辑：`rotateAboutCenter`

新增纯函数（建议置 `interact/rotate/rotateGeometry.ts`，不引 Konva）：

```ts
// 给定货架与新角度，保持几何中心不变，返回新锚点
export function rotateAboutCenter(
  rack: { x:number; y:number; cols:number; cellW:number; depthCount:number; cellD:number; rotationZ:number },
  newRotationZ: number,
): { x:number; y:number }
```

数学：

- `W = cols*cellW`，`D = depthCount*cellD`，局部中心 `lc = (W/2, D/2)`。
- 设 `R(θ)` 为绕原点逆时针旋转（θ 取角度，内部转弧度）。
- 旋转前几何中心（世界）：`C = (rack.x, rack.y) + R(rack.rotationZ)·lc`。
- 保持 `C` 不变，新锚点：`anchor' = C − R(newRotationZ)·lc`。
- 返回 `{ x: anchor'.x, y: anchor'.y }`。

**坐标系一致性铁律**：`R(θ)` 必须与 `CollisionHint.rackCorners` 使用的同一约定（`x' = x·cosθ − y·sinθ; y' = x·sinθ + y·cosθ`，θ 为 `rotationZ`）。`rotateAboutCenter` 的输入/输出全在世界坐标（mm），与屏幕 Y 翻转无关；屏幕翻转只发生在 `worldToScreen`。

### §2.2 命令：`RotateRackCmd` 升级为位姿命令

签名从 `(rackId, fromDeg, toDeg)` 改为携带完整起止位姿：

```ts
interface RackPose { x:number; y:number; rotationZ:number }
class RotateRackCmd {
  constructor(private rackId: string, private from: RackPose, private to: RackPose) {}
  do(ctx)   { const r = find(rackId); if(!r) return; r.x=to.x;   r.y=to.y;   r.rotationZ=to.rotationZ;   ctx.markDirty(rackId) }
  undo(ctx) { const r = find(rackId); if(!r) return; r.x=from.x; r.y=from.y; r.rotationZ=from.rotationZ; ctx.markDirty(rackId) }
}
```

- 下游 render / scanCollisions / SnapEngine 继续读 `rack.{x,y,rotationZ}`（锚点制），**无感**。
- 保存时后端按 `changedRackIds` 重算该货架库位几何（既有 `SceneService`），位移 + 旋转都被覆盖。
- 同步改动：`RotateTool` 构造调用、`command/commands.spec.ts` 中 RotateRackCmd 相关用例。

### §2.3 工具：`RotateTool` 重写为自定义手柄

去掉 Konva Transformer 旋转（`rotateEnabled` 不再用于旋转），自画旋转符号；Transformer 仅保留虚线选框（rotateEnabled=false）。

旋转符号（画在 `ghost` 层或独立 handle，随 group 位姿更新）：

- **中心枢轴点**：货架几何中心的屏幕坐标处画小圆点（pivot dot）。
- **旋钮**：从中心引一条线到半径 `r = 半对角线 + padding(px)` 外的圆形旋钮，初始指向货架当前朝向（或屏幕 −Y）。
- **角度读数**（②）：中心旁 `Konva.Text` 显示当前 `rotationZ`（整数度），已吸附时文字 + 旋钮变绿。

交互流程：

1. `onActivate`：Transformer 仅选框；按当前单选货架画旋转符号；绑自有指针事件（不复用 Transformer 的 transform 事件）。
2. `onClick`（空白/货架）：单选切换 + 重画符号（维持现状的单选语义）。
3. 旋钮 `pointerdown`：记 `from = {x,y,rotationZ}`、记起始指针相对中心的角 `a0`。
4. 旋钮 `pointermove`：算指针相对中心角 `a1`；`rawDeg = rotationZ0 + (a1−a0 的世界角增量)`；`newRotationZ = ctrlHeld ? rawDeg : snapAngle(rawDeg)`，规范化 `[0,360)`；`anchor' = rotateAboutCenter(rack, newRotationZ)`；**实时更新** `group.position(worldToScreen(anchor'))` + `group.rotation(-newRotationZ)` + 重画符号 + 角度读数。**预览=最终渲染，无跳变**。
5. 旋钮 `pointerup`：`to = { x:anchor'.x, y:anchor'.y, rotationZ:newRotationZ }`；`new RotateRackCmd(id, from, to)` → `stack.exec` → `updateUndoRedo` → `afterCommand`。
6. `onEscape`：旋转中则回滚 group 到 `from`（不入栈）；`onDeactivate`：清符号 + 解绑。

**角度增量的屏幕↔世界换算**：指针在屏幕空间，screen Y 向下；世界 `rotationZ` 逆时针为正。指针相对中心的屏幕角 `θ_screen = atan2(dyScreen, dxScreen)`，世界角增量 `Δworld = −(θ_screen1 − θ_screen0)`（Y 翻转取负，与 `rotation:-rotationZ` 同源）。运行态 QA 校准方向号。

---

## §3 ② 角度吸附手感

- 保持 `snapAngle`：15° 倍数 / ±3° 阈值（含 358°↔0° 环绕）/ Ctrl 关吸附。**参数不变**。
- 新增运行态反馈（已并入 §2.3 旋转手柄）：旋转中角度读数 `Konva.Text`；`snapped`（落在 15° 倍数 ±3°）时读数 + 旋钮变绿，否则常态色。
- `snapAngle` 补 vitest（边界：0/15/±3 阈内外/358 环绕/负角规范化）。

---

## §4 ③ lasso：AABB → OBB

### §4.1 纯逻辑：`obbIntersectsRect`

新增纯函数（建议置 `interact/select/lassoHit.ts`，复用 `CollisionHint` 原语；`CollisionHint` 将 `rackCorners` 之外的 `project`/`separated`（及 `Vec2`）export）：

```ts
// 轴对齐世界矩形（screenToWorld 后）
interface WorldRect { minX:number; minY:number; maxX:number; maxY:number }
// 货架 OBB 4 角（世界）与轴对齐矩形是否相交（SAT，触碰即真）
export function obbIntersectsRect(corners: Vec2[], rect: WorldRect): boolean
```

SAT 轴：世界 X `(1,0)`、世界 Y `(0,1)`（矩形两法线）+ 货架两边法线（由 `corners[1]-corners[0]` 与 `corners[3]-corners[0]` 推得）。任一轴上两投影 `separated` → 不相交；全部重叠 → 相交。矩形在 X/Y 轴的投影即 `[minX,maxX]` / `[minY,maxY]`。

### §4.2 接入：`SelectTool.onMouseUp`

- lasso 屏幕矩形两角（`(selX,selY)` 与 `(selX+selW, selY+selH)`）`screenToWorld` → 两世界点；取 `min/max` 组 `WorldRect`（worldToScreen 无旋转故合法）。
- 逐货架：`obbIntersectsRect(rackCorners(rack), worldRect)` → true 则入选。
- 删除原 `node.getClientRect({relativeTo:stage})` AABB 路径。tiny-drag（<3px 视为点击）守卫保留。

### §4.3 测试

`obbIntersectsRect` vitest：轴对齐重叠/分离；45° 旋转货架角刚好进/出矩形（AABB 会误判、OBB 正确）；完全包含；完全外离；边缘紧贴（separated 视为分离，与碰撞口径一致）。

---

## §5 ④ 幽灵预览跟随鼠标

### §5.1 纯逻辑：`arrayFootprint`

新增纯函数（建议置 `generate/arrayFootprint.ts`，与 `genZoneArray` 同源参数）：

```ts
// 整阵列（rows × racksPerRow）外包尺寸（mm，未旋转，原点在阵列锚点角）
export function arrayFootprint(
  template: TemplateVO,
  arrayParams: { rows:number; racksPerRow:number; /* 行距/架距等，对齐 genZoneArray */ },
): { w:number; d:number }
```

计算须与 `genZoneArray` 的排布步长一致（单架 `W=cols*cellW` / `D=depthCount*cellD` + 架距 + 行距）。**实现前对照 `generate/genZoneArray.ts` 的实际步进**，避免幽灵尺寸与真实落点不符。

### §5.2 渲染：`SceneStage` 扩幽灵 API

```ts
// 在 originWorld 处画 w×d（mm）外包矩形幽灵，valid 决定绿/琥珀
showFootprintGhost(originWorld: XY, w: number, d: number, valid: boolean): void
```

- 复用现 `showGhost` 的 ghost 层清画模式；矩形屏幕尺寸 `w*zoom × d*zoom`，画在 `y:-dPx`（与 renderRack 同向）。
- `valid=true` 绿（`rgba(80,200,120,.3)`/`#40cc70`）；`valid=false` 琥珀（`rgba(255,170,0,.25)`/`#ffaa00`）。
- 保留 `hideGhost()`。

### §5.3 接入：`FloorEditor` placement 跟随

- placement 进入时绑 stage `mousemove`：`screenToWorld(ptr)` → SnapEngine snap（复用既有）→ `originWorld`；`arrayFootprint(template, arrayParams)` → `{w,d}`；合法性 `valid` = `selectedZoneId` 已选 **且** 外包矩形四角（世界）全 `pointInPolygon(zone.polygon)`；`showFootprintGhost(originWorld, w, d, valid)`。
- `click` 落点：维持现 `bindStageClick` 逻辑（>200 确认、genZoneArray、markDirty、afterCommand、exitPlacementMode）。
- `exitPlacementMode` / Esc / 离开画布：`hideGhost` + 解绑 mousemove。
- 节流：mousemove 直接重画单矩形足够轻；如有抖动用 rAF 合并（运行态视情况）。

---

## §6 测试与验收

### §6.1 纯逻辑 vitest

| 模块 | 用例要点 |
| --- | --- |
| `rotateAboutCenter` | 0°→90° 中心不变、锚点位移正确；与 `rackCorners` 同坐标约定（旋转后中心 == 旋转前中心）；非方形货架 |
| `RotateRackCmd` | do/undo 三值（x/y/rotationZ）齐改齐还原；markDirty 调用 |
| `obbIntersectsRect` | 见 §4.3 |
| `arrayFootprint` | 1×1、rows×racksPerRow、与 genZoneArray 步长一致 |
| `snapAngle` | 见 §3 |

前端三门：vue-tsc 0 错 / vitest 全绿（既有 + 新增）/ build 成功。

### §6.2 运行态 QA（gstack / Playwright，真浏览器）

环境：后端 5177（`appsettings.Local.json`→`CP6DB_SpaceQA`，无 RabbitMQ 稳跑）/ 前端 5173 / admin·123456 / 路由 `/space/editor/{floorId}`，Floor `5C92E6A8…`。坑：el-input 用 `pressSequentially`；GUID 比较大小写无关。

验收点：

1. **旋转**：rotate 工具抓旋钮拖动，货架**绕几何中心原地转**、松手**无跳变**；角度读数实时；接近 15° 倍数自动吸附并变绿；Ctrl 关吸附自由角；undo 回原位姿。
2. **lasso**：把一个旋转 45° 的货架，用刚好擦过其 AABB 角但不碰 OBB 的框 → **不选中**（旧 AABB 会误选）；框真实覆盖 → 选中。
3. **幽灵**：选模板进 placement，外包矩形跟随光标；未选库区/出界 → 琥珀，落在库区内 → 绿；点击落点正确、Esc 取消清幽灵。
4. 无回归：既有拖拽 / 碰撞着色 / 绑码 / 保存正常。

固化 QA 证据至 `docs/superpowers/qa/space-p3-sp2/`（截图 + 步骤）。

---

## §7 文件清单（新增/改动）

新增：

- `cp6.web/src/space-editor/interact/rotate/rotateGeometry.ts`（`rotateAboutCenter`）+ `.spec.ts`
- `cp6.web/src/space-editor/interact/select/lassoHit.ts`（`obbIntersectsRect`）+ `.spec.ts`
- `cp6.web/src/space-editor/generate/arrayFootprint.ts` + `.spec.ts`

改动：

- `command/commands/RotateRackCmd.ts`（位姿命令）+ `command/commands.spec.ts`
- `interact/tools/RotateTool.ts`（自定义手柄重写）
- `interact/tools/SelectTool.ts`（OBB lasso 接入）
- `interact/collide/CollisionHint.ts`（export `project`/`separated`/`Vec2`）
- `interact/snap/SnapEngine.spec.ts` 或新增 `RotateTool` 角度测（`snapAngle` 补测）
- `SceneStage.ts`（`showFootprintGhost`）
- `views/space/editor/FloorEditor.vue`（placement mousemove 幽灵跟随）

无后端 / 无 EF 迁移 / 无 i18n 键新增（UI 文本 `t()` plain string；错误码不涉及）。

---

## §8 交付顺序（subagent-driven TDD）

1. **③ lasso**（纯逻辑易测，先拿分）：export 原语 + `obbIntersectsRect` + spec → 接 `SelectTool`。
2. **① 旋转中心枢轴**（核心）：`rotateAboutCenter` + spec → `RotateRackCmd` 位姿化 + spec → `RotateTool` 自定义手柄重写。
3. **② 角度反馈**：`snapAngle` 补测 + 旋转手柄读数/吸附高亮（搭在①上）。
4. **④ 幽灵跟随**：`arrayFootprint` + spec → `SceneStage.showFootprintGhost` → `FloorEditor` mousemove 接入。
5. **集中 gstack QA**：起真栈，四验收点 + 三门，固化证据。

每项实现→spec 审→质量审→修，纯逻辑 vitest 当场绿，画布交互运行态留第 5 步 gstack。

---

## §9 SP3 预告（不在本 spec）

SP2 后接 SP3 = 路径规划做真：`PickPathPlanner.ts` 的 v1 Dijkstra 升 A* / 真交叉口图 / 沿巷道重排对比 / 3D 多层（需查数据是否支持楼层连通）。独立 spec → plan → TDD。
