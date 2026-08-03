# E02-S06 CAD 基础语义解析器开发切片

日期：2026-08-03

## 交付结论

CP6 已在 E02-S05 集成基线 `b3c45a8f` 上完成 E02-S06 开发侧只读语义提案链，功能提交为 `c8e2ae87`。Prepared CAD IR、E02-S04 Inventory、不可变 Mapping Profile 与 E02-S05 Mapping Preview 现在可以经过同一条失败关闭链，生成带临时 `previewObjectId`、规范几何、来源、采用规则、默认值、置信度和选择状态的统一语义预览。

该产物明确是 `IsReadOnlyPreview=true` 的临时提案，不创建永久 LogicalId，不调用 `SpaceContext`、仓储或 `SaveChanges`，也不写 Draft。本切片不是正式 E02-S06 验收。

## 本次实现

1. 新增 `SpaceCadSemanticPreviewV1` 合同：绑定 Tenant、Floor、Source SHA、Coordinate Transform SHA、Inventory SHA、Profile ID/Version/Definition SHA、Mapping Preview SHA 和自身 Semantic Preview SHA；内容篡改、跨租户或任一上游工件错配均失败关闭。
2. 每个提案使用由 `MappingPreviewSha256 + SourceRef + Mapping Decision` 推导的确定性临时 ID；它不是永久 LogicalId。同一输入重复解析产生相同 JSON 字节和 SHA-256。
3. 统一输出 `Element/Zone/Aisle/Rack` 四类未来草稿对象，覆盖标准 Wall、Column、Door、Dock、Zone、Aisle、Rack 语义；不会把 Zone/Aisle/Rack 错塞进通用 `SpaceElementTypes`。
4. 几何在 `LOCAL_MM_Z_UP` 中量化为整数毫米，支持 Point、Path、Polygon、Circle、Arc、BlockInstance；多边形去连续重复点、固定逆时针方向并旋转到规范起点，零长度路径和零面积边界显式 Rejected。
5. 实现 DirectGeometry、Centerline、ClosedBoundary、InsertionPoint 与 BlockFootprint。真实块范围可生成规范多边形；若开发转换器只有插入点，则保留完整块实例仿射变换，置信度封顶为 `0.69` 并产生 Warning，不伪造货架宽深高。
6. Block 规则逐个引用重新检查受控属性。真正命中的 Block 规则优先于 Layer 规则且每个 SourceRef 只生成一个提案；未满足 Block 属性条件的引用回退到其 Layer 规则，不把某个引用的属性命中错误扩展到同名块的全部引用。
7. 置信度边界固定为：`>=0.90` AutoAccepted、自动选中；`0.70–0.89` Candidate、可确认且 Warning；`<0.70` Candidate-only，不进入确认集。无法解释或不支持的图元保留 Rejected Item 和精确 SourceRef 问题，不静默丢弃。
8. 必需来源虽然存在、但所有对应几何均 Rejected 时产生 Blocking；无 Blocking 且至少一个 `>=0.70` 提案时才 `ReadyForConfirmation=true`。
9. 新增 `parse-dev-semantic` 开发命令和 `semantic-preview.schema.json`。无 Migration、WebApi、权限、持久化、Draft Apply、供应商 SDK 或外部 AI Provider。

## 样例 13 连续证据

输入：合成开发语料 `13-automated-warehouse.dxf`。

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`；
- CAD IR SHA-256：`b6aa6501ea67e9e3b9622c8838eb28b4e3569ca79f777ff77fe44987e0614310`；
- Coordinate Transform SHA-256：`b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`；
- Inventory SHA-256：`634329583747825b5c40c37402e03cdfa046c6f3e54f3d0ae2a4eb8faa9697a9`；
- Profile Definition SHA-256：`732eef8a1014e35428d427c639dce4936936087a08c484b23d67275c08de59d1`；
- Mapping Preview SHA-256：`98a0a3153af112563a3075dd9ee9fff1f113d122d22f03b89b399ba04d8009ca`；
- Semantic Preview SHA-256：`e398d192aa4d7f8cb5e92c18ac60dd6ae2ea667a338ee1e99eece0f39befc866`；
- Semantic JSON 文件 SHA-256：`75845d1213ec036e9d1e7b89c3b97677921fd02f43d10faf0c07868c187202ea`，31,085 bytes；重复运行文件哈希完全相同。

结果：22 个源对象中 21 个进入统一提案，13 AutoAccepted / 8 Candidate / 0 Rejected，13 Confirmable / 13 Selected，8 Info / 8 Warning / 0 Blocking，`ReadyForConfirmation=true`。目标分布为 Wall 1、Column 8、Door 1、Dock 1、Equipment 2、Rack 8；几何分布为 Path 2、Polygon 3、Circle 8、BlockInstance 8。8 个 Rack 块没有真实轮廓，全部按设计保留实例变换、降为 0.69 候选，不伪造尺寸。

## 门禁

- E02-S06 聚焦：6 passed / 0 failed / 0 skipped；覆盖七类标准语义、逐引用 Block 属性、Block 优先且不重复、三段置信度、零长度/不支持图元、必需来源失败关闭、跨租户/工件篡改和确定性哈希；
- 20/20 合成 DXF 连续完成转换、坐标确认、清单、11 规则映射和语义解析，自动断言累计至少 100 个语义提案；
- CAD 实验工具完整测试：23 passed / 0 failed / 0 skipped；
- Space Unit 完整测试：322 passed / 0 failed / 0 skipped；
- 完整 solution Release 非增量构建：0 error / 10 条既有 warning；
- 受影响 C# 文件 `dotnet format --verify-no-changes`、Schema JSON 解析和 `git diff --check` 通过。

## 正式边界与下一步

正式 E02-S06 仍等待：

- E02-S01 授权原生 DWG/DXF 适配器、冻结隔离 Worker 与独立正式黄金集；
- E02-S02～S05 的生产 Artifact、流式大文件、持久化、租户/ModelVersion/Floor 权威关联和正式复杂图纸证据；
- 动态/匿名/嵌套块、真实块定义范围、ByLayer/ByBlock 属性、XRef 和曲线精度的正式解析；
- 将已确认提案事务性写入同租户 Draft 的命令、权限、审计、业务编码/父引用校验与回滚；
- E02-S07 的可视问题定位、人工修正、锁定和 `SourceRef + userCorrectionVersion` 重放；
- E02-S08 的持久化幂等键、任务取消与安全重试。

等待 CAD 外部解阻包期间，可继续 E02-S07 开发侧“问题定位与修正预览”切片，但不得把当前开发产物标记为正式 CAD 验收或直接 Apply 到 Draft。
