# E02-S05 CAD 图层映射方案开发切片

日期：2026-08-03

## 交付结论

CP6 已在 E02-S04 集成基线 `f4b596f0` 上完成 E02-S05 开发切片，功能提交为 `2736427c`，证据提交为 `29118c19`，并通过 no-ff 提交 `b6d58a1e` 集成到 `integration/space-v1-20260730`。E02-S04 清单现在可以套用不可变、哈希封装的 System/Tenant 映射方案，生成确定性的图层/块映射预览和跨楼层复用键；本切片不创建语义元素，也不写 Draft。

这不是正式 E02-S05 验收。实现继续使用纯合成 DXF 和内存合同，没有增加生产映射数据库、WebApi、权限 UI 或授权原生 DWG 能力。

## 本次实现

1. 新增 `SpaceCadMappingProfileV1`：方案 ID、版本、名称、System/Tenant 作用域、租户、启用状态、复制来源、规则快照和 Definition SHA-256 全部进入不可变合同。
2. System 方案无租户归属，可供所有租户读取但不能通过租户版本方法修改；`CreateTenantCopy` 生成带 System 基线的租户私有 v1，后续修改必须 `CreateNextTenantVersion`，不原地覆盖旧版本。租户方案跨租户使用抛出拒绝。
3. 规则来源分为 Layer 和 Block，支持 Exact、Glob 和有长度/100ms/NonBacktracking 约束的安全 Regex；Block 规则可增加属性键值条件。规则冻结目标类型、子类型、几何解释、默认高度/厚度、置信度、优先级和必需标记。
4. 解析优先级为：逐层 Override > 规则 Priority > Exact/Glob/Regex 特异性。相同优先级和特异性的多规则命中产生 Blocking 冲突，不按文件顺序猜测；必需来源缺失或只有空图层同样 Blocking。
5. 所有空图层和未映射来源仍进入预览。非空未映射来源为 Warning，空来源为 Info；显式 Ignore 作为可追溯 Override，不伪装成映射成功。
6. Preview 绑定 Tenant、Profile ID/Version/Definition SHA、Source SHA、Inventory SHA、源结构 SHA、Layer Override、Reuse Key 和 Preview SHA。复用键包含来源文件、源结构、方案版本和覆盖，但排除 Floor/坐标 Transform，因此同一 CAD 分配到不同楼层仍可复用同一映射选择。
7. 新增 `seal-dev-mapping-profile` 与 `preview-dev-mapping` 命令；Blocking 预览仍写证据并返回退出码 3。
8. 新增 `mapping-profile.schema.json`、`mapping-preview.schema.json` 和 11 规则合成标准仓方案草案。无 Migration、WebApi、Draft 仓储、供应商 SDK 或外部 AI Provider。

## 样例 13 连续证据

输入：`docs/space/acceptance/development-v2.0.0/seeds/13-automated-warehouse.dxf`

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`；
- Inventory SHA-256：`634329583747825b5c40c37402e03cdfa046c6f3e54f3d0ae2a4eb8faa9697a9`；
- Profile Definition SHA-256：`732eef8a1014e35428d427c639dce4936936087a08c484b23d67275c08de59d1`；
- Source Structure SHA-256：`9636bd729f2d79c0eb5f47be4f57ca2217cf9398c79375b94f1ca46a390911ab`；
- Reuse Key SHA-256：`014cdc7566915ee4cdf5336e3e4e74e5fea6dbf4f723c0551ed2889dca1c879b`；
- Preview SHA-256：`98a0a3153af112563a3075dd9ee9fff1f113d122d22f03b89b399ba04d8009ca`；
- 15 个图层中 10 mapped / 5 unmapped，1 个块 mapped；21 个图层对象和 8 个块引用进入映射候选；4 Info、1 Warning、0 Blocking，`ReadyForSemanticParsing=true`。

该预览只说明规则选择和来源覆盖，不代表已经生成 Wall/Rack 等规范几何；语义几何与来源置信度属于 E02-S06/S07。

## 门禁

- E02-S05 聚焦：12 passed / 0 failed / 0 skipped；
- 20/20 合成 DXF 完成转换、坐标确认、清单和标准 11 规则映射预览；全部无 Blocking；
- CAD 实验工具完整测试：23 passed / 0 failed / 0 skipped；
- Space Unit 完整测试：316 passed / 0 failed / 0 skipped；
- 完整 solution Release 非增量构建：0 error / 10 条既有 warning；
- Mapping Profile/Preview Schema、草案/封装方案/样例 Preview JSON、CLI 和 `git diff --check` 通过。

## 正式边界与下一步

正式 E02-S05 仍等待：

- E02-S01 授权原生 DWG/DXF 适配器、冻结隔离 Worker 和正式黄金集；
- E02-S04 正式持久化清单与生产分页/压力证据；
- System 方案管理与租户复制/版本的数据库、并发控制、只读约束和 Migration；
- 同租户 Source/ModelVersion/Floor/Profile 校验、WebApi 权限、审计、幂等和 UI；
- 真实设计院图层、动态/匿名/嵌套块、ByLayer/ByBlock 属性和复杂正则的正式方案验收。

E05-S01 已完成。在等待 CAD 外部解阻包期间，可继续 E02-S06 开发侧基础语义解析器：消费 Prepared CAD IR、E02-S04 Inventory 与本 Preview，输出只读临时提案及完整来源引用；不得直接写 Draft 或把开发结果标记为正式 E02-S06。
