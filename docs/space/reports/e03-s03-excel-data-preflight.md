# E03-S03 Excel 数据预检完成报告

- 日期：2026-08-02
- 状态：已完成并进入 Space 集成分支
- 功能提交：`9d0a59e7`
- no-ff 集成提交：`3571f677`
- 范围：Excel 上传、异步预检、结构化问题清单与受保护错误报告

## 1. 交付内容

- 新增 50 MB 上限的 Excel 来源上传入口，复用既有隔离区、文件扫描和租户文件存储流程。
- 新增 `ExcelPreview` 异步作业；开始预检时固定 `ProfileId + Version + DefinitionHash`，保证历史作业不会被后续映射方案修改影响。
- 使用 Open XML 流式读取工作簿，不执行公式；宏工作簿、外部关系、越界工作表/行/列/共享字符串等输入会失败关闭。
- 校验必填、整数/小数/文本类型、数值范围、枚举与单位换算、业务键重复、显式跨表引用、库位层数/格口/深度容量、未知列和空工作簿。
- 问题清单包含工作表、行、列、目标字段、严重级别与修复动作；只保存消息参数，不保存原始单元格值。
- 预检结果和 CSV 错误报告均沿用模型读取权限、租户隔离和私有缓存策略；报告按授权请求动态生成。
- 重试通过稳定问题签名避免重复写入，不删除或替换既有权威问题历史。
- 只有当前来源、当前映射定义和当前解析器版本一致、作业完成且无 Blocking 问题时，结果才允许进入后续确认阶段。

## 2. API 与契约

新增 4 个 Design V1 操作：

- `POST /api/space/design/v1/versions/{versionId}/excel-sources`
- `POST /api/space/design/v1/versions/{versionId}/sources/{sourceId}/excel-preflights`
- `GET /api/space/design/v1/versions/{versionId}/sources/{sourceId}/excel-preflights/{jobId}`
- `GET /api/space/design/v1/versions/{versionId}/sources/{sourceId}/excel-preflights/{jobId}/report`

OpenAPI 操作数由 52 增至 56；C#、TypeScript SDK 及前端适配器同步更新，SDK 漂移检查通过。

## 3. 安全与资源边界

- 最大 20 个工作表、每个已映射表 50,000 行数据、工作簿总计 1,000,000 个已填充单元格。
- 最大 16,384 列（XFD）、200,000 个共享字符串、10,000,000 个共享字符串字符、单元格 32,767 字符。
- 公式单元格不会计算，映射字段出现公式时记录 Blocking 问题。
- 上传/启动要求 `space:source:upload` 与 `space:model:edit`；结果/报告要求 `space:model:read`。
- 报告响应使用 private/no-store/nosniff；日志、审计、问题参数和报告都不输出原始单元格值。
- 预检阶段不写模型内容，也不推进模型内容修订号；本故事不需要数据库迁移。

## 4. 验收证据

| 门禁 | 结果 |
|---|---|
| 完整 solution Release 构建 | 0 error / 7 条既有 warning |
| CP6.Tests 全量 | 2731 passed / 17 环境门禁 skipped / 0 failed |
| Space IntegrationTests 全量 | 163 passed / 57 SQL 环境门禁 skipped / 0 failed |
| Space UnitTests 全量 | 231 passed / 0 failed |
| 前端全量 | 111 files / 619 tests passed |
| 前端 type-check 与 production build | passed；仅保留既有大 chunk 提示 |
| SDK drift | `generate-space-design-sdk.ps1 -Check` passed |
| i18n 快照基线 | 仍为 843 个既有缺口；本故事未新增 `t()` 引用 |
| 差异门禁 | `git diff --check` passed |

SQL Server 专项用例因本机未配置测试连接而按环境门禁跳过；核心原子性、租户隔离、重试与状态机行为由内存集成测试、控制器测试和完整构建共同覆盖。

## 5. 明确不在本故事范围

- E03-S04：Excel 行与 CAD/编辑器元素的匹配、未匹配项分类及新增/更新/不变/冲突统计。
- E03-S05：用户确认、幂等写入模型和最终导入结果。

因此本故事只形成可审阅的预检事实，不会在用户确认前修改模型。
