# WP12-16 连接器外呼的本地准入与稳定 effectKey

WP12-16 完整 DONE，OA-CONN-01 目标继续开放。Main 基线 `c7d6a99b62a13d58c96eef87cac95f46894185a1`，已验证产品提交 `c713913c46a0a074da8b750a78dd255dda5aaa08`。正式起点和完整 scope 见 START。六项直接前置连同依赖和余卡共 17 项完整 DONE，沿用 WP12-13 的版本控制及 WP12-07 的原审批接入链。 [派工 #410](https://github.com/GTX537/CP6.AI-Handoff/blob/4c033d8f86b88deea4e96384eb2d91ae417ceec3/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T043326Z-exec-wp02-14b-wp12-16/MESSAGE.md) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/0b836bb310c9731f6a7b94a7b12840f10b80c0c0/handoffs/CP6-DEV-20261012/tasks/WP12-16/messages/20261123T051814Z-exec-wp02-14b-start/MESSAGE.md) · [Main PR](https://github.com/GTX537/CP6/pull/1314) · [WP12-13](../WP12-13/DELIVERY.md)

## 实现与实际顺序

Main worker 在原 `WF_AUTH` 门和 PostgreSQL 事务中复核实际 Scope、在跑实例、Active token/node、原 Job 与租约、精确 pin、当前版本控制及登记的服务、披露和 Owner 资格。持久记录冻结输入和摘要、资格及前沿、`DISPATCH_ADMITTED`、原 effectKey 和 transport key。首次准入事务结束后才执行提供方操作。原先的名称门和按 Job 的接管门共同约束旧入口。 [准入运行时](../../../../CP6.Core/Services/Oa/ConnectorControlRuntimeV1.Dispatch.cs) · [前滚迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261123053000_OaConnectorDispatchV1.cs)

Worker 重领后只按原身份进行有期限、当前有权的查证。协议核对回传的 effectKey、transport key 与输入 SHA-256。HTTP 200 只保存 `HTTP_ACK`，`NOT_OBSERVED` 是时点观察，非幂等超时保留 `OUTCOME_UNKNOWN`。工作流 token 继续停在原服务节点。OA 连接器管理按原版本读派发历史，显示取消请求与运输状态。 [提供方合同](../../../../CP6.Core/Services/Oa/ConnectorDispatchContractV1.cs) · [状态入口](../../../../CP6.WebApi/Controllers/Oa/ConnectorVersionControlController.cs)

## 运行对账

环境为本机 Windows、.NET 8 Main Kestrel、实际 Main worker 和隔离 PostgreSQL 18.6。提供方以独立回环 HTTP 服务按同一 operation 合同模拟，记录真实 POST、GET 与模拟效果数。恢复用例实际结束首次 Main worker 进程并启动新进程。夹具提前结束首轮租约等待，实际 reaper 从到期的原 Job 接管。两轮队列均走产品扫描入口。 [运行用例](../../../../CP6.Tests/Erp/OaConnectorDispatchPostgreSqlTests.cs) · [回环合同夹具](../../../../CP6.Tests/Erp/OaConnectorDispatchWireFixture.cs)

- SIMULATED 远端等价授权门缺证，Job fac8540e-dcaf-4fe5-ab76-54967775d01f POST=0。当前 Owner 撤权与停用先提交，Job 701a9d45-91d8-4860-b420-858699dfef76 POST=0。
- SIMULATED 原操作恢复，Job 6bbd23e1-11fc-4f50-b1fa-140ba8ad8768，进程 45320 → 33992，POST=1，效果=1，GET=3。
- 原 effectKey=2fbbd2106dbbbc5724b3bc29bb3edc14024f694604b0a86be4f1650148a51991，transport key=412efe4d08a1e51d1ba9684e7de8c83fc7ee757ae3f0d77506aa2ace326d72d5，输入 SHA-256=29765d4222557db03f205538f5834e84f24e7ca8bd20e6cbfe7481b42bf64eb4。NOT_OBSERVED → 摘要错配未知 → 查到原操作。
- WF_AUTH 撤权先提交，Job cdd3c749-40ea-4139-b011-f17594716607，准入 0，POST 0，token 保持 Active。
- 独立连接在 HTTP 首次接收时读到准入，能取得原 WF_AUTH 与 Job 锁，Job 86ba9102-d418-480c-adee-4e871c64256e，输入摘要 29765d4222557db03f205538f5834e84f24e7ca8bd20e6cbfe7481b42bf64eb4。
- WF_AUTH 撤权提交发生在 HTTP 200 之前。原事实保留，恢复授权后仍为 CANCELLATION_REQUESTED，运输仅 HTTP_ACK。
- MESSAGE pin 及 TIMER 时间配置已登记并有复核，缺真实运行采用时均为 UNPROVEN，实例与触发流水为零。WF_AUTH 仅确认原生写者基础。
- SIMULATED 非幂等超时，Job d49beec4-68ee-428f-b264-a9b777407cc3，POST=1，自动重发=0，原键保留，OUTCOME_UNKNOWN。
- 旧 OUTCOME_UNKNOWN Job=d9006703-2fc0-41a4-9bb9-f965f54989dd，原历史=1，旧按名调用=0，缺原操作证明保持回查待办。无动作快照 Job=5b4f00cf-71c6-46cd-b4be-9b2cc2fb9f51 原节点停泊。
- 旧 SENT Job=96bfdd2c-9a9c-4601-b315-421361c0c91c，原历史=1，旧按名调用=0，缺原操作证明保持回查待办。无动作快照 Job=617b553d-7caa-40b7-9907-f371d8647ef1 原节点停泊。

Chrome 从正常登录进入 OA 流程管理，对原 Job `d1f665bc-ede3-4019-a0e1-c57fe40a52ab` 读取实际状态入口，显示 `CANCELLATION_REQUESTED`、运输确认及 SIMULATED。没有控制读取权的编辑人收到 403，错租户收到 404。回包不包含原键、摘要或凭据定位。 [浏览器用例](../../../../cp6.web/scripts/oa-connector-control-browser.mjs)

| Required 或提供方 | 缺真实采用的实际判据 |
|---|---|
| RP-CONN-01 PinnedWorkflowAdmission | 有准确流程 pin 和已复核 MESSAGE 配置，但开通拒绝 `EXTERNAL_CONTRACT_UNPROVEN`，实例和触发流水为零。正式触发准入仍为 UNPROVEN。 |
| RP-CONN-02 WorkflowAuthorityRegistration | 本地使用真实 WF_AUTH，同门决定先撤权零派发。Scope 状态保留 `FORMAL_WORKFLOW_CONSUMERS_PENDING`，完整入站、派发及跨 Owner 写者覆盖为 UNPROVEN。 |
| RP-CONN-03 ExternalOperationContract | 配置 `ADOPTED`、登记凭据及原复核仍需匹配实际参与者，实际入口显示 UNPROVEN。回环合同包括幂等窗口、同输入和原查询，状态为 SIMULATED。 |
| RP-CONN-04 TimeAndForecastContext | 已登记时区、日历、合法 cron，缺实际参与者的 TIMER 开通仍拒绝，入口显示 UNPROVEN。 |
| RP-CONN-05 DurableResultApplication | 实跑 ACK 与已查到原操作后原 Job 和 token 仍停泊。真实 Workflow 应用与业务 Owner 应用各由原权威凭证认定，本卡证据为 UNPROVEN。 |

提供方无效果证明的消费及可能重试、终态路由归 WP12-18。原查询返回 `NOT_OBSERVED`、摘要错配或旧 `SENT/OUTCOME_UNKNOWN` 只有历史字段时，继续保留原未知，由可靠原查询或受控核证恢复。WP12-17 承接 ResultInbox、映射、工作流应用和原 Owner 回执。实际 HTTP 请求都发往隔离回环提供方。 [OA-CONN-01 合同](../../../design-handbook/modules/OA-CONN-01.md#6-并发外部未知与失败恢复)

## 原断言核对

OA-CONN-01 R1 原册 249 行，SHA-256 `04f7fe716dfe5d69b4ebf7d3b11d4a3d8d0d9184bd931832ccbc2533f77c153c`。合读 COMMON R1，SHA-256 `6271f981bb9187c12b578ca6ef7259c5b37ebe09d71e0cfa3cd7554f14e1b7ce`。复核目录和台账中的 32 项原 AC。无单列必需运行修订。此前采用的 CONN-01、03、10、11、12 已核对，本次累计 **10/32**。 [原册索引](../../../design-handbook/modules/OA-CONN-01.md#2-当前设计组合与接受边界) · [原 AC 目录](../../assessments/20261011/catalogs/engineering-oa.json)

| 本卡原 AC | 运行结论 |
|---|---|
| CONN-18 | WF_AUTH 撤权先提交时，准入与 POST 都为零。反序时，独立连接读取已提交原件并取得锁，OA 显示 CANCELLATION_REQUESTED。 |
| CONN-20 | 非幂等真实回环 POST 超时，保 OUTCOME_UNKNOWN 与唯一原键，后续同 Job 重领不重发。 |
| CONN-29 | 旧接管按原 Job 定位，保留原 pin 状态。原 SENT/UNKNOWN 阻第二写者。有可靠身份时跨实际进程查证同一 operation。 |
| CONN-31 | 在实际入口校验各 Required 缺证和仅配置 Provider 的拒绝。夹具显示 SIMULATED，完整 Required 和真实 Provider 采用保持 UNPROVEN。 |
| CONN-32 | 既有批准和当前 Owner 门可用时，需即时撤权的 operation 缺远端等价门，准入及调用均为零。 |

| 剩余归属 | 原 AC |
|---|---|
| WP12-14 | CONN-02、04、05、06、07、08、09、13、14 |
| WP12-15 | CONN-15、16、17、30 |
| WP12-17 | CONN-21、22、23、24、26、27、28 |
| WP12-18 | CONN-19、25 |

## 验证与入账

Main 编译、前端类型检查和定向 Vitest **3/3** 通过，新运行用例 **8/8**，实际 Chrome **1/1**，既有 OA/Workflow 定向回归 **362** 项通过，台账工具回归 **123/123**。广跑的 SubFlowConcurrencyTests 四项 SQLite 排序规则错误在领卡基线复现，此范围单列。普通编译与验证在本地执行。采用、同树冒烟、分支处置与席位释放由原链 DELIVERY 回读。工件目录为 `D:/CP6-test-artifacts/WP12-16/20261123-wp1216`。

- `focused-candidate.trx` 的 SHA-256 为 `78e18cd3365f283d3e06b8ab28c35f9db835d5a822d289af047f3e98841d33a5`。
- `browser-candidate.trx` 的 SHA-256 为 `dc794521287956e2b8754abd919d4efb65d477bad09f15a5a0af79c87a8f7e88`。
- `regression-candidate.trx` 的 SHA-256 为 `2aa6fdda160b2adfdf52f39a03534fe21edb4273b5429f8fd81570bfd8fae93f`。
- `baseline-sqlite.trx` 的 SHA-256 为 `7e35fc5ba90efe2edfe403ad9fbdaac23915c8425a6c9dbc0408f2681eee86ed`。

按 #409 的原 DELIVERY 完整提交和路径查重，为 WP12-09 单次补记 **307.799 秒**至卡内 **2451.455 秒**，旧目标复核 **0.077 秒**单列。计入本卡前累计 **177633.929 秒**。 [签收 #409](https://github.com/GTX537/CP6.AI-Handoff/blob/61eba9e709cfdbbf3b3e4180032df55248b75ebe/handoffs/CP6-DEV-20261012/tasks/WP12-09/replies/20261123T043026Z-coord-acceptance-ack/MESSAGE.md) · [原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/358af6cf99a37002d235f6a936e2faaff593d0ae/handoffs/CP6-DEV-20261012/tasks/WP12-09/messages/20261123T035053Z-exec-wp02-14b-delivery/MESSAGE.md)

核销快照 `2026-11-23T05:58:52.413105+00:00`，本卡 **2483.165 秒**，目标复核 **0.075 秒**，计划累计 **180117.169 秒**。额度 28800 秒，43200 秒强制拆卡。本卡未拆分。OA 完成 12/20，候选排序为 WP12-11（3）、WP12-17（3）、WP12-05（2）、WP12-14（2）、WP12-15（2）。 [采用台账](../../roadmap.json) · [执行计划](../../planning/20261117-card-execution.md)
