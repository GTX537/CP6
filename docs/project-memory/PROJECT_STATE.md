# 项目当前状态

最后更新：2026-08-04

## E02-S07 CAD 语义证据与问题定位开发切片（2026-08-04）

- 在 E02-S06 集成基线 `68d59562` 上完成功能提交 `19b6c443`、证据提交 `2eee2081`，并以 no-ff 提交 `c792ea8c` 集成到 `integration/space-v1-20260730`：每个只读语义提案现在都带 SourceRef、采用规则、置信度分段、独立证据哈希和整数毫米画布位置；Mapping/Semantic 问题形成稳定、可筛选的空间索引。
- 诊断工件绑定 Tenant/Floor 及 Source、Transform、Inventory、Profile、Mapping、Semantic 全链 SHA-256；构建时重算语义链，错配与篡改失败关闭。Document/Layer/Block/Entity 四级定位显式区分可聚焦与不可聚焦，空图层保留 ID 但不伪造范围。
- 样例 13：Diagnostic Index `f0d18f95...17209448b`，JSON 文件 `aa04fc74...70eacdc0c`（46,892 bytes）；21 条提案证据为 High 13 / Review 0 / Low 8 / Rejected 0，21 条问题为 12 Info / 9 Warning / 0 Blocking，其中 17 条可聚焦、4 条真实空图层不可聚焦，重复运行字节完全相同。
- 门禁：E02-S07 聚焦 6/6、Space Unit 328/328、CAD 工具 23/23、合并后完整 solution Release 非增量单线程构建 0 error / 10 条既有 warning，受影响文件格式、Schema JSON 与差异检查通过。并行 Android AOT 曾瞬时崩溃，关闭残留构建节点后在不降低 AOT 强度的条件下通过。
- 这是开发切片而非正式 E02-S07 验收；尚无问题列表 UI/画布点击高亮、人工删除/合并/拆分、字段锁定或修正重放，也未写 Draft/数据库。正式验收仍等待授权原生 CAD 适配器、冻结 Worker、独立真实黄金集、生产持久化/API/权限/审计与精度/覆盖率证据。下一开发主线优先 E03-S04 Excel 行与 CAD/编辑器元素候选匹配，随后 E04-S05 消费本索引实现问题列表和画布定位。证据见 `docs/space/reports/e02-s07-cad-semantic-diagnostics-development.md`。

## E02-S06 CAD 基础语义解析器开发切片（2026-08-03）

- 在 E02-S05 集成基线 `b3c45a8f` 上完成功能提交 `c8e2ae87`、证据提交 `be32c9a7`，并以 no-ff 提交 `fdb210b4` 集成到 `integration/space-v1-20260730`：Prepared IR、Inventory、封存 Profile 与 Mapping Preview 现在形成同租户、全哈希绑定的失败关闭链，输出确定性只读语义提案，不创建永久 LogicalId，不写 Draft。
- 每个对象保留临时 `previewObjectId`、SourceRef/图层/块/属性、目标类型、采用规则、默认高度/厚度、整数毫米规范几何、置信度与选择状态；统一区分 Element/Zone/Aisle/Rack，覆盖 Wall、Column、Door、Dock、Zone、Aisle、Rack。零长度、零面积和不支持图元显式 Rejected，不静默丢弃。
- Block 规则逐引用检查属性，命中时优先于 Layer 且不重复；无真实块轮廓时保留 BlockInstance 仿射变换、置信度封顶 0.69 并告警，不伪造货架尺寸。阈值固定为 `>=0.90` 自动选中、`0.70–0.89` Warning 待确认、`<0.70` 候选展示；必需来源只有拒绝几何时 Blocking。
- 样例 13：Semantic Preview `e398d192...befc866`，JSON 文件 `75845d12...7202ea`；22 源对象中 21 提案、13 AutoAccepted / 8 Candidate / 0 Rejected、13 Confirmable / 13 Selected、8 Info / 8 Warning / 0 Blocking，重复运行字节完全相同。
- 门禁：E02-S06 聚焦 6/6、20/20 合成 CAD 完成语义链、CAD 工具 23/23、Space Unit 322/322、完整 solution Release 非增量构建 0 error / 10 条既有 warning、格式/Schema/差异检查通过。证据见 `docs/space/reports/e02-s06-cad-semantic-development.md`。
- 这是开发切片而非正式 E02-S06 验收；仍等待授权原生适配器、冻结 Worker、正式黄金集、生产 Artifact/持久化、复杂块/曲线证据和受权 Draft Apply。等待期间可继续 E02-S07 开发侧问题定位与锁定修正预览，不得提前声称正式 CAD 验收。

## E02-S05 CAD 图层映射方案开发切片（2026-08-03）

- 在 E02-S04 集成基线 `f4b596f0` 上完成功能提交 `2736427c`、证据提交 `29118c19`，并以 no-ff 提交 `b6d58a1e` 集成到 `integration/space-v1-20260730`：新增 Definition SHA-256 封装的不可变 CAD Mapping Profile；System 方案无租户、租户侧只读，租户复制记录 System 基线，后续修改创建新版本。Tenant Profile 跨租户失败关闭。
- Layer/Block 规则支持 Exact、Glob、受限 NonBacktracking Regex 和 Block 属性条件，冻结目标语义、几何规则、默认高度/厚度、置信度、优先级和必需标记。逐层 Override 优先；同优先级/特异性多命中 Blocking；必需来源缺失或为空 Blocking；空/未知来源仍完整列出。
- Preview 绑定 Tenant、Profile/Version/Definition、Source、Inventory、源结构、Override、Reuse Key 和 Preview SHA-256；复用键排除 Floor/坐标 Transform，因此同一 CAD 换楼层仍复用，但不同租户/方案/来源/覆盖不会串用。新命令 `seal-dev-mapping-profile`、`preview-dev-mapping` 不写 Draft，无 Migration/API/外部 AI。
- 样例 13：Profile `732eef8a...de59d1`，Structure `9636bd72...0911ab`，Reuse `014cdc75...1c879b`，Preview `98a0a315...8009ca`；15 图层中 10 mapped / 5 unmapped，1/1 块 mapped，覆盖 21 个图层对象和 8 个块引用，4 Info / 1 Warning / 0 Blocking，可进入开发侧语义解析。
- 门禁：E02-S05 聚焦 12/12、20/20 合成 CAD 标准方案预览、CAD 工具 23/23、Space Unit 316/316、完整 solution Release 非增量构建 0 error / 10 条既有 warning、JSON/CLI/差异检查通过。证据见 `docs/space/reports/e02-s05-cad-mapping-development.md`。
- 这是开发切片而非正式 E02-S05 验收；仍等待授权原生适配器、正式持久化清单/方案、并发、API/权限/审计/UI 和真实图纸证据。E05-S01 已完成，等待期间可继续 E02-S06 开发侧只读语义提案，不得直接写 Draft。

## E02-S04 CAD 图层与块清单开发切片（2026-08-03）

- 在 E02-S03 集成基线 `01a59696` 上完成功能提交 `b77faf96`、证据提交 `324c8755`，并以 no-ff 提交 `be639d07` 集成到 `integration/space-v1-20260730`：CAD IR v1 向后兼容增加图层颜色、线型和可见性；开发 DXF 转换器保留完整 `TABLES/LAYER` 和空图层，未声明图层显式合成并产生 Warning，不再只列出有对象图层。
- 新增来源/坐标 Transform/Floor/Inventory SHA-256 绑定的确定性清单：图层对象/支持/不支持/类型/块引用/属性数与范围，块定义/XRef/引用/属性摘要，以及每个块引用的稳定 SourceRef、受控属性值和范围。非 Ready、Blocking、来源/楼层/范围或坐标元数据不一致均失败关闭，无 Migration/WebApi/Draft 写入。
- 图层、块和引用支持受限分页查询，覆盖名称/ID、显隐、图元类型、XRef、图层、块名和属性键值；单页最多 200。开发工具新增 `build-dev-inventory` 与 `query-dev-inventory`，合同见 `docs/space/contracts/cad/v1/inventory.schema.json`。
- 样例 13：Source `aa573f04...1fb106`，新 CAD IR `b6aa6501...614310`，Transform `b1223a8f...353cfba`，Inventory `63432958...9697a9`；F01 范围 `(0,-1200)～(36000,24000)` mm，15 图层/7 空层、1 块、8 个带属性块引用、22 supported 对象；`RACK_ID=R-01-01` 精确查询返回 `H:110`。
- 门禁：E02-S04 聚焦 10/10、20/20 合成 DXF 清单链、CAD 工具 22/22、Space Unit 304/304、完整 solution Release 非增量构建 0 error / 10 条既有 warning、JSON/CLI/差异检查通过。证据见 `docs/space/reports/e02-s04-cad-inventory-development.md`。
- 这是开发切片而非正式 E02-S04 验收；仍等待授权原生适配器、冻结 Worker、正式黄金集、生产 streaming/持久化/API/权限/UI 与真实复杂图纸证据。等待期间可继续 E02-S05 开发侧图层映射方案。

## E02-S03 CAD 坐标确认开发切片（2026-08-03）

- 在 E02-S02 开发 CAD IR 集成基线 `97d6871f` 上完成功能提交 `09b26b87`、证据提交 `d78b3b09`，并以 no-ff 提交 `7741da61` 集成到 `integration/space-v1-20260730`：分析阶段分别给出源单位范围和建议毫米范围；已识别单位仍必须明确确认，未知单位不猜测，确认记录绑定来源 SHA-256。
- 确认合同冻结源原点、目标 Floor 原点、逆时针 Z 旋转、Floor LogicalId/Code/Level/Elevation、边界与 `LOCAL_MM_Z_UP`。变换可纠正错误检测比例，点、半径、偏移和边界按 AwayFromZero 量化为整数毫米；普通图元保持 Identity，块引用复合实例矩阵；同一输入产生稳定 Transform SHA-256。
- 默认图纸单边 1 m～5 km；边界缺失、范围异常、未确认单位、错误来源哈希、非法楼层坐标系或超出楼层边界 50 mm 均失败关闭。DWG/DXF `SpaceModelSource` 缺少规范坐标元数据时不能进入 Parsing；既有 Excel/底图/编辑器路径不受影响，无 Migration。
- 20/20 合成 DXF 完成转换、分析、确认和楼层准备。样例 13 归属 F01，22 图元、0 问题，范围 `(0,-1200)～(36000,24000)` mm，Transform SHA-256 为 `b1223a8f...353cfba`。
- 门禁：E02-S03 聚焦 13/13、CAD 工具 20/20、Space Unit 294/294、完整 solution Release 0 error / 10 条既有 warning，最终 SDK 可访问增量构建 0 warning / 0 error，JSON/CLI/差异检查通过。证据见 `docs/space/reports/e02-s03-cad-coordinate-development.md`。
- 这是开发切片而非正式 E02-S03 验收；仍等待授权原生 DWG/DXF 适配器、冻结 Worker、正式黄金集和同租户/同版本持久化服务链。等待期间可继续 E02-S04 开发侧图层/块清单。

## E02-S02 CAD IR 开发契约（2026-08-02）

- 在合成 CAD 图纸集成基线 `08fe896a` 上完成第一段可执行 CAD IR 链路：功能提交 `89759cec`、验证文档 `8f3e9252`、no-ff 受控集成 `9e8cf4af`。Contracts 定义供应商中立的 CAD IR v1，Application 定义 `ICadConverter`、只写 streaming sink 接口和失败关闭契约验证器；WebApi、Draft 仓储和供应商 SDK 类型不跨越该边界。
- 新增开发命令 `convert-dev-ir`，可把 UTF-8/ASCII DXF 转换为确定性 JSON IR；验证精确来源 SHA-256、稳定 sourceRef、图层/块/图元计数、坐标边界和转换器身份，支持毫米/厘米/米/英寸/英尺归一化，未知单位 Blocking，不支持图元显式保留并报问题，XRef 原始路径不出边界。
- 20/20 合成图纸转换通过：130 个图层记录、23 个块、292 个图元，其中 278 个受支持、14 个不支持且全部有显式问题、缺失 sourceRef 为 0。样例 13 产生 8 层、1 块、22 图元，IR SHA-256 为 `f080ac0c...20a9ba`。
- 门禁：CAD 实验工具 19/19、CAD IR 契约聚焦 9/9、Space Unit 281/281、完整 solution Release 0 error / 10 条既有 warning、`git diff --check` 通过。证据见 `docs/space/reports/e02-s02-cad-ir-development-contract.md`。
- 这是 E02-S02 开发切片，不是正式验收：仍等待 E02-S01 的原生 DWG 适配器/商业授权、冻结隔离 Worker、独立正式黄金集和生产规模 streaming/压力证据；完成供应商选择后再接同一契约并进入 E02-S03。

## E02 合成开发 CAD 图纸包（2026-08-02）

- 新增 `docs/space/acceptance/development-v2.0.0`：20 份仓库内可重复生成的合成 DXF，L1～L5 各 4 份，覆盖 AC1009/AC1015/AC1021/AC1027/AC1032 文件头以及规则、多楼层、非正交、综合和噪声场景。
- 新命令 `generate-dev-corpus` 同步生成 SHA-256 清单、场景索引、最小期望答案、期望问题、Provider IR、图层映射和开发使用声明。全部资产不含真实客户、供应商、地址、人员、标题栏或设备序列号数据。
- CAD 实验工具测试 12/12；20/20 文件完整性、哈希、成对 DXF 行、EOF、唯一 Handle、五类布局与 DXF 文件头矩阵通过。
- 该数据包明确为 `DevelopmentSeed` 且 `countsTowardReleaseGate=false`：可推进解析、映射、问题、IR、UI 和回归开发，但不替代原生 DWG、ODA/APS 授权、冻结 Worker 和独立正式黄金集。证据见 `docs/space/reports/e02-synthetic-development-cad-corpus.md`。

## E12-S05 完成状态（2026-08-02）

- E12-S05 已完成实现、全量门禁、远端备份、no-ff 受控集成和临时资源清理：起始基线 `ad77540d`，功能提交 `dd505f6f`，集成提交 `c4b139ab`。内部规划人员可从 Ready/Succeeded/Production Isolated 场景下载 glTF 2.0 单文件 `.glb`。
- 导出在 Serializable 一致性快照中稳定排序楼层、区域、巷道、货架、货架层、库位和通用元素，总数据节点上限 50,000；同一快照字节与 SHA-256 确定。CP6 `LOCAL_MM_Z_UP` 毫米坐标固定转换为 glTF `+Y` 向上、米制坐标。
- 货架、可定位库位和盒体元素提供共享网格、面法线与材质；边界、多边形、中心线、货架层规格和稳定 LogicalId 进入 `extras.cp6`。这是一份低成本可视化交换包，不是 CAD authoring、DWG 回写或生产发布入口。
- 新增 1 个 GET API、1 个只读权限和四个五语页面词条；Design V1 从 83 增至 84 operations，C#/TypeScript SDK 已同步。响应带 no-store、nosniff、ETag、schema 与 SHA-256 头，且不含库存、人员、设备事件或历史任务运行态。
- 全量门禁：Space Unit 272、默认 Space Integration 247 passed / 63 SQL-gated skipped、CP6.Tests 最终复验 2777 passed / 17 environment-gated skipped、前端 123 files / 676 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning；生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。证据见 `docs/space/reports/e12-s05-standard-gltf-exchange.md`。
- 合并态 GLB 2/2、权限/API/OpenAPI 65/65、前端同树 9/9、双 EF 与 SDK 门禁通过。功能 tip 已进入远端集成祖先链，远端/本地功能分支和功能工作树已删除，释放 2,869,523,790 字节（约 2.672 GiB）。E12-S06 仍等待正式黄金样本、DWG SDK/供应商授权与可审计试验环境；`main` 未修改。

## E12-S04 完成状态（2026-08-02）

- E12-S04 已完成实现、全量门禁、远端备份和 no-ff 受控集成：起始基线 `6d13d7da`，功能提交 `7b919b4b`，文档 tip `a9298bad`，集成提交 `577168e3`。内部规划人员可固定 2～10 个不同生产隔离场景的同源仿真证据，人工指定基线，并查看距离、拥堵、容量、吞吐和成本的原值、差值及显式阈值风险。
- 比较强制同 Site/Model/基础 Published 版本、来源数据哈希、历史窗口、任务口径、仿真定义、几何、币种、费率和吞吐窗口；容量假设差异显式标记。系统不计算总分、不排名、不推荐，也不预选决策方案。
- 人工决策只允许 Selected/Deferred/RejectedAll 和必填理由；后续记录必须替代唯一当前链头。比较、风险和决策全部追加式/不可变，永不合并、写入或发布到生产。
- 新增 6 个 planning API、4 个权限、四张租户隔离证据表、EF Migration/增量幂等 SQL；Design V1 从 77 增至 83 operations，C#/TypeScript SDK 已同步。规划页新增跨分支比较矩阵、风险与决策历史。
- 47 个比较词条均有五语运行时种子，静态 i18n 欠账仍为既有 908 项，本卡净新增 0。
- 全量门禁：Space Unit 272、默认 Space Integration 245 passed / 63 SQL-gated skipped、CP6.Tests 2775 passed / 17 environment-gated skipped、前端 123 files / 674 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning；生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。证据见 `docs/space/reports/e12-s04-scenario-comparison-decision.md`。
- 合并树与功能 tip 一致，合并态引擎 4/4、服务 3/3、权限/合同/OpenAPI/五语 66/66、前端 10/10、类型检查、双 EF、SDK 和 TypeScript 门禁通过。功能 tip 已确认进入远端集成祖先链，功能工作树、临时依赖链接及本地/远端功能分支已删除，释放 2,520,628,564 字节（约 2.348 GiB）。下一张可独立实施 E12-S05“标准交换格式导出”，`main` 未修改。

## E12-S03 完成状态（2026-08-02）

- E12-S03 已完成实现、全量门禁、远端备份和受控集成：起始基线 `1650e8ba`、功能提交 `ab21aed4`、文档 tip `2cd1faed`、no-ff 集成 `f2d68897`。内部规划人员可在生产隔离场景中基于不可变脱敏历史数据集运行确定性规划仿真。
- 距离使用同层货架格口锚点直线距离并显式报告未知覆盖；拥堵按目的位置历史执行区间重叠；容量使用调用方声明任务数量单位；吞吐使用精确历史时长和固定时间桶；人工按 worker token 区间并集；成本只使用显式距离/人工/拥堵单价。
- 新增 3 个 planning API、2 个权限、两张不可变租户隔离证据表、EF Migration/增量幂等 SQL；Design V1 从 74 增至 77 operations，C#/TypeScript SDK 已同步。规划页可配置容量、时间桶、币种和单价，并展示五类 KPI、热点、结果哈希与无生产回写护栏。
- 41 个仿真词条均有五语运行时种子。静态 i18n 欠账仍为既有 908 项，本卡净新增 0。
- 全量门禁：Space Unit 268、默认 Space Integration 242 passed / 63 SQL-gated skipped、CP6.Tests 2771 passed / 17 environment-gated skipped、前端 122 files / 670 tests、完整 solution 非增量 Release 0 error / 3 条既有 warning；生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。证据见 `docs/space/reports/e12-s03-planning-simulation.md`。
- 本卡不做巷道路由、实时交通、高精度物理求解、财务实际、方案排名或生产回写。合并态引擎 4/4、服务 3/3、权限/合同/OpenAPI/五语 65/65、前端 7/7 及剩余一致性门禁通过。功能 tip 已确认进入远端集成祖先链，功能工作树及本地/远端临时分支已删除，释放 2,182,809,248 字节（约 2.03 GiB）。下一张可独立实施 E12-S04“多场景比较与决策记录”，`main` 未修改。

## E12-S02 完成状态（2026-08-02）

- E12-S02 已进入远端受控集成基线：数据/时钟/迁移 `4fb6941d`、API/UI/权限/SDK `d89919b8`、no-ff 集成 `c8ccbf56`。内部规划人员可向 E12-S01 克隆成功且生产隔离的场景导入最多 10,000 条不可变历史任务，并以确定性回放时钟映射历史 UTC 时间。
- 合同只接受 64 位 SHA-256 task/worker token 和调用方不可逆脱敏确认，不含订单、人员、物料或 SKU 原始标识字段；所有任务位置必须存在于场景固定快照，数据集与任务不可修改/删除且永不回写生产。
- 新增 3 个 planning API、2 个权限、两张租户隔离证据表、EF Migration/增量幂等 SQL；Design V1 从 71 增至 74 operations，C#/TypeScript SDK 已同步。规划页仅对 Ready/Succeeded/Isolated 场景开放 JSON 导入、列表和回放证据读取。
- 25 个数据集词条及 3 个场景入口词条均有五语运行时种子。静态 i18n 欠账仍为既有 908 项，本卡净新增 0。
- 全量门禁：Space Unit 264、默认 Space Integration 239 passed / 63 SQL-gated skipped、CP6.Tests 2767 passed / 17 environment-gated skipped、前端 121 files / 667 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning、生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。合并态领域 3/3、服务 4/4、权限/契约/OpenAPI/种子 64/64、前端 5/5 及剩余门禁通过。证据见 `docs/space/reports/e12-s02-deidentified-history-replay-clock.md`。
- 功能 tip 已先远端备份并确认进入远端集成祖先链；随后删除功能工作树及本地/远端临时分支，释放 2,177,363,070 字节（约 2.03 GiB）。历史由远端受控集成分支完整保留，`main` 未修改。

## E12-S01 完成状态（2026-08-02）

- E12-S01 已进入远端受控集成基线：隔离模型/迁移 `c673b7ec`、功能 `8d75e79e`、no-ff 集成 `0ac603d4`、五语收口 `3d41c8d9`。新增内部生产隔离规划分支，固定当前生产 Published 快照，可并存且不占生产 Draft/Published 指针。
- 场景版本具有独立 `PlanningScenario` purpose，领域与数据库双重拒绝其进入生产发布生命周期；固定基础版本后即使生产版本变为 Superseded，异步 Worker 仍克隆原快照，不自动追随或合并生产变化。
- 新增 PUT/GET/list 场景端点、调用方 UUID + payload hash 幂等、不可变分支证据、租户复合外键、迁移与增量幂等 SQL；Design V1 从 68 增至 71 operations，C#/TypeScript SDK 已同步。
- `/space/planning` 提供站点选择、创建、固定血缘、版本、克隆任务、隔离状态和自动轮询；20 个页面词条已补齐五语运行时种子。i18n 静态欠账仍为既有 908 项，本卡净新增 0。
- 最终门禁：Space Unit 261/261、默认 Space Integration 235 passed / 63 SQL-environment skipped、CP6.Tests 2763 passed / 17 environment-gated skipped、前端 120 files / 664 tests、完整 solution Release 0 error / 10 条既有 warning、生产构建、两个 EF Context、SDK drift 与 TypeScript strict no-emit 全部通过。合并态聚焦复验与五语聚焦测试通过。交付证据见 `docs/space/reports/e12-s01-production-isolated-scenario-branch.md`。
- 功能 tip 先远端备份并确认进入远端集成祖先链，随后已删除功能工作树及本地/远端临时分支，释放 2,877,403,216 字节（约 2.68 GiB）。历史由远端受控集成分支完整保留，`main` 未修改。

## E11-S06 完成状态（2026-08-02）

- E11-S06 已进入本地受控集成基线：合同 `46884878`、功能 `f10b4b54`、文档 `f50ce454`、no-ff 集成 `d49fe1d0`。新增只读批次效果评估，组合 E11-S03～S05 的建议、审批、分派回执、当前执行事实和指定 Published 版本几何，不新增评估表或写操作。
- 看板提供推荐→选择→回执→开始→完成/关注/补偿漏斗、显式比率与带样本数的时长。任务按 `TaskId`、人员按 `SourceId + ExternalId` 稳定排序形成同一获批队列的计划几何反事实；样本不足、锚点不完整、跨层或原始距离约束不满足时整项不可用。
- 实际路线节省、吞吐提升和货币收益因缺少任务轨迹、历史控制与成本归因基线而固定不可用；回退结果如实显示，时间证据不完整或无效时排除样本并明示 limitation。响应不含姓名、邮箱、内部 `UserId`、`AssignedTo` 或逐任务收益明细。
- Viewer 新增效果看板、手动刷新、来源时点、样本量、计划改善/持平/回退和收益边界；新建议、新审批、关闭与卸载会使旧响应失效。28 个五语键使快照从 4,587 增至 4,615；i18n 仍有 908 项既有欠账，本卡净新增缺失为 0。
- 功能分支门禁：Space Unit 258/258、默认 Space Integration 232 passed / 62 SQL-environment skipped、CP6.Tests 2759 passed / 17 environment-gated skipped、前端 118 files / 660 tests、完整 solution Release、生产构建、两个 EF Context、SDK drift、TypeScript SDK strict no-emit、OpenAPI surface 与差异检查通过。合并态复验：引擎 9/9、服务 2/2、权限/合同/种子 7/7、前端 23/23及类型、EF、SDK/OpenAPI 与差异门禁通过。交付证据见 `docs/space/reports/e11-s06-outcome-evaluation.md`。
- 功能 tip `f50ce454` 先完成远端备份，再确认其为本地/远端一致的清理前集成状态 `fc123f5d` 的祖先；随后已删除功能工作树及本地/远端临时分支，共释放 2,707,376,655 字节（约 2.52 GiB），历史由远端受控集成分支完整保留。`main` 未被本轮操作修改。

## E11-S05 完成状态（2026-08-02）

- E11-S05 已进入受控集成基线：合同 `139c76b5`、功能 `e8df8288`、文档 `a0b247ab`、no-ff 集成 `cf35849c`。新增审批批次实时执行状态、持久化动作账本、调用方 UUID + payload hash 幂等、最多 3 次人工重试与整批未开始任务的安全补偿。
- 三层重放保护覆盖 OA 回调、任务适配器回执和执行动作；精确重放返回原结果，部分/冲突回执失败关闭。重试每次重新验证 Published、不可变建议、人员实时性与空闲、内部映射、WMS 范围及任务并发事实。
- 补偿仅在整批任务仍 Pending、仍为原分派人、执行版本未变、从未开始/完成且原始回执完整一致时撤销 `AssignedTo`；不修改执行版本或结果，不认领/启动/完成任务，不修改库存/订单，不发出 WCS/PDA 命令。Migration 为 `20260802192420_SpaceE11S05ExecutionReceiptsCompensation`。
- Viewer 新增执行聚合状态、逐任务事实、动作历史、重试余额、补偿阻断码及显式原因输入，并阻止旧异步响应覆盖。28 行五语种子中 26 个新键使快照从 4,561 增至 4,587；i18n 仍有 908 项既有欠账，本卡净新增缺失为 0。
- 功能分支门禁：Space Unit 249/249、默认 Space Integration 230 passed / 62 SQL-environment skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 658 tests、完整 solution Release、生产构建、EF/SDK drift、TypeScript SDK strict no-emit 与差异检查通过。合并态复验：服务/适配器 14/14、权限/合同/种子 35/35、前端 21/21、类型、SDK drift、EF pending model 与差异检查通过。交付证据见 `docs/space/reports/e11-s05-execution-receipts-compensation.md`。
- 功能分支先推送远端备份；确认功能 tip `a0b247ab` 是本地/远端一致的集成状态 tip `17d6a3e0` 的祖先后，已删除功能工作树及本地/远端临时分支。共享依赖目标保留，本轮释放 D 盘 2,170,007,351 字节（约 2.02 GiB），历史由远端受控集成分支完整保存。

## E11-S04 完成状态（2026-08-02）

- E11-S04 已进入受控集成基线：合同 `098fb54b`、功能 `a7298e28`、文档 `a552d05d`、no-ff 集成 `c19231db`。新增内部 PUT/GET/cancel 调度审批资源、调用方 UUID 幂等、OA BizType `SPACE_DISPATCH_ASSIGNMENT`、提交/读取/取消权限与审计；提交人与最终审批人严格分离。
- 审批请求冻结 E11-S03 建议哈希、Published/仓库、选中 rank、任务并发、人员真实身份与双时点以及内部用户映射；对外不返回人员姓名、邮箱或内部人员 `UserId`。最终通过前重新验证全部事实，任一漂移整批进入 `Stale` 或 `FailedNoEffect`，不产生部分写入。
- `cp6-mobile-task-assignment-v1` 只分配现有 Pending 且未分派的真实 `MobileTask`，完整预检、任务分配、事件和回执在同一工作单元中提交；不认领、不启动、不修改库存/订单、不伪造 WCS，也不另建 PDA 事实源。Migration 为 `20260802184419_SpaceE11S04DispatchApproval`。
- Viewer 新增显式选择、理由、提交、刷新、取消、状态与回执，并以请求版本阻止旧响应覆盖。21 行五语种子使唯一键快照从 4,542 增至 4,561；i18n 历史缺失由 909 降至 908，本卡没有新增缺失键。
- 功能分支门禁：Space Unit 249/249、默认 Space Integration 224 passed / 62 SQL-environment skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 656 tests、完整 solution Release 与前端生产构建、EF/SDK drift、两个 TypeScript strict no-emit 和差异检查通过。合并态冒烟：审批服务/适配器 8/8、权限/合同/种子/基础设施 44/44、前端 19/19、类型、SDK drift 与 EF pending model 通过。交付证据见 `docs/space/reports/e11-s04-dispatch-approval-adapter.md`。
- 功能分支先推送远端备份；确认功能 tip `a552d05d` 是本地/远端一致的集成状态 tip `b317dfa5` 的祖先后，已删除功能工作树及本地/远端临时分支。共享依赖目标保留，本轮释放 D 盘 2,190,180,352 字节（约 2.04 GiB），历史由远端受控集成分支完整保存。

## E11-S03 完成状态（2026-08-02）

- E11-S03 已进入受控集成基线：合同 `3cf42534`、功能 `419d3f6c`、文档 `eea62de0`、no-ff 集成 `cf7bf778`。新增内部 PUT/GET 人员调度建议资源、调用方 UUID 幂等、不可变推荐证据和 `space-dispatch-v1` 定义；不审批、不分配、不认领、不启动、不修改任务或人员，也不向 WMS/WCS/PDA 写入。
- 任务只来自当前 CP6 `MobileTask` 的 Pending 且未分配事实，首个可行动位置固定优先 From、缺失时 To，并携带 ContractVersion/ExecutionVersion/RowVersion。人员必须同时具备新鲜位置与工作状态、严格 Idle，默认排除 Simulated；所有任务、人员和 Published 身份越界均失败关闭。
- 匹配先用 Hopcroft–Karp 保证最大基数，再做确定性最小成本；配对乘积上限 100,000，返回上限 100。证据分别保存任务/人员/配对首因排除、最多 100 个样例、匹配容量、截断和限制说明；几何距离不冒充通道路线、时间或 SLA。
- Migration `20260802180049_SpaceE11S03DispatchRecommendations` 新增租户隔离的不可变证据表、Published 复合外键、计数/JSON/哈希约束和索引。Viewer 新增手动 `DSP` 面板，与 KPI/DIAG/PUT 互斥，展示来源、任务并发、人员双时点、建议与排除证据，并支持任务首端定位。
- 功能分支门禁：Space Unit 249/249、默认 Space Integration 216 passed / 62 SQL-environment skipped、CP6.Tests 2752 passed / 17 environment-gated skipped、前端 118 files / 653 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning、EF/SDK drift、两套 TypeScript strict no-emit、生产构建和差异检查通过。合并态冒烟：引擎/运行时合同 6/6、服务/适配器 6/6、权限/审计/API/种子 23/23、前端 16/16、类型与 SDK drift 通过。42 个五语键使快照到 4,542；i18n 历史缺失仍为 909，本卡净新增 0。交付证据见 `docs/space/reports/e11-s03-dispatch-recommendations.md`。
- 功能分支先完成远端备份，再确认其 tip `eea62de0` 是本地/远端一致的集成状态 tip `7e627624` 的祖先；随后已删除功能工作树及本地/远端临时分支，共释放 D 盘 2,528,428,032 字节（约 2.35 GiB），共享依赖目录保留，历史由受控集成分支完整保存。

## E11-S02 完成状态（2026-08-02）

- E11-S02 已进入受控集成基线：合同 `3ccd2936`、功能 `644293f1`、文档 `034a1b1b`、no-ff 集成 `a2b47826`。新增内部 PUT/GET 上架推荐资源、调用方 UUID 幂等、不可变推荐证据和 `space-putaway-v1` 定义；不预留、不移动库存、不创建任务，也不向 WMS/WCS/PDA 写入。
- 候选只使用当前 Published/Active 空间模型和一致的当前 WMS 库存/活动任务来源。精确合并要求显式货主与批次及全部正库存逐行完全匹配，否则只推荐空库位；返回稳定 rank、规则命中、九类首因排除计数和最多 100 个样例，几何距离不冒充路线距离，入库数量不冒充容量。
- Migration `20260802172258_SpaceE11S02PutawayRecommendations` 新增租户隔离的不可变证据表、复合 Published 外键、计数/JSON/哈希检查约束和查询索引。Viewer 新增手动 `PUT` 面板，与 KPI/DIAG 互斥，支持当前楼层、候选/排除定位、旧响应失效和失败保留上次成功结果。
- 功能分支门禁：Space Unit 245/245、默认 Space Integration 211 passed / 62 SQL-environment skipped、CP6.Tests 2748 passed / 17 environment-gated skipped、前端 117 files / 648 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning、EF/SDK drift、TypeScript SDK strict no-emit、生产构建和差异检查通过。合并态冒烟：引擎 5/5、服务 6/6、权限/审计/契约/种子 34/34、前端 14/14、类型与 SDK drift 通过。42 个五语键使快照到 4,500，i18n 历史缺失由 911 降至 909，本卡净新增 0。交付证据见 `docs/space/reports/e11-s02-putaway-recommendation-candidates.md`。
- 远端备份、祖先关系与集成本地/远端一致性验证后，已删除 E11-S02 功能工作树及本地/远端临时分支；共享依赖目录保留。本轮释放 D 盘 2,187,710,464 字节（约 2.04 GiB），功能历史由远端受控集成分支完整保留。

## E10-S06 完成状态（2026-08-02）

- E10-S06 已进入受控集成基线：合同 `bffe1877`、实现 `0676ba4a`、文档 `969e7c38`、no-ff 集成 `5f86edcb`。新增只读 `GET /api/space/design/v1/sites/{siteId}/runtime/overview`，只汇总当前 Published/Active 模型，ABC 窗口限定为 1～365 个完整自然日；库存、作业和 ABC 保持独立来源、观察时间和部分可用语义。
- 楼层面积使用毫米边界鞋带公式，缺失任一活动楼层面积时不伪造站点总面积；货架占地率只表达建模足迹。占用率按正库存物理库位计算；因没有容量主数据，容量利用率固定为空并给出 `WMS_LOCATION_CAPACITY_NOT_AVAILABLE`。库存不跨单位合计，活动任务数/Stop 数不冒充吞吐量。
- ABC 只使用正数 OUT 事实，按出库量和物料稳定排序，以排名前累计占比 `<80%`/`<95%` 划分 A/B/C；有当前库存但无正出库事实的 SKU 明确为 Unclassified。Viewer 新增 KPI/异常/逐层总览和固定 ABC 颜色，ABC、库存空间筛选、作业热图三个颜色权威互斥，请求版本阻止旧响应覆盖。
- Design V1 从 67 增至 68 operations，C#/TypeScript SDK 已同步，无数据库 Migration。功能分支全量门禁：Space Unit 236/236、默认 Space Integration 198 passed / 62 SQL-environment skipped、本卡真实 SQL 3/3、CP6.Tests 2739 passed / 17 environment-gated skipped、前端 115 files / 639 tests、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 和生产构建通过。合并态冒烟：合同 23/23、Runtime/适配器 81/81、权限/OpenAPI 46/46、前端 25/25、类型检查和 SDK drift 通过。
- i18n 快照仍未绿色：集成基线已有 881 项，本卡新增 30 项，共 911 项；没有篡改生成快照掩盖技术债。E10 P2 S01～S06 至此均有完成证据。CAD/E06 主链继续等待正式黄金集、授权供应商证据和冻结 Worker；下一张独立实施卡须按依赖重新选择，不能把快照口径直接扩写成趋势、推荐或执行控制。

## E10-S05 完成状态（2026-08-02）

- E10-S05 已进入受控集成基线：实现 `65c59555`、文档 `53bea9b9`、no-ff 集成 `e270c2cc`。复用 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory/locate`，新增可选货主条件；货主、SKU、批次和容器至少一个，多个条件固定精确 AND，货主在服务边界规范为大写。
- WMS 继续是库存业务事实源；服务端重新验证适配器返回的正库存及全部筛选条件，越界结果以 502 失败关闭。CP6 适配器通过仓库、库位、SKU、批次的唯一库存业务键为容器取得货主，不向 Design Revision 复制或在浏览器猜测业务事实。
- 3D Viewer 新增可持续库存空间筛选：命中库位琥珀色、当前层未命中库位压暗，展示本层/全站/分层数量和来源证据；筛选跨库存轮询与楼层切换保持，清除后恢复库存模式，并以请求版本阻止过期响应覆盖。库存筛选与作业热图互斥，原有一次性定位仍保留。
- Design V1 保持 67 operations，C#/TypeScript SDK 已同步，无数据库 Migration。门禁：运行合同 2/2、Runtime/适配器 68/68、权限/OpenAPI 45/45、前端聚焦 22/22、前端全量 114 files / 632 tests、Space Unit 236/236、默认 Space Integration 190 passed / 61 SQL-environment skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过；本卡真实 SQL 1/1。完整真实 SQL 矩阵 250 passed / 1 个已独立复现的 Excel 预检种子循环依赖基线失败。
- E10-S06“仓库 KPI 快照、利用率与 ABC 口径”是下一张具备前置条件的 P2 卡。CAD/E06 主链仍等待正式黄金集、授权供应商证据和冻结 Worker 等外部输入；本卡不改变该优先级或失败关闭边界。

## E10-S04 完成状态（2026-08-02）

- E10-S04 已进入受控集成基线：实现 `9a9802a8`、文档 `f961d7e5`、no-ff 集成 `b4d5b81e`。新增当前设备读取 `GET /api/space/design/v1/sites/{siteId}/devices`，沿用 `space:model:read`，支持来源/设备/状态/楼层/活动告警过滤与受保护游标；外部主体在读库前拒绝。
- `Space_DeviceState` 以独立位置/运行状态游标维护设备当前投影，`Space_DeviceAlarmState` 以设备+外部告警身份维护显式 Raise/Clear 生命周期。迟到事件继续追加台账并返回 `AcceptedStale`、`ProjectionApplied=false`，但不回退投影或重新激活已被较新 Clear 关闭的告警；台账和投影在同一 Serializable 事务中提交。
- 当前读取包含无事件的 Unknown 映射、当前 Published 映射有效性和锚点、来源位置/状态证据、5 分钟独立新鲜度、Real/Simulated 以及活动告警严重度与事件证据。Migration `20260802144027_SpaceE10S04DeviceRuntime` 新增两个投影表、rowversion、租户复合外键、唯一索引、检查约束和身份写保护。
- 3D Viewer 已移除旧设备演示接口调用：只绘制活动楼层，来源 XYZ 优先，缺失时仅回退当前 Published 映射元素锚点；状态色、模拟线框、过期透明度和活动告警环均显式呈现，Three.js userData 保留映射/来源/位置/状态/告警证据，切层、关闭和卸载会释放 GPU 资源。
- Design V1 从 66 增至 67 operations，C#/TypeScript SDK 已同步。门禁：领域 2/2、设备服务 9/9、真实 SQL 本卡 2/2、权限/审计/OpenAPI 70/70、前端聚焦 14/14、前端全量 113 files / 629 tests、Space Unit 236/236、默认 Space Integration 189 passed / 60 SQL-environment skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 248 passed / 1 已独立基线复现的 Excel 预检种子循环依赖失败。
- E10-S05“货主、SKU、批次和容器空间筛选”是下一张具备前置条件的 P2 卡；MQTT/OPC UA/厂商连接器、凭据、告警确认、设备控制、历史轨迹和预测分析仍未实现，也不得混入 S05。CAD/E06 主链继续等待正式黄金集、授权供应商证据和冻结 Worker 等外部输入。

## E10-S03 完成状态（2026-08-02）

- E10-S03 已进入受控集成基线：实现 `10b16c51`、文档 `8ce91d41`、no-ff 集成 `88efd23d`。新增版本化 `space-device-event-v1` 合同、设备主数据映射 GET/POST/PUT 与设备事件写入；读取沿用 `space:model:read`，变更要求 `space:integration:manage` 并使用稳定审计动作。
- 映射以 `TenantId + SiteId + SourceId + DeviceExternalId` 为权威身份，绑定当前 Published/Active 的稳定设备元素 LogicalId；设备类型与 Device/Conveyor/Workstation/Elevator/StaticEquipment 兼容子集失败关闭，同一来源的设备和元素保持一对一，更新使用 rowversion。
- 设备事件支持 PositionObserved、OperatingStateChanged、AlarmRaised、AlarmCleared 四类互斥形状，严格冻结 Real/Simulated、设备/状态/告警枚举、UTC 时间、五分钟未来偏差、非负序列、毫米 XYZ、Published 楼层/库位引用和来源事件幂等；相同载荷安全重放，不同载荷稳定冲突。
- Migration `20260802141148_SpaceE10S03DeviceEvents` 新增 `Space_DeviceMapping` 与追加式 `Space_DeviceEvent`，含复合租户外键、唯一索引、检查约束和事件历史写保护。旧 `WmsDeviceQuery` 仍明确保持 Unavailable 空占位，不冒充真实 WCS/IoT 来源。
- Design V1 从 62 增至 66 operations，C#/TypeScript SDK 已同步。门禁：E10-S03 服务/真实 SQL 7/7、权限/审计/OpenAPI 70/70、Space Unit 234/234、默认 Space Integration 186 passed / 60 SQL-environment skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 245 passed / 1 已独立基线复现的 Excel 预检种子循环依赖失败。
- 该节记录 E10-S03 完成时的后续边界；E10-S04 现已由上方最新状态接续完成。MQTT/OPC UA/厂商连接器、凭据、告警确认或控制写回仍未实现。

## E10-S02 完成状态（2026-08-02）

- E10-S02 已进入受控集成基线：实现 `e70c2715`、文档 `86ad63bb`、no-ff 集成 `29a69a2b`。新增内部当前位置读取与受审计的授权轨迹查询；当前位置沿用 `space:model:read`，轨迹要求 `space-audit:read` 并以 `space.personnel.trajectory.read` 失败关闭审计。
- 查询只返回稳定来源/人员外部 ID、空间运行字段、来源事件和时间证据，不返回姓名、邮箱或内部 `UserId`；外部主体在读库前拒绝，站点访问判断先于存在性查询。位置只来自 E10-S01 `PositionObserved`，不从 WMS、任务或几何推测。
- 当前新鲜度阈值为 5 分钟，过期数据仍返回并显式标记；轨迹单次最长 24 小时、可见查询期 30 天。30 天不是物理清除，追加式事件账本继续保留，物理归档/删除须在后续独立生命周期卡完成。
- 3D Viewer 已加入当前人员和授权轨迹图层，只绘制活动楼层的来源 XYZ，区分过期/模拟/工作状态，缺少 XYZ 时明确显示未定位并不推断；切层、旧请求和卸载均清理图层/GPU 资源。
- Design V1 从 60 增至 62 operations，C#/TypeScript SDK 已同步，无新 Migration。门禁：E10 服务 12/12、权限/审计/OpenAPI 68/68、前端聚焦 8/8、前端全量 113 files / 626 tests、Space Unit 234/234、默认 Space Integration 180 passed / 59 SQL-environment skipped、CP6.Tests 2736 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 238 passed / 1 已独立基线复现的 Excel 预检种子循环依赖失败。
- E10-S03 的设备实时状态、AGV/输送设备和告警尚未实现；E10 仍属 P2，不改变 CAD 阻塞主链的优先级和依赖。

## E10-S01 完成状态（2026-08-02）

- E10-S01 已进入受控集成基线：实现 `1c7aa0e2`、文档 `1da17591`、no-ff 集成 `ec29d41f`。新增版本化 `space-personnel-event-v1` 合同与 `POST /api/space/design/v1/sites/{siteId}/personnel-events`，只允许具有 `space:integration:manage` 的内部集成主体写入。
- 人员事件明确区分 `Real`/`Simulated`，同一站点和来源不能切换类型；来源事件 ID 提供业务幂等，相同载荷安全重放，不同载荷稳定冲突。Space 不猜测位置，不从 WMS/任务/几何推导忙闲状态。
- `Space_PersonnelEvent` 保存追加式事件事实，`Space_PersonnelState` 以独立的位置/工作状态游标维护当前投影；历史乱序事件进入账本但不回退投影，已绑定用户不能重分配。
- Migration `20260802125928_SpaceE10S01PersonnelEvents`、数据库检查/唯一约束、租户过滤、rowversion、OpenAPI 60 operations 及 C#/TypeScript SDK 已闭环。
- 门禁：E10 领域 3/3、服务/EF 7/7、权限/OpenAPI 43/43、Space Unit 234/234、默认 Space Integration 175 passed / 58 SQL-environment skipped、真实 SQL 本卡 2/2、CP6.Tests 2734 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 231 passed / 1 既有 Excel 预检种子循环依赖失败；该失败已在不含本卡的 `e8d4e1c2` 基线独立复现。
- E10-S02 的实时读取、授权轨迹和保留策略尚未实现；E10 仍属 P2，不改变 CAD 阻塞主链的优先级和依赖。

## E03-S01～S03 与 E13-S16 完成状态（2026-08-02）

- E03-S01～S03 已连续进入受控集成基线：标准建模 Excel 模板 `033e8872` / `8521a701`，版本化字段映射 `f1310b40` / `e0cc4964`，Excel 数据预检 `9d0a59e7` / `3571f677`。当前链路已具备标准模板下载、租户私有映射、不可变版本、50 MB 隔离上传、异步预检、结构化问题清单和受保护错误报告。
- E13-S16 已进入受控集成基线：实现 `0549a1f2`、文档 `6ec0c02a`、no-ff 集成 `ad4de0b0`。租户管理员可在 `/space/ai-admin` 管理版本化数据策略、站点、获批 Provider 别名、1～3 并发与日/月预算，并查询实际/估算/未定价用量；合同不接受或回显密钥、URL、Endpoint。
- E13-S16 新增 `Space_AiTenantPolicy` 追加式版本表、Design V1 策略/用量 API、`space-ai-admin:read/manage` 权限、五语 seed、OpenAPI 及 C#/TypeScript SDK。外部主体显式拒绝，无策略时继续 `Disabled` 失败关闭。
- 最新门禁：服务 5/5、权限 18/18、Space Unit 231/231、Space Integration 168 passed / 57 SQL-environment skipped、CP6.Tests 2733 passed / 17 environment-gated skipped、前端 112 files / 622 tests、type-check、production build、完整 solution 0 warning / 0 error、EF/SDK drift 与差异检查全部通过。
- `npm run i18n:check` 保持既有 843 项存量缺口，本卡未增加缺口。E03-S04 及 E04-S05 仍等待 E02-S07/CAD 语义预览链；E13-S04 仍等待 E02-S03，不能提前启用外部 Provider、CAD IR、输出校验或 Apply。

## E04-S06 完成状态（2026-08-02）

- E04-S06 2D/3D 同源预览已完成并进入受控集成基线：功能提交 `20f248bd`，no-ff 集成提交 `2b6ef127`，起始基线 `99e367f1`。
- Design V1 编辑器默认提供 2D+3D 分屏以及纯 2D、纯 3D 切换；两种投影消费同一个 `ISpaceDesignSceneDto`，保存成功后使用服务端回读场景同步重建，不维护第二套模型。
- 3D 只读预览复用参数化 SceneBuilder、InstancedMesh 与资产链，支持俯视/等轴/正视和自动适配；界面明确显示版本状态及“不含生产库存/任务”，不开放 3D 写操作。
- 机器一致性清单分别来自编辑器实际 Konva 投影和实际 Three.js 对象树/实例矩阵/几何，统一比较数量、LogicalId、父级、业务编码、毫米位置/尺寸、旋转、逐层规格与规范 primitive，并用 SHA-256 固化；篡改实际实例矩阵会被自动化识别。
- 生命周期边界保持失败关闭：Removed/Disabled 不渲染、移除货架不泄露遗留 Active 子层、真正孤立层仍报错；2D 路径多线段在套索选择时按 LogicalId 去重。
- 门禁：前端聚焦 4 files / 13 tests、全量 108 files / 612 tests、type-check、production build、Space Unit 231/231、默认 Space Integration 140 passed / 55 SQL-gated skipped、Design Scene 真 SQL 3/3、Space Integration + KOUSQLSERVER 195/195、CP6.Tests 2720 passed / 17 既有环境 Skip、完整 solution 非增量构建 0 error / 10 条既有 warning、SDK drift、TypeScript SDK strict no-emit 与 `git diff --check` 全部通过。
- 本卡无新端点、DTO、数据库模型、Migration、OpenAPI 或 SDK 表面变化。机器证据见 `docs/space/reports/e04-s06-shared-2d-3d-preview.md`。
- 下一张建议独立卡为 E03-S01“标准建模 Excel 模板”，其 E05-S02 依赖已满足；E04-S05、E06 与 E13 后续链路继续等待各自前置条件。

## E09-S05 完成状态（2026-08-02）

- E09-S05 外部访问审计与有效期已完成并进入受控集成基线：功能提交 `83798dcf`，no-ff 集成提交 `c658871c`，起始基线 `a5b53a2b`。
- 登录继续复用统一 `Sys_SecurityLog`；Space 追加写账本新增稳定 Portal 会话入口、组织选择、PublishedScene/Stock/Task 查看事件，记录 Organization Context、Site、结果、条目数、授权版本、Correlation/Trace 与受控客户端元数据，404 范围拒绝记为 `Denied` 且不保存异常明文。
- Organization、Membership、Grant、FieldPolicy 变更端点使用稳定业务动作码和 `space:external:manage` 证据；写入前审计失败关闭，成功写入后最终审计不可用返回 outcome unknown。暂停、撤销和退休通过同一 update 资源链可追踪。
- 外部 `Export` 求值现在强制审计允许/拒绝，并记录组织/成员安全戳、AuthorizationVersion、GrantIds 与 FieldPolicyIds；审计不可用时清空命中授权并返回 `SPACE_AUDIT_UNAVAILABLE`。本卡未新增独立导出端点。
- 同一现有会话已自动化证明 Membership/Grant 到期、暂停、撤销以及 FieldPolicy 退休在下一请求立即失效；Active Policy 版本变化在下一响应产生新 AuthorizationVersion。真 SQL 连续证明成员到期→Grant 到期→续期恢复→Policy 退休的逐请求重验证链。
- 本卡无新端点、DTO、迁移或 SDK 表面变化，OpenAPI 保持 36 paths / 47 operations。门禁：审计聚焦 48/48、Portal/真 SQL 合并态 16/16、Space Unit 231/231、Space Integration + KOUSQLSERVER 195/195、CP6.Tests 2720 passed / 17 既有环境 Skip、非增量 solution 0 error / 10 条既有 warning、EF/SDK drift、TypeScript SDK strict no-emit、前端 type-check/106 files/607 tests/production build 全部通过。
- E09-S01～S05 工程范围完成；产品、QA、WMS 与安全负责人的正式 GA 签字仍是发布治理动作，机器证据见 `docs/space/reports/e09-s05-external-access-audit-validity.md`。
- 下一张建议独立卡为 E04-S06“2D/3D 同源预览”；E04-S03、E05-S03 依赖已满足。E04-S05 继续等待 E02-S07，E10 仍为 P2。

## E09-S04 完成状态（2026-08-02）

- E09-S04 跨租户越权自动化已完成并进入受控集成基线：功能提交 `f045bd6f`，no-ff 集成提交 `c82d4fae`，起始基线 `dfacbb48`。
- 新增发布阻断矩阵，覆盖猜测 Organization/Site/Location ID、同码租户协作图、Published 场景身份、Stock/Task 运行态身份、分页游标、授权版本和字段裁剪；所有越权路径统一失败关闭，不泄露目标存在性。
- 内存与真实 SQL 使用两个租户创建相同 User、Site/Floor/Zone LogicalId、组织码、策略名、Owner 和 Task ID，九类外部协作表的正常查询各只见本租户，审计视图可确认两套数据确实同时存在。
- Portal 场景投影前新增 Schema/Authority/Site/PublishedVersion/Published 状态/Floor-Site 身份校验；Stock/Task 在处理条目前新增 SiteId + PublishedVersionId 校验；同码但非候选 LocationLogicalId 的运行态条目固定 404。
- `AuthorizationVersion` 现在显式绑定 Tenant/User/Organization/Resource，并继续包含组织/成员安全戳、Grant/Policy 版本；Data Protection 游标已自动化验证 Tenant、Actor、Organization、grant version、资源和过滤哈希任一变化均不能复用。
- 本卡无新端点、DTO、迁移或 SDK 表面变化；OpenAPI 保持 36 paths / 47 operations。功能门禁：Space Unit 231/231、Space Integration + KOUSQLSERVER 187/187、CP6.Tests 2713 passed / 17 environment-gated skipped、完整 solution 0 warning / 0 error、EF/SDK drift 与前端 106 files / 607 tests 全部通过。
- 合并态聚焦门禁：隔离矩阵含真实 SQL 16/16、游标/执行上下文中间件 42/42、完整 solution 非增量构建 0 error / 10 条既有 warning、EF/SDK drift、TypeScript SDK strict no-emit、前端 type-check/607 tests/production build 全部通过。交付报告见 `docs/space/reports/e09-s04-cross-tenant-isolation.md`。
- 下一张建议卡为 E09-S05：外部登录/组织选择/查看/导出/授权变化审计，以及 Membership/Grant/Policy 到期、暂停或撤销后现有会话下一次请求立即失效的证据。

## E09-S03 完成状态（2026-08-01）

- E09-S03 外部只读 Portal 与字段策略已完成并进入受控集成基线：功能提交 `88bc42d1`，no-ff 集成提交 `1850b2d8`，起始基线 `13c7b9da`。
- 新增租户权威 `Space_FieldPolicy` 与 `Space_FieldPolicyField`，覆盖 PublishedScene/Stock/Task、显式字段 allowlist、None/Partial/Hash/Redact 掩码、Audience、`CanExport`、版本和终态退休；数据库以复合租户外键、检查约束和过滤唯一索引失败关闭。
- 新增 `/api/space/field-policy` 管理 API，读取/变更沿用 `space:external:read/manage`；Grant 控制行与空间范围，字段策略控制字段、脱敏和导出能力，未知字段默认不可见，导出必须同时得到 Grant 与策略许可。
- 新增 `/api/space/portal/v1` 只读 Organizations/Sites/PublishedScene/Stock/Tasks。外部主体只允许 Portal 的 GET/HEAD；除组织选择外必须提供单一非空 Organization Context，内部主体、未知主体类型、缺失/歧义上下文和外部主体访问其他 Space 路径均拒绝。
- Portal 只读取当前 Published/Active；结构 ID 来自数据库权威候选，业务值字段按策略裁剪。多个合法 Grant 按完整子句 OR，字段采用命中范围内最少限制掩码；其他资源 Grant、运行源身份和 Zone-only 父层字段均不能扩大可见范围。
- OpenAPI 从 29 paths / 38 operations 增至 36 paths / 47 operations，并更新 C#/TypeScript SDK；OpenAPI、C#、TypeScript 工件 SHA-256 分别为 `BCFFEF09...DAF2`、`C7BCC222...6C4B`、`5F5132E7...9ABF`。
- 功能门禁：Space Unit 231/231；Space Integration + KOUSQLSERVER 181/181、0 skipped；CP6.Tests 2711 passed / 17 environment-gated skipped；完整 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift 与 C#/TypeScript SDK 编译通过。S02→S03 增量 SQL 在临时库连续执行两次通过。
- 合并态聚焦门禁：完整 solution build 0 error、字段策略领域 3/3、Portal/策略/Grant/求值器含真实 SQL 22/22、权限/OpenAPI/中间件/ProblemDetails 84/84、EF/SDK drift、前端 106 files / 607 tests、type-check 和 production build 全部通过。交付报告见 `docs/space/reports/e09-s03-external-portal-field-policy.md`。
- 下一张建议卡为 E09-S04：跨租户越权自动化测试，覆盖猜测 ID、同码组织/仓库/库位、分页/游标、缓存和 Portal DTO；E09-S05 随后补齐外部访问审计与有效期证据。

## E09-S02 完成状态（2026-08-01）

- E09-S02 外部组合授权与访问求值器已完成并进入受控集成基线：功能提交 `cae12c7e`，no-ff 集成提交 `feefa9cd`，起始基线 `8869ac58`。
- 新增 `Space_ExternalGrant` 及 Floor/Zone/Owner/Object 规范化子表；Site 必填，Floor/Zone 固定使用当前 Published Revision 的稳定 LogicalId，数据库以复合租户外键、状态/有效期/版本检查和过滤唯一索引失败关闭。
- 多个 Grant 保持完整子句 OR，单个 Grant 内 Site/Floor/Zone/Owner/BusinessObject 按 AND 匹配；禁止跨 Grant 展平维度造成笛卡尔权限升级。Export 还要求命中子句显式 `CanExport`。
- `ISpaceAccessEvaluator` 验证可信 Tenant/User、单一 Organization Context、Active Organization、有效 Active Membership 与有效 Active Grant；任一缺失、过期、暂停、撤销、歧义或跨组织拼接均拒绝。安全范围携带组织/成员安全戳、GrantVersion 和确定性 AuthorizationVersion。
- 新增 `/api/space/external-organization/{organizationId}/grant` 管理 API，读取/变更沿用 `space:external:read/manage`。`FieldPolicyId` 在 E09-S03 前只作保留字段，非空请求固定 422；外部主体仍被全局拒绝直接进入 `/api/space`，未提前开放 Portal。
- OpenAPI 增加 2 个 route family / 4 个操作并更新 C#/TypeScript SDK；运行时客户端表面哈希为 `FFCE63E749C7653E553A57D32EA85A7FF846F17199AF3436FE787CD26F509259`。
- 功能门禁：Space Unit 228/228；Space Integration + KOUSQLSERVER 169/169、0 skipped；CP6.Tests 2703 passed / 17 environment-gated skipped；完整 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift 与 C#/TypeScript SDK 编译通过。S01→S02 增量 SQL 在临时库连续执行两次通过。
- 合并态聚焦门禁：完整 solution build 0 error、领域 4/4、访问求值/管理/真实 SQL 10/10、权限/OpenAPI 35/35、EF/SDK drift 通过。交付报告见 `docs/space/reports/e09-s02-external-grant-scope-evaluator.md`。
- 下一张建议卡为 E09-S03：接入 Published-only 外部只读 Portal、资源 DTO allowlist、字段策略/脱敏和导出裁剪；完成前继续保留外部主体的全局拒绝。

## E09-S01 完成状态（2026-08-01）

- E09-S01 外部组织与成员模型已完成并进入受控集成基线：功能提交 `a599cfd7`，no-ff 集成提交 `09538ca3`，起始基线 `0c02fc80`。
- 新增租户权威 `Space_ExternalOrganization` 与 `Space_ExternalMembership`；支持 Customer/Supplier/ThirdPartyLogistics、ERP BusinessPartner 可选关联、用户多组织成员关系、有效期、终态生命周期、SecurityStamp、rowversion 和审计字段。
- 数据库以复合租户外键、过滤唯一索引、枚举/有效期/关联成对检查约束失败关闭；同类型组织编码唯一，客户/供应商/3PL 可以使用相同业务码而保持组织隔离。
- 新增 `/api/space/external-organization` 组织/成员管理 API，读取需要 `space:external:read`，变更需要 `space:external:manage`；跨租户用户、客商与组织引用不泄露存在性。
- OpenAPI、C# 与 TypeScript SDK 已更新；运行时客户端表面哈希为 `6011AA0FC2B4B2A81C5D915B1DEE1D0ADC84BE01BB8D2962A3D087B896E1EF76`。
- 功能门禁：Space Unit 224/224；Space Integration + KOUSQLSERVER 159/159、0 skipped；CP6.Tests 2703 passed / 17 environment-gated skipped；完整 18 项目 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift、TypeScript SDK 与运行时客户端表面均通过。
- 合并态聚焦门禁：领域 4/4、组织/成员内存与真实 SQL 6/6、权限/种子/ProblemDetails/OpenAPI 49/49、EF/SDK drift 与 TypeScript SDK 编译通过。交付报告见 `docs/space/reports/e09-s01-external-organization-membership.md`。
- 下一张建议卡为 E09-S02：实现 Organization Context、有效 Membership 与 Site/Floor/Zone/Owner/BusinessObject 组合 Grant，并在缺失、歧义或跨组织拼接时失败关闭。

## E08-S05 完成状态（2026-08-01）

- E08-S05 10,000 库位性能基线已完成并进入受控集成基线：功能提交 `cc1d8baf` + `24464fab`，no-ff 集成提交 `7a05c05f` + `675e485c`，起始基线 `5d37865a`。
- 锁定门槛：完整场景 ≤100 draw calls、Medium tier ≥50fps、≤3 秒可交互、标签 P95 ≤16ms/对象池 ≤200、拾取 P95 ≤150ms、10,000 条着色与运行态查询 ≤3 秒。
- 500 个货架框已从逐对象 `LineSegments` 合并为单个 wireframe `InstancedMesh`；完整标准仓 draw calls 从 535 降到 36。库位颜色缓冲在建桶时预分配，库存覆盖层走分桶批量着色并保留旧 ViewerHandle 回退。
- 硬件 WebGL 验收使用 Intel Iris Xe / D3D11：10,000 库位、36 draw calls、275ms 可交互、P95 帧间隔折算 83.3fps、标签 3.5ms、拾取 0.4ms、着色 3.0ms、35 个同屏标签、0 console errors。执行器检测到 SwiftShader 时拒绝形成 GPU PASS。
- 运行态服务现以精确 10,000 个 Published/Active 库位验证库存与任务查询各 20×500 分块且各自 ≤3 秒；既有 10,001 个不同库位在 WMS 调用前 400 拒绝。
- 功能分支门禁：Space Unit 220/220；Space Integration 105 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 106 files / 607 tests、CPU/硬件性能门禁、type-check、production build；WebApi/C#/TypeScript SDK build 与 SDK drift 通过。
- 合并态聚焦门禁：Viewer 19/19、CPU 性能 1/1、运行态 10,000/10,001 边界 2/2、follow-up 2/2、type-check 与 range whitespace 通过。交付报告见 `docs/space/reports/e08-s05-10000-location-performance.md`。
- 下一张建议卡为 E09-S01：外部组织与成员模型，使客户、供应商、3PL 可关联用户和租户。

## E08-S04 完成状态（2026-08-01）

- E08-S04 拣货任务与路径验收已完成并进入受控集成基线：功能提交 `9f7e38f8`，no-ff 集成提交 `994339a6`，起始基线 `944e465f`。
- 新增 `GET /api/space/design/v1/sites/{siteId}/runtime/tasks/path?taskId=...`；任务号必填且在 WMS 边界筛选，继续沿用 Published/Active、采纳身份、500 分块、10,000 上限及来源新鲜度。
- 响应提供 WMS 实际顺序、楼层/库区/坐标、跨层/跨区切换、总量与分区工作量，以及当前 Published 巷道拓扑；可用空结果与 `Unavailable` 严格区分，重复实际序号失败关闭为 502。
- Viewer 同时展示实际顺序、仅演示且不回写 WMS 的优化顺序、跨层/跨区和工作量；实际停靠点支持 Locator 跨层定位，残缺坐标不生成不完整优化路径。
- 当前 Design Revision 没有连接体拓扑，跨层段明确降级为近似直连并提示，不把近似路线伪装为精确结果。
- 功能分支门禁：Space Unit 220/220；Space Integration 105 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 105 files / 603 tests、type-check、production build；WebApi/C#/TypeScript SDK build 与 SDK drift 通过。
- 合并态聚焦门禁：runtime 47/47、OpenAPI/SDK 18/18、前端 9/9、type-check 与 SDK drift 通过。交付报告见 `docs/space/reports/e08-s04-task-path-acceptance.md`。
- 下一张建议卡为 E08-S05：10,000 库位性能基线，锁定场景交互、标签和批量查询门槛。

## E08-S03 完成状态（2026-08-01）

- E08-S03 物料/批次/容器定位验收已完成并进入受控集成基线：功能提交 `8d8f7e01`，no-ff 集成提交 `dfb6e93b`，起始基线 `faeacd4b`。
- 新增统一运行源端点 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory/locate`；物料、批次、容器至少一个，多个条件固定按精确 AND 匹配。
- 查询当前 Published/Active Space 库位并沿用采纳后的 WMS 身份、500 分块和 10,000 上限；响应按 Space 逻辑库位聚合，显式提供双身份/双编码、楼层、数量、匹配事实、命中库位数、楼层数与 E08-S02 来源新鲜度。
- 可用来源的零命中与 `Unavailable` 来源严格区分；不满足条件、非正库存或同一 WMS 身份多编码的适配器响应以 502 合同违例失败关闭。
- Viewer 搜索支持编码、物料、批次、容器；多结果按楼层分组，由用户选择候选后复用现有跨层 Locator，不再擅自跳第一条；旧并发响应不能覆盖新搜索。
- 功能分支门禁：Space Unit 220/220；Space Integration 101 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 103 files / 597 tests、type-check、production build；WebApi/C# SDK 0 warning / 0 error；TypeScript SDK 与 SDK drift 通过。
- 合并态聚焦门禁：runtime 44/44、OpenAPI/SDK 18/18、前端 5/5、type-check 通过。交付报告见 `docs/space/reports/e08-s03-inventory-locate.md`。
- 下一张建议卡为 E08-S04：拣货任务与路径验收，覆盖实际/优化顺序、跨区/跨层和工作量。

## E08-S02 完成状态（2026-08-01）

- E08-S02 库存来源、时间和延迟展示已完成并进入受控集成基线：功能提交 `9a478c7a`，no-ff 集成提交 `d4cd8a82`，起始基线 `bbe77f3e`。
- 3D Viewer 库存覆盖层已从旧楼层库存接口迁移到 E08-S01 统一运行源；按当前楼层 Space 逻辑库位 ID 查询和聚合，不以 WMS 编码漂移替换稳定身份。
- 运行源公开并显示来源类型、来源系统、运行连接、数据观察时间、CP6 接收时间、延迟、时钟超前，以及 Viewer 会话最近成功/最近失败/恢复状态。
- 统一源暂无容量、锁定和拣货流程事实，Viewer 只展示空/有库存，并把利用率明确标为占用估算；不伪造满、锁定或在拣状态。
- 功能分支门禁：Space Unit Release 220/220；Space Integration Release 96 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 102 files / 593 tests、type-check、production build；C# SDK 0 warning / 0 error；SDK 无 drift。
- 合并态聚焦门禁：runtime 40/40、OpenAPI/SDK 18/18、前端 20/20、type-check 通过。交付报告见 `docs/space/reports/e08-s02-runtime-freshness.md`。
- 下一张建议卡为 E08-S03：物料/批次/容器定位验收，覆盖多结果、空结果和跨层结果。

## E08-S01 完成状态（2026-07-31）

- E08-S01 统一运行态数据源已完成并进入受控集成基线：最终功能提交 `3df6b1d2`、no-ff 集成提交 `b2bb7a35`、设计提交 `636eb6d5`。
- 功能分支全量验证：Space Unit 220 passed / 0 failed / 0 skipped；默认 Space Integration 94 passed / 0 failed / 48 SQL 环境门禁 skipped；OpenAPI/权限/数据源合同聚焦 45 passed；Release 完整 solution build 0 error / 10 个既有 warning；SDK 无 drift；EF 无待迁移模型变化；feature range `git diff --check` 静默通过。
- 最终复审修复了生成 C#/TypeScript SDK 丢失合法 nullable 响应类型的问题；修复后 OpenAPI/权限 34/34、Client build 0 warning / 0 error、SDK 无 drift，并由原 API 复审者确认关闭。合并态门禁为 runtime/adapter unit 23/23、runtime/adapter/simulator integration 56/56、OpenAPI/权限 34/34、SDK 无 drift。
- 运行权威规则：当前 Published/Active Space 模型是空间与身份权威；生产 `Cp6SpaceWmsAdapter` 是库存/任务运行态权威；模拟器只允许显式选择/测试；Design Revision 不持久化库存、任务等运行事实。
- 已交付 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory` 与 `GET /api/space/design/v1/sites/{siteId}/runtime/tasks`，均要求 `space:model:read`，支持重复 `locationLogicalId` 筛选、Space/WMS 双 LogicalId 与双编码。
- 查询按 500 个位置分块、最多 10,000 个位置；来源/输出合同违例失败关闭为 502，适配器异常为可重试 503；明确 `Unavailable` 返回空 `Items` 并携带 `IsAvailable=false`，不与真实空结果混同。
- 该节记录 E08-S01 完成时的后续建议；E08-S02 现已由上方最新状态接续完成。E08-S01 交付报告见 `docs/space/reports/e08-s01-unified-runtime-source.md`。

## E07-S05 完成状态（2026-07-31）

- E07-S05 存量 WMS 采纳与绑定已完成：独立采纳账本、刷新、分页、单项/批量绑定、空位放置、差异 Issue 同步、rowversion 并发、权限/OpenAPI/SDK 和 Design V1 编辑器侧栏均已闭环。
- 功能提交 `15ccf992`，no-ff 集成提交 `389bf4ec`；交付报告见 `docs/space/reports/e07-s05-wms-adoption.md`。
- 验证：Space Unit 218；默认 Space Integration 56 passed / 48 SQL-gated skipped；WMS 聚焦 11/11，其中 KOUSQLSERVER 3/3；OpenAPI/权限 35/35；前端 98 files / 579 tests；production build、完整 solution build、EF model drift 和 SDK drift 均通过。
- E07-S01 至 E07-S05 已全部进入受控集成基线。该条记录的是 E07-S05 完成时的后续建议；E08-S01 现已由上方最新状态接续完成。

## Git

- Space E04 S06 功能/集成提交：`20f248bd` / `2b6ef127`
- Space E09 S05 功能/集成提交：`83798dcf` / `c658871c`
- Space E07 S05 功能/集成提交：`15ccf992` / `389bf4ec`
- Space E08 S01 功能/集成提交：`3df6b1d2` / `b2bb7a35`
- Space E08 S02 功能/集成提交：`9a478c7a` / `d4cd8a82`
- Space E08 S03 功能/集成提交：`8d8f7e01` / `dfb6e93b`
- Space E08 S04 功能/集成提交：`9f7e38f8` / `994339a6`
- Space E08 S05 功能/集成提交：`cc1d8baf` + `24464fab` / `7a05c05f` + `675e485c`
- Space E09 S01 功能/集成提交：`a599cfd7` / `09538ca3`
- Space E09 S02 功能/集成提交：`cae12c7e` / `feefa9cd`
- Space E09 S03 功能/集成提交：`88bc42d1` / `1850b2d8`
- Space E09 S04 功能/集成提交：`f045bd6f` / `c82d4fae`
- Space E10 S01 功能/文档/集成提交：`1c7aa0e2` / `1da17591` / `ec29d41f`
- Space E10 S02 功能/文档/集成提交：`e70c2715` / `86ad63bb` / `29a69a2b`
- Space E10 S03 功能/文档/集成提交：`10b16c51` / `8ce91d41` / `88efd23d`
- Space E10 S04 功能/文档/集成提交：`9a9802a8` / `f961d7e5` / `b4d5b81e`
- Space E10 S05 功能/文档/集成提交：`65c59555` / `53bea9b9` / `e270c2cc`
- Space E10 S06 功能/文档/集成提交：`0676ba4a` / `969e7c38` / `5f86edcb`
- Space E11 S01 合同/功能/文档/集成提交：`66b6c17f` / `53a07d46` / `a6d7a55c` / `8d4732e2`
- Space E11 S02 合同/功能/文档/集成提交：`3ccd2936` / `644293f1` / `034a1b1b` / `a2b47826`
- Space E11 S03 合同/功能/文档/集成提交：`3cf42534` / `419d3f6c` / `eea62de0` / `cf7bf778`
- Space E11 S04 合同/功能/文档/集成提交：`098fb54b` / `a7298e28` / `a552d05d` / `c19231db`
- Space E11 S05 合同/功能/文档/集成提交：`139c76b5` / `e8df8288` / `a0b247ab` / `cf35849c`

- 交付分支：`main`
- T6 通过 merge commit `d79a39c` 合入并推送；T7 冒烟修复为 `ffca422`
- Space 受控集成分支：`integration/space-v1-20260730`
- Space E00 + E01 S01–S03 集成提交：`539d56de`
- Space E01 S04 功能/集成提交：`bac76444` / `85792161`
- Space E01 S05 功能/集成提交：`3258d47f` / `36f534d9`
- Space E01 S06 功能/集成提交：`6daf1aeb` / `2ccdff7a`
- Space E02 S01 实验门禁功能/集成提交：`fe959066` / `3742fbff`
- Space E04 S01 功能/集成提交：`1d57a3b5` / `e8e84853`
- Space E04 S02 功能/集成提交：`20ee0af0` / `c1043d15`
- Space E04 S03 功能/集成提交：`b322e84a` / `39146c38`
- Space E04 S04 功能/集成提交：`9a87dc30` / `f9c7fd21`
- Space E07 S01–S03 功能/集成提交：`d06a8bd1` / `6e67a9d1`
- Space E07 S04 功能/集成提交：`74577015` / `6d751e0c`
- Space E13 S01 功能/集成提交：`8f7fc25e` / `ea161975`
- Space E13 S02 功能/集成提交：`cff25a25` / `94822669`
- Space E13 S03 功能/集成提交：`cebd401a` / `dca6e19c`
- Space E13 S12 功能/集成提交：`54456946` / `b33929fb`
- Space E13 S16 功能/文档/集成提交：`0549a1f2` / `6ec0c02a` / `ad4de0b0`
- Space E03 S01 功能/集成提交：`033e8872` / `8521a701`
- Space E03 S02 功能/集成提交：`f1310b40` / `e0cc4964`
- Space E03 S03 功能/集成提交：`9d0a59e7` / `3571f677`
- Space E05 S01 功能/集成提交：`5bb0cdfb` / `49dbabe3`
- Space E05 S02 功能/集成提交：`2fc03681` / `3d554852`
- Space E05 S03 功能/集成提交：`00021f0a` / `a1edecef`
- Space E05 S04 功能/集成提交：`85b57960` / `888de795`
- Space E05 S05 功能/集成提交：`856f138c` / `a3864d9c`
- Space 历史基线文档提交：`407dcbea`
- Space 后续候选安全检查点：`checkpoint/space-candidate-20260730`（`0d25da4d`，不得整包合入）
- 远端：`origin`（GitHub 私有仓库）
- 换机标签：`migration-2026-07-18-ready`
- 数据备份：Git LFS 三对象，已推送并校验

## 当前波：Space V1 受控集成

| 范围 | 状态 | 证据 |
|---|---|---|
| E00 S01–S04 | 已进入集成基线 | `539d56de`；事实清单、兼容护栏、数据源契约、审计/可观测性 |
| E01 S01–S06 | 已进入集成基线 | `539d56de` + `85792161` + `36f534d9` + `2ccdff7a`；版本/来源文件/Job Ledger、Published→Draft Clone、Design API v1、生成 SDK、文件安全扫描与保留清理 |
| E02 S01 | 部分进入集成基线，最终签收受阻 | `fe959066` + `3742fbff`；中立审计/压力/运行证据/preflight 已集成；另有 20 份可重复生成的合成开发 DXF（L1～L5 各 4 份，五种 DXF 文件头），但正式 DWG 黄金集、授权、供应商包/凭据和冻结 Worker 尚缺 |
| E03 S01–S03 | 已进入集成基线 | `033e8872` + `8521a701` + `f1310b40` + `e0cc4964` + `9d0a59e7` + `3571f677`；标准 Excel 模板、版本化字段映射、隔离上传、异步预检、结构化问题与受保护错误报告 |
| E04 S01–S04、S06 | 已进入集成基线 | `1d57a3b5` + `e8e84853` + `20ee0af0` + `c1043d15` + `b322e84a` + `39146c38` + `9a87dc30` + `f9c7fd21` + `20f248bd` + `2b6ef127`；安全底图、坐标标定、通用元素属性、统一批量编辑与补偿命令，以及同一 Design Scene 的 2D/3D 只读预览和实际渲染结构一致性证明 |
| E07 S01–S05 | 已进入集成基线 | `d06a8bd1` + `6e67a9d1` + `74577015` + `6d751e0c` + `15ccf992` + `389bf4ec`；版本化能力合同、CP6 真实适配器、持久化幂等账本、标准模拟器、确定性标准仓与存量 WMS 采纳/绑定 |
| E08 S01–S05 | 已进入集成基线 | `3df6b1d2` + `b2bb7a35` + `9a478c7a` + `d4cd8a82` + `8d8f7e01` + `dfb6e93b` + `9f7e38f8` + `994339a6` + `cc1d8baf` + `24464fab` + `7a05c05f` + `675e485c`；统一 Published 运行源、双身份、来源新鲜度、库存定位、任务路径与 10,000 库位性能基线 |
| E09 S01–S05 | 已进入集成基线 | `a599cfd7` + `09538ca3` + `cae12c7e` + `feefa9cd` + `88bc42d1` + `1850b2d8` + `f045bd6f` + `c82d4fae` + `83798dcf` + `c658871c`；外部组织/成员、组合 Grant、字段策略/脱敏、Published-only Portal、跨租户阻断矩阵，以及访问审计和授权有效期即时重验证 |
| E10 S01–S06 | 已进入集成基线 | `1c7aa0e2` + `1da17591` + `ec29d41f` + `e70c2715` + `86ad63bb` + `29a69a2b` + `10b16c51` + `8ce91d41` + `88efd23d` + `9a9802a8` + `f961d7e5` + `b4d5b81e` + `65c59555` + `53bea9b9` + `e270c2cc` + `0676ba4a` + `969e7c38` + `5f86edcb`；人员/设备事件与运行投影、3D 叠加、库存空间筛选，以及仓库 KPI、面积/占用、ABC 与异常快照 |
| E11 S01 | 已进入集成基线 | `66b6c17f` + `53a07d46` + `a6d7a55c` + `8d4732e2`；内部只读运营诊断、路径覆盖/折返/停留/观测重叠、诚实库位占用压力、隐私边界、审计权限和 Viewer DIAG 面板 |
| E11 S02 | 已进入集成基线 | `3ccd2936` + `644293f1` + `034a1b1b` + `a2b47826`；内部上架推荐、不可变证据、首因排除解释、精确合并与空库位候选、权限审计和 Viewer PUT 面板 |
| E11 S03 | 已进入集成基线 | `3cf42534` + `419d3f6c` + `eea62de0` + `cf7bf778`；内部人员调度建议、真实待分配任务与人员双时点、确定性最大基数匹配、不可变证据、首因排除和 Viewer DSP 面板 |
| E11 S04 | 已进入集成基线 | `098fb54b` + `a7298e28` + `a552d05d` + `c19231db`；OA 审批、提交/终审人分离、最终事实重验证、真实 `MobileTask` 整批分派、幂等回执与失败关闭 |
| E11 S05 | 已进入集成基线 | `139c76b5` + `e8df8288` + `a0b247ab` + `cf35849c`；实时执行状态、三层幂等回执、受限人工重试、安全整批补偿、权限审计和 Viewer 执行治理 |
| E13 S01–S03、S12、S16 | 已进入集成基线 | Provider/确定性端口、可审计 Run/Proposal/Decision/Usage 模型、可恢复 Worker 控制面、数据库并发槽与预算账本，以及不暴露密钥/URL 的租户策略和用量管理 UI |
| E05 S01–S05 | 已进入集成基线 | 通用元素、逐层货架、统一场景 DTO、版本化资产库及确定性参数化 3D 渲染 |
| E03 S04 以后、E04 S05、E06、E13 S04～S11/S13～S15/S17～S19 等剩余范围 | 候选证据或尚未实现 | E03-S04 与 E04-S05 等待 E02-S07/CAD 语义预览；其余按依赖逐卡推进。`0d25da4d` 只作提取来源，不得以候选报告替代集成验收 |

## 上一完成波：GR-VP

| 任务 | 状态 | 证据 |
|---|---|---|
| T1 标准一般用户角色种子 | 完成 | `ddcfa1ac`，7 测试 |
| T2 OA/WF v-permission | 完成 | `15823c38`，40 按钮/17 视图 |
| T3 ERP v-permission | 完成 | `4a48525e`，39 按钮/16 视图 |
| T4 MES v-permission | 完成 | `6e4ade1`，31 指令/12 视图/24 键 |
| T5 FIN v-permission | 完成 | `5732057`，66 指令/16 视图/51 键 |
| T6 PUR/PLAN/PUB v-permission | 完成 | `4bb7512` + `cf20d42`，37 页面级声明/12 视图/33 键；异步加载守权 |
| T7 部署与冒烟 | 完成 | API/Web 双镜像；A1 角色 SQL；`qa_general` 自审批/越权/403 冒烟 |

## 最近验证基线

- E11-S05 已推进至受控集成提交 `cf35849c`：合同 `139c76b5`、功能 `e8df8288`、文档 `a0b247ab`。功能分支全量门禁为 Space Unit 249/249、默认 Space Integration 230 passed / 62 SQL-gated skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 658 tests、完整 solution Release、生产构建、EF/SDK drift、TypeScript SDK strict no-emit 和差异检查通过；合并态服务/适配器 14/14、权限/合同/种子 35/35、前端 21/21、类型、SDK drift 与 EF pending model 通过。i18n 仍为 908 项既有欠账，本卡净新增 0。
- E11-S04 已推进至受控集成提交 `c19231db`：合同 `098fb54b`、功能 `a7298e28`、文档 `a552d05d`。功能分支全量门禁为 Space Unit 249/249、默认 Space Integration 224 passed / 62 SQL-gated skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 656 tests、完整 solution Release、生产构建、EF/SDK drift 与两个 TypeScript strict no-emit 通过；合并态审批服务/适配器 8/8、权限/合同/种子/基础设施 44/44、前端 19/19、类型、SDK drift 与 EF pending model 通过。i18n 历史欠账由 909 降至 908。
- E11-S03 已推进至受控集成提交 `cf7bf778`：合同 `3cf42534`、功能 `419d3f6c`、文档 `eea62de0`。Space Unit 249/249、默认 Space Integration 216 passed / 62 SQL-gated skipped、CP6.Tests 2752 passed / 17 environment-gated skipped、前端 118 files / 653 tests、完整 solution Release、EF/SDK drift 与生产构建通过；合并态引擎/运行时 6/6、服务/适配器 6/6、权限/审计/API/种子 23/23、前端 16/16、类型与 SDK drift 通过。
- E11-S02 已推进至受控集成提交 `a2b47826`：合同 `3ccd2936`、功能 `644293f1`、文档 `034a1b1b`。Space Unit 245/245、默认 Space Integration 211 passed / 62 SQL-gated skipped、CP6.Tests 2748 passed / 17 environment-gated skipped、前端 117 files / 648 tests、完整 solution 非增量构建 0 error / 10 条既有 warning、EF/SDK drift、两个 TypeScript strict no-emit、production build 与差异检查通过；合并态引擎 5/5、服务 6/6、权限/审计/契约/种子 34/34、前端 14/14 和 SDK drift 通过；i18n 欠账由 911 降至 909。
- E11-S01 已推进至受控集成提交 `8d4732e2`：只读运营诊断、Real-only 人员证据、路径/折返/停留/观测重叠、当前库位占用压力与真实容量不可用边界完成；Space Unit 240/240、默认 Space Integration 205 passed / 62 SQL-gated skipped、CP6.Tests 2744 passed / 17 environment-gated skipped、前端 116 files / 643 tests、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 通过。合并态引擎 4/4、服务 7/7、权限/审计/契约/种子 59/59、前端 12/12 和严格类型检查通过。35 个新界面键均有五语种子，i18n 欠账保持基线 911 项。交付证据见 `docs/space/reports/e11-s01-operations-diagnostics.md`。
- E10-S06 已推进至受控集成提交 `5f86edcb`：仓库 KPI、面积/占用口径、独立来源部分快照、ABC 分类和 Viewer 互斥覆盖完成；Space Unit 236/236、默认 Space Integration 198 passed / 62 SQL-gated skipped、本卡真实 SQL 3/3、CP6.Tests 2739 passed / 17 environment-gated skipped、前端 115 files / 639 tests、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 通过。合并态合同 23/23、Runtime/适配器 81/81、权限/OpenAPI 46/46、前端 25/25、类型检查和 SDK drift 通过。i18n 保留 881 项基线债务和本卡新增 30 项。交付证据见 `docs/space/reports/e10-s06-warehouse-overview.md`。
- E10-S05 已推进至受控集成提交 `e270c2cc`：货主、SKU、批次和容器精确 AND 空间筛选完成；运行合同 2/2、Runtime/适配器 68/68、权限/OpenAPI 45/45、前端 114 files / 632 tests、Space Unit 236/236、默认 Space Integration 190 passed / 61 SQL-gated skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK/TypeScript drift 通过，本卡真实 SQL 1/1。完整真实 SQL 矩阵 250 passed / 1 个已知基线失败。交付证据见 `docs/space/reports/e10-s05-inventory-spatial-filters.md`。
- E10-S04 已推进至受控集成提交 `b4d5b81e`：设备当前/告警投影、读取 API 和 3D 叠加完成；领域 2/2、设备服务 9/9、本卡真实 SQL 2/2、权限/审计/OpenAPI 70/70、前端 113 files / 629 tests、Space Unit 236/236、默认 Space Integration 189 passed / 60 SQL-gated skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 与两个 TypeScript strict no-emit 通过。完整真实 SQL 矩阵 248 passed / 1 已知基线失败；合并态设备 9/9、权限/审计/OpenAPI 70/70、前端聚焦 14/14、EF/SDK drift 通过。交付证据见 `docs/space/reports/e10-s04-device-runtime-overlay.md`。
- E04-S06 已推进至受控集成提交 `2b6ef127`：功能分支与合并态前端全量均为 108 files / 612 tests，聚焦 4 files / 13 tests、type-check 和 production build 通过；Space Unit 231/231、默认 Space Integration 140 passed / 55 SQL-gated skipped、Design Scene 真 SQL 3/3、Space Integration + KOUSQLSERVER 195/195 且 0 skipped、CP6.Tests 2720 passed / 17 environment-gated skipped、完整 solution 非增量构建 0 error / 10 条既有 warning，以及 SDK drift、TypeScript SDK strict no-emit 和差异门禁均通过。
- E09-S05 已推进至受控集成提交 `c658871c`：审计聚焦 48/48、Portal/真 SQL 合并态 16/16、Space Unit 231/231、Space Integration + KOUSQLSERVER 195/195、CP6.Tests 2720 passed / 17 environment-gated skipped、完整 solution 非增量构建 0 error / 10 条既有 warning、前端 106 files / 607 tests、EF/SDK drift 与 TypeScript SDK strict no-emit 均通过。
- E09-S04 已推进至受控集成提交 `c82d4fae`：功能分支门禁为 Space Unit 231/231、Space Integration + KOUSQLSERVER 187/187 且 0 skipped、CP6.Tests 2713 passed / 17 environment-gated skipped、完整 solution 0 warning / 0 error、前端 106 files / 607 tests、EF/SDK drift 与 TypeScript SDK strict no-emit；合并态聚焦门禁为 16/16、42/42、完整 solution 0 error / 10 条既有 warning、前端 607/607 及 EF/SDK drift。
- E09-S03 已推进至受控集成提交 `1850b2d8`：功能分支门禁为 Space Unit 231/231、Space Integration + KOUSQLSERVER 181/181 且 0 skipped、CP6.Tests 2711 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、C#/TypeScript SDK 编译与 S02→S03 幂等增量 SQL 双执行通过；合并态聚焦门禁为 3/3、22/22、84/84、前端 607/607 及 EF/SDK drift。
- E09-S02 已推进至受控集成提交 `feefa9cd`：功能分支门禁为 Space Unit 228/228、Space Integration + KOUSQLSERVER 169/169 且 0 skipped、CP6.Tests 2703 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、C#/TypeScript SDK 编译与 S01→S02 幂等增量 SQL 双执行通过；合并态聚焦门禁为 4/4、10/10、35/35 及 EF/SDK drift。
- E09-S01 已推进至受控集成提交 `09538ca3`：功能分支门禁为 Space Unit 224/224、Space Integration + KOUSQLSERVER 159/159 且 0 skipped、CP6.Tests 2703 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、TypeScript SDK 和运行时客户端表面通过；合并态聚焦门禁为 4/4、6/6、49/49 及 EF/SDK drift。
- E08-S04 已推进至受控集成提交 `994339a6`：功能分支门禁为 Space Unit 220/220、Space Integration 105 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 105 files / 603 tests、type-check、production build、WebApi/C#/TypeScript SDK build 与 SDK drift；合并态聚焦门禁为 runtime 47/47、OpenAPI/SDK 18/18、前端 9/9、type-check 和 SDK drift。
- E08-S03 已推进至受控集成提交 `dfb6e93b`：功能分支门禁为 Space Unit 220/220、Space Integration 101 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 103 files / 597 tests、type-check、production build、WebApi/C#/TypeScript SDK build 与 SDK drift；合并态聚焦门禁为 runtime 44/44、OpenAPI/SDK 18/18、前端 5/5 和 type-check。
- E08-S02 已推进至受控集成提交 `d4cd8a82`：功能分支门禁为 Space Unit Release 220/220、Space Integration Release 96 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 102 files / 593 tests、type-check、production build、C# SDK build 与 SDK drift；合并态聚焦门禁为 runtime 40/40、OpenAPI/SDK 18/18、前端 20/20 和 type-check。
- E08-S01 已推进至受控集成提交 `b2bb7a35`：功能分支全量门禁为 Space Unit 220、默认 Space Integration 94 passed / 48 SQL-gated skipped、OpenAPI/权限/数据源合同 45、完整 solution build 0 error / 10 个既有 warning、EF/SDK drift 均通过；合并态聚焦门禁为 23/23、56/56、34/34 和 SDK 无 drift。
- 历史 E04-S04 验证快照：当时集成代码提交为 `f9c7fd21`。合并态完整 solution 构建 0 error / 10 个既有 warning；Space Unit 213 passed，默认 Space Integration 48 passed / 45 SQL-gated skipped，Design Scene 真实 SQL 3/3 passed；OpenAPI/权限 25/25 passed；前端 96 files / 575 tests、type-check、production build 通过；EF 无待迁移模型变化，SDK drift 通过。
- E02 S01 实验门禁已推进至 `3742fbff`：中立工具 10/10 测试通过，Aspose 隔离实验适配器构建 0 warning / 0 error；5 个冻结 Seed 完整性通过，50MiB 与 100 万实体压力资产生成通过。严格 readiness 按预期退出 `3`，ODA/APS 模板 preflight 按预期退出 `4`，表明外部签收条件仍未满足。
- E02 合成开发语料新增 `development-v2.0.0`：仓库内生成器可重复生成 20 份 DXF，L1～L5 各 4 份，覆盖 AC1009/AC1015/AC1021/AC1027/AC1032 以及块/属性/填充/样条/椭圆/标注/XRef 等开发场景。工具测试 12/12、数据包完整性、哈希、句柄和 DXF 文件头矩阵通过；清单明确 `countsTowardReleaseGate=false`，不替代原生 DWG、供应商授权和正式黄金集。交付证据见 `docs/space/reports/e02-synthetic-development-cad-corpus.md`。
- E07 S01–S03 已推进至 `6e67a9d1`：Release 全解构建 0 error（7 个既有测试 warning），Space Unit 73 passed，Space Integration 35 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2674 passed / 17 environment-gated skipped，Client 71 passed，EF 模型与 Migration 一致；新增代码精确格式门禁通过。
- E07 S04 已推进至 `6d751e0c`：500 货架、10,000 库位、100 SKU、5,000 库存记录、100 拣货任务和 6 个固定故障样本由同一固定种子生成；两次独立生成的 17 个文件差异为 0，干净检出后的 Manifest 16 个受管文件哈希错误为 0。合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 79 passed，Space Integration 40 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2680 passed / 17 environment-gated skipped，Client 71 passed。
- E13 S01–S03、S12 已完成 Provider 安全端口、运行审计模型、可恢复 Worker 控制面、三并发槽和日/月预算原子账本；外部 Provider、CAD IR、输出校验与 Apply 仍未提前启用。
- E03 S01–S03 与 E13-S16 已推进至集成提交 `3571f677` 和 `ad4de0b0`；最新门禁为 CP6.Tests 2733 passed / 17 environment-gated skipped、前端 112 files / 622 tests、完整 solution 0 warning / 0 error、EF/SDK drift 通过。E13-S16 交付证据见 `docs/space/reports/e13-s16-ai-policy-budget-usage-ui.md`。
- E10-S01 已推进至集成提交 `ec29d41f`：人员事件合同、追加式账本和双时间游标投影完成；Space Unit 234/234、默认 Space Integration 175 passed / 58 SQL-gated skipped、本卡真实 SQL 2/2、CP6.Tests 2734 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK/TypeScript drift 通过。交付证据见 `docs/space/reports/e10-s01-personnel-event-contract.md`。
- E05 S01–S05 已完成通用元素、非均匀逐层货架、Design Revision 权威场景、System/Tenant 版本化资产库和 `space-parametric-v1` 确定性前端渲染链；资产不加载外部 URL 或脚本。
- E04 S01–S04 已完成 PDF/PNG/JPG 底图、安全扫描、挂接、标定、通用元素属性，以及货架/元素共享的框选、对齐、等距、旋转、删除、阵列和补偿式撤销/重做；命令继续保持 Draft/revision 失败关闭、协议幂等、整批原子性和逐命令 before/after 审计。默认扫描器继续失败关闭，多副本生产环境必须配置真实扫描引擎与共享耐久卷。
- Space 集成前端：type-check 通过，96 files / 575 tests passed，production build 通过；仅有既有大 chunk 提示。
- CP6.Tests 全量本轮为 2682 passed / 6 个既有 RFQ 固定日期失败 / 17 environment-gated skipped；同一 RFQ 失败已在 S03 前的 `f8dff096` 基线复现，不是 Space 回归。
- 后续候选检查点 `0d25da4d` 已独立通过更大范围候选回归，但它仍不是实现真相，也不授权整包合并。
- 后端在 GR-VP T1 报告中：2220 passed / 5 skipped。
- 前端在 T7 干净 `main` 重新验证：73 files / 488 tests passed，type-check 0，2649 modules production build 通过；在线 Web 与新 chunk 均为 200。
- T6 后端权限 oracle：11/11 passed。
- T7 真实权限链：4 菜单、8 动作；本人待办 200、他人待办 400、无权端点 403；测试流程数据已清理。
- 这些是最近任务报告基线，不代表生成本知识库时重新运行了全量测试。

## 数据状态

- `CP6DB`、`CP6DB_OA`、`CP6DB_SpaceQA` 已于 2026-07-18 备份。
- 三份均通过 SQL Server `RESTORE VERIFYONLY WITH CHECKSUM`。
- 新机恢复后需重新轮换 Secrets 并做登录、权限、i18n 与关键业务冒烟。
- 当前运行库 `CP6DB` 的租户注册表只有 `DEFAULT/A1`；RoleId=10 为 1 角色/4 菜单/8 动作，admin 为 148 菜单/323 动作。`qa_general` 保留为 A1 常驻测试用户。

## 下一动作

以 `624c1511` 为本次路线图审计起点，其中 E12-S05 no-ff 功能集成为 `c4b139ab`。E12-S05 已完成 glTF 2.0 GLB 标准交换导出、远端功能备份、受控集成、合并态复验、五语入口和临时资源清理。E03-S01～S03、E10-S01～S06、E11-S01～S06、E12-S01～S05，以及 E13-S01～S03、S12、S16 已完成。后续可执行性审计见 `docs/space/reports/2026-08-02-post-e12-s05-roadmap-audit.md`：当前没有一张满足全部前置与正式输入的未完成实现卡。开发侧现已有 20 份完全合成的 DXF 语料，可继续解析器、映射、问题、IR、UI 和回归开发；它们不计发布门禁。E12-S06 和 E02-S01 最终签收仍等待独立正式黄金样本、原生 DWG、明确 SDK/供应商授权和冻结试验 Worker；E02-S02～S08、E03-S04～S05、E04-S05、E13 CAD/Apply 后续与 E06-S01～S06 均被该链直接或传递阻断。E09 技术 S01～S05 已完成，跨职能 GA 签字仍由发布治理完成；发布 SQL 环境跳过项也不得记作通过。i18n 当前有 908 项显式快照债务，本卡净新增 0。下一步可先用 `development-v2.0.0` 进行不依赖供应商的开发准备，同时接收并审计正式解阻包；在授权前不创建生产 CAD/DWG 适配器。禁止把候选检查点 `0d25da4d` 整包合入，GR-VP T1–T7 不要重做。
