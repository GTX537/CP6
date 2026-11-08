# W320：原生导出载荷的受控处置 v1

原WP02-15已接受范围的逐Owner推进，当前撤销下载仅撤访问、payload字节仍在；本窗实现准确原生制品的本库载荷删除，不宣称物理介质/WAL/备份/外部副本清除，整体仍PARTIAL。只在新隔离PostgreSQL18/模拟业务与明确模拟政策验收，不执行既有环境处置或部署。

## 设计取舍与授权

只改撤销展示不能补实际删除；全局批量purge会越过精确来源、hold和最小回执；选择显式版本化Owner Main.Privacy.ExportPayload.v1。现有PrivacyDispositionOwnerV1的独立可信事务、Root/当前实际人/最终会话/Clock和全主体hold可直接复用；新Owner使用独立命令/原结果命名空间，不改旧身份处置输入或默认语义。保留当前租户/环境/Owner边界，不增加内部资格包装。

用户已批准原24h计划及逐Owner处置/恢复，项目AGENTS允许已登记连续窗口由总控核对实际依赖后推进。本窗COORD按这一明确授权登记设计/资源；brainstorming的探索、方案比较及自审在本合同完成，通用逐节重复确认不另作为暂停条件。writing-plans沿原单人执行计划继续；不新开目标/并行对话。

## 精确范围、政策与hold

制品以真实GUID及不可变原收件回执拥有其operator tenant、target tenant/subject、原SHA/捕获时间；不按文件名、当前用户名或客户端Actor猜归属。元数据管理限定当前operator tenant且有独立payload-read能力；不因此授读取PII载荷。原用户已匿名化或不存在不妨碍管理其准确旧制品。

新的版本化政策由明确能力管理员配置target tenant、basisRef、enabled、最短保留天数及是否要求访问已关闭。访问TTL不是法律保留；不猜真实企业期限。天数按可信捕获UTC时间加整数86400秒，不能照PG日历interval跨DST改变期限。政策可明确允许处置尚开放访问的副本，执行时一并撤销；策略/当前资格在最终决定点负责。客户端只给准确预览摘要/版本及依据引用，不接原字节、路径、Actor或审批人。

所有主体hold沿现environment+target tenant+stable subject全局权威，review逾期也不自动解除。新制品上下文可用现hold能力设置同一主体hold，准确对象从制品派生；即使主体原行已不存在仍可保护历史载荷。共享hold追加算法只保一个权威实现，旧身份hold入口的既有输入/资格不收窄。

## 申请、效果及原结果

封闭新命令：政策CAS；准确artifact/policy预览；以RequestRef和scope digest申请；独立当前批准/拒绝/撤销；按计划版本/digest/ConfirmationRef执行。申请人不能自批。当前批准者资格、政策/保留窗、hold及准确载荷修订在最终Root门内核对，已发生效果不随之后撤权改成未发生。

实际效果删除本库payload bytea，确保制品访问已撤销，保存最小不可变实际执行/墓碑/原命令。原回执/历史不包含导出明文，不删除历史身份或来源。所有相关原生写者共用已有IAM门；载荷删除须有真实同事务处置或恢复原件，插入/更新不得绕墓碑复活旧artifactId。墓碑/效果/最小回执同commit；无已拥有依据的DELETE/孤立效果必须拒绝，失败整组回滚。

提交未知只查原操作，不把NOT_FOUND当无效果换号重做。当前读权可恢复原结果，显式关闭与迟到执行互斥。页面元存储仅保存身份分区的原UUID/必要类型，不存payload或完整私有命令；取消不发送，身份变化/迟到响应清理。

## 恢复采用

新墓碑及相关政策/hold变化参加现真实restore_epoch，已受管候选缺当前源证明继续失败关闭。新的恢复预览/批准冻结SourceEpoch与两库实际身份，包含全部已接身份和原生导出载荷墓碑；只有导出墓碑时也能真实恢复。按当前源政策和hold核对后，在原SourceRoot→CandidateRoot顺序下共用准确载荷效果：旧副本存在则真正删除并撤访问；不存在则记录实际缺失并安装不可复活屏障，元数据冲突明确拒绝。

候选效果/原回执/开放标记同事务，Source责任与两次提交UNKNOWN沿现协议，不另造双库原子承诺。恢复的源事实不能伪装成候选本地原申请/审批历史。新增Scope导出计数为0及Unit Owner/ArtifactId为空时应保持旧序列化形状/摘要/原结果兼容；新计数和结果明确属于该Owner。仍未覆盖FILE、外部已下载副本、WAL和完整环境推广。

## 文件与实施检查单

- [x] 读取当前导出/处置/hold/恢复代码、W312/W315原约束，确认真实载荷保留缺口，登记本设计与方案选择。
- [x] CP6.Persistence.PostgreSql/Migrations/Core/20261108140000_ExportPayloadDispositionV1.cs：前向政策/计划/事件/执行/墓碑、载荷修订与实际原生写者/提交约束、原操作动作注册，无自动授权。
- [x] CP6.Core/Services/Platform/PrivacyExportDispositionV1.cs、PrivacyDispositionOwnerV1.ExportPayload*.cs、PrivacyExportPayloadEffectV1.cs：复用可信事务，独立真实Owner/当前政策/hold与原命令；先实际行为红测。
- [x] PrivacyExportOwnerV1.cs及PrivacyDispositionControllerV1：准确DISPOSED状态、封闭新API/主体hold入口和旧合同兼容。
- [x] PrivacyRestoreV1/NativeV1及现RestorePlans/Execution：已接载荷墓碑的计数/当前资格/实际候选效果/原结果，保0新墓碑时旧wire。
- [x] cp6.web/src/api/platform及GDPR现有面板：明确政策/准确原件/独立批准/执行确认/原结果和真实删除状态，不暗示全备份已清除。
- [x] 定向PG18/真实认证HTTP/两用户Chrome、相关Vue/类型及必要旧导出/身份恢复回归；一次集中审查，单一交付报告。
- [ ] 同步roadmap/生成页/四状态，正常两仓采用、准确分支清理、官方finish释放。

## 实际验证界限

覆盖最短UTC窗/访问条件、独立审批/当前撤权/准确CAS、全主体hold与双向真实原生writer竞争、最终会话和回执故障回滚、实际COMMIT后未知/原结果/关闭、原生复活及未拥有删除拒绝。模拟业务和政策，内部Owner/PG事务真实。

恢复实际pg_dump/pg_restore到唯一新候选，旧载荷真实存在/已经缺失/无原artifact/身份冲突、当前hold/政策、真实结果未知/开放门及原身份恢复回归。HTTP及Chrome确认取消零写、两身份批准、实际删除后丢回包/刷新不重做；旧有效证据只在输入未变的范围准确引用。仅所影响的测试，不用条数/静态断言/新证据数量当目标。

设计自审：未留TBD或无效果端口；访问关闭和载荷删除分开，源/候选和原操作责任分开，数据缺失不冒删除成功；未来Owner/正式环境/外部副本仍独立。具体SQL及界面沿当前代码落地，若真实调用链要求实质范围变化，由COORD在本窗口精化而不默默扩大。

## 实现核对

候选实际字节SHA只在新的外部恢复边界核对一次，源内复用不可变载荷/原回执契约。明确本Owner PostgreSQL23514拒绝为已回滚具体409，真正传输未知仍保原UUID恢复。已处置结果不再误称缺失，政策改目标或计划读取故障不抹去已知原回执。最后的采用/席位步骤在本提交仍待执行，以wave320-final-verification.json实际核销；不提前勾成完成。
