# WP02-02 后续正式版本登记与执行提案

本文件是PREPARATION_ONLY可审查提案，不是发布批准、版本预留或生产补丁。读取Platform `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0` 的真实API/schema/workflow/scripts；文件hash见inventory-final.json。建议继续正式包路线，源码采纳例外未获批准，不能用生产ProjectReference规避PLT-RELEASE Required-REL的feed消费要求。

## 版本与来源

提议下一个正式精确版本`0.10.3`。本轮只读查询七个package的完整分页版本元数据均未列出该版本，见[观测](feed-version-observation.json)的逐请求时点；这不是预留、不证明未来不存在，也不替代正式preflight。在写入前重新确认所有槽位与相同授权主体，404/403/网络失败保持UNKNOWN，禁止视为未占用。

最终producer source尚未产生：必须是下述登记实现经独立审查、相关门禁、串行合入后核对的完整main SHA。不得使用旧本地`7c59256...`包、起始`e5c63...`SHA或混合后续WP02-08/25 DLL拼装候选。Main采用版本、producer源码、七包hash、publication/当前policy、consumer matrix与lock都需该最终身份。当前字段保持PENDING，不能填占位hash伪装已批准。

## 请求的未来Platform精确窗口

本Main准备任务没有以下文件写权限；由COORD另开单一owner、Platform:package-trust串行窗口，基于届时main重新核变动。

| 文件/入口 | 当前事实 | 提议修改及必须保留的约束 |
|---|---|---|
| src/CP6.Platform.Release/Cp6FormalPackagePublicationValidator.cs | 只读允许0.10.0/.1/.2 | 加入准确.3；仍绑定root与七个entry的version/source/feed/hash，保原版本全部合法语义与拒绝项，不接受任意patch |
| contracts/release/v1/formal-package-publication.v1.schema.json | 对旧三版逐项约束 | 增加.3对应的root、七个package/version/feed条件分支；API/source-schema/实际packed-schema三者相同；旧原publication字节不重写 |
| src/CP6.Platform.Release/CapabilitiesV1/Cp6CapabilityValidator.cs | 所有formal版本只准旧三版且capabilities空 | 精确.3分支允许已证实的ctx-binding-v1/1.0.0，ctx-v1及三scope；旧.0/.1/.2仍能力空；保独立批准matrix/publication/policy、实际archive验证、consumer逐行精确匹配和adoption Unknown |
| contracts/release/capabilities/v1/capability-matrix.v1.schema.json | FormalPublished分支capabilities/compatibility maxItems=0 | 分成旧版本空能力和准确新版本分支；root/package/feed同版语义不变；未实测行仍空/Unknown；TaskLocal永不变成正式受信任 |
| contracts/release/capabilities/v1/consumer-lock.v1.schema.json | 锁定身份结构包含package version/feed | 先核是否含旧版枚举约束；仅在.3实际无法表达时增加对应语义，保lock非永久信任/授权属性，不无理由改结构版本 |
| .github/workflows/p10-formal-packages.yml | input说明与Windows/Linux两处exact .2门 | 三处准确切换.3写入身份；保workflow_dispatch、expected_commit=事件main=checkout、p10-formal-release保护、Windows45分钟+Linux20分钟、固定actions、最小权限、两OS独立验签和公共证据原字节 |
| eng/p10/Test-P10FormalPrerequisites.ps1 | writer ^0\.10\.2\z | 精确.3；保当前main、PUBLIC、Environment reviewers/main-only、跨仓policy/cert pin、完整七包absence、RFC3161和真实signer预检，不把fixture runner送入正式路径 |
| eng/p10/Pack-P10FormalPackages.ps1 | writer精确.2，七包显式传版本 | 精确.3，同一最终source/一次build，不使用本地预包，不改producer依赖基线来绕过检查 |
| eng/p10/New-P10FormalPackageSet.ps1 | writer精确.2 | 精确.3，保一次构建、真实author签名、RFC3161、source/run/attempt与逐包hash，保护私钥生命周期 |
| eng/p10/Publish-P10FormalPackageSet.ps1 | writer精确.2 | 精确.3，保预检后immutable version-consumed marker、七包不可覆写、BytePreserving readback、Current验证、失败/未知结果查回；不得简单重跑或复用旧slot |
| eng/p10/New-P10FormalPublicationRecord.ps1 | reader ^0\.10\.[012]\z | 读.0/.1/.2/.3；保原Windows/Linux/readback逐entry同源同版hash与真实run/toolchain。不得用本地成功编造workflow元数据 |
| eng/p10/Test-P10FormalPackageSet.ps1 | reader ^0\.10\.[012]\z | 读准确四版；保exact七归档集合、build provenance、Current签名/撤销/timestamp与source校验 |
| tests/CP6.Platform.ReleaseTests/FormalPackagePublicationTests.cs、ReleaseSchemaParityTests.cs、ReleaseSchemaAssetTests.cs | 旧版及坏输入矩阵 | 增加新正例/混版/错feed/source/hash/未知.4等反例，实际pack schema门；历史fixture不改expected来吞退化 |
| tests/CP6.Platform.ReleaseTests/CapabilitiesV1/CapabilityValidationTests.cs、CapabilityTrustTests.cs | 旧formal能力为空 | 增新formal准确ctx能力；旧包伪称新能力、local声称formal、missing publication/policy、revoked/历史/缺schema/未测兼容仍拒绝 |
| tests/CP6.Platform.ReleaseTests/P10FormalWorkflowContractTests.cs | 断言两个exact.2门及脚本一构建/不可变规则 | 改为exact.3 writer，保其余环境/权限/cleanup/readback/windows-linux顺序；对应脚本测试仅准确扩读范围 |
| docs/P10-RELEASE-GOVERNANCE.md | 原历史发布事实 | 在开头追加准备身份/未发布事实，保历史原记录；正式run完成后才能记录真实发布 |

`tools/CP6.Platform.ReleaseTool/FormalPackageVerifier.cs`实际接受canonical stable SemVer（拒绝prerelease/metadata），不是仅旧三版枚举，因此不应机械修改。它负责密码学/归档身份，版本登记仍由上层formal API/脚本完成。`Program.cs`、ContractIds/MediaTypes、trust policy/DER、旧publication history、0.10.0 recovery workflow、signer revalidation workflow不在预计修改集，字节保留。

本轮不提出根Directory.Build.props或七个csproj默认版本变更：正式pack显式传PackageVersion。若版本审计文件或测试证明必须修改，先另列确切文件和理由扩大窗口。并行WP02-08/25可先按其窗口交付，随后最终freeze再建立完整source→seven-pack关联。

新正式版本可仅声明WP02-02所需ctx能力。native S01-09 schema目前未打包；不得为了宣称完成扩展native资格化。ctx能力声明本身也不代表真实Main IContextSourceVerifier/IContextBindingAuthority、当前权限或EF/PG路由采用。

## 准备阶段门禁（未来登记实现后本地运行）

1. API/source-schema/packed-schema的旧三版与新.3正反矩阵；准确七包set/source/hash/feed，拒绝mixed/.4/latest/build metadata；原.0恢复合同保真。
2. formal writer exact版、保护环境/最小权限、one-build、marker/并发/已占用/未知发布结果查回、BytePreserving readback及Windows/Linux来源检查；测试不执行真实发布。
3. Current/Historical/Revoked/unknown signer、坏/缺RFC3161、撤销不可用、篡改archive、错误publication批准hash；原政策与证书hash保持。
4. Capabilities及相关Release/Schema测试、一次完整Architecture门；实际唯一prerelease仅rehearsal。新producer依赖必须解决或保留当前probe的DiagnosticSource8/10冲突，不能以本轮局部10case通过代替真实Main closure恢复/编译。
5. 一次完整任务diff审查，推送/PR前同时查候选和protected-main全部workflow事件/过滤/依赖。不得触及RFC3161窄路径文件触发远程运行；如确有修改需求，先处理具体授权。

## 正式执行清单与授权项（本轮全部NOT_RUN）

| 顺序 | 放行条件/动作 | 身份与失败处理 |
|---|---|---|
| 1 | COORD授予上述生产者登记窗口并集成通过；固定最终main S | 记录完整S/tree/SDK/依赖/审查，不把准备输入当最终源码 |
| 2 | 独立核新.3七slot当前不存在、保护Environment/current signer及跨仓policy/cert一致 | 不读取本机私钥；网络/权限不足UNKNOWN；旧批准不可复用 |
| 3 | 向用户提交一次具体正式run授权请求 | 明确repo、最终S、.3、workflow、Windows/Linux预算、签名/七包feed写入/readback、费用范围；获得新的明确授权后才dispatch |
| 4 | 正常p10-formal-release批准后执行一次Windows build/sign/timestamp/publish/readback | 只保护环境身份接触签名私钥，one-build；写marker后失败需只读核未知结果，不自动重建/覆写 |
| 5 | 同一run的Linux独立验证同份七包并产真实publication | 不重构包；Current政策与在线撤销失败则停，保全真实失败/清理证据 |
| 6 | 下载原公开Artifact，核服务返回digest、内部清单与每包hash，核可信feed原字节 | Preserve BytePreserving；保留实际runId/attempt/source/toolchain/评估时点；不以人工填成功代替run验证 |
| 7 | 审阅批准准确Main consumer身份/compatibility及所需后续窗口 | 保生产NuGet签名要求/source mapping。新包可信不等于host可信 |
| 8 | 后继WP02-02采用与原17PGT真实验证 | 另授EF/tenant-storage/host/lock及独占PG资源；本轮无DB窗口 |

预算是规划估计，不是账单报价：单次正式workflow预计合计约20–50 runner分钟，当前硬上限Windows45+Linux20=65 runner分钟（不含等待人工批准）。本轮未查询账户计费或消耗远程额度；实际货币费用取决于届时仓库/runner收费策略与账户额度，在授权请求时再核。独立RFC3161 preflight、signer再验证、额外重试/恢复均不包含在这次预算或授权中，若需要应分别给出scope/分钟再批准；不自动触发。

没有执行任何上述正式动作。准备产物可以审查合入，WP02-02仍BLOCKED/NOT_CHECKED；并不要求当前用户先批准一个尚无最终源码的发布。
