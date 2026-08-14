# Space Studio WP3 CAD 映射确定性重放快照

日期：2026-08-14

范围：sealed Preparation 到后台 Parse Job 的映射重放输入

接受状态：仓库实现完成；真实 Provider 与 Site CAD GA 仍为 No-Go

## 结论

CAD 起始向导允许用户确认图层覆盖。此前 Preparation 只保存 `MappingPreviewSha256`，后台 Job 能验证“期望哪个结果”，却没有携带产生该结果的覆盖内容，因此真实 Provider 无法仅凭冻结 Job 输入确定性重放用户确认的映射。

本任务新增版本化、规范 JSON 的 `SpaceCadMappingReplaySnapshotV1`，把以下内容作为一个 SHA-256 密封快照保存并传入后台 Job：

- Tenant、Source、Mapping Profile ID/Version 与 Profile Definition SHA-256；
- Inventory、Source Structure 与期望 Mapping Preview SHA-256；
- 用户确认的完整 Layer Overrides，按 Layer ID 规范排序；
- 对上述内容计算的 Snapshot SHA-256。

Preparation 在语义预览 Ready 后才创建快照，并与 Base Content Revision/Hash 一起持久化。唯一 Parse 启动接口只读取服务器保存的快照；客户端不能提交、替换或补造快照内容。

## 执行边界

- 新启动的 Parse Job 使用 payload schema v4，并原样携带 Preparation 的重放快照。
- Parse 启动前复核快照的 Tenant、Source、Profile、Definition Hash 和 Mapping Preview Hash；快照缺失、损坏或身份不一致时返回稳定 422，零 Job 写入。
- Worker 在打开原始 CAD 和调用 Provider 前再次验证同一组身份及 Job input hash；无效 v4 payload 按 Input failure 失败，Provider 调用次数保持零。
- schema v2/v3 历史 Job 保持显式只读/执行兼容；新 Job 不再生成旧 schema。schema v4 不允许降级为“只有哈希、没有重放内容”。
- Provider 适配器仍须从不可变 Profile ID/Version 取得定义并核对 Definition Hash，使用快照内的覆盖重新生成 Mapping Preview，再调用 `ValidateReplay` 核对全部输入/输出 Hash 后才能生成语义产物。

## 数据与迁移

- `Space_CadParsePreparation.MappingReplaySnapshotJson`：`nvarchar(max)`、必填；历史短期 Preparation 使用空字符串保留读取兼容，但不能启动新的 schema v4 Job。
- EF Migration：`20260814060254_SpaceCadMappingReplaySnapshot`。
- 幂等 SQL：`CP6.Space.Infrastructure/Migrations/Scripts/20260814060254_SpaceCadMappingReplaySnapshot.sql`。
- 迁移只新增上述列；Down 只删除上述列。没有修改已发布迁移，也没有 OpenAPI/SDK 变更。

## 自动化证据

- Mapping 快照聚焦单测：18 passed / 0 failed / 0 skipped。
- CAD Preparation/Parse 聚焦集成测试：15 passed / 0 failed / 0 skipped。
- 覆盖规范序列化、未知/重复字段、内容篡改、空覆盖项、完整覆盖重放、Preparation 持久化、Start v4 传递、服务器快照损坏零 Job、Worker v4 缺快照零 Provider 调用，以及历史 v3 明确兼容。
- 全量 Space Unit：512 passed / 0 failed / 0 skipped。
- 全量 Space Integration：313 passed / 0 failed / 106 SQL/environment-gated skipped。
- CP6.Tests：2,916 passed / 0 failed / 19 environment-gated skipped。
- Release solution：0 warning / 0 error；EF pending-model 无漂移；GA 索引结构验证通过且仍派生 `NoGo`；`git diff --check` 通过。

## 未关闭门禁

该快照只关闭真实 Provider 接入前的确定性输入缺口，不提供 ODA、APS 或其他生产转换器。尚未取得真实 Worker 注册、Provider 评分、Site 审批、20 份黄金 CAD、真 SQL/真实 WMS、双仓 Pilot 或五方签字，因此 WP3 实现状态保持 `Partial`、接受状态保持 `Pending`，核心 GA 继续 `NoGo`。
