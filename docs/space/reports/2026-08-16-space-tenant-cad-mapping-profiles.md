# Space 租户私有 CAD Mapping Profile 报告

日期：2026-08-16

任务分支：`codex/space-tenant-cad-mapping-profiles`

## 结论

详细 Spec LM-FR-013 的仓库实现已闭环。CAD Mapping 不再只有一个进程内 System Profile：System 方案保持只读，租户可以在 Space Studio 中复制系统方案、结构化编辑图层/块规则、启停方案，并将每次修改保存为不可变新版本。Preparation、后台 Parse 与管理 UI 继续消费同一 `SpaceCadMappingProfileV1`、规范排序和 Definition SHA-256，没有建立第二套映射定义。

该结论只覆盖仓库实现与真实 SQL 自动化。真实 DWG/DXF、Site 主备 Provider、20 份授权黄金 CAD、双仓 Pilot 和五方签字仍未完成，因此 WP4 继续 `Partial/Pending`，核心 GA 保持 72% / `NoGo`。

## 数据与并发权威

- `Space_LayerMappingProfile` 保存租户归属、当前名称/版本和 SQL RowVersion；同一租户的活动名称唯一。
- `Space_LayerMappingProfileVersion` 保存完整规范 Profile JSON、Definition SHA-256、复制来源、创建人/时间；`(TenantId, ProfileId, Version)` 唯一。
- Profile/Version 使用 Tenant 复合外键和全局 Query Filter；其他租户无法读取、选择或复制该方案，服务返回稳定 `SPACE_CAD_MAPPING_PROFILE_NOT_FOUND`。
- System Profile 不入租户表且不可更新；租户创建可绑定 System 或本租户来源。更新必须携带 Expected RowVersion，并以 Serializable 事务追加恰好下一个版本。
- 保存请求绑定 Idempotency-Key 与规范 Request Hash；同输入重放不追加版本，不同输入复用键返回 409。
- 版本实体为 append-only 证据；`SpaceContext.SaveChanges` 与 `SaveChangesAsync` 均拒绝修改或删除旧版本。
- 已发布迁移未修改；新增 `20260816054703_SpaceCadTenantMappingProfiles` 同时提供 Up/Down、约束、索引和模型快照。

## 接口与用户行为

- `GET /api/space/design/v1/mapping-profiles/cad`：列出 System 与当前租户的当前版本，包括启停、规则、哈希、RowVersion 和审计。
- `GET /api/space/design/v1/mapping-profiles/cad/{profileId}?version=`：读取当前或历史不可变版本。
- `POST /api/space/design/v1/mapping-profiles/cad`：复制或追加版本；要求 `space:model:edit`、Idempotency-Key，返回 Created/Replay 状态。
- CAD 起始向导内可选择管理方案，复制 System Profile、编辑规则 ID/优先级/来源/匹配/模式/目标/几何/置信度/必须标记和高级属性条件/默认尺寸。
- 保存成功后向导重新加载启用 Profile 并选中新版本；已有 Preparation Preview 被标为过期，必须重新生成和确认。

## 自动化证据

- CAD Profile 服务：5/5 passed，覆盖系统只读、复制、版本追加、幂等、不可变旧版本、当前/历史目录、RowVersion 要求和跨租户读取/复制拒绝。
- CAD Profile SQL：聚焦 1/1 passed；全量 SQL Server 17 LocalDB Space Integration 453/453 passed、0 skipped，耗时 6 分 04 秒。
- 权限与 Design V1 OpenAPI 聚焦：95/95 passed；新增路径、必填 body/header、规则/请求/响应 schema 和 controller allowlist 均锁定。
- Space Unit：540/540 passed。
- CP6.Tests：2,933 passed；19 个既有环境门禁 skipped。
- Web：172 个文件、866 个测试 passed；新增 API/管理器与既有 CAD 向导聚焦 7/7 passed。
- Vue TypeScript 与 production build passed；OpenAPI、C#/TypeScript SDK 无漂移；EF 无 pending model changes。
- 完整 `CP6.slnx` Release build：0 warning / 0 error。

## 未关闭范围

- LM-FR-010～011、014～016、019/019A 仍须按详细 Spec 与真实三路径证据继续审计和实现。
- 本机 AutoCAD Core Console 仍是开发转换链，不是 Site 已认证 Provider；GUI `acad.exe` 的 HashMismatch、许可/安全审批和独立备 Provider 仍待关闭。
- 真实授权 CAD/Excel/PDF、Iris Xe Viewer、CP6 WMS、双仓连续 14 天 Pilot 与产品/QA/WMS/架构/安全签字均未完成。

因此 LM-FR-013 的仓库实现完成，但核心 GA 仍为 72% / `NoGo`。
