# W486-01 · 合法政策字段与固定必填公开输入

状态：按用户接受的[12h连续执行](../../planning/20261111-codex-12h-business-chain.md)及原生8h检查点登记实施。COORD单对话，无子代理；status/start已完成，复用Main DEV-1 / CRM DEV-2，两仓分支`codex/w486-01`，基线分别`2905582874aa520665b4b6e596e478f1c9c81d77`、`d0183afd23a200f49f536a23fb31f1c853fcc8a0`，集成席位空闲。

## 前项与范围

[W485](../W485-01/DELIVERY.md)已正常采用Main #953 / CRM #174，Issue #173关闭，准确任务分支清理、固定工作区干净detached、两席官方finish及释放均核销；主目录安全快进，Main原14,932个未跟踪资料路径完整保留。原询盘37/60 PASS、23 NOT_RUN；修订2/247，PG16/11及原375的38 DONE/1 IN_PROGRESS/336 PLANNED保持。

本窗口关联WP03-02：原Schema/Owner接受的合法政策字段组合与固定必填公开输入兼容：真实配置激活/公共请求先重现，按原合同最小修正；可选字段策略和导出字段不扩张，原键/事务/权限/合法域保持。 原公共DTO必填contactName/need/consent，当前真实页面也固定提供这些输入；政策字段列表在原Schema内为六字段枚举中的4..6项、Owner另要求可用email或phone。先确认实际接受/激活的组合能否提交页面和DTO必填输入，不先推定产品缺陷或通过缩窄配置域消除问题。

## 实施与退出

1. 原Schema/固定必填/已有Owner语义和当前源码核对；新增最小实际HTTP/PG重现，失败记录保留。
2. 仅有真实兼容缺口时，在拥有公开输入不变量的入口修正；固定字段信任原合同，可选字段按政策，导出只按原政策字段。不得扩大可选数据收集或改变原指纹/回执/事务/授权。
3. 相关行为验证；前端代码/依赖未变时复用W485最终Next构建，恢复原缓存固定路径后只运行所需真实页面场景，不因SHA重建。
4. 一次任务级集中SPEC/QUALITY审查、正常两仓采用、进程停止、准确分支/工作区及官方席位收尾。AC-INQ-027等完整义务未齐则保持，不用本子项冒整卡完成。

唯一报告本文件，原始命令/失败/结果和自有PG在`D:/CP6-local-test/CP6-12H-20261111/W486-01`。分支执行中；两个席位由本任务独占，尚未释放。无Actions或部署权限变化，12h目标继续ACTIVE。

## 实际缺陷与修复

[CRM Issue #175](https://github.com/GTX537/CP6.CRM/issues/175)已登记P1。两种配置均经真实Owner保存/预览/激活及公开GET接受，但固定必填输入实际POST得到422 `crm.public_field_not_allowed`：`[contactName,email,phone,need]`和`[email,phone,company,consent]`。修前`public-policy-red-01`两项实际HTTP/PG失败；有限合法组合单测`public-policy-unit-red-01`为15失败/12通过。

最小修正仅在拥有该不变量的公开输入入口：`InquiryPublicSubmissionContentV1.Normalize`不再将固定合同必填contactName/need/consent当作可选字段禁止；原严格解码、归一化后必填检查、optional email/phone/company政策、原指纹及事务顺序保持。合法域依据原Schema、Owner实际接受/激活和公共DTO交集，不以收窄配置域消除问题，也不改冻结合同版本。

首次复用W485原Next构建进行实际页面检查时，`public-policy-next-01`超时失败：前端`preparePublicSubmission`有相同错误判定，尚未发出POST。追加前端行为RED `web-policy-red-03`确认`crm.public_field_not_allowed`后，删除对这三个固定必填字段的额外政策要求；可选字段仍按政策置null，必填和至少一种联系方式仍校验。前端源码已变化，因此保留旧构建并实际重建，未把旧W485构建当修复后证据。夹具改为同时等待提交/回执以保留正常失败截图，不让未处理的第二个超时掩盖失败。

## 实际验证及复用

全部业务及IAM为明确模拟输入，实际.NET 8.0.424、Node22.22.0、Next16.3.3生产模式、Chromium、HTTPS、PostgreSQL18和后台Worker真实执行；仅使用自有隔离端口/库。实际成功结果：

| 来源（本任务private） | 实际结果与范围 |
|---|---|
| `public-policy-unit-green-02/results.trx` | 27 PASS/0跳过：21种合法4..6字段组合；3种optional越界拒绝；3种固定必填无效拒绝。 |
| `public-policy-green-02/results.trx` | 2 PASS/0跳过：两种已激活政策实际201/Lead；越界先422且六表计数/H01均零；原键重放原201字节、计数及调用不增加。 |
| `web-policy-green-04.log` | 9 PASS/0跳过，提交相关原测试及21种组合逐一构造；固定必填无效始终拒绝，政策外optional始终null。TypeScript测试编译通过。 |
| `next-build-01.log` | 因前端源码改变，实际`next build --webpack`成功，含应用TypeScript检查。 |
| `public-policy-next-02/results.trx` | 1 PASS/0跳过，37秒：真实页面在无consent政策项时仍完成必填同意→匿名提交/原回执→人工放行一Lead→原202/后台生成→实际文件下载一次消费。文件字段精确为原四项contactName/email/phone/need，未加入consent/company。 |

已实际查看`public-policy-next-02/actual-next-chain.png`。该链继续核实际HTML防嵌入、JSON/登录完整CSP，内登录会话不能替代公开receipt凭证、412显式重读后再发新意图、PG原图封存与一任务一次消费，未改变业务时钟或缩短原期限。受影响的39个测试结果分属上述范围，UI内部21组合不另冒21个测试。未重跑无关全仓/原期限保留；W482–W485有效证据仅按原范围复用，不改称本次新执行。

初期Web命令的两个ENOENT源于测试fixture要求apps/web工作目录；保留`web-policy-red-01/02.log`，不当产品RED。纠正工作目录后的red-03才是产品失败。原始全部失败/TRX/新旧构建和PG目录保留，不重写结果。

## 集中审查与剩余范围

执行者完成一次任务级集中SPEC/QUALITY审查，检查基线至最终产品/测试完整diff（含新增文件）：固定公共合同和Owner合法域保持、optional限制与导出字段不扩张、归一化/精确数值/指纹不变、越界发生在扣额/H01前、原键恢复零重复；无DDL/依赖/权限/租户/路由改动。必需相关检查成功，未发现阻止正常合并的问题。文档只检查完整diff、结构与引用，不启动额外产品测试或审查代理。

本项不等于AC-INQ-027完整义务或整个CRM-INQ-01完成，原询盘37 PASS/23 NOT_RUN、修订2 PASS/245 NOT_RUN、PG16 PASS/11 NOT_RUN及原375的38 DONE/1 IN_PROGRESS/336 PLANNED保持。AC-INQ-054尚未完整核验，不借本次输入检查标PASS。

## 远端采用与生命周期

CRM [PR #176](https://github.com/GTX537/CP6.CRM/pull/176)正常合并，源`662f90b2cfd6487f68aa56743beae3c913d5f533`，远端main`1d952a579d06404bac17315d99113d2734f2f0f9`；fetch核对源祖先及完整树`4144fcbf8ed4a38db5943153b2234d434ace6060`相同。[Issue #175](https://github.com/GTX537/CP6.CRM/issues/175)在采用及生命周期完成后关闭。官方finish成功，准确本地/远端任务分支删除，DEV-2干净detached供复用，席位已释放；证明为private `crm-finish.json`。

自有PG端口55570按原settings/Owner/数据目录核对后smart stop，数据/日志保留；HTTP、Next、Node和Chromium子进程已退出。验证生成的`.next`/`.test-dist`仅核边界后可逆移动至private `retained-generated`，原W485构建亦保留；固定缓存node_modules保留，无强删或新增工作树。工作流树与受保护main一致：CRM `0c2d74f9fdc63f5178f97baf33950fb3d4dad751`、Main `3355235636fd45116f5445fca32fc65e49f13003`，相关依赖未变、已复用触发分析，本次路径不触发生产tag；CRM任务Actions列表为空。无取消项、触发配置更改、远程运行或部署。

Main [PR #954](https://github.com/GTX537/CP6/pull/954)已将源`6db248ee6e902fd17b45d31aa7c2fe5aca73dc93`正常合入远端main`d90c324b3a26bda3963297d32f3fa2cdcbd123b4`，fetch核对祖先及完整树`b08e87ca5b2f52d3e0d128f146902891f1ca9038`一致。官方finish成功，准确本地/远端任务分支删除，DEV-1干净detached、席位已释放；两仓证明为本任务private `main-finish.json` / `crm-finish.json`。两个主目录已安全快进，Main原14,932未跟踪路径完整保留，Main任务Actions列表为空。12h原生目标继续ACTIVE。
