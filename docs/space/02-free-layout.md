# Space 02 · 受控自由布局交互 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | SPACE-02 受控自由布局交互 |
| 所属模块 | Space 空间数字底座 · Part 1（P1） |
| 里程碑 | **P1**（建模精修；与 [01](./01-editor-template.md) 共用同一 Konva 画布 + Pinia 场景） |
| 技术栈 | Vue3 + TypeScript + **Konva.js（Transformer/事件/拖拽）** + Pinia / .NET8 Web API + EF Core |
| 命名空间 | `cp6.web/src/space-editor/interact`、`cp6.web/src/space-editor/command` / 复用 01 的 `useEditorStore` |
| 落地决策 | **受控自由布局**=模板化生成为主、手工精修为辅；不做完整自由绘制（CAD 导入推迟二/三期） |
| 依赖 | [00 数据模型](./00-data-model.md)（写 Rack 位姿、Marker、触发 §6.2 RecalcRackLocations）、[01 编辑器框架](./01-editor-template.md)（画布/图层/场景对象图/保存） |

> **题眼**：01 负责"从无到有成批建几何"，02 负责"对已有几何手工精修"。本章把**拖拽 / 旋转 / 打点 / 框选 / 捕捉对齐 / 撤销重做 / 碰撞提示**七件交互做成一套**受控**编辑体验——"受控"= 不让用户随手乱画，而是在模板生成的骨架上做有约束的微调（捕捉到网格/货架边/巷道中心线，越界与碰撞实时提示）。**记住一句**：02 只改**几何位姿与标注**（Rack 的 X/Y/RotationZ、Marker），库位坐标是货架位姿的派生（保存时由 00 §6.2 重算），**绝不在 02 直接编辑库位编码或单库位坐标**——编码归 [03](./03-code-engine.md)、发布归 [04](./04-publish-contract.md)。

---

## 目录
- 第1章 功能概述与定位（与 01 的边界）
- 第2章 交互总架构（事件层 + 工具状态机 + Command 栈）
- 第3章 选择系统（单选 / 框选 / 多选 / 选中集）
- 第4章 拖拽移动（drag + 捕捉 + 多选整体平移）
- 第5章 旋转（Transformer / RotationZ / 锚点 / 角度吸附）
- 第6章 捕捉对齐（网格 / 货架边 / 巷道中心线 / 等距分布）
- 第7章 打点标注（Marker 增删改）
- 第8章 碰撞检测与越界提示（实时，不阻断）
- 第9章 撤销 / 重做（Command 模式双栈）
- 第10章 与 00/01 的状态同步与保存
- 第11章 API 接口（增量，复用 01）
- 第12章 消息一览
- 第13章 集成与依赖
- 自检

---

## 第1章 功能概述与定位

**目的**：在 01 搭好的 2D 俯视画布与模板生成的几何骨架上，提供一套**受控**的手工精修交互，让用户把成批生成的货架挪正、转向、对齐，并补充打点标注。

**本章范围（02）：**
- 选择系统：点选、框选（橡皮筋）、加选/减选、整组选中集管理。
- 拖拽移动：单个 / 多选整体平移，落点实时捕捉。
- 旋转：绕货架锚点改 `RotationZ`，角度吸附（0/15/30/45/90°）。
- 捕捉对齐：吸附网格、货架边缘、巷道中心线；等距分布、对齐成行。
- 打点标注：`Space_Marker` 的新增 / 移动 / 改文本 / 删除。
- 碰撞与越界：货架重叠、超出所属 Zone 的**实时提示**（不阻断）。
- 撤销 / 重做：Command 模式双栈，覆盖以上所有可逆操作。

**不含（划清边界）：**
| 能力 | 去哪章 |
|---|---|
| 模板化批量生成 / 草稿保存 / 场景复制 / 导入导出 / 反向建模入口 | [01 章](./01-editor-template.md) |
| 库位编码生成 / 重排 / 规则（02 不碰 LocationCode） | [03 章](./03-code-engine.md) |
| 发布给 WMS / 采纳对账 / 冻结闸门 | [04 章](./04-publish-contract.md) |
| 3D 渲染 / 相机 / 拾取 | [05](./05-viewer-core.md)/[06](./06-camera-pick.md) |

> **01 与 02 的分工再强调**：01 = "**生成**几何的框架与批量手段"（一次刷一片，产草稿）；02 = "**手工调整**几何的交互"（拖一个、转一个、对一排）。二者共用同一 Konva `SceneStage` 与 Pinia `useEditorStore`；01 写 `generate/` + `io/`，02 写 `interact/` + `command/`。**保存复用 01 第6.2 的 `/floor/{id}/scene` 差量提交**，02 不新增保存通道。

> **"受控"三条边界**：① 只动 `Rack.{X,Y,RotationZ}` 与 `Marker`，不动库位（库位坐标保存时由 00 §6.2 派生重算）；② 不做自由顶点编辑（Zone 多边形 v1 不在画布徒手拉点，仅整体平移）；③ 碰撞/越界**只提示不阻断**——商用底座允许临时不规整，最终规整性由保存校验与 03/04 把关。

---

## 第2章 交互总架构

### 2.1 三层结构
```
cp6.web/src/space-editor/interact/
  InteractionManager.ts   总调度：绑定 Konva 舞台事件 → 分发给当前工具
  tools/                  工具状态机（互斥，同一时刻一个 active tool）
    SelectTool.ts           选择（点选/框选）—— 默认工具
    DragTool.ts             拖拽移动（在 SelectTool 选中后按住拖触发）
    RotateTool.ts           旋转（Transformer 旋转手柄）
    MarkerTool.ts           打点（点击落 Marker）
  snap/SnapEngine.ts      捕捉求解：候选吸附点/线 → 最近命中
  collide/CollisionHint.ts碰撞与越界检测（实时，仅着色提示）
cp6.web/src/space-editor/command/
  Command.ts              接口：do() / undo() / merge?()（同类可合并）
  CommandStack.ts         undo/redo 双栈 + 事务分组 + 容量上限
  commands/               MoveRackCmd / RotateRackCmd / AddMarkerCmd / MoveMarkerCmd / EditMarkerCmd / DeleteCmd / BatchCmd
```

### 2.2 工具状态机（互斥）
- 同一时刻只有一个 active tool；工具栏切换或快捷键切换。
- 默认 `SelectTool`；选中对象后按下并拖动自动进入 `DragTool`，松开回到 `SelectTool`。
- `RotateTool` 由选中后出现的 Konva `Transformer` 旋转手柄驱动；`MarkerTool` 由工具栏"打点"按钮显式进入，`Esc` 退出回 `SelectTool`。

```
        ┌───────── SelectTool (默认) ─────────┐
   点空白=清选 / 点对象=选 / 拖空白=框选        │
        │ 在选中对象上按下并移动 → DragTool      │
        │ 抓 Transformer 旋转手柄 → RotateTool   │
        │ 工具栏"打点" → MarkerTool             │
        └───── 操作完成/Esc → 回 SelectTool ─────┘
```

### 2.3 一次交互 = 一个 Command
**所有几何/标注改动都不直接 mutate Pinia，而是构造一个 Command 交给 `CommandStack.exec()`**：
```ts
interface Command {
  label: string                 // 用于消息/调试，如 "移动货架 ×3"
  do(scene: EditorScene): void  // 应用变更（写 Pinia + 打 dirty）
  undo(scene: EditorScene): void// 逆操作
  merge?(next: Command): boolean// 同类连续操作可合并（如连续微拖）
}
```
- `exec(cmd)`：`cmd.do()` → 压 undo 栈 → 清空 redo 栈；尝试与栈顶 `merge`（节流连续拖拽为一条）。
- 这是 02 的**地基**：选择/捕捉/碰撞提示是"读"与"预览"，不入栈；只有**落定的位姿/标注变更**入栈（详见第9章）。

---

## 第3章 选择系统

### 3.1 选中集
```ts
// useEditorStore 扩展
selection: { kind: 'rack'|'marker'|'zone'|'mixed', ids: Set<string> }
```
- 选中集是**交互态**（不入 Command 栈、不计 dirty）；它驱动 Transformer 包围盒、属性面板、拖拽/旋转的作用域。
- 货架是主操作对象；Zone/Aisle v1 仅支持**整体选中平移**（不编辑顶点）；Marker 可单独选。

### 3.2 点选与加减选
| 操作 | 行为 |
|---|---|
| 单击对象 | 清空选中集 → 选中该对象 |
| `Ctrl`+单击 | 切换该对象的选中态（加选/减选） |
| `Shift`+单击 | 追加选中（不减） |
| 单击空白 | 清空选中集 |
| `Ctrl`+A | 全选当前图层可选对象 |

### 3.3 框选（橡皮筋）
- 在空白处按下拖动 → 画半透明矩形 `selectionRect`；松开时把**包围盒与矩形相交**的可选对象纳入选中集。
- `Shift`+框选 = 追加；`Alt`+框选 = 从选中集移除。
- 框选只命中**当前可选图层**（默认 RackLayer + MarkerLayer；可在图层面板切换），避免误选底图/网格。
- 性能：货架级（千级）用包围盒矩形相交即可；库位不参与框选（01 已定库位不画在 2D）。

> 选择系统不产生 Command（纯交互态）。它的产物——选中集——是后续拖拽/旋转/删除 Command 的**作用域输入**。

---

## 第4章 拖拽移动

### 4.1 触发与预览
- 在选中对象上按下并移动进入 `DragTool`；拖动期间实时更新**幽灵位置**（Konva 节点跟随，但**尚未写 Pinia**），并跑捕捉（第6章）与碰撞提示（第8章）。
- 松开鼠标 → 用**起点位姿 → 终点位姿**构造一个 `MoveRackCmd`（多选则 `BatchCmd` 包多个），`exec` 入栈。

### 4.2 多选整体平移
- 多选拖拽 = 对选中集每个对象施加同一 `(dx, dy)` 世界位移；捕捉以**整组包围盒**或**主对象**为吸附参照（可配，默认主对象）。
- 一次多选拖拽 = **一个 `BatchCmd`**（undo 一次回退整组），不是 N 个独立 Command。

### 4.3 MoveRackCmd（核心 Command）
```ts
class MoveRackCmd implements Command {
  constructor(private id: string, private from: XY, private to: XY) {}
  label = '移动货架'
  do(s){ const r = s.rackById(this.id); r.x = this.to.x; r.y = this.to.y; markDirty(s, this.id) }
  undo(s){ const r = s.rackById(this.id); r.x = this.from.x; r.y = this.from.y; markDirty(s, this.id) }
  merge(next){ // 同一货架连续微拖合并为一条（只更新终点）
    if (next instanceof MoveRackCmd && next.id === this.id){ this.to = next.to; return true }
    return false
  }
}
```
- **只改货架 X/Y**，库位坐标不在此刻重算（库位是派生：保存时服务端 00 §6.2 `RecalcRackLocations` 统一重算；画布内库位本就不逐个显示）。
- `markDirty` 把货架 id 计入 01 第6.1 的 dirty 集，保存时随 `/scene` 提交。

### 4.4 拖拽约束
| 约束 | 处理 |
|---|---|
| 拖出 Floor 画布边界 | 软约束：允许但 W-SPACE-201 越界提示（最终归属仍按 Zone） |
| 拖动中按 `Esc` | 取消本次拖拽，回起点，不入栈 |
| 拖动步长 | 默认连续；按住 `Ctrl` 临时关捕捉（自由微调），不按则吸附（第6章） |

---

## 第5章 旋转

### 5.1 Transformer 旋转手柄
- 选中货架后挂 Konva `Transformer`（仅启用 `rotateEnabled`，**禁用缩放手柄**——货架尺寸由模板参数 `cellW/H/D × cols/levels/depth` 决定，不在画布拉伸）。
- 拖旋转手柄 → 实时改 `RotationZ`（偏航角，绕货架**锚点**，与 00 §6 角点锚 + 绕角转一致）；松开构造 `RotateRackCmd`。

### 5.2 锚点与角度
- 旋转锚点 = 货架定义的锚（00 §6.1 角点锚）；保证旋转后 `computeAbs` 库位坐标自洽。
- **角度吸附**：默认吸附到 0/15/30/45/90° 的最近档（阈值 ±3°）；按住 `Ctrl` 关吸附做任意角。
- 多选旋转：v1 仅支持**各自绕自身锚点**同步旋转同一增量（不做绕共同中心公转，留 P2+）。

### 5.3 RotateRackCmd
```ts
class RotateRackCmd implements Command {
  constructor(private id: string, private fromDeg: number, private toDeg: number) {}
  label = '旋转货架'
  do(s){ s.rackById(this.id).rotationZ = norm360(this.toDeg); markDirty(s, this.id) }
  undo(s){ s.rackById(this.id).rotationZ = norm360(this.fromDeg); markDirty(s, this.id) }
  merge(next){ if (next instanceof RotateRackCmd && next.id===this.id){ this.toDeg=next.toDeg; return true } return false }
}
```
> 旋转同样**不在画布重算库位坐标**——`RotationZ` 一变，库位绝对坐标随之改，但这是保存时 00 §6.2 的服务端职责。画布只需把货架矩形按新角度重绘（俯视矩形绕锚旋转），属性面板显示新角度。

---

## 第6章 捕捉对齐（受控的核心）

捕捉是"受控自由布局"区别于"自由乱画"的关键：拖拽/旋转过程中实时把落点吸附到有意义的参照，保证生成的骨架在精修后仍**对齐、规整**。

### 6.1 捕捉候选（SnapEngine）
| 类型 | 吸附目标 | 说明 |
|---|---|---|
| 网格吸附 | `snapStep` 网格交点（01 §3.3，默认 100/1000mm 两级） | 最基础，随 zoom 选档 |
| 货架边吸附 | 邻近货架的边/角 | 让相邻货架贴齐成排，间隙等于 `rackGap` |
| 巷道中心线吸附 | 邻近 `Space_Aisle` 中心线（00 §5） | 货架排沿巷道对齐 |
| 同行对齐 | 选中集内/邻近货架的同一 X 或 Y | 拖动时显示对齐辅助线（红色 guide line） |

### 6.2 求解
```
拖拽中每帧：
  cand = SnapEngine.candidates(movingBBox, sceneIndex, zoom)   // 收集附近吸附点/线
  best = argmin(distance)   且 distance < snapThreshold(px→mm 按 zoom 换算)
  if best: ghost.pos = snapTo(best); 画吸附高亮（点亮目标边/线）
  else:    ghost.pos = rawPos
```
- 阈值用**屏幕像素**定义（如 8px），换算到世界 mm 随 zoom 变化——缩得越大吸附范围越宽，符合直觉。
- 空间索引：货架/巷道用简单网格分桶（bucket by cell）做邻近查询，避免每帧全表扫描（千级对象足够）。

### 6.3 等距分布与对齐成行（批操作）
选中 ≥3 个货架时，工具栏提供：
| 操作 | 行为 | Command |
|---|---|---|
| 左/右/顶/底对齐 | 选中集对齐到极值边 | `BatchCmd(MoveRackCmd[])` |
| 水平/垂直等距分布 | 首尾固定，中间均匀分布 | `BatchCmd(MoveRackCmd[])` |
| 设为同一旋转角 | 统一 RotationZ | `BatchCmd(RotateRackCmd[])` |

> 这些是"把成批生成后略歪的货架一键规整"的生产力操作——正是受控自由布局的价值：**模板生成铺骨架，对齐分布做精修**，全程不徒手画。

### 6.4 关捕捉
按住 `Ctrl` 临时关闭捕捉做自由微调（第4/5章已述）；图层面板可全局开关各类捕捉与 guide line。

---

## 第7章 打点标注

`Space_Marker`（00 §4.8 / README 数据模型）。02 提供其画布交互。

### 7.1 Marker 字段（消费 00）
- `Id / FloorId / X / Y / Z / Type / Text`（Type 如：柱子/障碍/说明/作业点/危险）。
- Marker 是**纯标注**，不参与库位/编码/发布；仅供 2D 画布与 3D 渲染（05）展示。

### 7.2 交互
| 操作 | 行为 | Command |
|---|---|---|
| 工具栏"打点" → 画布点击 | 在点击世界坐标落一个 Marker（弹层选 Type、填 Text） | `AddMarkerCmd` |
| 拖动已有 Marker | 改 X/Y（同样走捕捉） | `MoveMarkerCmd` |
| 双击 Marker | 编辑 Text/Type | `EditMarkerCmd` |
| 选中 + `Delete` | 删除 | `DeleteCmd`（含 Marker 快照以便 undo 还原） |

```ts
class AddMarkerCmd implements Command {
  constructor(private m: MarkerVO) {}
  label = '新增打点'
  do(s){ s.markers.push(this.m); markDirty(s, this.m.id) }
  undo(s){ s.markers = s.markers.filter(x=>x.id!==this.m.id); markDirtyDelete(s, this.m.id) }
}
```
> Marker 的删除/新增 undo 必须保留对象**完整快照**（删了能原样恢复），所以 `DeleteCmd` 在 `do` 前先 deep-clone 被删对象存于命令内。

---

## 第8章 碰撞检测与越界提示

### 8.1 实时、不阻断
- 拖拽/旋转/生成（01 §5.3）过程中实时检测，**只着色提示不阻断**：商用底座允许临时重叠/越界，最终规整由保存校验与 03/04 把关。
| 提示 | 判定 | 视觉 |
|---|---|---|
| 货架重叠 | 两货架俯视包围盒（含旋转 OBB）相交 | 重叠货架描红边 + W-SPACE-202 |
| 超出 Zone | 货架包围盒不完全落在所属 Zone 多边形内 | 货架描黄边 + W-SPACE-201 |
| Marker 落在货架上 | （允许）无提示 | — |

### 8.2 OBB 相交（带旋转）
- 货架有 `RotationZ`，用**有向包围盒（OBB）** + 分离轴定理（SAT）判相交，而非轴对齐 AABB（旋转后 AABB 会误判）。
- 检测范围：仅对**移动/旋转中对象 vs 邻近桶内对象**做 SAT（空间索引同 6.2），避免全场两两比较。

### 8.3 提示汇总
- 画布右下角显示当前层"重叠 N 处 / 越界 M 处"的徽标；点击可逐个定位（相机/视口居中到问题货架）。
- 保存时（01 §6.2）把越界/重叠作为**警告**回传，不阻断保存（草稿允许带瑕疵）；真正阻断在 03 编码或 04 发布前的规整校验。

---

## 第9章 撤销 / 重做（Command 模式双栈）

### 9.1 CommandStack
```ts
class CommandStack {
  private undoStack: Command[] = []
  private redoStack: Command[] = []
  private cap = 100                     // 容量上限，超出丢最旧
  exec(cmd: Command, scene: EditorScene){
    cmd.do(scene)
    const top = this.undoStack.at(-1)
    if (top && top.merge?.(cmd)) { /* 合并进栈顶，不新增 */ }
    else { this.undoStack.push(cmd); if (this.undoStack.length>this.cap) this.undoStack.shift() }
    this.redoStack.length = 0           // 新操作清空 redo
  }
  undo(scene){ const c=this.undoStack.pop(); if(!c) return; c.undo(scene); this.redoStack.push(c) }
  redo(scene){ const c=this.redoStack.pop(); if(!c) return; c.do(scene);   this.undoStack.push(c) }
}
```

### 9.2 事务分组（BatchCmd）
- 多选拖拽/旋转、对齐分布等"一次手势改多个对象" = 一个 `BatchCmd { children: Command[] }`：`do` 顺序执行子命令，`undo` 逆序执行——保证一次 undo 整组回退。
```ts
class BatchCmd implements Command {
  constructor(private children: Command[], public label='批量操作') {}
  do(s){ for (const c of this.children) c.do(s) }
  undo(s){ for (let i=this.children.length-1;i>=0;i--) this.children[i].undo(s) }
}
```

### 9.3 合并策略（merge）
- 连续微拖同一货架 → `MoveRackCmd.merge` 把多帧合一条（避免 undo 要点几十下）。
- 合并的判据：**同类型 + 同目标 id + 时间相邻**（由 InteractionManager 在一次"按下→松开"手势内只 exec 一条终态命令，已天然合并；merge 主要兜底节流场景）。

### 9.4 入栈与不入栈
| 入栈（可撤销） | 不入栈（交互态/读操作） |
|---|---|
| 移动货架/Marker、旋转货架 | 选择/框选/加减选 |
| 新增/删除/改文本 Marker | 缩放/平移视口（zoom/pan） |
| 对齐/等距分布（BatchCmd） | 捕捉高亮、碰撞着色、guide line |
| 删除货架（含库位快照） | 图层显隐、工具切换 |

### 9.5 与保存/并发的关系
- undo/redo 操作的是**前端 Pinia 场景 + dirty 集**；保存（01 §6.2）后**不清空** Command 栈（仍可继续 undo，但 undo 后需再次保存才落库）。
- v1 不做跨会话 undo 持久化；刷新页面栈清空（与草稿保存解耦）。
- 多人协同冲突仍靠 00 `RowVersion` 乐观锁（01 §6.3），与本地 undo 栈无关。

### 9.6 快捷键
| 键 | 动作 |
|---|---|
| `Ctrl`+Z / `Ctrl`+Y（或 `Ctrl`+`Shift`+Z）| 撤销 / 重做 |
| `Delete` | 删除选中集 |
| `Ctrl`+A | 全选 |
| `Esc` | 取消当前手势 / 退出 MarkerTool / 清选 |
| `Ctrl`（按住）| 临时关捕捉 |
| `Shift` / `Alt`（框选时）| 追加 / 移除选择 |

---

## 第10章 与 00/01 的状态同步与保存

### 10.1 谁动了什么
- 02 只写 `Rack.{X,Y,RotationZ}` 与 `Marker.*`；**不写 Location**。
- 每个落定 Command → `markDirty`/`markDirtyDelete` 更新 01 第6.1 的 dirty 集（新增/修改/删除三类）。

### 10.2 保存（完全复用 01 §6.2）
```
POST /api/space/floor/{id}/scene  { racks(dirty), markers(dirty), deletes, ... }
  → 事务内 upsert/删除
  → 货架位姿变更（X/Y/RotationZ）触发 00 §6.2 RecalcRackLocations：服务端按新位姿 + computeAbs 重算该货架所有库位 AbsXYZ
  → 校验 RowVersion（00 乐观锁），冲突 E-SPACE-009
```
- **关键**：库位坐标的真相在**保存时服务端重算**，前端 02 全程不碰库位坐标——这让"画布只持货架、库位懒展开"（01 §2.3）与"undo 只回退货架位姿"自洽，避免前端维护万级库位的 undo 历史。
- 纯几何编辑（含 02 的所有操作）**不触发发布**（00 §6.2 表 / D4：冻编码不冻几何）；发布是 04 章独立动作。

### 10.3 库位坐标的"懒一致"
- 画布内若展开了某货架的库位（01 §2.3 选中懒展开），其 VO 的 AbsXYZ 在 02 拖动后会**短暂过期**（前端不重算）；可选地前端用 computeAbs 即时刷新被展开货架的库位预览，但**权威值以保存后服务端返回为准**。

---

## 第11章 API 接口（增量）

02 **不新增写接口**——保存复用 01 的 `/floor/{id}/scene`。仅交互态、Command 栈、捕捉/碰撞全在前端。

| 端点 | 方法 | 说明 | 来源 |
|---|---|---|---|
| `/floor/{id}/scene` | GET/POST | 整层场景读 / 差量保存（含 02 的 Rack 位姿、Marker 变更） | 复用 01 §6.2 / 第9章 |

> 设计要点：把 02 做成**纯前端交互 + 复用 01 保存通道**，是为了让"建模框架（01）+ 精修交互（02）"对后端是同一个"整层差量保存"契约——后端只认最终的 Rack/Marker 状态，不关心前端用了拖拽还是模板生成。

---

## 第12章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| W-SPACE-201 | Warn | 货架超出库区范围 | 拖拽/旋转后包围盒越出所属 Zone（不阻断） |
| W-SPACE-202 | Warn | 货架与既有货架重叠 | OBB 相交（不阻断） |
| I-SPACE-201 | Info | 已对齐 / 已等距分布 N 个货架 | 对齐/分布批操作完成 |
| I-SPACE-202 | Info | 已撤销：{label} ／ 已重做：{label} | undo/redo |
| W-SPACE-203 | Warn | 选中对象不可旋转/移动（如 Zone 顶点编辑未开放） | 对不支持对象施加交互 |
| E-SPACE-009 | Error | 数据已被他人修改，请刷新重试 | 保存 RowVersion 冲突（00 章，复用 01） |

---

## 第13章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 00 数据模型 | 写 Rack `{X,Y,RotationZ}`、Marker；保存触发 §6.2 RecalcRackLocations；OBB/锚点用 §6 几何；RowVersion 乐观锁 |
| ← 01 编辑器框架 | 共用 `SceneStage` 画布 + 图层 + `useEditorStore` 场景对象图 + dirty 集 + `/scene` 保存；01 管"生成"，02 管"精修" |
| → 03 编码引擎 | 02 调正几何后，03 才按规整布局生成/重排 `LocationCode`；02 本身不碰编码 |
| → 04 发布契约 | 02 的纯几何编辑不发布（D4）；发布是 04 独立闸门动作 |
| → 05/06 渲染 | 保存后的位姿/Marker 供 3D 渲染（05）、按编码定位（06） |
| → PUB 权限 | "精修/打点/删除"接 PUB 功能权限；场景读接数据权限 |
| 多租户 | 与 01 一致，场景/Marker 按 TenantId 隔离 |

---

## 自检
- [ ] 01 和 02 的分工边界？02 到底动哪些字段、绝不动哪些（库位坐标/编码归谁）？
- [ ] 为什么"所有几何/标注改动都走 Command、不直接 mutate Pinia"？选择/捕捉/碰撞为什么不入栈？
- [ ] 多选拖拽为什么是一个 BatchCmd 而非 N 个 Command？undo 一次回退什么？
- [ ] 捕捉吸附阈值为什么用屏幕像素定义、换算到世界 mm？"受控"体现在哪三条边界？
- [ ] 旋转/拖拽后库位坐标何时、由谁重算？为什么前端不维护库位 undo 历史？
- [ ] 碰撞/越界为什么只提示不阻断？真正的规整校验闸门在哪几章？
- [ ] 02 为什么不新增保存接口、复用 01 的 `/scene`？对后端意味着什么？

---

*实现：新建 `cp6.web/src/space-editor/interact/*`（InteractionManager + tools + SnapEngine + CollisionHint）+ `cp6.web/src/space-editor/command/*`（Command/CommandStack/commands）；复用 01 的 `useEditorStore` 与 `/floor/{id}/scene`。配套 xlsx（交互矩阵 / 快捷键表 / Command 清单 / 捕捉规则表 / 碰撞判定线框）见同名 `.xlsx`。*
