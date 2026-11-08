# W324：租户导出载荷的真实恢复采用

统计校正：原始20261102-001406Z库存CSV的“已局部交付，整卡未关闭”59项与W250逐卡ID完全一致。当前仅其中8项DONE，剩51；WP12-03/04/08/10虽已完整DONE，却不在原59集合。旧followup把这4项一并从59扣减，造成47，现纠正51。375的36DONE/2IN_PROGRESS/337PLANNED和这4项合法完成均不回退，125/247不变；后续原卡逐项核销继续按原合同，不用新窗口数代替原59。此前47数字为旧计数错误，历史报告保留并由本校正说明取代当前统计。 [原59来源](../W250-01/DELIVERY.md)。

W324完成Main.Privacy.TenantExportPayload.v1真实恢复采用：独立无subject的tenant Unit，准确原制品元数据/revision/实际SHA一次外部核对，真实删除与载荷缺失/整制品缺失0分别记录；当前源政策/全目标hold和独立恢复批准，父回执/实际效果/墓碑/fence/开放标记同候选commit，保持两次commit与原结果责任。其他Owner/WAL/外部副本及完整环境恢复仍PARTIAL。

Main基线 `be6ec3bea15d3e0a6d54f1960f9f7eaabb13f641`，START `16b3e98d691a6355d4de26ad95b66c08e0254a90`。COORD单人固定DEV1/DEV2，无子代理或新增工作树。[范围合同](../../contracts/tenant-export-payload-restore-v1.md)。直接采用已有当前平台会话、权限/独立恢复批准、IAM共同根、当前源政策/hold与原操作协议，不新增资格包装或重复内部哈希。

## 实际效果与边界

保持原RestoreUnit.SubjectId身份，新增独立PrivacyRestoreTenantPayloadUnitV1/可选TenantPayloadUnits；tenant Unit没有SubjectId。Scope新TenantPayloadTombstoneCount为0时省略，结果数组null省略，无新Owner时旧wire/摘要与原结果保持。当前源按原operator tenant/policy/准确target读取现政策，原可靠UTC捕获时刻计算保留窗；全目标所有主体当前hold跨operator tenant生效，逾期仍有效。源政策/hold/墓碑变化推进source epoch，旧批准/开放证明失效，W323的正向unsupported屏障以真实适配替换。

候选是外部恢复数据库。CandidateGate拥有物理身份、环境、归属和S→C锁顺序；本Owner在此一次读实际元数据、原回执身份、payload revision/长度/SHA，与可信源已接受scope核对，不对内部源回执重复哈希。FULL JOIN识别无元数据的孤立字节并412拒绝；同revision/长度但字节损坏、原回执目标改动均412拒绝。必要新迁移、必需Owner函数或native fence缺失503，不能以通过预览或默认空集合冒成功。

有真实字节才REAPPLIED/真实RemovedBytes；metadata存在而payload缺失为ABSENT_FENCED/PAYLOAD_ABSENT/0，整个制品不在原备份为ABSENT_FENCED/ARTIFACT_ABSENT/0，均安装同源墓碑和防回填。原制品/Manifest/历史保留，没有假主体或其他Owner已清声明。父回执、新Unit、实际DELETE/撤访问/墓碑/fence和开放标记同候选事务，延迟约束拒孤立父成功与静默跳过DELETE；后者实际COMMIT明确409，不发布源成功。

候选旧active hold不能替代当前源已解除的决定；只有准确same-writer-xid父回执所属的原行修订和真实字节数恢复效果可采用当前源决定，普通live/native删除仍受现hold阻止。历史hold不被删除或改写。源与候选仍是两次实际提交：候选丢COMMIT回包时源结果未保存，保持隔离，当前read可在撤execute后RECOVER保存原结果；源提交前不可用是明确模拟拦截，候选实际已提交而源无结果，同样隔离后接回。真实源COMMIT回包丢失时源已保存APPLIED，按原键直接读回已保存结果，没有重施；开放门依据当前数据库证明，不依据客户端是否收到了答复。不承诺两库原子，也不把unknown解释成失败/无效果。

页面增加真实租户载荷墓碑数和独立Unit，区分实际删除、制品不存在0和载荷缺失0；确认/独立批准/取消零写/UUID分区/已知原回执行为沿原协议。当前本库/恢复采用范围固定显示，计划读取失败不会抹掉事实或范围；历史W323回执未采用说明明确为当时范围，历史原回执不改写。其他Owner、WAL/备份/已下载外部副本及完整环境恢复仍未因此完成。

## 实际验证与审查

14项新增实际PostgreSQL18场景、8项受影响live处置与既有身份/主体载荷/安全字段恢复回归（含新增恢复与既有处置Chrome）、25项Vue行为及vue-tsc通过。 逐场景来源 `D:/CP6-local-test/wave324/foundation/final-validation.json`，数量只记录实际运行。隔离新PG18.6/真实pg_dump、pg_restore、Main认证/CSRF/HTTP与Chrome，用户/租户/政策和故障输入明确模拟，内部事务/原生效果真实执行。覆盖准确删除/后生制品缺失屏障、metadata存在但字节缺失、同revision损坏/孤立载荷/回执身份损坏、源当前政策/hold ABA与候选旧hold、缺迁移/函数/fence、实际候选/源COMMIT丢答及提交前不可用、最终会话/静默DELETE回滚、新HTTP503和旧链兼容。

新增两人Chrome真实恢复本次111340字节，另7项备份载荷已缺失如实显示0（这些是同夹具已存在墓碑）；无API内部mock，一次申请/执行、取消零写、丢答重载原结果、准确无subject Unit，截图已查看。既有处置Chrome回归真实保留原回执与失败刷新行为，范围提示调整后定向通过。最小权限夹具其他分面403、既有导航i18n key/现有状态码显示原样保留，不宣称全站UI无问题。

初始unsupported503、回执身份与缺helper红测，以及旧浏览器范围提示随计划隐藏的失败日志/TRX均保留。只修复实际发现点并执行对应回归；合法输入的未改变字节/hold/commit结果复用，纯范围说明位置变化不重复Vue/类型构建，提交/SHA变化不重跑。一次COORD任务级SPEC/QUALITY集中自审覆盖：保持原非空主体Unit身份并新增独立无subject数组、默认0/null省略保旧wire；准确Source/Target物理身份、源政策/全主体hold/ABA/当前权限/独立批准/最终会话和SourceRoot→CandidateRoot；外部候选一次元数据/原回执身份/revision/字节SHA核对，FULL JOIN拒孤立字节；真实字节与两种缺失0、同源墓碑/native原生效果/父回执/开放同commit；过时候选hold仅在准确当前parent-owned恢复效果采用当前源决定；必要新迁移/函数/触发fence缺失503；静默DELETE在actual candidate commit拒绝409；候选丢答、源提交前不可用和源已提交丢答各有真实责任和原键恢复，不承诺双库原子；页面取消零写/UUID分区/独立批准/原回执与失败刷新、当前采用范围及历史范围分别表达。审查修正外部回执目标身份未比较和缺必需helper未被预览识别，均实际红测后定向回归；当前恢复范围提示移到不依赖计划刷新位置，旧浏览器定向通过。无未解决阻断项。

## 目标与生命周期

原375=36DONE/2IN_PROGRESS/337PLANNED，原59=8闭环/51剩余（统计校正），125目标/247正式验收不变。WP02-15仍余未分类安全内容/不可归属事件、授权/组织引用、OA/PLM/CRM历史、FILE及其他来源副本、整租户全部Owner导出/处置、WAL/备份/外部副本与完整环境恢复。当前新租户载荷此Owner的真实恢复已采用，不再列为未实现。W306/W249/七天及已闭环FORM不重复；下一W310预算末尾实际原375/59及工时核销，不另起周期。

开工累计77945秒，交付采样80342秒，原24h仍active/incomplete。22h已实测校准79283秒；实际本窗以官方finish证明计，较快不以短窗结束目标。下一按预算末尾W310核销当前375/59真实剩余、本轮实际工时和可复用证据，不空等、不另开周期。

本提交正常Main/Handoff采用、准确分支清理和官方finish待执行；分支处置结果、工作区状态及席位释放以 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave324-final-verification.json` 最终核销。没有Actions/工作流或依赖/保护变更、生产部署、既有数据库迁移或恢复；本地模拟业务验收不作为生产候选或环境推广。
