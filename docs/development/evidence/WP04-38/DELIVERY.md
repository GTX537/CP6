# WP04-38 正式伙伴导出与原验收补正

交付范围为 B03、#366 指定的 24 项原 AC 及本卡台账。Main 基线 `340804cd1f297aff488572b99ea49dda3762f76a`，受验产品 `7045d79d8792238f7fa2b6689653acd216520119`。在保护分支合并后核对远端、已采用树及冒烟，正式结果沿原链交回。[Main PR](https://github.com/GTX537/CP6/pull/1294) · [派工 #367](https://github.com/GTX537/CP6.AI-Handoff/blob/b053d688eabd4e0724bd9e1f940e4806a158a3b7/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T154219Z-exec-wp02-14b-wp04-38/MESSAGE.md) · [原链 START](https://github.com/GTX537/CP6.AI-Handoff/blob/5f04fade035d20b136e9c60c6da20c569da4876c/handoffs/CP6-DEV-20261012/tasks/WP04-38/messages/20261120T155006Z-exec-wp02-14b-start/MESSAGE.md)

## B03 实际出入口

- 正式伙伴 Vue 的导出使用已提交的列表筛选与当前法人。Main 经独立 export、当前 read、据点及对象范围，在 PostgreSQL 同一 Serializable 快照中执行查询与字段裁剪。写出固定列和当前有权的敏感列，记录策略版本和 asOf。旧 CSV 的旧档隔离另行复测。
- 实际 HTTP 核对 5000 行逐编号、包含当前页之外记录、其他法人、跨租户及同租户的对象隐藏、导出零伙伴写。BOM、CRLF、RFC 4180 引用和五类公式首字符均由下载字节复核。5001 行拒绝，无附件和正常下载头。
- 实际 Vue、Chromium 与 Main 录得第二页仍导出全部 26 个可见样本，编辑尚未提交的筛选未改变下载，切换法人后 5001 行请求提示缩小范围，零第二次下载。
- 修正普通建档执行阶段的当前注册权限、B22 独立审计读取和 B21 币种历史投影。审计权限由租户授予，跨作者重放仍拒绝。[注册缺陷 #1291](https://github.com/GTX537/CP6/issues/1291) · [审计缺陷 #1292](https://github.com/GTX537/CP6/issues/1292) · [币种历史 #1293](https://github.com/GTX537/CP6/issues/1293)

## #366 原 AC 逐项证据

原目录使用 `docs/development/assessments/20261011/catalogs/commercial-finance.json`，编号与原来源行保持一致。实际 PostgreSQL 18 和 Main 隔离宿主在 D:/CP6-test-artifacts/WP04-38/current-20261120。下表的名字均指证据目录中的结构化输出。

| 原 AC | 原始输入 | 原始预期 | 实际结果 | 证据 | 模拟输入 |
|---|---|---|---|---|---|
| AC011 · 8506 | 符合规则候选超过50 | 返回DUPLICATE_REVIEW_REQUIRED，不能只显示50后声称全确认 | 通过。50 个可列全量候选，51 个时正式预览返回 DUPLICATE_REVIEW_REQUIRED 和空候选集。 | `wp04-38-candidates-parent.json` | 用已建档前沿衍生同名根和规范索引。 |
| AC021 · 8516 | PARENT_CUSTOMER形成环或超32层 | RELATION_CYCLE/REFERENCE_UNPROVEN；无父链部分写 | 通过。32 层预览可用。成环返回 RELATION_CYCLE，第 33 层为 REFERENCE_UNPROVEN，两次 apply 均无父链及业务版本写入。 | `wp04-38-candidates-parent.json` | 以已确认父节点和模拟 33 层父链作为输入。 |
| AC022 · 8517 | BP兼Supplier+AP，旧PaymentScheduleCd只有一个值 | 不能默填两个槽；明确SUPPLIER_AP/AP_SCHEDULE各自证据；未知不通过对应交易使用 | 通过。旧单值 LEG-ONE 可查、两个正式槽均为空，I02 拒绝。明确写入自身 SUPPLIER_AP 与异伙伴 AP_SCHEDULE 后，I02 记录独立依赖并保留旧历史。 | `wp04-38-payment-slots.json` | 模拟审批采用的旧档、供应商、付款伙伴和外层采购调用者。 |
| AC023 · 8518 | SupplierPattern!=5却传subcontractTarget=true或外注专属值 | FIELD_INVALID；不能获得外注/制造许可 | 通过。模式 1 的真外注标记与专属字段分别触发 FIELD_INVALID，零伙伴写。外注和制造用途不取得 I02 收据。 | `wp04-38-month-end.json` | 模拟外注输入、用途消费者及候选编号。 |
| AC024 · 8519 | PaymentSchedule paidEachTime=false且closingDay缺失，另测99 | 缺失PROFILE_INCOMPLETE；99保存为末日规则，不替成今天日期 | 通过。paidEachTimeFlg=false 且缺少结算日时注册 422 PROFILE_INCOMPLETE。写入 99 后可注册，API 和数据库均回读 99。 | `wp04-38-month-end.json` | 模拟付款日及正式伙伴角色。 |
| AC026 · 8521 | 旧CurrencyCd=null，新用途读取或维护 | 历史保留NULL未证，新使用不默认为JPY；明确新Currency决定不改旧订单币种 | 通过。旧商业币种历史仍为 null，首次 I02 无证拒绝。明确 EUR 后新用途记录 EUR，既存订单仍 USD。 | `wp04-38-legacy-and-directory.json` | 模拟已批准采用的历史前沿、旧订单和 Sales 外层调用。 |
| AC027 · 8522 | 5001行导出；名称以=开头 | 前者EXPORT_LIMIT无截断；合法范围CSV作公式文本转义、当前字段裁剪 | 通过。同一筛选完整导出 5000 行，追加第 5001 行时返回 422 EXPORT_LIMIT，无附件。核对五类公式首字符、双引号、逗号、换行、BOM 与 CRLF。 | `wp04-38-b03-boundary.json` | 通过实际建档前沿衍生大批量样本，另用真实 Vue 和 Chromium 下载与切换法人。 |
| AC028 · 8523 | Finance/字典Owner暂未知，尝试无关名称维护与注册/使用 | 合法不依赖该证据的维护可完成；涉及未证引用的注册/使用失败关闭，历史可读 | 通过。字典 STA 停用后与其无关的名称维护成功，旧历史可查，相关注册 422、I02 未证且无新收据。另回归 Finance Owner 离线下可用的独立操作、注册拒绝及当前调用者事务内引用。 | `wp04-38-legacy-and-directory.json` | 关闭模拟字典条目。Finance 故障使用隔离库的来源表锁及模拟原发布。 |
| AC035 · 8530 | PRE_REGISTERED且FROZEN执行REGISTER | 满足资料时可REGISTERED但Hold不变；没有交易放行 | 通过。预登记冻结后可正式注册，Hold 仍 FROZEN，同对象实际 I02 返回 BP_FROZEN，零收据。 | `wp04-38-state-race.json` | 模拟角色、伙伴及 Sales 外层调用。 |
| AC036 · 8531 | UNKNOWN_LEGACY旧删档普通REGISTER或RESTORE无confirm | 拒绝；有权明确当次重新确认后才建当前REGISTERED，旧历史仍UNKNOWN | 通过。UNKNOWN_LEGACY 的普通注册和无确认恢复均 422。无 register 时明确确认仍 403。授权后当次恢复为 REGISTERED，历史首版保持 UNKNOWN_LEGACY。 | `wp04-38-legacy-and-directory.json` | 以原明文、摘要和 Main 密钥环生成模拟审批采用的旧删档起点。 |
| AC037 · 8532 | 冻结/停用BP修改合法名称或交易资料 | 有权可维护，独立状态轴不变；不自动绿灯/恢复 | 通过。冻结后修改名称、退休后修改电话均成功，登记、冻结和退休各轴保持，退休仍拒绝 I02。 | `wp04-38-state-race.json` | 模拟可维护的当前角色、伙伴和 Sales 外层调用。 |
| AC072 · 8567 | FK把另Tenant同UUID关联到Profile/Mapping/Relation | 服务与数据库共同拒绝，所有关系必须含T/L | 通过。同一异租户 UUID 经资料、父链和映射入口拒绝，物理 profile_revisions、relations、mapping_revisions 三项 FK 均返回 23503。 | `wp04-38-foreign-uuid.json` | 两个模拟租户、用户和签名 CRM Account Owner。 |
| AC075 · 8570 | 历史/错误/CSV读取敏感字段时失权 | 按当前字段权遮罩，不从错误、候选或日志泄漏原文 | 通过。实际字段撤权后 CSV 无列及原值，历史遮罩且无可反推摘要，错误、候选请求与实存审计不回敏感原文。正常下载的策略版本与 asOf 在安全审计回读。 | `wp04-38-b03-fields.json` | 模拟电话、角色字段和当前权限变化。 |
| AC079 · 8574 | 一个普通Profile更新与同BP状态动作同ExpectedRev | 最多一个成功；另一412，资料和状态历史都不覆盖 | 通过。真正并发的普通资料更新与冻结都使用修订 1，只一项写成修订 2，另一请求返回 412，资料与状态历史按赢家各自保留。 | `wp04-38-state-race.json` | 模拟竞争请求和伙伴。 |
| AC082 · 8577 | 仅文书JSON/摘要检查通过 | 业务AC仍NOT_RUN，不计阶段100，不声称DB并发/物理接口已通过 | 通过。只提交原目录 JSON、文书说明与摘要引用，原业务 AC079 保持 NOT_RUN，已交付目标计数为 0。将其强行标作 PASS 会被真实台账校验拒绝。 | `ledger-verification.json` | 台账工具内存副本，静态材料为测试输入；此证据只核销 AC082 的登记行为。 |
| AC090 · 8586 | 纠错混入其他Scope/Revision、缺原body/Trust或CRM高于Main | B27 UNPROVEN/OWNER_STREAM_CONFLICT，禁止MAX+1/假UNLINK | 通过。CRM 领先、混入异法人或修订、缺原 body 或 Trust 均使 B27 保持 UNPROVEN。关系头留在 r12，原 BP、日志和业务写集合不变。 | `wp04-38-ac090.json` | 模拟签名 CRM 和决策 Owner，原七字段与本地持久前沿由实际 Main 产生。 |
| AC092 · 8588 | FieldPolicy隐藏paidEachTimeFlg | ProfileRead保成员且null+redactedFields；公开私有内容hash=null，不返回false或省略成员 | 通过。隐藏 paidEachTimeFlg 后，ProfileRead 仍有其成员，值 null，redactedFields 含该字段，原内容摘要为 null。 | `wp04-38-b03-fields.json` | 模拟当前字段隐藏。 |
| AC093 · 8589 | 将R01遮罩null作为非nullable写bool提交 | 原失权Actor403；即使有权Actor也FIELD_INVALID，不放宽ProfileValues | 通过。用遮罩 null 写非空布尔，当前无写权时 403。恢复字段权后同一输入为 400 FIELD_INVALID。 | `wp04-38-b03-fields.json` | 模拟相同 actor 撤和恢复字段权。 |
| AC102 · 8599 | 有create/Base/字段权，无register | 只提供PRE_REGISTERED模式；REGISTERED B05/B06/B17/B18均403 | 通过。保留创建、据点和字段权，但撤注册。只提供 PRE_REGISTERED，B05/B06 及 B17/B18 的 REGISTERED 请求均 403，无业务写。 | `wp04-38-creation-grants.json` | 模拟角色工作流与签名 CRM 账户。 |
| AC104 · 8601 | 其他权齐备，仅所选Base可读不可管理 | ORG_SCOPE_DENIED；不能用能看到Base证明可创建 | 通过。据点在可读范围内，但 canManage=false 时无创建入口，B05/B06 返回 ORG_SCOPE_DENIED。 | `wp04-38-creation-grants.json` | 模拟据点授权和请求历史。 |
| AC105 · 8602 | 完整newPartner任一Core/Profile/关系创建字段无写权 | FIELD_DENIED，显式null/false也不旁路；只读Finance字段仍不可写 | 通过。分别撤销 Core、Profile 与关系写权，完整草稿即使显式 null、false 或空关系也返回 FIELD_DENIED。写入原银行字段被拒绝。 | `wp04-38-creation-grants.json` | 模拟字段权限、原 Finance 只读字段以及完整草稿。 |
| AC109 · 8606 | 普通B05/B06选择REGISTERED却无register | 同AuthorizeCreateIntent拒绝，不形成另一注册旁路 | 通过。普通创建和关联建档使用同一创建意图授权。原有注册预览在 B06 重验，撤销权后返回 403，零写入。 | `wp04-38-creation-grants.json` | 模拟审批时至执行时权限变更。 |
| AC119 · 8617 | 其他Actor尝试POST原Command，即使可审计查询 | 不代原Actor重放或写；不泄露无权对象，B22审计按独立范围 | 通过。独立审计权限及对应范围允许 B22 取得原回执。撤范围后与未知键相同。另一 Actor 的 POST 重放均 404，业务计数不变。 | `wp04-38-command-audit.json` | 模拟另一 Actor 与独立审计角色。 |
| AC124 · 8622 | 原NO_CHANGE或REJECTED已有终态，今日条件改变 | 按查询/披露权回原终态，不当空位重新执行业务 | 通过。实际 NO_CHANGE 及模拟已存 REJECTED 均在当前条件改变和撤写后按旧键得到同摘要结果，新写入为零。重新授权后新键按当前资料可正式注册。 | `wp04-38-old-terminals.json` | 模拟已持久的原拒绝终态；实际 Main 执行前置拒绝、查询、重放和新的成功注册。 |

## 运行与核销

- Main 与隔离 PostgreSQL 专项 共通过 13/13。输入和过滤条件见 `proof.json`。
- 正式 Vue 与 Chromium 补跑 共通过 1/1。输入和过滤条件见 `proof.json`。
- 旧档历史及恢复专项补跑 共通过 1/1。输入和过滤条件见 `proof.json`。
- Main 伙伴、I02、映射与审计相邻回归 共通过 29/29。输入和过滤条件见 `proof.json`。
- Unicode 15 正式向量补跑 共通过 1/1。输入和过滤条件见 `proof.json`。
- 相邻回归首轮缺少 Unicode 向量路径，所跳过的同名用例使用本地已固定的官方 Unicode 15.0.0 输入在同一受验产品重跑通过，独立列记。
- 首轮浏览器已取得正常文件，法人选择器的测试定位超时。修正浏览器夹具定位后全场景重跑，使用同一 Main/Vue 产品。
- 旧删档用例初轮将两个业务拒绝误写为 409，实际合同为 422。修正断言后以相同 Main 产物重跑完整币种、字典和恢复场景。
- `vue-type-final.log`、`vue-build-final.log` 保存类型检查和正式构建。原 Finance 证据使用真实 Main 及隔离数据库，原发布、合法调用者和离线故障为模拟。签名 CRM 桥、身份设备、历史订单和万级以内的批量伙伴数据均是模拟输入。
- ERP-BP-01 原 AC **103/124**，必需运行修订 **3/10**，保持开放。其余 21 项原 AC 和 7 项修订沿 #363 共五个出口，AC063 由 WP14-08 承接。两份原 QUESTION 和历次裁决都保留。[目标台账](../../roadmap.json)
- `WP03-07` CRM A13 实际采用七字段和 R03。覆盖接收乱序、原始冲突、签名分叉裁决及 CRM 当前资格竞争。 原项 AC047、AC058、AC086、AC091。 必需修订 W1-ACC-03、W1-ACC-04、W1-ACC-05、W1-ACC-06。
- `WP03-06` CRM 账户来源证明及旧 C03 结果按请求回读。保留 ResultVersion、原字节和无证 H02 原 Manifest 的等待行为。 原项 AC065。 必需修订 W1-ACC-08。
- `WP04-16` Sales 正式确认的实际信用、技术和伙伴 Owner 参加。验证 Main 当前映射在缓存投影之后改变及原资格已失效的最终拒绝。 原项 AC041、AC081。 必需修订 W1-ACC-07。
- `WP14-04` 用真实采购来料和应付链核实无 Account 的供应商用途，保留其原 BP 参与、关系和实际业务写集。 原项 AC039。
- `WP14-08` 跨仓实际环境和参与者的撤权、未知与迟到原结果专项。复核已采用权限、同原键/额度和实际边界，待核部分保留原范围。 原项 AC063、AC070、AC073、AC074、AC076、AC077、AC078、AC107、AC108、AC115、AC117、AC121、AC122。 必需修订 W1-ACC-01。 按 #363 复验两次 Main 映射修订之间的迟到原回应。
- 按 #366 给 WP04-38 一次划拨 14400 秒，已结清的 WP04-03 历史预算 28800 秒、实用 4415.904 秒保持，划后节余 9984.096 秒。原 375 卡、7 次拆卡与额外补正分别记录，2052 小时卡池保持。按 #365 查重后把 WP02-23 收尾 288.834 秒补记一次，该卡累计 3601.043 秒，计划起点 115606.103 秒。
- 核销快照 `2026-11-20T16:33:20.515145+00:00`。本卡 2593.725 秒，独立目标核对 0.235 秒，计划累计 118200.063 秒。下一候选 `WP03-12`，领取等总控派工。
- 实际采用、冒烟、分支处置、席位释放和正式收尾时间由 `adoption.json`、`finish.json` 和原链 DELIVERY 回读。领取时 DEV-1 指向上述基线。[工作区登记](../../worktrees/REGISTRY.md)

[授权 #366](https://github.com/GTX537/CP6.AI-Handoff/pull/366) · [前卡签收 #365](https://github.com/GTX537/CP6.AI-Handoff/pull/365) · [原 24 项目录](../../assessments/20261011/catalogs/commercial-finance.json)
