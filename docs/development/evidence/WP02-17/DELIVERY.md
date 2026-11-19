# WP02-17 交付报告

SYS-LANG-01 的 20 项原验收通过，必需修订为 0。验证范围包括共享字典、词条当前授权、五语整包发布与回退、客户端采用和原操作恢复。

| 项目 | 证据 |
|---|---|
| 主仓与席位 | GTX537/CP6，Main DEV-1，`D:/CP6-worktrees/wave17-dispatch-20261018`，`codex/wp02-17` |
| 开工基线 | `d678ea8eefa135c897009698c20943f8e359c6c3` |
| 产品验证提交 | `9a5ccc11540aea70b364ddbfed39f31bfeb54f28` |
| 原派工 | [Handoff #335](https://github.com/GTX537/CP6.AI-Handoff/pull/335)。原件 `7c0a4cdf7ddbd0cc2e97355dfac59ee827c4c7b7`，登记采用 `6115ca1b798c337a171c3ff3fbf234161908044f`。[派工消息](https://github.com/GTX537/CP6.AI-Handoff/blob/6115ca1b798c337a171c3ff3fbf234161908044f/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261119T125205Z-exec-wp02-14b-wp02-17/MESSAGE.md) |
| START | `7ebc95ab4171e53c4fb9a144d2e0c38c5a0b30e0`。[原通信分支消息](https://github.com/GTX537/CP6.AI-Handoff/blob/7ebc95ab4171e53c4fb9a144d2e0c38c5a0b30e0/handoffs/CP6-DEV-20261012/tasks/WP02-17/messages/20261119T132520Z-exec-wp02-14b-start/MESSAGE.md) |
| 正常 PR | [CP6 #1273](https://github.com/GTX537/CP6/pull/1273) |
| 严格前置 | WP02-10、WP02-14 及 WP02-14b 均为 DONE，核对各 PR 受审及采用提交均为基线祖先。[#1238](https://github.com/GTX537/CP6/pull/1238) · [#1243](https://github.com/GTX537/CP6/pull/1243) · [#1254](https://github.com/GTX537/CP6/pull/1254) |

## 实际改动

- 字典管理通过原 IAM Owner 的当前授权和事务门。新管理页使用按类型版本、稳定 TypeCode 与 Value、引用保护、历史标签和原回执。原兼容 API 保留调用形状，共用稳定码及引用保护。PostgreSQL 的现行选项直接读取已提交数据。表单 Owner 在最终决定点依原共享字典合同重检当前成员，并附该目录版本。
- 多语言按公共和当前组织分域。租户以服务器验证后的 GUID 为边界，公开全量及命名空间只读已审公共内容。管理列表和导入导出按现行字段授权，显式审校绑定条目版本。旧兼容接口的空数组或缺请求体继续审校所在授权域的全部合格草稿。
- 全部五个包先写入配置的对象存储，再按确切长度和 SHA-256 复核。PostgreSQL 在同一事务更新指针及原操作凭据。回退校验整版，指针竞争失败要求重读，按原键恢复采用原凭据。Native bootstrap 返回同一活动版本。先前按秒编号的包保留兼容读取，新回退清单列出可按原始摘要核验的版本。
- Web 按清单上的精确版核验五包之后整组安装。缺包或坏包保留上一个完整版本。公共缓存与当前组织覆盖的生命周期由各自合同决定。切组织、切语言及旧分包的晚到响应经过当前身份和版本核对。

## 验收结果

[原 AC 目录](../../assessments/20261011/catalogs/foundation.json) · [接受的设计](../../../design-handbook/modules/SYS-LANG-01.md)。原始文件 SHA-256 为 `4638ed3c19d5f24a2935edf39e9c7c518725e92b1c96259d282ebf14e6c94580`。

| 原 AC | 实际结论 | 证据入口 |
|---|---|---|
| AC-LG01-01 | PASS。两个宿主用同一字典版本改标签，唯一成功写保留 Value 为 A。表单 Owner 原样保存带小数形式的字符串 1.0。 | Dictionary_owner_versions / FormOptions_optional_external_value |
| AC-LG01-02 | PASS。保留的表单引用阻止类型及其值的删除，HTTP 409。停用仍可按原值回读标签，新表单提交给出 FORM_OPTION_VALUE_UNAVAILABLE。 | Dictionary_owner_versions / FormOptions_draft_retains_revoked_original |
| AC-LG01-03 | PASS。事务内和提交后分别读取旧标签、新标签。跨宿主读取新字典版本，同一原键返回凭据。未知历史值为空，业务 Owner 重新检查可选集。 | Dictionary_options_are_invalidated / Dictionary_owner_versions / FormOptions_current_source_rejects |
| AC-LG01-04 | PASS。重复 Value 和改型编码均为 409，同一基线并发仅一笔 200，另一笔 412。模拟删除事务晚阶段失败时，类型与数据整组保持。 | Dictionary_owner_versions / Dictionary_native_late_cascade |
| AC-LG02-01 | PASS。同键可分别存在于公共及两个组织，公共重复 409，普通主体写公共 403。公开包无任何组织词条，两个持权组织只收到各自覆盖。 | Language_owner_scoped_fields / Language_owner_legacy_review |
| AC-LG02-02 | PASS。无点日文旧键由全量及 _core 读取，wms、sales、erp、mes 和新增命名空间均按原键返回。 | Language_owner_legacy_review / publication.spec.ts |
| AC-LG02-03 | PASS。占位符不符合时报 LANG_PLACEHOLDER_MISMATCH 和 422，发布指针保持。用日语及简体中文触发的 CSRF 错误均保留 E-SEC-010。 | Language_owner_scoped_fields / Language_owner_legacy_review |
| AC-LG02-04 | PASS。已审组织词条编辑返回 draft，旧审校版本 412。请求体重挂 TenantId 为 400，另一组织已审内容保持。禁止字段导致整组导入回滚。 | Language_owner_scoped_fields / Language_owner_legacy_review |
| AC-LG03-01 | PASS。模拟存储不可写发布返回 503，缺最后一个韩语包和同长度异摘要均返回 409，公共指针和原可读版保留。 | Language_owner_five_packs |
| AC-LG03-02 | PASS。每次新发布分配独立版本，同一原操作在第二宿主逐字段回读与提交重放完全相同，同键异命令 409；回退后原字节一致。 | Language_owner_five_packs / Language_browser_exact |
| AC-LG03-03 | PASS。五语清单准确为 zh-CN、zh-TW、en、ja、ko，Chrome 取齐后才采用。缺词条按当前语言、约定回退语言、ja、原键解析。 | Language_owner_five_packs / Language_browser_exact / publication.spec.ts |
| AC-LG03-04 | PASS。逐个验证实际字节长度和 SHA-256；包中包含已审公共词条。未审词条、旧数字租户和现行组织覆盖均排除。隐藏字段导出 403。 | Language_owner_five_packs / Language_owner_legacy_review / Language_owner_scoped_fields |
| AC-LG04-01 | PASS。旧接口实际空数组审公共域内全部合格词条，缺请求体审各组织自身的合格词条。现行显式集合为空返回 400。 | Language_owner_legacy_review / Language_owner_scoped_fields |
| AC-LG04-02 | PASS。过期审校或已审源变化返回 412。全包只含已审词条，合法修改后该组织译文回到草稿。 | Language_owner_scoped_fields / Language_owner_legacy_review / Language_owner_five_packs |
| AC-LG04-03 | PASS。逐包完整验证后回退到原精确版本，五个文件字节、草稿状态与词条审计数量保持。Native bootstrap 同步报告当前精确版。 | Language_owner_five_packs / Language_browser_exact |
| AC-LG04-04 | PASS。发布与回退同时提交只出现一个 200 和一个 412。Chrome 丢弃真实 200 后刷新，回读同一原回执，浏览器共发一次原写请求。 | Language_owner_five_packs / Language_browser_exact |
| AC-LG05-01 | PASS。真实指针返回 no-cache，五包返回 immutable，按清单核验实际长度与摘要。客户端拒绝同版身份变化和不匹配字节。 | Language_owner_five_packs / publication.spec.ts |
| AC-LG05-02 | PASS。旧 _core 与模块包并能读取新命名空间。Chrome 缺新版最后一包时保留完整原版，恢复后整组采用新版。离线缓存重启复核五包。 | Language_owner_legacy_review / Language_browser_exact / publication.spec.ts |
| AC-LG05-03 | PASS。旧语言请求晚到后保留较新的语言。Chrome 在真实退出并登录另一组织后释放旧响应，另一组织的内容保持；覆盖私有且无公共缓存。 | publication.spec.ts / Language_browser_exact |
| AC-LG05-04 | PASS。浏览器语言切换和重载时保持业务 Ref，字典标签变更保留业务值。缺译保留原稳定键和 E-SEC-010，数值展示继续走原格式化入口。 | Language_browser_exact / publication.spec.ts / Dictionary_owner_versions / FormOptions_optional_external_value |

## 运行环境与模拟范围

Windows 席位运行 .NET 8、隔离 PostgreSQL 18.6、真实 Main ASP.NET Core Kestrel、Vue/Vite 和本机 Chrome。测试使用模拟用户、组织、词条、共享字典及表单。两个 Main 宿主共享当前 PostgreSQL 和临时的本地文件存储。模拟目录不可写、缺包、异摘要、浏览器丢确认和延迟送达。浏览器请求进入真实 Main，经实际 Cookie、CSRF 和当前角色。S3 生产端点、外部制品和生产环境的运行状态未纳入这批结果。

- `language-candidate.trx` 共 37 项，覆盖语言 Owner、字典新版、语言公开/引导兼容和正常 Chrome 链路。结构化输出在 `permission-consumption-language-candidate` 内，包括五包实际摘要、动作及字段过滤、兼容审校、字典版本和 `language-browser/result.json`。
- `dict-green-1.trx` 共 37 项。原字典写请求、当前撤权、事务失败、并发和表单 Owner 组合回归使用相同已验证的字典变更。
- `lang-permissions.trx` 共 9 项，独立隔离库复核共享当前授权合同。Vitest 119 项通过，Vue 类型检查和生产模式构建通过。
- 扩大网页测试时，基线上已有的 `main.spec.ts` 两条旧断言失败。该文件和入口源文件与基线一致，测试桩缺少实际入口等待的 `router.isReady()`，另有一条仍要求已迁移的入口预加载。启动与恢复由真实 Chrome 场景验证。

## 工时与交付

- 领取于 `2026-11-19T08:24:47.161728-05:00`，技术证据锁定于 `2026-11-19T09:04:24.676501-05:00`。本卡候选 2377.515 秒，目标复核单列 63.177 秒。预算 14,400 秒，强制拆卡点 21,600 秒。
- 按 Handoff #335 单次补记 WP02-18 正式收尾 **359.614 秒**，该卡最终 **3428.071 秒**。累计 79984.196 秒（22.218 小时）。本轮累计未达到 24 个有效小时检查点。 [总控签收 #334](https://github.com/GTX537/CP6.AI-Handoff/pull/334)
- 受审提交和核销台账随同一 PR 采用。远端精确合并树、合并后冒烟和实际席位释放以官方 `finish` 与原通信分支 DELIVERY 的回读为准。
