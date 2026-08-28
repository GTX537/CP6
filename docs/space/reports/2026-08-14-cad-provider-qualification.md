# Space Studio WP3 CAD Provider 资格证据与主备排名

> 当前口径：本文记录 2026-08-14 的历史双 Provider 合同。Lean Core GA
> Schema 3 已由 2026-08-27 的单 Primary 合同取代；一个满足全部硬门禁且
> 资格分不低于 80 的 Primary 即可令 Core GA Provider 能力 Ready，Backup
> 仅是可选的 GA 后韧性增强。

日期：2026-08-14

任务分支：`codex/space-cad-provider-qualification`

基线：`main@623c7d51ad4d55c89fa5a85a88b8a5aa84174c94`

## 结论

本任务关闭 WP3 的一个仓库规则缺口：Site 管理员不能再只凭 Provider Key 和角色把任意运行注册标为 Primary/Backup。新认证必须携带 ADR-0001 资格证据，服务端强制四项硬门禁、80 分阈值、同一冻结评测基线和确定性排名；执行路由只使用资格完整的记录。

该结论不代表任何真实 Provider 或 Site 已通过验收。生产注册仍默认为空，GA 证据索引继续保持 WP3 `Partial/Pending` 和整体 `NoGo`。

## 冻结规则

- Licensing、Security、Data Region、Deletion/Retention 必须全部通过。
- 资格总分必须在 80–100；权重继续以 ADR-0001 为唯一权威，本任务不创造第二套评分规则。
- 每条记录绑定评分规则版本、20 份黄金集 SHA、冻结 Worker/环境 SHA 和不可变资格证据引用。
- 同一 Site 的 Primary/Backup 必须使用完全相同的规则版本、黄金集和冻结环境。
- Primary 最终分必须严格高于 Backup；Primary 更低或两者并列均返回 `SPACE_CAD_PROVIDER_CONFIGURATION_INVALID`，零配置写入。
- 本节以下规则是 2026-08-14 的历史行为；当前 `cad-provider-adr-0001-v2`
  下，一条完整合格且运行版本一致的 Primary 即可令 `CadGaReady=true`。

## 兼容与迁移

迁移 `20260814051514_SpaceCadProviderQualificationEvidence` 只为现有认证表增加资格列和 0–100 数据库约束，提供完整 `Down`。历史行的资格引用和分数保持 `NULL`，四项通过状态默认 `false`；系统不会用默认值伪造历史通过记录。

能力接口把历史行报告为 `CAD_PRIMARY_QUALIFICATION_INCOMPLETE` / `CAD_BACKUP_QUALIFICATION_INCOMPLETE`，`CanPrepareCad` 和 `CadGaReady` 均失败关闭。`SpaceCadProviderRouter` 同样排除资格不完整记录，避免 UI 阻断与后台执行口径分裂。

## 合同与安全

- `PUT /api/space/design/v1/sites/{siteId}/cad-provider-configuration` 的认证输入新增全部必填资格字段。
- `GET /api/space/design/v1/sites/{siteId}/cad-capability` 返回总分、规则/数据集/环境标识、证据引用和四项状态，不返回 Secret 内容。
- 资格认证继续受 `space:model:provider:manage`、Tenant/Site scope、Expected Revision、Site applock、Serializable 事务和 Idempotency-Key 保护；认证实体仍不可修改或删除。

## 自动化范围

- 聚焦服务测试覆盖正常双链、低于 80、主备同分、主链低分、冻结基线混用、硬门禁失败、Revision/幂等和证据不可变。
- SQL Server 用例新增历史资格缺失失败关闭和幂等迁移脚本路径；没有 `CP6_TEST_SQLSERVER` 的机器只编译并 skip，不能冒充真库证据。
- OpenAPI Required 字段、C#/TypeScript SDK、前端能力类型和向导/E2E fixture 同步。

| 门禁 | 结果 |
|---|---|
| Release solution | 通过，0 warning / 0 error |
| Provider 聚焦 | 12 passed / 2 SQL skipped |
| Space Unit | 506 passed |
| Space Integration | 310 passed / 106 environment-gated skipped |
| CP6.Tests | 2,916 passed / 19 environment-gated skipped |
| Client | 71 passed |
| Web Vitest | 775 passed |
| Space Studio Playwright | 13 passed |
| Vue type-check / production build | 通过 |
| OpenAPI + C#/TypeScript SDK drift | 通过 |
| EF pending-model changes | 无漂移 |
| GA 证据索引 | 结构通过，派生结果仍为 `NoGo` |

## 仍为 No-Go

- 未接入或认证真实 ODA、APS 或评分后替代者。
- 未取得客户、租户、法务、采购、安全、数据区域和删除保留批准材料。
- 未在同一冻结 Worker 上对 20 份授权 CAD 运行两条真实链并形成正式分数。
- 未在真实 SQL Server、CP6 WMS、两个 Pilot Site 或五方签字流程中接受本能力。
