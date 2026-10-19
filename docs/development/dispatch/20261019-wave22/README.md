# 第二十二轮：Main身份变更写入链路

## 第二十二轮有限范围已完成

写者盘点、已证实捕获缺口修正、实际服务PG/完整采集实证、集中审查与产品集成四个阶段已完成。候选 `7a4e248d5bcbf9f525889b503eba3de621a257a3` 经[PR](https://github.com/GTX537/CP6/pull/305)合入远端main `0b6da3853f3b09a4397ca9f2c6da460b701ba2fb`；Handoff `33d439b768e3af091a2a24a7094e41e30b8cd3a7`回读，BUG304已关闭，当前活动Issue0项。

单份DELIVERY记录21项有效原生验证、所有失败/跳过的实际范围，以及一次作者SPEC/QUALITY。没有独立代理审查。6自有PG及HTTP/testhost停止；新树/代理/Actions0，D余45.75GiB，根7项用户资料保留。文档收尾远端核对后完成外部持续目标，24小时是检查点而不是必须等待时长。

完整公共基础仍未完成：所有IAM策略/非EF写者与授权epoch、CRM基线/增量投影发布、Owner最终写授权门、普通写账号/既有升级恢复及真实业务。未开下一轮实施窗口。

合并后因共享测试夹具输入变化，仅补验两个反向并发案例，2通过；全轮仍21去重有效原生验证。下方引用保留原开工与候选时点。


> 候选已完成：写者矩阵、#304 PostgreSQL菜单fanout互斥修正、21项有效原生验证及作者集中审查无阻断。[单份DELIVERY](../../evidence/WP02-05/identity-writer-coverage/DELIVERY.md)。Issue仍Open，待正常合并/远端核对与收尾。

> 当前优先修正P0 [#304](https://github.com/GTX537/CP6/issues/304)：原生PG复现菜单变更遗漏另一事务未提交的角色操作权。分支 `codex/bug-304-identity-menu-fanout-20261019`，沿同一真实START/领取范围，不另起审查或ACK。正常主线合并与远端核对前保持Open。

> 执行：当前COORD单对话串行使用writing-plans/executing-plans，按项目精简验证节奏。复用已释放目录，不创建代理、聊天或worktree。

**目标：** 盘点实际Main IAM写入入口，补已证实的捕获边界，以真实服务和PostgreSQL证明业务事实、快照、版本、Outbox同事务，并由既有完整采集器读取结果。

**架构：** 保留CP6Context现有SaveChanges捕获管线、C02五类事件、普通PG目录宿主和首尾完整目录协议。只修正当前协议内的写入缺口；不新造权威身份类型、绕过授权、迁移旧库或发布CRM授权。

**技术：** .NET8、EF/Npgsql、既有Platform0.10.3合同、PostgreSQL18隔离fixture、既有普通LOGIN HTTP宿主。

基线main/origin/main `cc3bd51f0ebfc005b4fb6ebb27abb5b2c632b85d`；W21全部收尾，Handoff `b0d4798a4a95ae752ff8727ba80d2335521dd3e0`一致。活动Issue0，无本任务排队/执行Actions。持续目标起点UTC2026-10-19 05:41:46，24小时检查点UTC2026-10-20 05:41:46（纽约01:41:46）。预计13～20小时；提前完成即可交付，时限不等于完成。

## 四项顺序任务（同一完整任务审查单位）

| 阶段 | 预计 | 输出 |
|---|---|---|
| 入口盘点 | 2～3小时 | 用户/部门/角色/权限/租户/会话写者清单、捕获与批量/直接SQL边界，标明验证和未验证来源 |
| 捕获边界 | 4～7小时 | 对已证实的漏捕获、关联重定向、租户归属问题进行最小修正；原步骤RED→GREEN |
| 原生实证 | 5～7小时 | 实际UserRoleService、RolePermService、DeptService等入口的最终快照/版本/原字节，失败回滚与C02完整采集 |
| 集成收尾 | 2～3小时 | 作者一次SPEC/QUALITY、正常PR及远端核对、单份报告、共享状态/Handoff/资源/检查点 |

## 实施与验证

- [x] 按[WP02-05](../../task-plans/WP02.md#wp02-05)、[INT-ID-01](../../../design-handbook/modules/INT-ID-01.md)和既有真实事务设计，追踪实际写者→SaveChanges→capture→snapshot/outbox。读取原生既有证据，避免把静态扫描当运行通过。
- [x] 先写能证明缺口的行为回归，再修改 `IdentityChangeCapture.cs`、必要的 `IdentitySnapshotWriter.cs`/`IdentityPermissionSnapshot.cs`/`CP6Context.cs`。合法同租户关联编辑应更新受影响旧/新聚合；稳定主体/租户身份不得通过普通变更迁移。发现活动BUG按Issue规则登记、优先修复并保留失败记录。
- [x] `CP6.Tests/CrmIdentityChangeCaptureTests.cs`覆盖新增边界；`CrmIdentityWriterPostgreSqlTests.cs`复用TenantManagementPostgres原生迁移fixture，调用真实服务写入，核对业务事实、精确快照、普通/优先Outbox及失败回滚。使用不同测试租户隔离。
- [x] `Persistence/Tenancy/IdentityWriterBaselinePostgresTests.cs`复用配置宿主fixture及真实IdentityBaselineCollector，验证变更后完整原字节结果、跨租户不污染及旧采集边界冲突。不得用手写拼页冒充产品采用。
- [x] 定向过滤器 `FullyQualifiedName~CrmIdentityChangeCaptureTests`、`FullyQualifiedName~CrmIdentityWriterPostgreSqlTests`、`FullyQualifiedName~IdentityWriterBaselinePostgresTests`，按风险一次完成相关验证。若修改共用写者，追加既有受影响一致性集合；不扩成全仓测试。
- [x] 单份 `docs/development/evidence/WP02-05/identity-writer-coverage/DELIVERY.md`记录实际变化、运行/复用证据、一次作者集中审查及剩余范围。审查修正只局部复查。普通文档仅diff、链接和台账生成检查。
- [x] 每个实际完整任务或BUG独立分支，正常PR合并并核对远端后关闭相应状态；本轮最终收尾另用同目录文档分支。没有独立审查者，不声称独立审查。

## 范围与资源

已读源码存在 tracked SaveChanges捕获；直接SQL、ExecuteUpdate、启动种子、配置策略、OIDC/服务令牌专用写者按实际路径单列。未进入本轮实际证据的写者和新身份轴仍未完成。菜单Enable与动作定义是否属于现行权限语义，以原实现/合同为依据，不能顺手改变合法授权合同。

唯一实施目录 `D:/CP6-worktrees/wave17-dispatch-20261018`，规划开始干净，旧提交可从远端恢复。根7项用户资料保留。复用串行构建 `D:/CP6-local-test/wave18/w18-01/artifacts` 和NuGet缓存；原日志/失败不删除。新增证据 `D:/CP6-local-test/wave22/`，必要PG只在自有全新permission-consumption根及55435串行启动/停止。

全公共基础/WP02、完整IAM权威向量、CRM基线/增量投影与授权发布、Owner最终写门、既有环境升级/普通账号全面采用、真实DEV/业务/生产均不因本轮局部交付提升。无Actions、工作流/保护变更、既有库操作或部署；每次push/PR核对当前工作流内容与实际路径，未变分析复用。
