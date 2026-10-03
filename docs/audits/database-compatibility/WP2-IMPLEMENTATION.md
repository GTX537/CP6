# DB-COMPAT-01 WP2：四 Context 映射与迁移实施

日期：2026-10-03。主任务 [#134](https://github.com/GTX537/CP6/issues/134)，保持 Open。当前状态：**WP2 本地门禁已通过：最终 SQL136→140 保留数据升级2/2、完整目录/定向写入/负对照35/35、未知history拒绝2/2；同一新API首次和重复初始化均exit0、无HTTP，各次352表种子保留验证2/2。既有PG36/36、历史fresh33/33、PG实际首次/重复初始化与334表保留、151/151及独立25/25保留原执行来源；另一次报价PG2/2不与36合并计数。BUG137/139/141/143均Closed。两分工30/45文件集中审查及两P2定向复查完成，后加报价/最终升级四文件及reader两行修正按原增量review复用。8个owned测试库清理及不存在核对完成，WP2远端交付Pending。** 分支 `codex/db-compat-wp2-20261002` 从已确认的 `d6074aaad3098b61adaeda902b92bf24b3eb0c04` 建立，保留任务改动并依次fast-forward至各独立BUG主线；当前基线是 [PR144](https://github.com/GTX537/CP6/pull/144) 合入的 main `94c0f8c9cac63a72008c4e6b242c94358f25b305`。d607继续作为原SQL合同oracle；本次实际源码仍是未提交WP2任务输入，基线SHA不是完整candidate身份。下方旧Pending/错误记录保留历史时点，不改称新执行或阶段交付完成。

前置 WP1 已通过 [PR #136](https://github.com/GTX537/CP6/pull/136) 正常合入，于 13:50:01 UTC merged。远端 main 包含实现检查点 `d30da70e` 与收尾证据 `7bbe2eca`；完整 diff 的代码与原始 JSON 与已验证分支相同，按策略复用配置 133/133、SQL28/PG31 限定表证据，不把 SHA 改变当作重新执行。13 份 GitHub workflow 与四 Azure YAML 保持原样，没有本任务引发的运行、取消或环境推广。首次普通 push 发生 HTTP408，远端未收到分支；单命令 HTTP/1.1 重试成功，没有 force 或全局 Git 配置修改。

实施输入是 [WP1 冻结决策](WP1-DECISIONS.md) 与 [WP2 检查清单](WP2-MAPPING-CHECKLIST.md)。同一代码构建以 Provider 选择完整模型；SQL Server 历史迁移保持不变，新增 tenant generation 前向迁移，另由独立 BUG139/141/143 恢复历史遗漏的索引/FK/报价容量。PostgreSQL 单独程序集、四组迁移和快照、public 固定 history；完整安装后登记并验证数据库 token trigger、generation、OIDC 原始对象、财务不可变保护与历史必要索引/默认/数据规则。

先用独立模型测试留下可编译的 RED，再修改共享 Core / Identity / ERP 和 Space 的 Provider 映射。UTC、本地 wall-clock 与日期分别保留原合同；精度、键/FK、租户过滤、NULL 唯一性、soft-delete/状态谓词、所有 check 与标识符规则逐项验证。构建、restore、EF scaffolding 使用串行本地槽及仓库 EF8.0.30 入口。

本阶段的收尾证据必须包括两库真实空库迁移、四 history applied/pending、重复初始化与种子幂等、关键类型/约束/事务/财务保护，以及 SQL Server 保留数据前向升级。PG 首版没有历史发布版，不虚构 PG 历史升级通过。记录失败、复用来源、受控库身份与所有权清理。PostgreSQL 应用运行 guard 在后续业务适配和最终验收前保留；只允许完整验证后的 one-shot db-init 单独入口，不提前启用 API/worker。

## 当前已执行证据与边界

### 最终 SQL136→140、完整目录与真实初始化

Root于 **2026-10-03 07:25:32–07:25:49 UTC** 执行 `tmp/wp2-migration-sql-final-populated136-to140.json`，实际 **2/2**（含owner），SHA-256 `665A73B441C14C0CB6C8697A46BEEB38821E43626ED3B56564F9230BA90D0966`。支持的136条Core history精确保留，只按序追加 generation、21个索引、四FK、六列容量四个指定迁移至140。351张原有表计数保留（history增加4，临时Snapshot增加2后清理）；两条捕获Snapshot的完整字段及8字节token、4122条语言和管理员密码/metadata摘要保持。实际EF save使generation 1→2并返回新token，再在事务内2→3后rollback恢复2及Snapshot字节；精确fixture删除后重新核对原数据/计数。范围是该任务库的捕获字段及限定升级，不宣称全部业务payload或生产升级验收。

`tmp/wp2-migration-sql-final140-full-write-negative.json` 于 **07:27:10–07:27:26 UTC** 实际 **35/35**，SHA-256 `1CAF204D7C2D1CC8C617D6CFCDBB8A396D655DFB8FAC50AAB9D9C38C7E8013D9`。四模型逐表/列/类型/容量/精度/键/FK/索引/filter/check身份、安装token及真实EF/native/ExecuteUpdate、generation、65个raw默认/四全局唯一、语言/时间/金额/Space/Order/quotation/财务竞争和目录负对照均按报告范围通过。Core/Space/IdentityPriority/ERP模型表项分别233/83/4/8，token分别157/41/4/6；这是328个Context模型表项，不能等同352个实际表或327项旧oracle。Core140、Space47均pending0且repeat保留；两个消息Context仍由SQL Core canonical history持有，不伪造模板history applied。报价最后一个超长字符仅为空格时SQL会截断、PG拒绝的差异仍在共同等价承诺之外，check任意表达式和完整业务规则也不因目录通过而扩大范围。

`tmp/wp2-migration-sql-final140-unknown-history-rollback.json` 于 **07:28:25–07:28:28 UTC** 实际 **2/2**，SHA-256 `38C132E2176B9C90678B895E81A476C84F51A57377F352618FEC1F2AD0623497`：未知Core ID在Migrate前拒绝，rollback恢复140条精确history，无迁移或业务写入。随后 `tmp/wp2-migration-sql-final140-seed-capture-before-new-initializer.json` **2/2**只捕获352表计数及现有translation/admin基准，不单独算初始化成功。

实际新API `709710CFC2888819D99CCA9559142475A4BD072A0B17AFC152F1F9F738A9740A` 首次升级后初始化 **07:28:45–07:29:03 UTC** 与重复 **07:30:50–07:30:58 UTC** 均exit0、Completion=true、ListenerObserved=false；脱敏原件 `tmp/wp2-init-sql-final140-after-upgrade-first-initializer.json` / `tmp/wp2-init-sql-final140-repeat-initializer.json`。对应 `tmp/wp2-migration-sql-final140-seed-verify-first-initializer.json` / `-seed-verify-repeat-initializer.json` 各 **2/2**，分别于07:30:45–47、07:31:56–57实际执行：全部352表计数及histories、global/tenant自定义翻译、管理员昵称和密码摘要在首次及重复后保持。强制canonical label策略不在该断言内。旧136 fresh初始化、实际保留数据升级与新140首次/重复各保留自身输入，不称为另一轮新140空库安装。

最终probe `BFB8B9E8783EC16B44BB00539169A1009BDCBA0C86A279C3C1CEEFF4439314C0` / Core `F84A3ED6B48536FC81F1CCEE853C0FC5F53BC7CB951708A74961CA4803AF0DE1` 来自main94加未提交WP2输入。API与probe build各0warning/error，用时19.35秒及1:07.67，保留 `tmp/build-wp2-final-sql140-api.log` / `-migration-probe.log`。`tmp/wp2-final-sql140-probe-input.json` 捕获2950个源码哈希及20个output根DLL，实际完成 **07:25:36.7483507 UTC，在首次native执行期间**；原Scope误写pre-native的原件保留。`-input-verified.json` SHA-256 `301657FED2370B7A1066583F2AECFDEC4A3B4DD8935DC811CD81A7805706F10B` 更正元数据，并将8个已记录actual runtime hash逐个匹配recursive output。第一次仅查根目录SqlClient stub的hash准备比较失败；已记录的actual SqlClient hash与输出 `runtimes/win/lib/net6.0/` 的DLL字节匹配，原生执行本身成功，未因此重跑。原probe未捕获确切loaded path，不把该output路径称作已捕获的loaded path；2950个源码仅为执行输入观察，不扩大测试/review范围。`tmp/wp2-final-sql140-api-input.json` 是第一次initializer之后的build/output观察，API hash匹配已执行进程，也不称pre-run捕获。

[BUG143](https://github.com/GTX537/CP6/issues/143) 在PR144合并与remote main包含性核对、同API原步骤exit0/noHTTP及合并后native比较15/15后，于 **07:23:27 UTC Closed**。`tmp/bug-143-remote-main-closure-proof.json` 保留closure comment及独立任务来源；该BUG已归档的完整原件不在WP2重复包装。当前本地门禁LocalVerified；8个owned库清理及不存在核对已完成；**WP2提交/PR/远端main核对Pending**，未启动Actions、改变保护或部署，WP3–WP6及父任务继续。

### 八个 owned 库清理完成与原件入口

Root于 **2026-10-03 07:47:52–07:48:00 UTC** 执行[最终清理脚本](wp2-native/Complete-DbCompatWp2Cleanup.ps1)，脚本SHA-256 `8DA601BEF6C4C4982B61E01EA3580ED454E61ED490F7E8D2EC0007C1FF3980EA`。[实际脱敏清理报告](wp2-native/wp2-cleanup-final-owned-eight-verified-env.json) SHA-256 `16C6C78F558FADF56861E5EC13CBE53CBC1747B2DECE3E92610AFF7339851EB2` 记录Status=Completed：八库全部预检及每库删除前复查通过，DroppedAndAbsenceVerified=8、AllEightAbsentVerified=true、无未知删除结果。PG测试role保留，五份原所有权记录字节未改；报告只保留其hash，归档不复制凭据、原记录、seed-state或应用raw streams。

[第一次Failed](wp2-native/wp2-cleanup-final-owned-eight.json)及[第二次Failed](wp2-native/wp2-cleanup-final-owned-eight-verified.json)均在all-target preflight阶段、**0 drop、无未知结果**。第一次是sqlcmd `-h -1`与`-y0`互斥，定向改为`-y4000`；第二次是PowerShell方法绑定将null变成现存空字符串环境值，导致PGSERVICEFILE空路径，定向改用NullString.Value移除并正确恢复环境值。这两次是清理工具兼容准备失败，保留Failed原件，不归为数据库迁移失败；没有重跑已通过的native/unit门禁。

原脚本[sqlcmd选项版本](wp2-native/Complete-DbCompatWp2Cleanup.original-sqlcmd-output-options.ps1)、[空PGSERVICEFILE版本](wp2-native/Complete-DbCompatWp2Cleanup.original-empty-pgservice-env.ps1)与最终脚本分别保留。清理前[一次脚本定向审查](wp2-native/wp2-cleanup-final-script-review.json)、[sqlcmd修正](wp2-native/wp2-cleanup-sqlcmd-option-correction.json)、[PG环境修正](wp2-native/wp2-cleanup-pgservice-env-correction.json)及[只读PG预检诊断SQL](wp2-native/wp2-cleanup-pg-preflight-original.sql)给出原hash/修改范围和真实失败分类，不改称新仓库完整review。原件已实际归入本目录的 `wp2-native/`：原prepared清单140份，加最终源码输入字节证明及其脚本2份，共142份原件、2,829,993 bytes。当前本地门禁、八库清理及原件归档核对完成，WP2正常提交/PR/remote main包含性仍Pending。

[逐文件归档清单](wp2-native/manifest.json) SHA-256 `77034B607406EC8E26721BC4264DAF1F35AABFBF16C762CD90BE2121F60E08D5` 记录142个原始路径/hash/byte数；本次只读重新核对全部原件与归档副本，**142/142字节/hash匹配、0失败**。所有本地证据链接目标实际存在，不把原报告重写成新candidate执行。

[最终记录源码输入字节比对](wp2-native/wp2-final-review-source-applicability.json) SHA-256 `28A7910531BAE917049DE3A13DD1B22B225533A2AABCF668709AD1350E39EE35` 于07:53:35 UTC观察现有原review及定向修正输入的75个唯一源码文件：75/75字节一致，无未审查源码delta；[比对脚本](wp2-native/Verify-Wp2FinalReviewApplicability.ps1)保留方法。这是已记录输入的适用性证明，**不是新75文件AI审查、测试、build或DB执行**；原30/45 review与后加四文件/reader两行delta的时间/范围仍各自保留。文档和归档补记按局部检查核对，不扩大native或全部业务验收范围。

### 最新既有 PG 36/36、精确 history 与参数负对照

Root 于 **19:26:27–19:26:40 UTC** 在已安装的 owned PostgreSQL18.6执行 `tmp/wp2-migration-pg-final-exact-history-full-write-gates.json`：**36 Passed / 0 Failed**，SHA-256 `265D3F6E15963D871391F0C90463EE45665A4FFAC6EE7051349AE1D07F765C0C`。四个 canonical owner 的 Forward 均为 **previouslyApplied=1、applied=1、pending=0**，RequireSupportedPrefix 在 Migrate 前检查，RequireExact 在后检查；各 Repeat 保持 exact相同history。这次是既有安装上的完整限定复验，**不是另一轮新空库安装**，也不将稍后 main4d 或报价 gate 新源码称作本次执行输入。报告 SourceBase 为2d、SourceState为未提交 task input，actual probe binary 为 `1EB704BE0E253CD9D5E79B11545F0B3CA07A79DA29D95D45554B6C8809D547FD`。

配套 `tmp/wp2-migration-pg-final-exact-history-full-write-input.json` SHA-256 `673605C9B5CAA253AC6AE03B4F8DBABE5CD62F44CAC1FE4DE792249F13AA2B33` 记录实际 SDK10.0.302和1439个源码哈希。先前遗漏 SDK 文件、ActualSdkVersion=null 的 sidecar 保留为 `-input.invalid-missing-sdk-file.json`，SHA-256 `08838AA5D27C56E7736CF552864F29639A7D7407FB5BD93CD4B26B36526746B9`；这是输入元数据补正，不改本次 build/runtime/native结果或声称重跑。

`tmp/wp2-migration-pg-unknown-history-identity-rollback.json` 于 **19:24:24–19:24:27 UTC** 实际 **2/2**，SHA-256 `E605923F15FE3A33E1EE2AF38711F3069390BC206E69483515A60C21E1605D07`：actual Core在外部native事务中读取含未知ID的history，pre-Migrate guard拒绝；detach/rollback后精确恢复原完整history，没有运行migration或应用runtime。`tmp/wp2-migration-missing-seed-mode-value-rejected.json` 与 `tmp/wp2-migration-missing-seed-state-path-rejected.json` 各实际 exit1、DatabaseVersion=null、SetupOrRun Failed，SHA-256分别为 `B036E2A1A8EF555CD57A5CCF4E66496FE96548199FE8847F65B1B43FFE254CA3` 和 `13515F3F2C3B7892D7376F8D53B805A41069E556646A42C50B2F7EFC328DEACB`。它们是访问DB前的预期拒绝，保留原Failed，不计为业务Passed。

### 一次集中审查与定向修复范围

初始化/Provider/两工具分工审查 **30文件**，原scope manifest `F2A87FF23B1A9EDF9F08F2D84A382E18491C8A916C7DEEF7C440396418678A86`；映射/帮助器/迁移/模型测试分工 **45文件**，scope manifest `3B32A0976118E3A4CCA0C0B76B18C8061DD2B52BEDDFA44D9F1F7FDD757324BA`。两份 ignored 原始归档 `tmp/wp2-review-initialization-probes.json` / `tmp/wp2-review-mapping-migrations.json` SHA-256为 `734BA6F4051E42CE43A1FF9FDBF3E4D7589262AB29E85682F978B35CA24D4C23` / `0E59B8D5CF06B529A8D21B3399C6B17FCB7DD777445A3B4C074EF77EAEF6CA21`，各保留准确文件/hash和边界，不相加声称75个唯一文件。前者两个P2（未知/混合history未严格拒绝，缺seed-mode值可落入默认迁移）已修复并仅定向复查新Program/history helper；归档分别保存旧30文件scope与新修复hash，不把新Program称作旧F2输入。初次审查及定向复查的精确起止时间未记录，原字段为null；19:31:52.9654746Z是归档时间，不代替review起止。映射归档记录reviewCompletedAtUtc=19:26:59Z。

该分工未发现其他实质阻塞。后续报价/最终四项升级的四文件增量review及Dapper容量reader两行修正已按实际输入定向复核，记录在独立BUG143归档，原30/45范围不改称覆盖后加输入；BUG143源码与native已由独立任务闭环。文档/SHA更新不启动另一轮完整WP2审查，owner cleanup及远端交付边界仍保留。

### 当前配置/模型/初始化测试与实际 PG 应用种子

本轮两次真实单元来源分别是 root `tmp/db-compat-wp2-tests/final-provider-model-initialization/wp2-final-provider-model-initialization.trx` 的 **151 Passed / 0 Failed / 0 Skipped**，以及 `tmp/db-compat-wp2-tests/index-repair-integration/wp2-index-repair-integration.trx` 的 **25 Passed / 0 Failed / 0 Skipped**。151 项包含 Provider38、Wiring40、Model53、SearchPath7、Ownership8、Initialization5；25 项只属于已集成 BUG139 的索引回归，不将两次运行写成一次 176 项执行。TRX SHA-256 分别为 `A4297E89005C6B895615DBD0184CCD5A72E9ADDDAA67DAA29C4DAC5222EE5F05`、`A9BC8ACF1EF3DBA72B827EB1D199E397427D0EDBA498AA5C6E767455A6C5256D`；root `tmp/wp2-final-provider-model-initialization.log` / `tmp/wp2-index-repair-integration.log` 保留实际结果。更早 scoped 成功及失败保留在[模型记录](WP2-MODEL-TESTS.md)，本次不扩大旧输入的证明范围。

实际 compiled PG DatabaseInit 使用相同应用 binary `8CB786C21F42B5EDDF05989E1794635FA6372D79EDEFD037B7EA389F0B0F02AB`：首次 `tmp/wp2-init-pg-full-restored-model-first-seed.json` 于 **18:15:10–18:15:23 UTC**、重复 `tmp/wp2-init-pg-full-restored-model-repeat-seed.json` 于 **18:16:10–18:16:16 UTC** 均 exit 0、`ListenerObserved=false`。两报告 SHA-256 分别为 `16363331680504A211E56E282C27A77041E06065BC11D5A600F4FCDA006534A3`、`B0A12819FE06B73E41397AD03968A5A7CDDD98407EDF8A53F0B5A4E14FB3ACD7`。只执行完整 one-shot 初始化，不启动普通 API/worker。

配套 native `tmp/wp2-migration-pg-full-restored-model-seed-prepare.json` / `tmp/wp2-migration-pg-full-restored-model-seed-verify.json` 各 **2/2**，SHA-256 分别为 `1E12C4BF2FDA61A30BE1FA1FF36FF39395B585BAC7FB96EA6210557E0F03B272`、`B386DEC9F47403006A041B8D59196C3B018EB3521A65F9066A04D3DDCF80C728`。首次后捕获全部 **334 张实际表**，放入 translation/admin retention fixtures；重复后全部表计数及四 migration histories 不变，全局/租户翻译、管理员昵称和密码保留，仅比较密码摘要，不归档值。该断言明确不覆盖强制 canonical label 策略，也不证明所有权限或业务种子语义。Native verifier 的实际 probe binary 为 `7EEDD7E0DE257A0B4813EEC68679EE1DE034663545CD7ED7A43B996E9A5C8E43`；报告仍记录当时 d507 + 未提交 task input，不能改称新提交执行。

普通 PG 运行的实际负对照 `tmp/wp2-init-pg-ordinary-runtime-guard-preserved.json` 于 **18:37:11–18:37:14 UTC** 记录 `RuntimeGuardRejected=true`、`ListenerObserved=false`、非零退出，SHA-256 `E1C69C49E649641478C05B53BB7BA4062E2871C090EA1C90F8AE4622B76F7AA0`。其应用 binary `B13BEFECF30CBFEFCA2A6ED1653A657149FD15EB92CD53F75496B3C4F3571490` 与较早 seed binary 分别记录；成功初始化没有移除普通 PG runtime guard。

[模型记录](WP2-MODEL-TESTS.md) 保留可编译 RED、真实实现失败和各次 scoped GREEN。固定字符仅允许有 writer/domain 证据的 **83 个 ASCII 域（Core 6 / Space 77）**；未知固定字符、nchar 或容量不一致拒绝自动映射。[Core manifest](WP2-CORE-MAPPING.md#fixed-text-domain-provenance) 与 [Space manifest](WP2-SPACE-MAPPING.md) 给出来源。四 SQL 模型由固定 `d6074aaa` 的独立 JSON oracle 比较，最新定向 **5/5** 通过，另有改变原列长度的负对照。Provider/factory 的固定 `Search Path=public` 检查 **85/85** 通过，拒绝自定义路径；它不是开放普通 PG 运行入口的授权。

Root 于 **16:45:47–16:46:05 UTC** 在隔离的 PostgreSQL **18.6** 新库执行并保留 `tmp/wp2-migration-pg-fresh-catalog-write-audited.json` 和 `-input.json`：**31 Passed / 0 Failed / 0 Blocked**。这次输入是未提交 WP2 源码，加构建记录、源码与实际运行程序集哈希，不把基线 SHA 称作实现提交。报告 SHA-256 为 `AFC2EF8E860591F3C7ED72D2AB4EC643934E3DACF5A47BE5D15B879E702AB51F`；input SHA-256 为 `E26347FCADF5F8AD1EF3C8E7D70D0DBC8C83606B25D6D0D77133FFC3B681F325`；探针 binary SHA-256 为 `6CC948AC025F1A0D8125D116CFE8D07A39F5EE58E45CC9010D0BA08B92A74B16`。原始文件位于主工作区 Git 忽略目录，收尾归档由主任务处理。

| Context / 旧 31/31 报告实际 migration ID | 模型表 / 数据库 token | 当次已执行范围 |
| --- | --- | --- |
| Core `20261002161715_PostgreSqlCoreBaselineV1` | 233 / 157 | 正向应用一项、pending=0；重复 Migrate history 不增加；完整列与模型约束目录；实际 Context ORM/native/ExecuteUpdate 和陈旧 token 拒绝。 |
| Space `20261002161945_PostgreSqlSpaceBaselineV1` | 83 / 41 | 同上，使用实际 Space 模型与完整迁移。 |
| IdentityPriority `20261002151342_PostgreSqlIdentityPriorityBaselineV1` | 4 / 4 | 同上，固定 Platform 0.10.2 私有 setter 实体仍由实际包提供。 |
| ErpIntegration `20261002151345_PostgreSqlErpIntegrationBaselineV1` | 8 / 6 | 同上，普通 replay InputRowVersion 与数据库生成 token 分开。 |

四模型覆盖的表条目合计 **328**、token 安装合计 **208**。目录门禁核对类型、容量/精度/nullability、键/FK/index 的列顺序、filter，以及 check 的名称、validated 状态和引用列；**名称/引用列一致不证明任意 check 或 partial predicate 的完整行为等价**。模型外报告另确认 OIDC 两表与财务 trigger 存在。实际写入还覆盖 generation 的 insert/update/跨 tenant move/physical delete 与同事务回滚，Sys_Lang global NULL/case/padding 重复拒绝及不同 tenant override 共存，UTC/wall-clock/offset/DateOnly 和金额舍入/溢出，Min/Max sentinel。普通 UTC instant 的 Kind 与 DTO offset 保持严格；Min/Max ticks 是显式保留的 sentinel 例外，不推广为一般时间输入。PG 仅承诺 microsecond 精度，不承诺 SQL 的额外 100ns 精度。

财务专项使用两个真实 posting/mutation session：UPDATE 与 DELETE 均等待 parent posting 行锁，Status=2 提交后以 E-FIN-160 拒绝修改并保留原行；精确 fixture 删除且验证不存在。源码 guard 使用 OLD.EntryId 和 FOR SHARE；不能用仅检查 trigger 名称代替这一行为证据。

这次旧 31/31 输入尚未包含 raw 默认/四 global unique；不改写它的报告或扩大其证明范围。后续新增输入的实际结果见下段。独立 [BUG #137](https://github.com/GTX537/CP6/issues/137) 在 source `bab643` 的 SQL 历史 136 链首次/重复初始化与 351 表计数通过后，经 [PR #138](https://github.com/GTX537/CP6/pull/138) 正常合入上述 d507 main，祖先核对后 Issue Closed；这一远端交付只属于 BUG137，WP2 仍未交付。

### 历史 fresh PG 33/33 与 raw 默认有效 RED

Root 于 **17:34:57–17:35:12 UTC** 在 owned 新空 PG 库（尾号 `31d68591`）运行 `tmp/wp2-migration-pg-fresh-restored-order-full-write.json`，**33 Passed / 0 Failed**，SHA-256 `D66DF2EA7C5F8DB4C74FA8E40379CA5D313DC2EC784599299C90132D2F3BD7C8`。配套 `-input.json` SHA-256 `28E60FE420FEFC0C57227FE877ABD66E32EB3882D844BDFBEEA908CD603BACDE`，记录 1,122 source hashes、成功构建 `tmp/build-migration-probe-raw-order-pg-array-query.log` 和实际 probe binary `F7BE6966A91012BB068B06AAD73E47126D4969DDE46E843109B87A0820446103`。SourceBase 是 d507、SourceState 是未提交 task input，不把它称作 exact committed candidate。

四模型 **328 个表条目 / 208 个 token 安装** 的完整目录通过，包含实际 column collation、类型/容量/精度/nullability、ordered keys/FKs/indexes/filters 和 check 名称/validation/引用列；实际 application connection 的 `search_path=public` 也通过。四 Context 各一个 token fixture 覆盖 EF insert/update、native、ExecuteUpdate、陈旧 EF 拒绝和 rollback；generation mutation/rollback、global NULL language、选定时间/金额边界与确定性 finance posting race 通过。新增事务内 missing-index 和错误 nullable-column-filter 负对照均被拒绝，rollback 后完整目录恢复。**目录比较不是每个 check/业务 writer 的全语义证明。**

独立 [RawOrder gate](../../../tools/CP6.DatabaseCompatibility.MigrationProbe/Wp2RawOrderGates.cs) 从原 raw migration 和 SQL136 actual catalog 冻结 65 defaults / 四 uniques，不使用新 DDL helper 当 oracle。SQL reference `tmp/wp2-migration-sql-raw-order-defaults-reference.json` 实际 **2/2**（ownership + gate）。PG 首次 `tmp/wp2-migration-pg-raw-order-defaults-preinstall-red.json` 因 verifier 的 `IN @Tables` 返回 **42601**；保留为 query/setup 错误，**不是有效 contract RED**。修正 PG catalog query 为 `ANY(@Tables)` 后，旧已安装 PG 库（尾号 `36ef9cae`）的 `tmp/wp2-migration-pg-raw-order-defaults-contract-red.json` 实际报告 **65 missing defaults + 四 missing global unique**；该 RED 来自真实 catalog，不是 pending migration 推断。随后上述新空库 33/33 包含 raw GREEN：65 默认按 native column type 求值正确，八个 `clock_timestamp()::timestamp without time zone` 在同一事务两次采样间推进，四个 unique 启用/有效/无 filter、exact global ordered keys 且 `NULLS NOT DISTINCT`。不声明八表完整 Order 业务 graph 验收。

### 后续实际 SQL 升级与 PG 目录负对照

Root 使用 BUG137 实际 SQL136 + Space47 链并完成种子的隔离新 pair，于 **17:13:17–17:13:23 UTC** 执行 `tmp/wp2-migration-sql-populated136-to137-upgrade.json`，**2 Passed / 0 Failed**（ownership + upgrade）。只新增 `20261002151353_CrmIdentityTenantGenerationV1`：351 个既有表计数保留，history 增一；迁移输入的两个完整 Snapshot fixture 字段及 exact 八字节 token 不变，4122 个语言行和 admin 密码/metadata 摘要保留。Generation 初始一次 seed=1，实际 EF save 1→2 返回新 token；事务内第二次 save 到3后 rollback 恢复 generation2 与 exact Snapshot bytes；精确临时 snapshot/generation fixture 清理并复核原数据/计数。报告 SHA-256 为 `896306BA1DD7F0A623C0B71479F2523331D9F9DBF4E782468ABE506D00F65133`。这证明所捕获任务数据库的升级和关键字段，不能推广为所有业务 payload 或生产升级验收。

同一时段另执行 `tmp/wp2-migration-pg-catalog-collation-negative-reviewed.json`，实际为 **20 Passed / 0 Failed**，SHA-256 `369F22998FC0F18CE5749D021A80CCF43059D05DCEF5DA60ABCE21AA2403D880`。扩展目录门禁覆盖实际 collation、PK/unique nondeferrable 与 SQL ignore-duplicate 合同；事务内暂时移除 department index 的负对照被 exact assertion 拒绝，rollback 后完整目录恢复且没有修改业务行。该运行明确是只读 catalog Mode，**没有改变 migration history，也不声称当前 history 已适用未来重生成基线**。两报告的 SourceBase 字段仍是原任务起点 d607，须结合当前 d507 HEAD、未提交 task input 与 root 的实际构建/源码/运行程序集证据解释。

### SQL full-catalog 的真实失败与继承的索引遗漏

Root 在升级后的实际 SQL 库继续完整目录核对。`tmp/wp2-migration-sql-upgraded-catalog-diagnostic.json` 于 **17:17:46–17:17:52 UTC** 记录 **14 Passed / 3 Failed**：Core/IdentityPriority 的 InboxAggregateCheckpoint 和 ERP OutboxMessage unique filter 比较失败。原因是不可变 SQL queue migration 在原生 NOT NULL 列上额外生成 IS NOT NULL；gate 仅在 **model 和 catalog 都证明 NOT NULL** 时移除这个冗余子句再比较，不改模型、原 migration 或数据库 DDL，不泛化到 nullable 列，也不是 collation 失败。该原始报告 SHA-256 为 `A5B6F02F5B55F49C5A27ADD76CFF6EAE9956FA0019BCAAB97D92BC6C1384382B`。

对应修正后 `tmp/wp2-migration-sql-upgraded-catalog-filter-semantic-reference.json` 于 **17:24:51–17:24:56 UTC** 记录 **16 Passed / 1 Failed**，SHA-256 `B2098FE281CEED2A751E9AA1E2D42FAA9974731203C0EBE02458BA2C0721F9C8`。Space/Identity/ERP model constraint/storage 已通过；Core 当次失败为 `Missing index dbo.T_Order.IX_T_Order_OrderType_IsDeleted`。这是 BUG139 修复前的真实记录，前述 2/2 保留数据升级不能改称完整目录通过；当前剩余 Order FK 与最终升级见后续专项。

静态来源表明该索引在 [frozen d607 snapshot:1866](https://github.com/GTX537/CP6/blob/d6074aaad3098b61adaeda902b92bf24b3eb0c04/CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L1866) 已声明，当前 [模型:1514](../../../CP6.Core/EFDbContext/CP6Context.cs#L1514) 同样是非 unique、无 filter/include 的 `(OrderType,IsDeleted)`，不含 TenantId。最早存留 [AddBpAndFsc Designer:1634](../../../CP6.Core/Migrations/20260502225006_AddBpAndFscPA110.Designer.cs#L1634) 也已声明，但该 Up 不创建订单表。后续 [Restore 8–10行](../../../CP6.Core/Migrations/20260506103359_RestoreMissingOrderTablesPA070.cs#L8) 明确说明原 `20260502020929_AddOrderTablesPA070` 丢失，其 [51–56行](../../../CP6.Core/Migrations/20260506103359_RestoreMissingOrderTablesPA070.cs#L51) 只补 CustomerCd+IsDeleted、OrderDate+IsDeleted、McOrderNo，遗漏 OrderType+IsDeleted。整个可执行 Core migration 源没有该名称或相同 key 的 CREATE/DROP；[OrderStatus index:73](../../../CP6.Core/Migrations/20260603151821_Phase6OrderCancelAndIntegrationEvent.cs#L73) 和 [CrmOpportunity index:132](../../../CP6.Core/Migrations/20260912071606_CrmErpIntegration.cs#L132) 是不同列，不是重命名。Restore 与最早 Designer 当前 blob 都与 d607 相同，证明遗漏继承自历史链。PG baseline 则已有该 model index。

后续独立 frozen model / actual SQL catalog 对照确认 **21 个历史遗漏的 nonunique indexes**，不再只停留于首个 OrderType 例子。[BUG139](https://github.com/GTX537/CP6/issues/139) 在独立 `codex/bug-139-order-indexes` 实现 forward repair，source `083e9c4dcdb3d5b1aad3b57c32cb337f86859361`。原 model/旧 migration 不改，不能删除 index 或放宽定义断言使失败消失。该功能和原始证据 `94124c62` 已由 [PR140](https://github.com/GTX537/CP6/pull/140) 正常合入远端 main `2d9747360a38dfc519ddebea7aced06a9466b879`，合并后原步骤目录冒烟通过，BUG139 **Closed**；这不声明 WP2 SQL full-catalog 或最终四项升级已通过。

BUG139 实际 upgrade `tmp/bug-139-sql-compare-before-upgrade-to-after-upgrade.json` **25/25**：**352** business table 的 PK-ordered JSON SHA-256/count 全保留，1,357 个原完整 index metadata records 原样保留，只有 21 个 frozen expected nonunique indexes 新增，Core history **137→138** exact append。重复运行 `tmp/bug-139-sql-compare-after-upgrade-to-after-repeat.json` **25/25**：352 表及 1,378 原 index records 保留，history **138→138**、零新增 index。两报告 SHA-256 分别为 `B3B5F437786CBCCF0B2D9311C641BEFB3956D8D3AC34B4CD3AC0586840F21283`、`4E6C6BF5A85EAF0FBACE7E423C17B878EC486A6CA08DB16189FD9DD5B2AD64B0`。这是独立 BUG139 的 captured-db 修复证据，**不能将此前 WP2 generation 136→137 的 2/2 改称 136→138 验证**。

`tmp/bug-139-native-guards.json` 八项全部 Passed，SHA-256 `887FA4DE49E4F9A6C990219305A87F74DD09CEB6477DCE350FE3B53028210DA9`：正确已存在定义可重放，wrong column/unique/filter/include/direction/disabled 冲突拒绝，后续命令冲突使前面已修 index 一起回滚；每项均复核 table hashes/counts、history、完整 index metadata 恢复。correct-existing / wrong-column / unexpected-unique 三项在原报告明确标记复用 earlier checked execution，保留该出处。BUG139 已接入本任务当前主线；索引比较仅接受两个明确历史等价名字：Order `IX_T_Order_WebOrderNo→AK_T_Order_WebOrderNo`，Detail `UX_OrderDetail_OrderProduct→UX_T_OrderDetail_OrderProduct`。Key 比较另明确接受 Detail `AK_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd→UX_T_OrderDetail_OrderProduct` 和 Quotation `AK_T_Quotation_QtnNo→IX_T_Quotation_QtnNo`，均为全局无 filter 的精确有序列 UNIQUE。所有 ordered keys、unique/filter/storage/enabled/duplicate-rejection 定义继续照验，不创建冗余 unique，也不 ignore 对象；当时四条实际缺失Order FK仍需独立修复；后续BUG141及2026-10-03最终目录门禁已单独完成，来源见当前证据。

### 实际 Order graph、旧 SQL136 RED 与四项升级来源

同一实际生产 Context graph 专项分别运行于 **18:59:22–18:59:29 UTC** 的 `tmp/wp2-migration-pg-order-relations-reference.json` 和 **18:59:22–18:59:27 UTC** 的 `tmp/wp2-migration-sql-old136-order-relations-negative-control.json`。PG **2 Passed / 0 Failed**：实际 EF 存储合法六行 Order graph；四条 native FK 的各自孤儿输入按精确约束身份拒绝；native Detail/Order 删除执行四个 Cascade，整项事务 rollback 后恢复全部表计数。旧 SQL136 **1 Passed / 1 Failed**：owner 验证通过，关系写入 gate 在首条 `FK_T_OrderDetail_T_Order_WebOrderNo` 应拒绝孤儿的断言实际失败；未把随后未达到的其他孤儿/级联断言计为成功。两报告 SHA-256 分别为 `A45C0056A0078118088202A3536EFC17AB173FF8BDDC4CDB50849D8FC6609B51`、`D51DC795D99B1D2E5C95EF9D03E793FC36F45CF97010189C233A1D5FB93AF82C`，共同 probe binary `1AA9EF0B9BB1D449D359200C14A762A99734143DCC21A1663F3F5B1087A9B376`、Core `F4BFEE9FD7D1C1435A88B66495AE712C292F97F7A7D53BC20B53F65FF62C1C2D`。SourceBase 为当前 main2d，另有未提交 task input；这只证明四条 Order 关系/级联及该 graph，不是完整 Order 业务验收。

先前 PowerShell wrapper 变量名碰撞在数据库操作前 setup 失败，已修复后才执行上述有效 RED/GREEN；`tmp/wp2-wrapper-receipt-name-failure.json` 是不含密码的失败记录，不能当作数据库负对照。专用本地试验角色的凭据已轮换并验证，脱敏证明 `tmp/db-compat-test-credential-rotation.json` 保留范围；本记录不读取或归档 receipt。另一个 `tmp/wp2-migration-conflicting-modes-rejected.json` + `.log` 在 **18:58:32 UTC** 拒绝互斥 modes，原报告为 `Probe.SetupOrRun Failed`，SHA-256 `3CA2834A3D0B8BAA4D4CCDEEA083485B14E302DF6433B90A323B1546A5C9B042`；这是访问数据库前的预期拒绝，不改写为 Passed 或迁移成功。

四条 SQL FK 缺失由独立 [BUG141](https://github.com/GTX537/CP6/issues/141) 的 `20261002184500_RestoreMissingOrderModelForeignKeys` 恢复。相关 **37/37**、独立完整审查与真实SQL first/repeat初始化已验，Core138→139只加4FK、总202；352表内容/count与1378旧index、198旧FK保留，upgrade/repeat各9/9，native guard最终11/11且每项restore9/9。原NFR fixture51003 setup失败保留；修正场景为combined NFR/untrusted，不声称isolated NFR证明。独立WP2 actual SQL Order gate `tmp/wp2-migration-sql-restored-four-fks-order-write.json` **2/2**，SHA-256 `38A7D85A43AD1553DCD5D1AB7F5D0702B607222E60C896335F39E6EA14515339`，四孤儿/四cascade/全counts rollback通过。该BUG已经 [PR142](https://github.com/GTX537/CP6/pull/142) 合入并核对远端main4d，Issue **Closed**；[原件与限定范围](../2026-10-02-bug-141-order-foreign-keys/README.md)保留。

随后完整SQL目录发现六个报价Creator/Modifier历史容量漂移，由独立 [BUG143](https://github.com/GTX537/CP6/issues/143) 恢复。初版static17项GREEN之后，actual首次APP遇SQL102（quoted COLLATE），missing-column guard遇SQL207（静态DATALENGTH提前绑定）；失败原件保留，完整native rollback **15/15**当时只证明恢复，不能改称capacity门禁GREEN。修正与identity guard定向修复后的功能2b6548最终17/17、actual SQL139→140及重复各15/15、四ordinary cases/恢复和清理29/29均由独立任务证明；正常PR144、远端main94包含、合并后原步骤与native15/15核对后Issue Closed。另一次2026-10-03 WP2实际从已种子的SQL136追加generation、21索引、四FK、六容量共 **四forward，136→140**，2/2及完整目录/写入35/35见本页当前证据；旧分项结果保留自身来源，不拼接成这次最终升级。

### 两库实际金融 / Space 约束存储写入

Root 运行 constraint-writes-only：PG **18:06:02–18:06:06 UTC** 的 `tmp/wp2-migration-pg-financial-space-constraint-writes.json` 和 SQL **18:06:15–18:06:18 UTC** 的 `tmp/wp2-migration-sql-financial-space-constraint-writes.json` 均 **3 Passed / 0 Failed**（ownership + 金融 + Space）。SHA-256 分别为 `ACCD0CEEA2CBB824F0FB67E72A3FB48E0E5DBBB08735753A4557A1C6F2890204`、`FCBD11A0E1E6703D6BC7B65EE464BE2152891DDFAB83C9990227605E42330F37`；两份都保留 d507 / uncommitted SourceState，实际 probe binary `269F5506A6A624F5D25C1CD9BF61A09921343BEDB0E34756CA085981C22D6799`，不与旧 33-case binary 混称同次运行。

[ConstraintWrite gate](../../../tools/CP6.DatabaseCompatibility.MigrationProbe/Wp2ConstraintWriteGates.cs) 真实 `Fin_JournalLine.Debit decimal(18,2)` 将 **9.995→10.00**，最大值 **9999999999999999.99** 原样，**10000000000000000** 溢出拒绝；缺失 Fin parent FK 拒绝并保留原 link。实际 Space model/version 的复合 tenant FK 拒绝换到其他 tenant，即使 parent ID 存在；Name 的 **100 个 😀 / 200 UTF-16 units** 原样保留，**201 个 ASCII a / 202 supplementary units** 拒绝且未截断或覆盖先前合法值。各 fixture 完整 rollback 并验证不存在。这是实际存储/约束边界，不替代完整财务 posting/domain 或 Space 业务/API 验收。

## 历史 raw SQL 的基线覆盖

原检查清单统计 **28 文件 / 471 个 literal migrationBuilder.Sql 调用**；[ChangeDocNumberFormat13](../../../CP6.Core/Migrations/20260531141900_ChangeDocNumberFormat13.cs#L69) 的两个 `b.Sql` helper 静态 sites 另循环 29 列，完整 `.Sql(` 源码范围为 **29 文件 / 473 sites**。这些数不是持续对象数量。没有从 snapshot 缺省推断 raw 默认或额外约束已经消失。

Core 基线在 [21 行](../../../CP6.Persistence.PostgreSql/Migrations/Core/20261002161715_PostgreSqlCoreBaselineV1.cs#L21) 先创建 text 函数，表/check 创建后在 [17406 行](../../../CP6.Persistence.PostgreSql/Migrations/Core/20261002161715_PostgreSqlCoreBaselineV1.cs#L17406) 安装 token、generation、OIDC、finance、MES 和 budget 两键。Space / Identity / ERP 分别调用版本化 token 安装器；四条 Down 明确拒绝回退。下表区分当前基线对象覆盖与历史数据操作；“模型覆盖”只表示源/目录合同，不扩大为全语义通过。

| 历史迁移（28 个 literal-call 文件） | 当前最终处理 / 尚待验证 |
| --- | --- |
| [RestoreMissingOrderTablesPA070](../../../CP6.Core/Migrations/20260506103359_RestoreMissingOrderTablesPA070.cs#L18) | 八表原 raw 列全部保留；65 surviving 默认与四 global unique 已由独立 SQL reference / fresh PG gate 通过，见下文。完整 Order business graph 未验收。 |
| [AddMesStoredProcedures](../../../CP6.Core/Migrations/20260518115050_AddMesStoredProcedures.cs#L24) | [CoreObjects.MesSql](../../../CP6.Persistence.PostgreSql/PostgreSqlCoreObjectsV1.cs#L54) 安装三函数及两索引：ProductionResult(CreateDate,IsDeleted) INCLUDE(GoodQty,DefectQty,MachineCd)，WorkOrder(ActualEndDate,Status,IsDeleted)。业务日通过显式 business_day；结果/参数/聚合行为及 caller 适配仍待后续夹具。 |
| [WidenProductCategorySml](../../../CP6.Core/Migrations/20260531094653_WidenProductCategorySml.cs#L16) | final model 的三列容量六，不重放旧四→六 ALTER。 |
| [RemoveArticleAndDashboardRevamp](../../../CP6.Core/Migrations/20260531153048_RemoveArticleAndDashboardRevamp.cs#L18) | 最终 seed 排除 MenuId=1、nav.1、dashboard.totalArticles；原 RoleMenu→Menu delete ordering 保留在 SQL 升级。PG 最终 seed 计数待 root。 |
| [I18nP1_SysLangUniqueKey](../../../CP6.Core/Migrations/20260614065230_I18nP1_SysLangUniqueKey.cs#L14) | final NULLS NOT DISTINCT 与真实 NULL/tenant fixture 通过；旧仅按 LangKey 的 MIN(Id) 删除不用于当前 tenant override。 |
| [FinJournalLineNoMutateTrigger](../../../CP6.Core/Migrations/20260615153849_FinJournalLineNoMutateTrigger.cs#L17) | [JournalSql](../../../CP6.Persistence.PostgreSql/PostgreSqlCoreObjectsV1.cs#L37) 安装 OLD parent guard，真实 UPDATE/DELETE posting race 已过；其他业务财务门禁不由此替代。 |
| [MultiTenantOperLog](../../../CP6.Core/Migrations/20260616125141_MultiTenantOperLog.cs#L26) | final tenant alert index 由模型创建；EmptyGuid→A1 是历史归属修复，不重写新 tenant 行。 |
| [MultiTenantCompositeUniqueIndexes](../../../CP6.Core/Migrations/20260616132134_MultiTenantCompositeUniqueIndexes.cs#L13) | 保留最终 tenant key/filter/FK 例外，193 Up DROP 不是额外 PG 对象；旧 raw 别名仍须独立最终目录对照。 |
| [A2CostSheetTruth](../../../CP6.Core/Migrations/20260618114602_A2CostSheetTruth.cs#L98) | final Actual/Standard 两组字段由模型创建；旧 Labor/Overhead 值复制与总额无损属于非空升级/import fixture，不能在空库冒充执行。 |
| [A5BudgetI18nFix](../../../CP6.Core/Migrations/20260621064516_A5BudgetI18nFix.cs#L27) | [FreshSeedV1](../../../CP6.Persistence.PostgreSql/PostgreSqlFreshSeedV1.cs#L7) 在迁移中插入两个 reviewed 全局五语键；十一项日文已在 [应用 seed](../../../CP6.WebApi/Seed/I18nA5BudgetScreenSeed.cs#L16)。实际重复初始化、翻译及 tenant override 保留已通过；强制 canonical label 策略不在该断言范围。 |
| [SpaceP5ConnectorCost](../../../CP6.Core/Migrations/20260629162744_SpaceP5ConnectorCost.cs#L27) | 三 type 的 20/6、0/15、0/10 是旧数据 backfill；fresh rows 按实际初始化合同赋值，不向新配置重放全表 UPDATE。实际首次/重复初始化及所有表数量稳定已验，三 type 的具体成本来源/数值不由总计数断言证明。 |
| [SysRoleTenantize](../../../CP6.Core/Migrations/20260708093013_SysRoleTenantize.cs#L41) | final tenant/role keys 由模型创建；seed 必须直接创建正确 tenant 角色归属。历史复制、NOT EXISTS 与已知子表守卫留在原 SQL 升级，禁止扩散权限。 |
| [SysRoleMenuTenantize](../../../CP6.Core/Migrations/20260708100345_SysRoleMenuTenantize.cs#L33) | 同 tenant 存在角色后才有映射；历史复制守卫不作为 fresh 空操作丢弃其业务规则。实际首次/重复初始化及表计数稳定已验，不据此证明全部权限或租户业务行为。 |
| [SysRoleMenuUniqueIndex](../../../CP6.Core/Migrations/20260710172302_SysRoleMenuUniqueIndex.cs#L19) | final unique(TenantId,RoleId,MenuId) 由模型创建；历史去重不跨 tenant。 |
| [SpaceRetryCompletionAndDeadLetterOutbox](../../../CP6.Core/Migrations/20260725181400_SpaceRetryCompletionAndDeadLetterOutbox.cs#L47) | final fields/lease indexes 由模型创建；仅旧 SPACE+DEAD 未通知行的预确认不应用于新死信。 |
| [SpaceIntegrationEventOccurredAtUtc](../../../CP6.Core/Migrations/20260725203000_SpaceIntegrationEventOccurredAtUtc.cs#L24) | final UTC occurrence 字段/索引保留；只对已识别 JobId 形状的历史值回填，不将 CreateDate 整列标 UTC。 |
| [CrmOidcGrantStore](../../../CP6.Core/Migrations/20260908010000_CrmOidcGrantStore.cs#L13) | [OidcSql](../../../CP6.Persistence.PostgreSql/PostgreSqlCoreObjectsV1.cs#L7) 安装 16 列 Grant、4 列 Logout、PK、CodeHash global unique 与 expiry index；BIN2/C、容量、UTC expiries 保留。当前 native 报告确认两表存在，consume/cleanup caller 仍待适配。 |
| [CrmOidcBrowserSessionFamily](../../../CP6.Core/Migrations/20260908011000_CrmOidcBrowserSessionFamily.cs#L12) | BrowserSession / RefreshToken column/index 已模型覆盖；OidcSql 单独补 AuthenticationEpoch 零 Guid DEFAULT。UTC logout 与 local revocation 区分，family 业务适配未由目录验收替代。 |
| [SpaceE01S06FileSafetyRetention](../../../CP6.Space.Infrastructure/Migrations/20260730152005_SpaceE01S06FileSafetyRetention.cs#L32) | final retention/deletion fields、checks、partials 由模型创建；仅旧 deleted/state5 无 request 行的时间 backfill，不造 fresh 删除证据。 |
| [SpaceE05S04AssetLibrary](../../../CP6.Space.Infrastructure/Migrations/20260731010047_SpaceE05S04AssetLibrary.cs#L14) | final Scope/Owner/version FK/check 由模型创建；旧 ModelAssetId 非空必须审计处理，空库自然无行不是非空升级通过。 |
| [SpaceE13S09ProposalDecisions](../../../CP6.Space.Infrastructure/Migrations/20260806054950_SpaceE13S09ProposalDecisions.cs#L39) | final resolution shape 由模型创建；仅旧 resolved 且有 command batch 行补 kind。 |
| [SpaceE13S10AtomicApply](../../../CP6.Space.Infrastructure/Migrations/20260806110504_SpaceE13S10AtomicApply.cs#L97) | final Name/atomic evidence 由模型创建；旧 NULL 名从 code 补值不覆盖用户名称。 |
| [SpaceE13S17AiRetention](../../../CP6.Space.Infrastructure/Migrations/20260806160931_SpaceE13S17AiRetention.cs#L72) | raw 仅 Down THROW；final evidence 表和 forward-only 策略均保留，无需虚构缺失 Up SQL。 |
| [SpaceE06S01ValidationEngine](../../../CP6.Space.Infrastructure/Migrations/20260807105256_SpaceE06S01ValidationEngine.cs#L133) | 同上，保留 validation evidence 生命周期。 |
| [SpaceE06S03PublishOrchestration](../../../CP6.Space.Infrastructure/Migrations/20260807135544_SpaceE06S03PublishOrchestration.cs#L304) | 同上，保留 publish/reconciliation evidence。 |
| [SpaceE06S04PublishRecovery](../../../CP6.Space.Infrastructure/Migrations/20260807144532_SpaceE06S04PublishRecovery.cs#L14) | final retry/request/queue evidence 由模型创建；升级先解决 active slots，仅 MinValue queue 从 StartedAt 回填，Down forward-only。 |
| [SpaceE06S05HistoricalRepublish](../../../CP6.Space.Infrastructure/Migrations/20260807170204_SpaceE06S05HistoricalRepublish.cs#L139) | raw 仅 Down THROW；保留历史重发证据和 forward-only。 |
| [SpaceV1UnifiedDraftCreation](../../../CP6.Space.Infrastructure/Migrations/20260827053057_SpaceV1UnifiedDraftCreation.cs#L42) | final source/template shape 由模型创建；BasedOnVersionId 旧 clone 补 source1，不改 fresh blank 默认分类。 |

另一个 alias helper 文件 ChangeDocNumberFormat13 的 final 29 列容量二十及七个 Cascade FK 由完整基线模型保留；原 SQL 拆 FK→widen→恢复的顺序留在历史链。不把两个静态 sites 算成两个列。MES PG daily function 目前明确限制 days=1..366，原 [Controller](../../../CP6.WebApi/Controllers/Mes/MesDashboardController.cs#L87) 和 [Dapper](../../../CP6.Core/Services/Mes/MesDashboardDapperService.cs#L32) 没有上层 Range/clamp；原 SP 的 <=0 和递归极限边界不同，root 将该业务 adapter 合同留在 WP5，当前不宣称全参数等价。

## raw 默认与附带约束：SQL reference 与 fresh PG GREEN

只读比较八个 raw CREATE TABLE 与 PG baseline，原列数量依次为 **27/89/37/13/18/22/22/83**，PG final 为 **41/134/58/14/21/23/23/93**，原列 missing=0。新增列来自后续/current 实体，列存在本身不证明 default/unique。原 28 raw 文件中出现 SQL DEFAULT 的只有 RestoreMissingOrderTablesPA070 与 BrowserSessionFamily；前者 **62 个源码 DEFAULT 行展开为 65 个表/列规则**，后者的 AuthenticationEpoch 已单独补入 PG。旧 31/31 输入未安装这些 65 项，后来的有效 catalog RED 证明其缺失；现在 managed DDL 与独立 gate 已经 SQL reference **2/2** / fresh PG **33/33 中该项** 通过，不因 snapshot 缺省推断它们消失。

以下来源全部为 [RestoreMissingOrderTablesPA070](../../../CP6.Core/Migrations/20260506103359_RestoreMissingOrderTablesPA070.cs)，括号为精确源码行；0 用于其原 numeric/bool 类型，日期必须保留业务 local wall-clock 合同。

| 表 | 原 raw 默认（65 项） |
| --- | --- |
| T_Order（4） | Status=0(37)，McTransferFlg=0(38)，CreateDate=GETDATE()(43)，IsDeleted=0(46)。 |
| T_OrderDetail（6） | Status=0(140)，WfApprovalFlg=0(141)，McTransferFlg=0(142)，ProvisionalPriceFlg=0(143)，CreateDate=GETDATE()(147)，IsDeleted=0(150)。 |
| T_OrderProcess（7） | MachineFixedFlg=0(180)，LossRate=0(189)，MachineCount=0(190)，LeadTimeDays=0(191)，SortOrder=0(193)，CreateDate=GETDATE()(200)，IsDeleted=0(203)。 |
| T_OrderProcessNote（2） | CreateDate=GETDATE()(223)，IsDeleted=0(226)。 |
| T_OrderMaterial（6） | MaterialTypeDiv=N'3'(244)，SupplyDiv=N'1'(247)，SupplyUnitPrice=0(248)，SortOrder=0(249)，CreateDate=GETDATE()(251)，IsDeleted=0(254)。 |
| T_SheetUnitPrice（3） | UnitPrice=0(282)，CreateDate=GETDATE()(285)，IsDeleted=0(288)；foreach 两表来自263行。 |
| T_SheetUnitPriceEstimate（3） | 与上一行三项相同，分别为第二张实体表，不能把循环当作一个对象。 |
| T_PlateMold（34） | WdRev=1(313)，DuplicatePlateFlg=0(333)，SheetWidth=0(334)，SheetFlow=0(335)，BladeWidth=0(336)，BladeFlow=0(337)，CompositionQty=0(338)，MfgQty=1(339)，ColorQty=0(340)，BookQty=0(341)，EstimateAmount=0(348)，DecisionAmount=0(349)，PurchaseAmount=0(350)，WdQty=0(358)，LimitWdQty=0(359)，AtachInfoSheetFront=0(367)，AtachInfoSheetBack=0(368)，AtachInfoActual=0(369)，AtachInfoBaseplate=0(370)，AtachInfoPositive=0(371)，AtachInfoNegative=0(372)，AtachInfoMo=0(373)，AtachInfoFd=0(374)，NeedDraft=0(375)，NeedMylar=0(376)，NeedGalley=0(377)，NeedProof=0(378)，NeedBlueprint=0(379)，NeedComp=0(380)，NeedDesignSheet=0(381)，Status=0(386)，McTransferFlg=0(387)，CreateDate=GETDATE()(389)，IsDeleted=0(392)。 |

四个 source 仅出现 CREATE、后续未见同名 DROP 的 global unique 是：`UX_T_OrderProcess_Pk`(208: WebOrderNo,WebOrderDetailNo,ProductCd,OperationCd)，`UX_T_OrderProcessNote_Pk`(231: 同四列)，`UX_T_OrderMaterial_Pk`(259: WebOrderNo,WebOrderDetailNo,ProductCd,ProcessCd,MaterialCd)，`UX_T_PlateMold_NoRev`(397: WdPtnNo,WdRev)。SQL final catalog 确认原有 key 顺序、disabled=0、unique=1；新空 PG 已安装并通过 exact enabled/valid/unfiltered/global ordered keys 与 **NULLS NOT DISTINCT** gate，不含 TenantId 前缀。旧 PG 缺四项的 real RED 保留，不能只凭同名存在推断语义相同。Order 的 raw global AK(49)、Detail raw三列 unique(155) 仅接受已审计的历史等价 alias，全定义继续核对，不新建冗余 unique；两 Sheet raw index 在 [Composite 316/323行](../../../CP6.Core/Migrations/20260616132134_MultiTenantCompositeUniqueIndexes.cs#L316) 明确 DROP 后以 TenantId 前缀重建，保留该最终形态。

OIDC Grant/Logout 与 BrowserSession raw 列分别为16/4/9，静态对照PG helper/base各自列数相同，missing=0；八个restored raw PK及Order/Detail global principal AK保留。OIDC容量与BIN2/NULL/UTC由managed helper登记，caller consume/cleanup仍属后续业务适配。Raw defaults/four uniques、实际PG application首次/重复、151/25两次相关单元与既有PG36按原输入复用；BUG137/139/141/143均Closed。另一次最终SQL136→140、全目录/定向写入、新API两次初始化与352表种子保留已真实通过，详见本页当前证据，**8个owned库清理完成，WP2远端交付Pending**。旧captured-data proof不改称这次最终升级或全业务/生产验收。普通PG API/worker guard保留，WP3–WP6及整体兼容未完成；文档整理仅核对指定脱敏原件，不运行.NET/DB/Archive/Actions或部署。
