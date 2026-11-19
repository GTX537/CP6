# WP11-65 交易伙伴财务原引用交付

WP11-65 完成。Main 读取已经持久保存的伙伴财务引用，按原 Owner、ID、版本、摘要和用途返回原件观察。当前资格由使用方的现有 PostgreSQL 事务复核。FIN-MASTER-01 与 FIN-CREDIT-01 保持开放。

| 项目 | 记录 |
|---|---|
| 派工与 START | [#339 派工](https://github.com/GTX537/CP6.AI-Handoff/pull/339)，原提交 `9424a7d415ac13ddf3c7ede97bdf99dd4aa589f8`，登记采用 `90ae66aaee8d5e0d0a1e1c9466db5d9d3944d7d2`。[START](https://github.com/GTX537/CP6.AI-Handoff/commit/9edec859aeee767630acbb3642ba6e567d748aeb) |
| 工作入口 | Main `DEV-1` · `D:/CP6-worktrees/wave17-dispatch-20261018` · `codex/wp11-65` |
| 开工基线 | `1236b69aecb5d9cf8c5851e8336f4ce7703d36fb`。WP02-10 #1238、WP02-13 #1241、WP02-13b #1248、WP11-01 #1275 的受审和合并提交均已进入该基线。 |
| 提交与审核 | 实现提交 `3f5da284e63023f1b82126206c5f68c57621e084`。[交付 PR](https://github.com/GTX537/CP6/pull/1278) |
| 本地证据目录 | `D:/CP6-test-artifacts/WP11-65/current-20261119`，运行记录 `checked-release.trx`、相邻回归 `release-pg.trx`、历史表结构专项复测 `historical-fixture.trx` 和 `issue-1276-before.json`。 |
| 远端与席位 | 合并后的完整提交、远端采用结果、冒烟及正式 finish 以同一 PR 和原通信分支中的 DELIVERY 为准。 |

## 实际实现和环境

- 真实 Main Kestrel 以登录会话调用 `GET /api/business-partners/finance-references-v1/{id}`。账套上下文由服务端按当前人员签发，并沿用 X-Finance-Context 请求头，伙伴对象、`finance-reference-read`、当前字段权与原来源作用域均在 Main 核对。响应采用 `no-store`，字段撤权时返回 `NOT_AUTHORIZED`，原引用、金额与账套信息为空。
- Main 的 `ReadFinanceLinkInUowAsync` 读取持久 BP 链接及无损版本。使用方以 `EnlistAsync` 在同一原生连接和事务中持有并复核来源门。它核对当前伙伴版本、档案状态、请求中的五字段引用、账套和发布前沿。返回的 `GuardRevision` 来自已登记账套的 PostgreSQL 记录。
- PostgreSQL 见证旧 `Fin_BankAccount` 和 `T_WebBusinessPartner.CreditLimit` 的原始来源。旧库迁移只见证当时存在的版本，须有独立可信登记才能建立伙伴与法人账套的准确关联。银行历史记录只保存遮罩和带随机量的摘要承诺。原示例用本方资金账户请求供应商受益账户，返回 `UNPROVEN/NOT_THIS_KIND`。
- 环境为 Windows、.NET 8、隔离 PostgreSQL 18。测试模拟用户、设备配置、租户、账套、伙伴、银行和信用数据、高位 BP 历史、独立消费者、财务发布原件及故障。真实 Main 接口、会话授权、原件读表、旧资金账户服务、源写入锁和最终复核使用实际实现。FIN-MASTER 与 FIN-CREDIT 的正式生产发布端仍归原负责卡。

## 卡级验收

| 场景 | 实际结果与证据 |
|---|---|
| 旧银行与信用原件 | 实际本方资金账户 Owner 回读原引用和 `****4444`。原信用来源为 `123456.78`，币种保持空；显式零返回字符串 `"0"`，未设置返回空。旧额度仅为来源观察，当前采用标志为 false。银行和信用即使都有数值也不产生执行许可。见 `wp11-65-native-history.json`。 |
| 同名、错账套及当前权限 | 另一租户使用同一伙伴代码请求得 403。同法人另一账套得 `UNPROVEN`。新主体借用旧账套 token 得 404，重新签发自己的 token 后能回读原五字段。撤掉信用字段读权后旧引用与金额一同隐藏。 |
| 后继发布与历史保真 | 真实旧银行行改写、旧 CreditLimit 更新与模拟银行后继发布后，旧 Owner、locator、版本、摘要、用途及金额/遮罩保留，返回 `REFERENCE_VERSION_CHANGED` 和 `enabled=false`。以当前账套重新取得 BP 持久链接的内部门同样返回原件。见 `wp11-65-int64-and-recovery.json`。 |
| 缺源、错误摘要与有效上下文 | 没有可信关联的旧银行及旧额度、错主体和错误摘要返回 `UNPROVEN`，无可披露原件。过期 token 和 DEMO 账套被真实 REAL 宿主拒绝。隔离的 FIN-CREDIT 原件使用 30 整数位加 8 小数位，金额与 `JPY` 原样回读。见 `wp11-65-credit-exact-and-absence.json`。 |
| 事务及并发 | 同数据库连接取得 `VALID`、真实正整数门版本及持锁证明。重复项、缺项及另一事务失败关闭。实际旧银行写入在读取方提交前等待；后续按旧引用参加返回 `DENIED/REFERENCE_VERSION_CHANGED`。同一事务晚改来源触发 412，银行原状态、新版本和前沿一起回滚。见 `wp11-65-transaction.json`。 |
| 财务离线、冻结与退休 | 来源标为离线、来源表被独立事务锁住时，实际读取得到 `UNPROVEN`。BP 原链接读取仍有原五字段。模拟的 WP04-01 状态调用在真实当前授权下写入冻结；实际 `BusinessPartnerService.DeleteAsync` 使用当前管理员权限和原行版本完成软退休，保持冻结事实。旧退休 HTTP 入口在真实登录返回 403，跟踪于 [#1277](https://github.com/GTX537/CP6/issues/1277)。见 `wp11-65-offline.json`，HTTP 复现见 `acceptance-a.trx`、`acceptance-b.trx`。 |
| 存量迁移 | 隔离库先装到 WP11-01，在旧入口写入真实表并升级。核对银行和信用各一份首期见证、银行无明文、副作用中无推测作用域或伙伴链接。 |

## FIN-MASTER-01 原 AC 对照

原目录 156 项，现有 **44/156** 通过；239 个补充场景中 **69/239** 通过。单列必需修订 0。原规格 SHA-256 `fe3093133f8fa6ca73a051c558dfd3b6141235c5235bbf28f9f5dd132a9310a8`。WP11-01 已核销的 42 项原 AC、64 个补充场景保持原证。 [原目录](../../assessments/20261011/catalogs/commercial-finance.json) · [WP11-01 报告](../WP11-01/DELIVERY.md)

| 原 AC | 本卡核对及剩余出口 |
|---|---|
| AC-GL-12 | WP11-01 已证单凭非空伙伴串拒绝。本卡提供原件精确查验和持锁门。正式科目消费的 BP UseReceipt 和业务效果归 WP11-04。 |
| AC-SEC-10 | 重复/缺项被拒绝，其他账套查不到来源，部分批次无 guardRevision。FM27 精确批量请求和完整银行消费归 WP11-03。 |
| AC-R1-04 | PASS。实际 Main 返回 2^53−1、2^53 和 2^53+1 三个相邻 BP 版本字符串，与各自持久版本精确一致。 |
| AC-R1-05 | PASS。Int64 最大值原字节往返。实际查询拒绝前导零、负数、零、指数和 max+1，声明为字符串的应用 DTO 拒绝 JSON 数字。 |
| AC-R1-06 | 已保存的五字段由 Main 内部桥及独立新会话恢复。FM26R 发放新键的全链归 WP11-03。 |
| AC-R1-07 | 本卡证实过期账套上下文拒绝。银行短期 key 的过期和重签归 FM26R/FM27，WP11-03。 |
| AC-R1-08 | 新 Actor 使用自身账套上下文，旧 token 拒绝，错伙伴来源拒绝。FM26R 新键与精确 `WRONG_SUBJECT` 回执归 WP11-03。 |
| AC-R1-09 | 补证字段撤权、摘要不匹配与来源离线失败关闭。完整 FM26R 的错误和新 key 状态归 WP11-03。 |
| AC-R1-10 | 后继模拟发布后仍返回旧版本，`enabled=false`。实际科目之外的正式银行后继发布、FM26R 重签与 FM27 拒采用归 WP11-03。 |
| AC-R2-05 | BP 提供者能传原 `9007199254740993` 字符串。银行核验撤销控制 6、真实 Preview/Release 及原键回执归 WP11-03。 |
| AC-R2-06 | BP 侧验证最大合法版本，并拒绝超界和数字。银行完整依赖观察字段、银行业务预览和实际 Owner 写入仍归 WP11-03。 |

| 补充场景 | 本卡已见及剩余出口 |
|---|---|
| BP01 | 精确来源和当前主体已核；FM26 的银行选择、短期键及选择证据归 WP11-03。 |
| BP02 | 同一 Main 实际事务产生准确来源和持锁事实；正式 FM27 供应商来源与冻结请求格式验收归 WP11-03。 |
| BP03 | 错主体、缺可信作用域返回 `UNPROVEN`。FM27 的 `BOUND_BP_CONTEXT_UNPROVEN` 正式响应归 WP11-03。 |
| BPR01 | PASS。独立 Main 持久链接读回主体、法人、`PAYMENT_BANK`、准确五字段、当前无损 BP 版本、UTC 读取时刻和实际模式。 |
| BPR02 | 过期账套 token 被拒绝。原 FM27 银行 key 的过期出口归 WP11-03。 |
| BPR03 | 换人走原持久链接和当前授权。原 FM26R 新键和绑定回执归 WP11-03。 |
| BPR04 | 新主体当前有权保留历史读和原版本；原 FM27 的新键、请求回显和银行属性归 WP11-03。 |
| BPR05 | 错主体和错账套均无原件。FM26R 精确错误封套归 WP11-03。 |
| BPR06 | 当前 field scope 撤权后原件隐藏。FM26R 拒绝新 key 的结果归 WP11-03。 |
| BPR07 | 错摘要不选后继，不泄露原件。FM26R 的原定未证原因和无效绑定回执归 WP11-03。 |
| BPR08W | 已模拟后继来源供 BP 回读。银行 Workflow 读取真实 Proposal 和准确 Head 归 WP11-03。 |
| BPR08 | 模拟后继源下已证旧引用保真。FM06 真正 Proposal/Version 变更归 WP11-03。 |
| BPR09 | 已证后继不替换旧引用并返回不可采用。原 FM26R 重绑 key 归 WP11-03。 |
| BPR10 | Main 财务读离线返回 `UNPROVEN`。FM26R 调用 BP 当前身份 Owner 失联的合同封套归 WP11-03。 |
| BPR64-1 | PASS。精确 `9007199254740991` 小于相邻版本。 |
| BPR64-2 | PASS。精确 `9007199254740992` 小于 `9007199254740993`。 |
| BPR64-3 | PASS。精确 `9007199254740993` 大于前一版本。 |
| BPR64-4 | PASS。精确 `9223372036854775807` 原样相等。 |

其余缺口仍归 WP11-02 的税码与成本中心、WP11-03 的供应商银行发布和原短期键、WP11-04 的财务应用、实际业务凭据及 FM15。完整 BP 状态及正式 UseReceipt 分别在 WP04-01／WP04-02 验证。旧 HTTP 退休的独立出口见 [#1277](https://github.com/GTX537/CP6/issues/1277)。

## FIN-CREDIT-01 原 AC 和修订对照

原目录 104 项，**0/104** 全断言核销；**0/6** 必需修订核销。已取得下列提供者部分的证据。原规格 SHA-256 `9442439f12ceb101fb49f469f596a529f061526d0a7b504289ef5fd3185e9349`。 [原目录](../../assessments/20261011/catalogs/commercial-finance.json) · [必需修订](../../../design-handbook/remediation/08-acceptance-catalog.json)

| 原项 | 本卡核对及剩余出口 |
|---|---|
| AC-FIN-CREDIT-01-01-07 | 旧零可回读且仅有来源含义。显式无限策略的 Owner 证据、信用评估 PASS 归 WP11-05。 |
| AC-FIN-CREDIT-01-01-08 | 已证 BP/来源一致且持锁。Sales Confirm 的真实 I02、UseReceipt 和用后不可复用归 WP11-06。 |
| AC-FIN-CREDIT-01-01-09 | 当前使用检查冻结事实，独立冻结写与来源门分开验证。BP 先冻结后实际信用终门拒绝归 WP11-06。 |
| AC-FIN-CREDIT-01-01-18 | Main 回读按当前信用字段权隐藏金额，未设置时保持空。C03 多金额单元及 REDACTED 回读归 WP11-05。 |
| AC-FIN-CREDIT-01-01-20 | 旧零和空存为不同观察，未见证任何公司策略、币种或信用承诺。实际策略生成拒绝把旧值当决定归 WP11-05。 |
| AC-FIN-CREDIT-R1-18 | 无损 BP 版本与事务内复核已证。真实 Sales UoW 的 I02 UseApplyInput、UseReceipt、回滚归 WP11-06。 |
| OPS-IF-001-POS | 这轮回读原 Owner 作用域。200 字符信用身份到 GL 的可逆 alias、类型、原摘要归 WP11-07。 |
| OPS-IF-001-NEG | 实际财务上下文拒跨模式。信用到 GL 的重复 binding、缺 type 和摘要不配拒绝归 WP11-07。 |
| OPS-IF-042-POS | 归 WP11-05 核对原封闭 MAT 向量和实际操作及回执。 |
| OPS-IF-042-NEG | 归 WP11-05 验证原 MAT 额外成员的 HTTP 400／JSON Pointer 及零写入。 |
| OPS-IF-002-POS | 本卡证明 30 整数位加 8 小数位的来源回读。完整 38 位全 9、原业务写集／回执归 WP11-05。 |
| OPS-IF-002-NEG | 归 WP11-05 在原入口拒绝 39 位、指数与负零并保持原结果。 |

目标后续责任为 WP11-05 的信用政策及源数据、WP11-06 的敞口、预留、特批和真实 Sales 当前门、WP11-07 的 AR／GL 使用、六字段证据与实际采用。两个目标均未进入关闭。

## 工时、排序与登记

- 开工 `2026-11-19T16:39:59.214773+00:00`，技术验证截至 `2026-11-19T17:24:15.092216+00:00`，目标复核截至 `2026-11-19T17:24:31.753885+00:00`。本卡实施及定向验证 **2655.877 秒**，目标复核单列 **16.662 秒**。M 卡预算 14,400 秒，21,600 秒是强制拆卡点。
- 按 #338 单次将 WP11-01 收尾 **470.929 秒**补入该卡，最终 **3,924.491 秒**。本次计划累计 **87008.371 秒**。首个 24 有效小时检查点于 2026-11-19T17:14:23.382773+00:00 达到，累计 86400.000 秒。当时登记卡 65/382，原号和完整 DONE 各 58/375，目标 10/125，累计拆分余卡 7 张，本卡新增拆分 0。 [总控结转决定](https://github.com/GTX537/CP6.AI-Handoff/pull/338)
- 对 [#1276](https://github.com/GTX537/CP6/issues/1276) 固定采用提交 `1236b69aecb5d9cf8c5851e8336f4ce7703d36fb` 运行 `python docs/development/tools/manage_roadmap.py --rank-stage C --at-ref 1236b69aecb5d9cf8c5851e8336f4ce7703d36fb`。历史正确顺序为 WP11-65（41）、WP03-05（41）、WP02-22（39）、WP03-12（6）。历史记录保留，本 PR 附更正。当前候选顺序为 WP03-05（41）、WP04-01（40）、WP02-22（39）、WP03-12（6）。后续开工按 COORD 派工。
- Main/PostgreSQL 定向运行 **7 项**，相邻回归 **71 项**，无跳过。历史伙伴迁移夹具以旧表结构插入，解决新审计模型写入旧表的问题，并单独复测通过。#1276 阶段排序所在的单卡 Python 回归 **8 项**，台账工具全量 **120 项**。远端采用后的定向冒烟和席位释放记录在 DELIVERY。
