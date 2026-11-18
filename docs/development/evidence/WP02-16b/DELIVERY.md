# WP02-16b 编号域、受控切换恢复与规则管理

**交付范围**。WP02-16 的两个剩余出口已在本卡核销。Main 业务源码为 `4a78be8dbe354a3826f50da5bcf45d98825ec5db`，准入主线为 `3ea9d2ba2ab4e1a644369432460a5251e2f1bcda`，执行席位为 DEV-2，分支为 `codex/wp02-16b`。原卡与本卡覆盖 SYS-NUM-01，覆盖表中无其他所属卡。必需修订场景为零，原验收计 16 项。[业务 PR #1256](https://github.com/GTX537/CP6/pull/1256) · [前置 PR #1244](https://github.com/GTX537/CP6/pull/1244) · [原卡的两项剩余出口](../WP02-16/card-closure/DELIVERY.md)

COORD 的正式决定已在登记分支提交 `9d02e1077d3fac1fdbfc8ac8ec36245819ec65b2` 回读，消息原始提交为 `32bc79ed05b461bf332f7bc1c83b4f90b7f16cd0`。EXEC-WP02-14B 领取 DEV-2 后，在原通信分支发布一次 START，完整提交为 `a0955ee2229038a3989c9c427344cad1cf283824`。[派工原件](https://github.com/GTX537/CP6.AI-Handoff/blob/9d02e1077d3fac1fdbfc8ac8ec36245819ec65b2/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261118T070245Z-exec-wp02-14b-wp02-16b/MESSAGE.md) · [START 原件](https://github.com/GTX537/CP6.AI-Handoff/blob/a0955ee2229038a3989c9c427344cad1cf283824/handoffs/CP6-DEV-20261012/tasks/WP02-16b/messages/20261118T072122Z-exec-wp02-14b/MESSAGE.md)

## 实际改动

- 编号域登记逐项列出 94 个 Main 主调用点及 23 个包装、派生和外部入口。125 个源码定位均已核对到固定提交上的实际行，覆盖 85 个源文件。按原分配器分别保留业务归属方、租户或全局键、法人／仓库关系、日期输入和切换资格。CRM 原生及旧公开回执、Main 受注接入与仓储账务引用保留各自原始身份。[完整编号登记](../../number-domain-register.md)
- 旧 Pub 域需要受信任业务归属方的批准、原规则行锁、逐周期水位、各序位外露号／跳号和原操作清单。PostgreSQL 同事务封存，旧写入随即拒绝。未决键按原归属方查询。恢复候选的写入还需当前唯一写入批准及外部保有的完整分配原件，缺项保持只读。新域原合同与其他旧业务域的写入入口继续各自承担原有责任。[提供者与恢复合同](../../number-allocation-v1.md)
- 实际 `/pub/seq` 分别呈现旧规则及受控域。管理页读取原分配器、租户与唯一域、业务／法人／仓库、实际用量、当前版本、有效时间、周期及时区。旧预览标记跨期限制，草稿预览零占号。412 要求重读再核对草稿。UNKNOWN 保留原键和完整输入，错键或未找到均继续待确认。[管理页源码](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/cp6.web/src/views/pms/NumberRuleEditor.vue)

## 本地验证和边界

| 验证 | 实际环境和结果 |
|---|---|
| 编号提供者、旧分配器与 HTTP | 最终源码对 CP6.Tests 执行 `dotnet test --filter "FullyQualifiedName~.Number_\|FullyQualifiedName~SeqService"`，启用 `Cp6PostgreSqlDevelopment=true`。41 项通过，零失败、零跳过。其中 37 项使用隔离 PostgreSQL 18.6，4 项是原 SeqService 组件用例。HTTP 场景使用实际 Kestrel、登录与权限链。 |
| 恢复 | 已提交但回包丢失的 N004 在原库和还原库按原键得到同一回执。模拟业务归属方独立持有外露清单。两个新库真实经过 pg_dump 和 pg_restore。旧备份缺少 N005，禁止续号；完整候选取得批准后发 N006。两个恢复库均按其实际身份回收，本卡 PG 进程已停止。 |
| 浏览器 | 本机 Chrome 打开真实 Vite 路由和组件，测试账户通过正常登录表单。模拟登录、授权响应、管理数据、412、丢回包和原键查询。4 组场景通过，前端 `npm run type-check` 通过。业务号码和模拟审批仅用于上述测试。 |
| 静态与复用 | Main、CRM 逐源码位置回读，CodeGen 的实际预览、比较、持久元数据和采用实现仍与 W263 已采用版一致，沿用 W261／W262／W263 与 WP02-16 的证据。普通分支推送与 PR 的两侧工作流及依赖按每个实际候选检查。 |

初次新增 HTTP 场景发现目录缺少预期的禁止缓存响应，浏览器还发现未加载翻译条目时确认版本和旧示例会显示占位符。两处均修正并在最终输入复验。首次浏览器运行使用的默认浏览器包未安装，最终使用本机已安装 Chrome。构建保留库中已有警告。本卡未运行 GitHub Actions，也未部署生产。此处不将模拟归属方代表为实际业务消费者已切换。

可复核的本机证据在 `D:/CP6-test-artifacts/WP02-16b/`。最终 `number-final.trx`、`permission-consumption-number-final/*/native-restore.json`、`browser-final/result.json`、`browser-final/recovered.png` 和 `web-types-final.log` 分别对应上述结论。初次运行及中间修正结果按轮次另存。

## SYS-NUM-01 原验收

表中 16 项均为 PASS。涉及 CodeGen 的四项单列历史证据；相关运行和模拟边界以证据原件为准。当前台账的 `runtime_case_ids`、修订输出和设计问题引用均为空。[原设计与验收入口](../../../design-handbook/modules/SYS-NUM-01.md)

| 原验收 | 核对结果 | 可回读的证据 |
|---|---|---|
| AC-NU01-01 | 同原业务键与输入返回原分配；异义输入拒绝。切换前的已知与未决操作均回到原归属和原回执。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberAllocationPostgreSqlTests.cs#L32) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L88) · [W254](https://github.com/GTX537/CP6/blob/7557fec34b428bed56408bf6c195ba4de4ac9e55/docs/development/evidence/W254-01/DELIVERY.md) |
| AC-NU01-02 | 独立连接同域并发唯一；业务事务回滚不回收已分配号。切换后的六路并发越过原外露号与跳号。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberAllocationPostgreSqlTests.cs#L43) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L88) · [W254](https://github.com/GTX537/CP6/blob/7557fec34b428bed56408bf6c195ba4de4ac9e55/docs/development/evidence/W254-01/DELIVERY.md) |
| AC-NU01-03 | 未登记、非法格式、耗尽均拒绝；最小宽度不截断有效长号码，失败不推进流水。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/SeqServiceTests.cs#L56) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/SeqServicePostgreSqlTests.cs#L130) · [W254](https://github.com/GTX537/CP6/blob/7557fec34b428bed56408bf6c195ba4de4ac9e55/docs/development/evidence/W254-01/DELIVERY.md) |
| AC-NU01-04 | 编号回执的分配状态与业务提交分开；提交后回包丢失走原编号操作，未获原业务结果时保留未知。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberAllocationPostgreSqlTests.cs#L65) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L88) · [W254](https://github.com/GTX537/CP6/blob/7557fec34b428bed56408bf6c195ba4de4ac9e55/docs/development/evidence/W254-01/DELIVERY.md) |
| AC-NU02-01 | 旧页与受控域草稿都标明未占号。真实 HTTP 的预览不产生分配，浏览器断言只有预览请求。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L250) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberRulePostgreSqlTests.cs#L115) · [Chrome 场景](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/cp6.web/scripts/number-rule-browser.mjs) |
| AC-NU02-02 | 旧规则跨周期示例保留原计数并提示限制；受控域按登记时区和实际周期返回示例。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L250) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberAllocationPostgreSqlTests.cs#L104) · [Chrome 场景](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/cp6.web/scripts/number-rule-browser.mjs) |
| AC-NU02-03 | 格式编辑只追加新版本；BizKey、周期计数和旧分配原件保持原值。管理用量按外露事实计数。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberRulePostgreSqlTests.cs#L18) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L126) · [W257](https://github.com/GTX537/CP6/blob/d742cd642e0ced782fefca8a086337ad677719d2/docs/development/evidence/W257-01/DELIVERY.md) · [W260](https://github.com/GTX537/CP6/blob/6c9a9e34171fa66a8d10c982a3d9cd001d308e82/docs/development/evidence/W260-01/DELIVERY.md) |
| AC-NU02-04 | 格式、容量和历史重号在保存或实际分配处明确拒绝。规则并发只接受同一原版本的一次更新，页面保留 412 草稿。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberRulePostgreSqlTests.cs#L136) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberRulePostgreSqlTests.cs#L18) · [W257](https://github.com/GTX537/CP6/blob/d742cd642e0ced782fefca8a086337ad677719d2/docs/development/evidence/W257-01/DELIVERY.md) · [Chrome 场景](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/cp6.web/scripts/number-rule-browser.mjs) |
| AC-NU03-01 | 94 个主调用点及 23 个包装、派生和外部入口逐项列出原分配器、真实键和时间依据。同名租户域实际分别写入，MES／PUR 等同名键按原表区分。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L163) · [业务源登记](../../number-domain-register.md) |
| AC-NU03-02 | 原业务时点与登记时区决定周期；跨时区日期、格式变更和重新进入旧周期均保留历史号。周期与时区兼容提示见管理页。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberAllocationPostgreSqlTests.cs#L104) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberRuleHttpPostgreSqlTests.cs#L23) · [W254](https://github.com/GTX537/CP6/blob/7557fec34b428bed56408bf6c195ba4de4ac9e55/docs/development/evidence/W254-01/DELIVERY.md) · [Chrome 场景](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/cp6.web/scripts/number-rule-browser.mjs) |
| AC-NU03-03 | 真实旧写入与切换共用规则锁。切换重新核对旧水位、封存原操作并拒绝旧写入。未决原键查原归属方，拒绝错号或异义输入。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L139) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L88) |
| AC-NU03-04 | 两份实际 pg_dump／pg_restore。过时库可读原回执，缺少外露 N005 时拒绝新号；完整候选核对原件及写入批准后取 N006。跳号与外露未落业务的号码均不回收。 | [本地用例 1](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L193) · [本地用例 2](https://github.com/GTX537/CP6/blob/4a78be8dbe354a3826f50da5bcf45d98825ec5db/CP6.Tests/NumberCutoverPostgreSqlTests.cs#L126) |
| AC-NU04-01 | 即时生成预览为纯计算；固定模板和建议路径生成可复核输出，本卡未改 CodeGen 产品输入。 | [W261](https://github.com/GTX537/CP6/blob/804140e8b05f435b90835366c51b43d7f64e8360/docs/development/evidence/W261-01/DELIVERY.md) · [WP02-16](https://github.com/GTX537/CP6/blob/2e1d038f1cdf6db29a81e5e540334a3084589712/docs/development/evidence/WP02-16/card-closure/DELIVERY.md) |
| AC-NU04-02 | 持久元数据按租户与当前 save／view 授权读写，采用旧元数据时保持原来源及单一写入者。 | [W262](https://github.com/GTX537/CP6/blob/cbbe57582562a10b506862dc281c4dab62acfad9/docs/development/evidence/W262-01/DELIVERY.md) · [W263](https://github.com/GTX537/CP6/blob/bab56981b95631fe349e4feede706d4fd35b642d/docs/development/evidence/W263-01/DELIVERY.md) |
| AC-NU04-03 | 相同输入、模板版本与输出可逐项对照，候选差异、白名单及原自定义区域由比较合同核验。 | [W261](https://github.com/GTX537/CP6/blob/804140e8b05f435b90835366c51b43d7f64e8360/docs/development/evidence/W261-01/DELIVERY.md) |
| AC-NU04-04 | 元数据保存 UNKNOWN 按原键回查版本；仅确认原结果后显示保存完成。候选文本不产生仓库应用或部署。 | [W262](https://github.com/GTX537/CP6/blob/cbbe57582562a10b506862dc281c4dab62acfad9/docs/development/evidence/W262-01/DELIVERY.md) · [W263](https://github.com/GTX537/CP6/blob/bab56981b95631fe349e4feede706d4fd35b642d/docs/development/evidence/W263-01/DELIVERY.md) · [WP02-16](https://github.com/GTX537/CP6/blob/2e1d038f1cdf6db29a81e5e540334a3084589712/docs/development/evidence/WP02-16/card-closure/DELIVERY.md) |

## 受保护合并及席位

产品源码、上述验收与台账通过同一业务 PR 进入 main。台账中的 `remote_main_sha` 固定本卡核验过的原主线及既有能力。新增实现的实际采用由 PR 的合并提交、与最终候选的树比对和官方 `finish.json` 证明。其后执行人用原通信分支发布 DELIVERY，记录准确合并提交、功能分支处置及固定席位释放。主工作区的用户文件保持原状。[业务 PR 和采用记录](https://github.com/GTX537/CP6/pull/1256)

## 工时及后续归属

WP02-16b 卡 DONE，WP02-16 及拆分余项完整 DONE。SYS-NUM-01 的 16 项原验收全部通过，适用必需修订 0 项。原号 DONE 50/375，完整 DONE 49/375，登记卡 54/380，目标 5/125。

本卡从 2026-11-18T07:20:39.151149+00:00 到 2026-11-18T08:44:34.5223508+00:00 实施 5035.371 秒，剩余预算 14264.456 秒。另计目标核对 554.544 秒和上卡正式收尾 214.296 秒。计划累计 10.629 小时，未到 24 小时检查点。本卡合并及席位收尾随官方 finish 记录。

阶段 A 无满足严格前置的下一卡。WP02-09b 的依赖环仍阻断 WP03-02／WP03-04 的后续推进，等待唯一 COORD 裁决和正式派工。
