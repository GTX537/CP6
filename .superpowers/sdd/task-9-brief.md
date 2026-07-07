### Task 9: 端到端联调验证（真库迁移 + live QA 冒烟）

**Files:**
- 无代码变更（验证任务）；如冒烟暴露缺陷 → 修复 + 补测试后单独 commit

**Interfaces:**
- Consumes: Task 1–8 全部产物
- Produces: 波1 DoD 证据（迁移应用成功 + 发布/停用真链路落 T_WmsBin 的实证）

- [ ] **Step 1: 应用迁移到开发库**

Run: `dotnet ef database update --project CP6.Core --startup-project CP6.WebApi`
Expected: `Applying migration '..._SpaceWave1WmsBin'` → Done。

- [ ] **Step 2: 起后端 + 冒烟发布链路**

启动 `CP6.WebApi`（项目惯例命令/容器栈见记忆 new-env-setup-2026-07）。用既有 QA 账号（admin / 123456）拿 token 后依次：

1. `POST /api/space/floor/{floorId}/publish`（挑一个有已生码草稿库位的楼层；无则先 `POST /api/space/floor/{id}/generate-codes`）
2. 查库：`SELECT Id, LocationCode, WarehouseCd, Version, IsActive FROM T_WmsBin` → 应有新行，`WarehouseCd` = site 映射值或 SiteCode
3. 重复步骤 1 再发布同层 → 无新增草稿时返回 0；对同批库位人为重投事件（`GET /api/space/publish/events` 确认事件 `SUCCESS`，无重复批次行）
4. `PUT /api/space/location/{id}/deactivate`（挑无库存库位）→ `T_WmsBin.IsActive=0`、Space `Status=2`
5. 给某库位对应 `(WarehouseCd, LocationCd)` 插一条 `T_Stock`（`PhysicalQty=5`）再停用 → 返回 W-SPACE-404，`Status` 保持 1
6. 带 `{"zoneId": "<某库区>"}` 请求体发布 → 只有该库区库位落 T_WmsBin（H5）
7. `POST /api/space/floor/{id}/scene` 携带把已发布库位 `status:0` 的载荷 → 保存后查库 `Status` 仍为 1（H1）

Expected: 7 步全符合预期；`T_IntegrationEvent` 中 SPACE 事件状态 SUCCESS 且重试计数无异常增长。

- [ ] **Step 3: 全量测试基线复核 + 收尾**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj`
Expected: 全 PASS，通过数 ≥ 既有基线 + 本波新增 19 个（Task 2×2 + Task 3×9 + Task 4×3 + Task 5×1 + Task 6×1 + Task 7×1 + Task 8×2）。

若冒烟无缺陷，本 Task 无 commit；有缺陷则修复+测试后：

```bash
git commit -m "fix(space): 波1联调修复——<具体问题>

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## 自检记录（写计划时已核；2026-07-05 评审修订后更新）

- **Spec 覆盖**：ch04 v1.1 五项补丁——① T_WmsBin（Task 1/3）② WarehouseCd 映射（Task 1/2）③ publishedBy 溯源落 `T_WmsBin.LastPublishedBy`（Task 3，P1 最低交付口径；`PersistEventAsync` 加 userId 参数属基建改造，本波不动共享基类）④ 停用同步 RPC（Task 4）⑤ 逐项结果 schema（Task 3）。盘点三隐患——发布非原子（Task 5 事务）、重试重复落事件（Task 5 persistEvent）、发布闸门 TOCTOU（Task 5 事务包裹闸门→提交 + 既有过滤唯一索引兜底，剩余窗口由 DB 约束拦截）。
- **评审修订覆盖（2026-07-05 用户批准）**：H1 场景保存状态机后门（Task 8）；H5 zoneId 假参数 + 闸门收窄（Task 6）；H6 停用乱序孤儿 Bin → 墓碑机制，**这是对契约 §5.1"无 bin 跳过"的有意修正**（Task 3 消费端 + Task 4 同步 RPC 两处）；H7 库存校验仓维度（Task 7）；H8 并发冲突 409（Task 7）。
- **明确不做（划出波1）**：H2/H3/H4 编辑动作↔发布联动（缩格幽灵库位/删巷道护栏/改挂 re-publish + 库位删除通道）→ **波 1.5「发布触发矩阵兑现」计划，波 1 完成后基于新代码基线编写**；`/reconcile` 采纳对账端点（契约 §8.2，随波3）；H9 采纳内存去重优化（随波3）；删除护栏 `?mode=deactivate|rehome`（契约 §7.2，并入波1.5）；错误码 BizException 化（波4）；SpaceSqlIntegrationTests 真库 CI（波5）。
- **类型一致性**：`LocationPublishService` 6 参构造在 Task 4 定义、DI 与测试帮手同步；`persistEvent` 带默认值不破坏既有调用；`WmsDeactivateRequest/Result` 仅 Task 4 内定义与消费；`ResolveWarehouseCdAsync` Task 2 定义、Task 4/7 复用；`PrecheckAsync`/`GetStockQtyAsync` 均加默认参数，既有调用点编译不破坏；Task 6/7 对 `DeactivateAsync`/`PublishFloorAsync` 的修改基于 Task 4/5 之后的代码形态（执行顺序 1→9 严格串行）。
