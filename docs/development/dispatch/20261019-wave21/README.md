# 第二十一轮：完整目录基线取得与核验

> 执行：当前COORD用executing-plans单对话串行完成下列任务；用户授权歧义由总控决定，沿项目精简验证规则。writing-plans/brainstorming用于上下文与合同核对，不另开代理、工作树或审批回合。

**目标：** 基于现有Main身份权威和普通PostgreSQL目录宿主，完整取得、验证并冻结当前C02目录结果，供后续消费者采用。

**架构：** 保留已接受的versions→准确snapshot→再次完整versions协议；boundary只比较等值。新增原字节读取和不可变基线核验/采集组件，整个采集成功前无可发布结果，不创造第二身份权威或真实事件来源。

**技术：** .NET8、现有Platform0.10.3/事件bundle、Npgsql8.0.8、PostgreSQL18隔离fixture、现有真实C02.Services认证和普通LOGIN宿主。

持续目标开始UTC2026-10-19 04:15:29，24小时检查点UTC2026-10-20 04:15:29（纽约2026-10-20 00:15:29）；预计16～22小时。完成即交付，时间到不等于完成。

## 已核对来源与设计选择

Main/main/origin/main `f552995d36c37ad736d84058df2d2a3ba7085568`；W20四窗口/收尾PR295、Handoff `30439c293385b3464361b91112ff5088e073644b`一致，活动Issue为空，无本任务排队/运行Actions。原七项用户资料保留。

[WP02-05](../../task-plans/WP02.md#wp02-05)、[INT-ID-01](../../../design-handbook/modules/INT-ID-01.md)和准确S6原文§4.5（SHA d9b57a039e18a689615cfb48e181bb248dedecf957d8bdd7dd72d85a53a6dccc）要求：每页≤200、最多100万项、tenant/boundary一致、aggregate唯一、cursor不重复；准确snapshot的type/version/hash/deleted匹配；最终完整目录与首轮完全一致才Complete。原data字节摘要与真实运输envelope摘要分开，snapshot-only来源不能补造messageId、接收事实或撤权时间。

当前reader/真实宿主已完成安全页读和generation冲突；尚无原payload字节的明确传输和完整基线取得组件。采用原首尾核验流程，结果一旦产出按源身份/原版本/原字节冻结。服务端持久历史版本/新数据库profile会扩展安装及恢复范围，本轮不引入；也不把当前页拼接直接作为完整授权基线。

“完整”限定为已就绪C02 Snapshot目录在同一不透明generation的全部条目（含tombstone/撤销）；不证明所有Main IAM写者已经覆盖，也不含尚未批准的新IAM事件种类。完整WP02、CRM授权发布和最终Owner写门保持未完成。

## 24小时任务与顺序

| 窗口 | 预计 | 交付 |
|---|---|---|
| W21-01 | 4～5小时 | 不可变来源/条目/完整基线合同，原UTF-8 payload核验、来源/版本/摘要/完整集合验证及稳定内容摘要 |
| W21-02 | 4～6小时 | 版本化原字节snapshot GET，普通PG可信宿主、同一语句就绪/generation/内容读取；旧接口保持兼容 |
| W21-03 | 5～7小时 | 有界实际HTTP采集器，完整首轮目录→准确snapshot→完整末轮目录；全部成功才输出基线 |
| W21-04 | 3～4小时 | 原生PG/HTTP并发、隔离、取消/失败与恢复矩阵，集中审查、正常集成、台账/Handoff/资源收尾 |

### W21-01：合同及核验器

文件：新增 `CP6.Core/Services/CrmIdentity/Baselines/IdentityBaselineContracts.cs`、`IdentityBaselineVerifier.cs`、`CP6.Tests/IdentityBaselineTests.cs`；必要接入 `IdentityEventValidator.cs`及相关`CrmIdentityContractTests.cs`；单份报告 `docs/development/evidence/WP02-05/baseline-contract/DELIVERY.md`。

- [ ] 固定source tuple：TenantId、准确Issuer、Region、ContractBundleSha256、安装身份摘要；调用方从可信配置提供预期值，不能信任首个响应自报来源。
- [ ] 定义版本化raw snapshot：源身份、boundary、原aggregate/type/int version/hash/deleted、canonical base64原UTF-8字节。只复制原数据，不重新序列化后冒称相同字节。
- [ ] 实现有界核验builder：首轮目录唯一且合法；逐项原字节/hash/schema/tenant/aggregate/version/tombstone完全匹配；末轮boundary与集合相同才Complete。重复、漏项、未知类型、不同来源或边界都拒绝。错误后builder不可继续发布。
- [ ] 产出只读拥有副本的基线、确定性内容摘要和显式SOURCE_SNAPSHOT来源；不含虚构运输message/raw-envelope摘要或原撤权时间。重新传递同对象/条目顺序不改变内容身份。
- [ ] 定向自动化验证原字节、恶意/不完整/冲突输入、修改调用者集合不能改变结果、取消无结果；复用事件schema及语义检查。schema校验可构造仅验证用载体，绝不持久化为真实事件来源；不能以新包装长度缩小原合法payload。

### W21-02：原字节HTTP读取

文件范围：新增Core `Baselines/IIdentityRawSnapshotReader.cs`及原字节reader，扩展 `TenantIdentityDirectoryReader.cs`、必要host source描述、`CrmIdentityController.cs`；定向测试复用 `IdentityDirectoryConfiguredHttpHost.cs`/原生fixture。报告 `WP02-05/baseline-raw-http/DELIVERY.md`。

- [ ] 新路由 `/internal/crm/identity/directory/v1/snapshots/{aggregateId}` 仅在真实普通身份宿主启用时开放；继续当前JWT、租户/令牌授权、固定安装期望、no-store与受控400/404/503。
- [ ] 在受管普通连接事务中，以单个SQL观察Bootstrap+generation+准确snapshot；返回原PayloadJson的严格UTF-8编码与其原摘要，暴露冻结可信source，不提供任意tenant/database参数。
- [ ] 验证A/B隔离、原字节（含空白/转义）、原hash不符拒绝、未配置不可用、Owner查询0；保留旧versions/snapshots行为，未改存储结构或seal。

### W21-03：完整采集

新增 `Baselines/IdentityBaselineCollector.cs`及相关配置/HTTP源适配、`CP6.Tests/IdentityBaselineCollectorTests.cs`；报告 `WP02-05/baseline-collector/DELIVERY.md`。

- [ ] 可信固定base URI/来源、服务认证提供者与HTTP client；不从payload决定目的地。拒绝重定向与非预期来源，测试loopback显式隔离配置，不回退Owner或匿名读取。
- [ ] 串行页大小≤200，检测重复cursor/aggregate、响应超限、最多100万条；每份原payload≤原事件允许的1MiB，累计原字节默认上限256MiB，全部使用有界响应读取，超限整体失败而非截断后称完整。部署可选择更低运行预算，预算不是源数据合法性变化。
- [ ] 完整首轮目录→所有准确raw snapshots→完整末轮目录；boundary只等值比较，不当全局水位排序；失败/409/401/403/503/取消无部分基线，不自动重放或产生授权。
- [ ] 全成功才返回W21-01不可变结果；调用方保存/发布策略由消费者任务负责，本轮不写CRM投影或READY。

### W21-04：串联及收尾

复用现有真实宿主fixture，在 `CP6.Tests/Persistence/Tenancy/IdentityBaselineHttpPostgresTests.cs` 定向验证：真实A/B完整采集、源写并发导致旧基线拒绝、快照hash/缺项/未就绪、当前撤权、取消无结果、普通账号/无Owner fallback。单份报告 `WP02-05/baseline-integration/DELIVERY.md`。使用产品collector，不用手写测试拼页冒称采用。

- [ ] 只运行新增/受影响验证，原W20权限/profile/安装及未变HTTP结果直接引用。
- [ ] 每窗口一次作者SPEC/QUALITY全diff审查，修正只局部复查；正常PR合并并核对远端后，在同目录从最新main开下一任务分支。
- [ ] 同步台账/生成页/四状态/Handoff；停自有资源，最终关闭四窗口和外部持续目标。完整目标/真实业务验收不自动提升。

## 目录、资源和验证执行

复用 `D:/CP6-worktrees/wave17-dispatch-20261018`，规划前tracked/untracked/ignored为空，旧提交已远端包含。新增worktree/代理0；不删除历史树/库/证据。D盘余47.28GiB。沿原NuGet缓存及串行 `D:/CP6-local-test/wave18/w18-01/artifacts`；新增日志 `D:/CP6-local-test/wave21/`。

配置/合同单元测试不启PG。必要原生集合显式设置CP6_PERMISSION_PG_BIN和全新自有permission-consumption-*根，在空闲55435串行启动并停止；既有CP6DB/PG副本不操作。示例选择器 `FullyQualifiedName~IdentityBaselineTests|FullyQualifiedName~CrmIdentityContractTests` 与对应PG类分开执行；必须核对实际Executed/Passed，跳过不算通过。

无Actions、workflow/分支保护修改、生产部署或既有库迁移。每次push/PR核对当前workflow引用及实际路径过滤；未变分析复用。普通文档只查diff/链接/台账，不能因提交或合并SHA改变重跑相同业务验证。
