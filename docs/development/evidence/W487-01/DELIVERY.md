# W487-01 · C#集合所有权与不可变权限输入

按用户接受的[12h连续执行](../../planning/20261111-codex-12h-business-chain.md)及8h滚动顺序实施，COORD单对话串行，无子代理。官方status/start完成；Main DEV-1 / CRM DEV-2复用，集成席位空闲。两仓分支`codex/w487-01`，最新基线Main `d90c324b3a26bda3963297d32f3fa2cdcbd123b4`、CRM `1d952a579d06404bac17315d99113d2734f2f0f9`。

[W486](../W486-01/DELIVERY.md)已正常采用Main #954 / CRM #176，Issue #175关闭；两仓准确任务分支删除，固定目录干净detached、官方席位均已释放，两个主目录安全快进；Main原14,932未跟踪路径完整保留。

## 范围与出口

核AC-INQ-054实际C#集合合同和权威使用边界：构造/with后的别名修改、null条目及严格wire，真实缺口才修生成入口；保留完整合法域和当前权限，不逐层重复防御。

原主文§12要求授权集合使用前防御复制，AC-INQ-054原义务是C#集合防御复制并拒绝null条目。核生成的命名DTO/页面集合和当前签名权威输入，先实际别名修改/构造/with与null条目重现；不从IReadOnlyList名称或原设计PASS推运行通过。只有实际缺口才在生成脚本显式适配，原输入快照保留，不手改生成文件；wire字段、构造参数/合法Guid/精确数值、schema和授权语义不缩小。入口保证建立后，下游按Trust Internal Contracts采用，不重复包一层资格或校验。

相关行为与必要生成/编译验证，加一次任务级集中SPEC/QUALITY审查；正常分仓采用、远端核对、准确分支/工作区及官方席位收尾。未齐完整原义务则保持NOT_RUN，不将纯结构验证、源码检查或单项成功冒整卡。

唯一报告本文件，原始输出在`D:/CP6-local-test/CP6-12H-20261111/W487-01`。两个开发席位由本任务独占，尚未释放；无Actions或部署，12h目标继续ACTIVE。11h后按既定安排聚焦当前任务收尾，不新开大范围工作。

## 实际缺口与修复

[CRM Issue #177](https://github.com/GTX537/CP6.CRM/issues/177)，P2。原主文§12/原AC-INQ-054及原生成源码对照后，真实`collection-red-01/results.trx`为73失败/0跳过：12个命名record、20个IReadOnlyList属性在构造或init后保留调用方List别名；清空原List即改变DTO，null集合/引用null条目也可进入。实际`PermissionProjectionScopes with`同样重现。当前签名证据入口原本单独复制授权集合，因此不将该缺口描述为已经证实的授权绕过。

只在`scripts/import-s01-contracts.py`增加显式适配，随后正常生成，未手改原快照或生成文件：各列表在构造和init时复制到只读快照，原record构造参数和deconstruct保留，属性仍使用原JSON字段名；null集合/条目立即JsonException。快照不新增Guid.Empty、数值、日期或数量限制；旧public helper的既有语义保留，新增内部快照仅拥有列表不变量。原schema/TS/OpenAPI/精确时间和数值表示保持。

`InquiryProjectionSignatureV1`直接采用已拥有列表快照的PermissionProjection，去掉重复的五份授权列表拷贝；原严格wire、真实Ed25519签名、issuer/key/catalog/purpose/time和当前信任版本校验仍在其权威边界。record复制可共享既有只读列表，新的with赋值仍在入口取得快照；不加下游证明层或重复资格检查。

## 实际验证与复用

| 来源（本任务private） | 实际结果和范围 |
|---|---|
| `collection-green-02/results.trx` | 258 PASS/0跳过：新73项列表构造/实际init/with/null行为，以及现有InquiryContractAdoptionTests和InquiryProjectionTrustTests。覆盖严格wire、原49 UI状态、精确数值/时间/Unicode、真实签名/轮换/撤销/用途及只读权威证据。 |
| `ui-ownership-03/results.trx` | 3 PASS/0跳过：用原正向UI实例构造的PublicFormValidating、ConfigurationValidating和PublicFormAccesslost，原调用方别名变更不改状态，不能修改返回集合，null集合/条目拒绝。只新增测试，前项成功不重复执行。 |
| `importer-tests-01.log` | 6 PASS：独立临时输出可重现、确定性、只读check、缺失/篡改输入在写前失败。`import-s01-contracts.py --check`另实际通过24固定输入/38定义/55修复/257解析引用/17输出。 |
| `native-owner-next-01/results.trx` | 3 PASS/0跳过：两种合法政策组合实际配置/激活/HTTP/PG/原201及重放；实际Next生产模式匿名提交/原回执、人工放行一Lead、后台202生成及下载、一次消费和精确四字段。共48秒。 |

实际.NET SDK8.0.424/Node22.22.0/Next16.3.3/Chromium/PostgreSQL18；明确模拟业务/IAM输入，应用/HTTPS/数据库和Worker真实执行。已实际查看`native-owner-next-01/actual-next-chain.png`。命名列表构造测试验证的是其输入所有权，不将反射构造的局部值当完整合法业务命令；原严格wire/业务链由相应测试另行覆盖。73行是有限列表不变量矩阵，不是新增73项业务验收。

Web源码/依赖/配置未变，精确复用W486 `next-build-01`成功构建，原缓存核边界恢复固定路径后运行新.NET宿主；未因SHA改变重建Next。原W482–W486的期限、保留和其他有效证据仅按其原范围复用，未冒本次重新执行。完整Main项目及未受影响的其他仓/数据库场景未运行。

## 一次集中审查与剩余范围

执行者对本任务完整产品/测试/生成diff作一次集中SPEC/QUALITY审查：显式适配唯一来源和确定性、12类20属性覆盖、构造/init/deconstruct/原JSON名、合法Guid/时间数值域、深层授权Scopes均只读、严格验证先于可信证据、原键/租户/事务不变。受影响必需检查成功，无阻止正常合并的问题。文档仅查diff/必要引用和台账结构，不另开审查代理或产品全测。

本窗口完整解决已登记的20个命名DTO列表所有权问题，另核三种原UI字段错误列表和当前授权输入。事件`JsonExtensionData`初始化可变字典、非DTO元数据数组等其他集合的完整生命周期尚未按原R-054闭合，未断言它们构成业务漏洞，也不把列表修正冒完整AC-INQ-054通过。原询盘仍37 PASS/23 NOT_RUN；修订2 PASS/245 NOT_RUN、正式PG16 PASS/11 NOT_RUN及原375的38 DONE/1 IN_PROGRESS/336 PLANNED保持。S01-08原DONE范围不重开，不把本窗口冒整卡新增完成。

自有PG55570已按settings/Owner/目录和无外来连接核对后smart stop，数据/日志及RED保留；HTTP/Next/browser子进程已退出。Next生成缓存可逆移回private `retained-generated/web-next`，node_modules保留，未新增目录配额或强删内容。一次集中审查已通过，正常采用按下列实际事实核销。

## 远端采用与生命周期

CRM [PR #178](https://github.com/GTX537/CP6.CRM/pull/178)已将源`dab48e5025c656c83fcb7fa3b982686ca243f289`正常合入远端main`526817f7a9fa05ea4391b683f35466f360348927`，fetch核祖先及完整树`dfec4657f2c2c80ef271e6f371d334fae9375341`一致。[Issue #177](https://github.com/GTX537/CP6.CRM/issues/177)在采用及官方生命周期完成后关闭。准确本地/远端任务分支删除，DEV-2干净detached供复用、席位已释放；private `crm-finish.json`保留官方证明。

工作流树与受保护main一致、相关依赖未变：CRM `0c2d74f9fdc63f5178f97baf33950fb3d4dad751`、Main `3355235636fd45116f5445fca32fc65e49f13003`；复用既有手动入口/生产tag分析，本任务非tag推送不命中生产触发。CRM任务Actions列表为空，无取消项、触发配置变更、远程运行或部署。

Main [PR #955](https://github.com/GTX537/CP6/pull/955)已将源`58a3926c96472434cb66f9935f202618828b6954`正常合入远端main`a4324b0df2c7cdaef221b77b16e40fd01deb49d4`，fetch核祖先及完整树`812289545afe2427c1db25bccfc8b2820851b7c7`一致。官方finish成功、准确本地/远端任务分支删除，DEV-1干净detached供复用、席位已释放；两仓证明为private `main-finish.json` / `crm-finish.json`。两主目录安全快进，Main原14,932未跟踪路径完整保留；Main任务Actions列表为空，12h继续ACTIVE。
