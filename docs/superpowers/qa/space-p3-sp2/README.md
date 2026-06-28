# Space P3 · SP2 编辑器交互运行态收尾 — QA 证据

日期：2026-06-28 | 分支：`feat/space-p3-hardening` | worktree：`D:\CP6-space-backend`
范围：lasso OBB / 旋转中心枢轴 / 角度读数 / 幽灵跟随（前端 Konva 2D，零后端）

---

## 1. 静态门（全部已验证 ✅）

| 门 | 命令 | 结果 |
| --- | --- | --- |
| 类型 | `cp6.web && npm run type-check` | **0 error**（vue-tsc --build） |
| 单测 | `cp6.web && npm run test` | **180 passed / 32 files**（含 SP2 新增 `lassoHit`6 / `rotateGeometry`7 / `arrayFootprint`3 + `commands` 更新） |
| 构建 | `cp6.web && npm run build` | **成功**（2530 modules；仅 pre-existing chunk-size 警告） |

新增纯逻辑单测覆盖了交付的全部可测核心：
- `obbIntersectsRect`：轴对齐/远离/包含/**45° 旋转货架 AABB 误判但 OBB 正确**/紧贴分离。
- `rotateAboutCenter`：0→90° 锚点位移、**几何中心旋转前后不变量**、角度不变锚点不变。
- `snapAngle`：15° 阈内吸附 / 阈外保持 / 358° 环绕 / 负角规范化。
- `RotateRackCmd`：do/undo 三值（x/y/rotationZ）齐改齐还原。
- `arrayFootprint`：1×1 / 含间隙累加 / 与 `genZoneArray` 末架终点一致。

## 2. 终审（独立子代理，对抗式 ✅）

全分支 8 实现 commit 对抗式 review，**Verdict: APPROVE_WITH_NITS，零 blocking bug**。独立核实：
- 旋转：Konva Transformer 单节点枢轴=包围盒中心=货架几何中心（消跳变前提成立）；`-node.rotation()` 与渲染 `rotation:-rotationZ` 自洽；`rotateAboutCenter` 与 `rackCorners`/`computeAbs` 同坐标约定；snapped 提交后 re-render 位置匹配 Transformer 落点（仅 ≤3° settle）；事件 on/off 平衡、无 Text/监听泄漏。
- lasso：屏幕矩形两角 `screenToWorld` 取 min/max（Y 翻转下正确）；SAT 4 轴；tiny-drag 守卫保留；无重复 `const scene`。
- 幽灵：监听 `mousemove.place` 进绑出解、`stageRef.destroy()` 兜底；`snapWorld` 禁用态可调；`showFootprintGhost` 与 lasso 不同时占 ghost 层。

4 个 nit 中 2 个已修（`2741def`：去冗余 spread + `bindPlacementGhost` 幂等防重复监听）；余 2 个为纯装饰（角度读数 3 位数偏移、`onBeforeUnmount` 对称性），不影响功能。

## 3. 运行态 smoke（gstack headless，隔离栈 ✅ 部分）

隔离栈：后端 5177（`appsettings.Local.json`→`CP6DB_SpaceQA`）+ 我方 vite **5174**（`/api`→5177，**未碰 5173/`D:\CP6`**）+ gstack headless Chromium，admin/123456，路由 `/space/editor/5C92E6A8-…`。

**已确认（截图 `editor-render-smoke.png`）**：
- 登录成功（`cp6_authed`），编辑器路由 200。
- **我方 SP2 模块全部被 vite 提供**（`FloorEditor.vue`/`SceneStage.ts`/`InteractionManager.ts`/… 均 200）。
- `GET /api/space/floor/{id}/scene` → 200（2006B 有数据）。
- **画布渲染场景**：库区蓝多边形 + 演示货架（含格口网格线）渲染，工具栏齐（选择/拖拽/旋转/打点/取消/重做/选择库区/保存/导出/导入/反向建模）。
- **零 console error（编辑器加载链路）**：仅 pre-existing 良性告警（intlify object-flatten / Vue Router next() deprecation / NotificationBell ElBadge prop / 登录前瞬态 401·400）。切换「旋转」工具按钮无异常。
- **结论**：所有结构性改动（RotateTool 重写 / SelectTool 改 / SceneStage·InteractionManager 新增 / FloorEditor 接线）在真实运行时挂载并运行，无崩溃、无报错 —— **回归级 smoke 通过**。

**headless 限制（未在本轮 headless 完成，留监督视觉确认）**：
- 合成 canvas 鼠标拖拽精度不足（Konva 命中靠指针位置；合成 `about:blank` 误导航 + 系统资源紧张 `ERR_INSUFFICIENT_RESOURCES`/fork failure），无法可靠验证「旋转拖拽手感」。
- 演示库**模板库为空**（`/api/space/template` 返 `[]`），UI 无法触发 placement → 幽灵跟随 UI 未跑（其纯逻辑 `arrayFootprint` 已单测、接线已终审）。

## 4. 监督视觉验收脚本（用户回来后 ~5 分钟跑）

重启隔离栈（两条，worktree 内）：
```
# 后端（终端1）
cd /d/CP6-space-backend && ASPNETCORE_ENVIRONMENT=Development dotnet run --project CP6.WebApi --urls http://localhost:5177
# 前端（终端2）—— 若 5173 被占会自动用 5174
cd /d/CP6-space-backend/cp6.web && npm run dev
```
浏览器登录 admin/123456 → `http://localhost:<vite端口>/space/editor/5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F`

**① 旋转（核心）**：选「旋转」工具 → 点演示货架 → 抓 Transformer 旋转手柄拖动。期望：货架**绕几何中心原地转**、松手**无大跳变**（仅 ≤3° 吸附 settle）；顶部角度读数实时；接近 15° 倍数读数变绿；按住 Ctrl 拖动自由角不吸附；Ctrl+Z 回原位姿。

**③ lasso**：先把货架旋转 ~45°（或用既有角度）；切「选择」工具，用一个**刚擦过其 AABB 角、不碰真实 OBB** 的橡皮筋框 → **不选中**（旧 AABB 会误选）；再用真实覆盖本体的框 → 选中。

**④ 幽灵**：需先在模板库建至少 1 个模板（演示库当前为空）。建后选模板设阵列参数→「点击画布放置」：外包矩形幽灵**跟随光标**；未选库区/出库区 → **琥珀**；落在库区内 → **绿**；单击落点正确；Esc 取消清幽灵。

固化：跑完把截图补进本目录。

## 5. 提交链（本地，未 push）

`b79c4a3` spec → `8402357` plan → `8f57b0f`/`0d7757d`（③）→ `8c167d5`/`f6f93d8`/`ff4b829`（①）→ `4966088`/`e922879`/`3997805`（④）→ `2741def`（终审 nit）。**push 留用户自跑**（会话权限拦 git push）。
