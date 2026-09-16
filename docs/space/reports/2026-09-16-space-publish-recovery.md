# Space 历史发布恢复修复整合

日期：2026-09-16。任务分支从 `main@af405acb4a687e87126923a8ab6ad0f0a696347d` 建立，最终同步至包含首页与 Viewer 修复的 `main@fe5e6c1bdb7099f2a8be024aa06dc003806eb974`。

## 范围

恢复六项经当前源码确认仍缺失的后端修复；旧项目状态快照不随代码恢复。

| 原提交 | 当前行为 |
| --- | --- |
| `e6a0dcf18357041708fae9af4a96100408a66237` | 校验能力查询使用已解析的仓库编码，避免以站点 GUID 代替仓库身份 |
| `97ab7234a670ec660b54afb294f05c5db617d18d` | 补齐模型发布与回退权限种子 |
| `a9618eab63ef50862aaf3ee78dd65db6fd0502e4` | 内部生成的发布计划允许最多 64,000,000 字符，覆盖超过旧 4,000,000 上限的仓库计划 |
| `2a35521c75b8110395c618216aab396d6732c971` | 发布 worker 建立可解析的系统 Actor GUID、租户上下文与执行诊断 |
| `755ca07aa1a8c3d97b435af079138ab3e1d353b7` | 持久化批次经显式 DTO 重建 mutation，并校验 mutation 与批次哈希/身份；运行投影只读取需要的计划字段 |
| `de55219dad2025a0904ff8d99488eb199bab064b` | 先读取站点运行行 ID，在内存中确定失效行，再加载并停用对应实体 |

SQL 回归把故障注入点设在批次写入之前，确保测试实际经过“已持久化批次 → 超时 → 读取批次 → 重试”。新增篡改场景直接修改测试临时库中的 mutation 哈希，验证重试返回 `Security` / `PublishJobMismatch`，旧 Published 指针保持，旧 WMS 库位仍启用，新库位未写入。

## 本地验证

| 检查 | 实际结果 |
| --- | --- |
| 四个相关 `CP6.Tests` 类及必要依赖的 Release 编译 | 26 passed / 0 failed / 0 skipped |
| `SpacePublishOrchestratorSqlServerTests` | 4 passed / 0 failed / 0 skipped，约 80 秒 |
| 最终测试补充显式清理 ChangeTracker 后，仅重跑篡改场景 | 1 passed / 0 failed / 0 skipped |
| 合入首页修复后重新编译 `CP6.Tests` / `CP6.WebApi`，复跑上述四类 | 26 passed / 0 failed / 0 skipped |
| SQL 临时库清理 | `CP6SpacePublishSaga_*` 数量前后均为 0；LocalDB 数据库总数前后均为 5 |

SQL 使用现有 `MSSQLLocalDB` 引擎的随机 `CP6SpacePublishSaga_<GUID>` 库。每个场景只迁移自己的新库，`finally` 删除该库；不读取连接配置或使用现有业务数据库。SQL 验证覆盖批次恢复、哈希拒绝、运行激活、部分应用对账和历史版本重新发布。最后同步的 Viewer 提交只改变前端及文档，没有改变已验证的后端、SQL 或依赖输入，因此复用上述 SQL 结果。

复现命令（仓库根目录）：

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj -c Release --filter 'FullyQualifiedName~SpaceValidationControllerTests|FullyQualifiedName~SpaceAuditPermissionSeedTests|FullyQualifiedName~SpacePublishPlanTests|FullyQualifiedName~SpaceProcessingJobWorkerTests' -m:1 -p:UseSharedCompilation=false -p:RestoreLockedMode=true

$env:CP6_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30'
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~SpacePublishOrchestratorSqlServerTests' -m:1 -p:UseSharedCompilation=false -p:RestoreLockedMode=true
Remove-Item Env:CP6_TEST_SQLSERVER
```

本轮仅证明相关源码和隔离 SQL 自动化行为；没有重跑完整 Space/全仓套件、生产容量验收或现场接受，也没有触发 Actions、替换运行环境或生产部署。

## 排除的库存 URL 候选与后续验收

`d2836f33ca3a619875f81194cd6d7834967f41d7` 在选择超过 100 个库位时省略全部 ID，改为整站查询。当前服务限制单次选择最多 10,000 个库位，因此超过 10,000 库位的站点中，原本有效的 101 库位楼层请求会被扩大并拒绝。该候选不适合原样整合，`runtime.ts` 及其测试保持当前主线实现；此次没有新增 API 或修改 SDK 合同。

后续修复须采用有界传输，保留精确选择的 ID、租户/站点/Published 版本校验和现有数量限制。验收至少覆盖：超过 100 个选择 ID、站点超过 10,000 库位但选择量仍合法、重复 ID 去重、缺失或越权 ID 失败关闭，以及库存来源/版本的一致性。不能通过省略过滤条件绕过 URL 长度问题。
