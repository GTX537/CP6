# E03 S02 Excel 字段映射方案完成报告

- 状态：Complete
- 日期：2026-08-02
- 集成分支：`integration/space-v1-20260730`
- Migration：`20260802103430_SpaceE03S02ExcelMappingProfiles`
- API / OpenAPI / SDK：新增 Excel 映射方案查询、版本读取、表头预览与租户方案保存端点，并同步 C#、TypeScript 客户端
- 范围：系统标准映射、租户私有映射、不可变版本、表头预览、并发与幂等保护

## 1. 交付结果

E03 S02 已完成以下能力：

1. 提供只读系统标准映射方案，方案标识固定为 `00000000-0000-0000-0000-000000030001`，覆盖 E03 S01 冻结的六张工作表和目标字段。
2. 租户可以复制、保存私有映射方案；方案版本采用只追加模型，已经保存的版本不可修改或删除。
3. 映射项支持来源工作表、表头、目标字段、类型、格式、默认值、主键、引用、枚举、单位，以及空值、重复值和未知字段处理策略。
4. 预览接口只接收工作表名称和表头快照，返回缺失、重复、未知及已映射字段诊断；不持久化、不记录 Excel 文件或单元格内容。
5. 保存接口实施租户隔离、幂等键、行版本并发控制、审计记录和输入校验；系统方案不能被租户覆盖。
6. 前端新增 Design V1 Excel 映射 API 适配器，可查询方案、预览自定义表头并保存租户方案，为 E03 S03 数据预检界面提供稳定入口。

## 2. API 与权限

| 端点 | 用途 | 权限 |
|---|---|---|
| `GET /api/space/design/v1/mapping-profiles/excel` | 列出系统与当前租户可见方案 | `space:model:read` |
| `GET /api/space/design/v1/mapping-profiles/excel/{profileId}?version=` | 读取指定方案及不可变版本 | `space:model:read` |
| `POST /api/space/design/v1/mapping-profiles/excel/preview` | 按表头快照预览字段匹配与诊断 | `space:model:read` |
| `POST /api/space/design/v1/mapping-profiles/excel` | 保存或追加租户私有方案版本 | `space:model:edit` |

保存动作进入 Space 审计链路；OpenAPI 明确了请求、响应和错误模式，Design V1 合同现共 52 个操作。

## 3. 数据模型与约束

- `SpaceExcelMappingProfile` 保存租户归属、方案身份、显示名称、当前版本和并发令牌。
- `SpaceExcelMappingProfileVersion` 保存完整映射快照；应用层与 `SpaceContext` 双重阻止更新或删除既有版本。
- 系统标准方案由代码定义，不依赖租户数据库数据，也不允许通过保存接口修改。
- 关系型数据库保存使用可串行化事务，方案、版本、最终响应和幂等记录在同一事务内提交。
- 后续导入作业将固定引用 `ProfileId + Version`，避免方案新版本改变历史作业语义。

## 4. 验收证据

| 门禁 | 结果 |
|---|---|
| 映射服务聚焦测试 | 7 passed / 1 SQL 环境门禁 skipped |
| 控制器与权限聚焦测试 | 18 passed |
| OpenAPI 聚焦测试 | 22 passed |
| 前端映射适配器测试 | 3 passed |
| CP6.Tests 全量 | 2725 passed / 17 既有环境门禁 skipped / 0 failed |
| Space IntegrationTests 全量 | 151 passed / 56 SQL 环境门禁 skipped / 0 failed |
| Space UnitTests 全量 | 231 passed / 0 failed |
| 前端全量 | 110 files / 616 tests passed |
| 前端 type-check 与 production build | passed；仅保留既有大 chunk / plugin timing 提示 |
| 完整 solution Release 非增量构建 | 0 error / 10 条既有 warning |
| SDK drift | `generate-space-design-sdk.ps1 -Check` passed |
| i18n 快照基线 | 仍为 843 个既有缺口；本卡未新增 `t()` 键 |
| 差异门禁 | `git diff --check` passed |

SQL Server 专项用例按环境门禁跳过，不伪装为通过；同一行为已经由纯服务测试、控制器测试、迁移编译与完整构建覆盖。

## 5. 安全与隐私边界

- 映射预览只处理工作表名称和表头，不上传或保存实际数据行。
- 日志、审计事件和幂等记录均不包含原始工作簿、单元格值或文件载荷。
- 查询过滤器按租户隔离数据库方案；系统方案仅以只读代码配置暴露。
- 保存时验证目标字段目录、来源表头位置、策略枚举、重复目标、键和引用配置。

## 6. 边界与下一步

本卡不解析 Excel 数据行，也不执行必填、类型、范围、重复业务键或跨表引用预检；这些属于 E03 S03。CAD 匹配、人工确认和 Draft 导入分别留给 E03 S04、E03 S05 及后续故事。

下一张 E03 S03 将以已保存的 `ProfileId + Version` 为固定输入，建立逐表、逐行数据预检结果、错误摘要和可下载问题明细，同时维持“预检不写入正式模型”的边界。
