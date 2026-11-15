# W647-01 Space 版本克隆事务

本地分支 `codex/w647-01` 从 Main `f978f0541252509d8a029a355bfa73977a2af69c` 开工，已提交 `4314d92b39dade3bd495a19bedeef2de8a1eecbd`。变更集中于克隆处理器、共享事务 helper 的完成作业选项和 PostgreSQL 实测夹具，对应 PGT-017 #1012。本批通过集中 diff 审查与 `git diff --check`。

## 事务范围

普通 Published→Draft 和规划版本共用 `EfSpaceVersionCloneProcessor.ProcessAsync`。PostgreSQL 在同一物理连接和 Read Committed 事务上先取 Main 根并读取当前租户，再独占锁定原 `Space_Job` 和 `Space_JobAttempt`。克隆行、原目标版本、Job、Attempt 与 CloneSnapshot Step 一起提交。最终读取 PostgreSQL `clock_timestamp()`，按锁定时的原租约截止和 Main 租户到期时间核对提交资格。共享 helper 的默认 metadata 路径继续使用原租约和取消校验。[克隆处理器](../../../../CP6.Space.Infrastructure/EfSpaceVersionClone.cs#L630) [共享事务](../../../../CP6.Space.Infrastructure/SpaceArtifactWriteTransaction.cs#L33)

明确取消保留原取消、Attempt 取消、Draft 释放合同。复制过程中失败先回滚复制事务，再另起 Main 授权事务，以原重试策略共同更新 Job、Attempt 和必要的预约清理。生产 DI 中的通用 lease ledger 有独立上下文，技术领取仍沿用它。克隆 COMMIT 确认丢失时按原 job 与 attempt 回读既有结果，只有已持久化结果才用于恢复。历史重发继续由其原父事务调用独立 `CloneSnapshotAsync`。[失败与恢复](../../../../CP6.Space.Infrastructure/EfSpaceVersionClone.cs#L863) [独立历史克隆](../../../../CP6.Space.Infrastructure/EfSpaceVersionClone.cs#L553)

## 实测证据

PostgreSQL 18.6 与 Main Kestrel 使用模拟租户、实际登录和已发布楼层、区域、机架、层、库位、元素。普通预约经真实签名 `POST /api/space/design/v1/sites/{siteId}/versions`，规划预约经真实签名 `PUT /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}`。测试宿主使用实际 `ISpaceJobLeaseStore` 领取原 job，测试以生产 DI 的 `ISpaceVersionCloneProcessor` 手动驱动。暂停和恢复走签名 Main 命令，独立连接读取 PostgreSQL 等待链和完整原写集。

固定基线的同一组产品 DLL 保持 SHA-256 一致。暂停签名成功并已禁用租户时，普通 clone 的原 job `2c0a3130-a9ef-4332-937c-5abdae891b6b` 仍在现实 FloorRevision 关系锁上等待。释放该锁后旧处理器返回六条克隆记录，同一原 Attempt `9ad9126a-e5f4-47e9-a307-5355c94da460` 及一个 Step 完成。初次基线夹具触发了与当前宿主无关的 CRM 计数断言，修正后保存实际红灯与完整数据库写集。[固定基线 TRX](D:/CP6-test-artifacts/W647-01/baseline-proof/results.trx) [固定基线等待及回读](D:/CP6-test-artifacts/W647-01/baseline-proof/permission-consumption-baseline-proof/w647-clone-False-False-paused.json)

| 场景 | 经 PostgreSQL 独立连接观察的结果 |
| --- | --- |
| 普通和规划，克隆先 | 原 clone 已持 Main 根，在 `Space_FloorRevision` 的业务 SQL 等待。签名暂停进入根锁等待。克隆前目标行零，放开时暂停仍未提交，六条内容和同一目标、原 Job/Attempt/Step 一起可见。两序各内容保留逻辑 ID 和业务字段，获得新物理 ID。 |
| 普通和规划，暂停先 | 签名暂停持根且卡在租户更新，已领取的实际 clone 正在根上等待。暂停提交后返回 `SPACE_ARTIFACT_TENANT_INACTIVE`，原状态快照逐行一致。恢复后由同一原 job/attempt 完成，空队列再领无变化。规划结果保留规划 Purpose、分支与源版本身份、独立的生产 ActiveDraft 槽。 |
| 普通，克隆 COMMIT 回执丢失 | 回传中继在 PostgreSQL 确认 COMMIT 后断开回执。实际处理器回读原 Job/Attempt，返回原六行统计，Step 和内容保持一次。再次使用已结束 lease 维持原 lease lost 行为。 |
| 普通，明确取消 | 实际 `SpaceJobCoordinator.RequestCancellationAsync` 命令进入原运行中的 job。候选写 Job/Attempt Cancelled、目标 Abandoned、释放原 Draft 槽，共同提交且克隆/Step 为零。 |
| 普通，晚期保存失败 | 隔离 PostgreSQL 的 CloneSnapshot Step `CHECK` 让真实晚期 INSERT 失败。第一次及第二次依原策略排队并保留预约，第三次原 job DeadLetter、目标 Failed、Draft 释放和原 Attempt 失败共同可见。三次均完整回滚克隆行与 Step。 |
| 历史重发控制 | 同一个候选产物执行原 W634 历史克隆两序，2/2 通过。 |

候选新用例 7/7，历史相关 2/2，零跳过。两序原始 JSON 含租户、版本、原 job/attempt、等待 PID、完整 PostgreSQL 原始行和 `xmin`。克隆 SQL 与 EF SaveChanges 保存点可取得不同的子事务 `xmin`，初轮候选夹具因此出现五个失败，原证据已保留。最终断言分别记录两组写入，以独立读取边界、真实提交回执和失败回滚验证共同提交。[最终 TRX](D:/CP6-test-artifacts/W647-01/candidate-final/results.trx) [历史 TRX](D:/CP6-test-artifacts/W647-01/historical-controls/results.trx) [普通克隆先](D:/CP6-test-artifacts/W647-01/candidate-final/permission-consumption-candidate-final/w647-clone-False-True-paused.json) [规划暂停先](D:/CP6-test-artifacts/W647-01/candidate-final/permission-consumption-candidate-final/w647-clone-True-False-paused.json) [实际丢回执](D:/CP6-test-artifacts/W647-01/candidate-final/permission-consumption-candidate-final/w647-clone-acknowledgement.json) [晚期回滚](D:/CP6-test-artifacts/W647-01/candidate-final/permission-consumption-candidate-final/w647-clone-late-failure-terminal.json)

九项 hosted `SpaceProcessingJobWorker` 的现有列表缺少 CloneVersion。本批验收范围是手动调用既有生产处理器和真实租约，自动调度仍待采用。规划签名 PUT 的前置预约由独立 `SpacePlanningScenarioService` Serializable 事务提交，源码仅见入口权限和站点访问检查。本次用该入口完成正常预约，实测两序覆盖后段处理器。正式 PGT-017 仍为 `NOT_RUN`。[Hosted 作业列表](../../../../CP6.WebApi/BackgroundServices/SpaceProcessingJobWorker.cs#L22) [规划预约](../../../../CP6.Space.Infrastructure/SpacePlanningScenarioService.cs#L36)

## 本地发布与工作区

最终运行用 PostgreSQL 57192、签名 Main 57196 和回执中继 57195。历史控制为 PostgreSQL 57193、签名 Main 57197。发布按原候选程序集运行，四个产品 DLL 与实测副本 SHA-256 一致。发布文件 310 个，发布清单 SHA-256 `203a6772c594c4f16a7dbe6d9b522ab7e1eb03dba07536a745f1df13f5f3eef9`。从发布物在 57198 启动，`GET /health/live` 为 200，匿名 `POST /api/space/design/v1/sites/{siteId}/versions` 为 401。此为本地发布检查，生产发布另行验收。[最终源指纹与运行参数](D:/CP6-test-artifacts/W647-01/candidate-final/invocation.json) [发布清单](D:/CP6-test-artifacts/W647-01/publish-sha256.json) [源码与冒烟记录](D:/CP6-test-artifacts/W647-01/publish-smoke.json)

本地提交已保存，DEV-2 工作树干净，任务 PostgreSQL、Kestrel 与中继进程均停止，57190–57199 无监听。分支待协调方集成和处置，席位待协调方 `finish` 释放。Actions 与部署状态无操作。

## 与最新 Main 组合验证

组合 `40ebba3de9920ac106debd09354d97271b5debff` 采用 W646 主线 `4876b416de689fca4d5a4bd8020470b8c3f234af`。W647 三个受测文件逐字节相同，复用原 7+2 结果。更新后的共享事务默认选项重复验证 W646 的校验终态两序及原 COMMIT 应答丢失，3/3 通过，零跳过。组合发布 310 文件，清单 SHA-256 `434e5c184fbceccca44acba4e9669f2c5e73d00369b4c42a8fddfe357b25efc2`，四份产品程序集与通过测试的程序相同，实际 200/401。组合过程均已停止。 [组合 TRX](D:/CP6-test-artifacts/W647-01/combined-validation/results.trx) · [组合发布](D:/CP6-test-artifacts/W647-01/publish-combined-smoke.json)
