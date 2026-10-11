# 共享能力与工程消费者补充 R1

本页及 `assets/shared-consumers/` 是本次新设计决定。OA 原 `oa.app/2.0`、Space 原授权版本以及 ENG 原技术 Manifest 字节不变；新补充使用独立版本和注册项。真实服务、所有写者与当前授权门尚未证明采用。

## 1. ITER → OA 的持久消费映射

采用 `ITER-OA-APPLICATION/1`。Owner 注册主键为准确环境/租户、原 PLM Owner、业务类型及 `SubjectKind=PLM_ITER_REVIEW`；服务身份和回调路由由部署注册表固定。浏览器不能传入 Owner、Tenant 或回调地址改变受众。

### 冻结主体和候选集合

ITER 候选是集合，不能用首项冒充 OA 的一个 Candidate。新增不可变 `ReviewCandidateAggregate`，在原冻结 Round/ReviewPackage 的事务内分配本地 UUID 小写文本 `aggregateId`（36字符，符合 OA Key1..100）和十进制 `aggregateVersion`。完整 `members` 保留每项原 candidateId、candidateVersion、准确原技术/文档引用及摘要；按原身份 UTF-8 字节序排序并拒绝身份重复。成员原值不截断、不转 UUID。集合过大不能截为前1000项；超过 OA 原传输上限时阻发起，使用原支持的不可变快照引用及分包能力必须另注册准确协议，不能自行增字段。

`aggregateDigest` 是本新合同 `ITER-CANDIDATE-AGGREGATE/1\n` 加 canonical JSON UTF-8 的 SHA-256；预映像含 schema、Scope、Iteration、Round、aggregateId/version、全部 members，排除自身 digest。canonical规则固定对象键按Unicode codepoint升序、UTF-8、不转义非ASCII合法字符，ASCII控制字符用JSON标准转义，无空白；拒绝重复键和孤立surrogate。数组按已经冻结的顺序，原业务数字用字符串，Unicode不做隐式标准化。它不是原 ReviewPackageDigest，也不重写任何原 Candidate Digest。members必须与冻结ReviewPackage的实际候选集合按完整ID/version/技术引用/摘要双向相等，缺项、多项或同ID异版均不能提交；Binding的Scope、Round和aggregate全身份必须同时等于该集合及原Package证据。

OA `engineering.candidateId/version` = 本聚合 ID/版本；`engineering.digest` 和 `reviewPackageDigest` 均来自原冻结评审包的准确 Digest 结构，`technicalManifestDigest` 保原独立可空值。Subject.subjectId、Submit.roundId 和 engineering.roundId 均为绑定中的 `oaRoundKey`：原 RoundId 合乎 OA Key 域时原样保存，否则使用本轮首次建立并持久的 UUID 别名，完整原 Round 身份不变。`businessRevision` 使用本地生成的不可变 `roundBindingId`（UUID文本），映射整条 `(Round, aggregateId/version/digest, PackageRef/Digest, PolicyRef/version/digest, technicalManifestDigest, mappingVersion, rejectDisposition)`。它是映射身份，不拼接或截短外域键。原 Ref 超域时由准确 PLM 注册解析器提供不可变别名 Ref，存完整原字节、原 Schema 和双向身份；不冒充原 Owner 自己签出的新 Ref。未注册 Digest 算法仍阻本路由，绝不把非sha256内容塞入sha256字段。

完整逐字段来源见 [OA 映射登记](assets/shared-consumers/oa-field-mapping-1.0.0.json)。新 Snapshot 封闭内容为 `{schema:"ITER-OA-SNAPSHOT/1",aggregate,binding}`，按本节 canonical规则保存完整字节；snapshot.digest 与 Subject.contentDigest 都是这些准确字节的 SHA-256，canonicalizationVersion 为 ITER-OA-SNAPSHOT/1，不能与原评审包摘要混为一谈。OA 原 256KiB Snapshot、1MiB body、1000 项 ID 限制依旧有效。原命令/Submission/Outbox ID、可信 starter/SOD/授权证据、业务 rowversion、政策及完整 Engineering 各自按映射登记保存；任何缺项都不能以同名字段猜值。

OA SubmitCommand、DecisionEnvelope、ApplicationReceipt 的字段、大小写及必需/可空规则逐字采用 OA R2 §4。新增 `rejectDisposition=RETURN|REJECT` 只存本地冻结映射/策略快照，不能加入 OA 封闭 Engineering 对象，更不能新增 OA 决定枚举 RETURN。审批回调必须精确匹配完整绑定；仅 IterationId、同一截图或同摘要并不足够。

### 状态、事务与恢复

| 阶段 | 唯一性与同事务写集 | 失败或重放 |
|---|---|---|
| ITER 发起 | 原 `review.submit`、当前 InDesign、无暂停/漂移、SOD；冻结 Package+Aggregate+Binding+Round+InReview+Owner OA Outbox+原 CommandReceipt+Audit | 同命令同体返回原回执；异体409；未知提交只查原键，不重新造 Round |
| OA 接收/启动/决定 | 保原 Tx S/E/D、AppSubmission、StartOutbox、DecisionOutbox；engineStartId=SubmissionId | START_UNKNOWN 查原实例；不因超时起第二实例 |
| ITER 接包 | Scope/服务/版本/原bytes摘要合格后先持久 Inbox；UQ Scope+Producer+EventId，另按 Instance+SubjectRevision+Decision 去重 | 同Event异body隔离；缺序列保 GAP；HTTP接收不等业务 APPLIED |
| ITER 应用 | 当前控制头→Round/Binding→Inbox/Fact→ApplicationReceipt→OwnerApplicationOutbox→Audit；业务阶段/问题处置、原命令结果和回执同commit | 当前 Round、完整 Binding、当前权限/用途/SOD和全部 Blocking 已 VerifiedClosed 才允许 APPROVED 应用；任一失败不写业务成功 |
| OA 回执投影 | 原 Tx R 消费Owner完整ApplicationReceipt，最高连续revision CAS | 仅更新展示；不再次驱动 ITER 状态 |

APPROVED 应用原批准后的阶段；REJECTED 只依据已冻结 rejectDisposition 映射 RETURN→InDesign 或 REJECT→待评价，不能自动Closed。缺映射为 BLOCKED_POLICY_MAPPING，保决定原事实。WITHDRAWN 只应用准确原撤回轮次。`action=PLM_ITER_REVIEW_DECISION` 对应一个应用单元，不能换 action 名再次应用同一次决定。

撤回与应用争用同一控制头/轮次 CAS。应用先commit：撤回409并给原回执；撤回先commit：迟到APPROVED只能记 STALE_RECEIPT，不能改变新一轮。Owner 已应用而发送回执失败时只重投相同不可变回执；BLOCKED 恢复依原 OA 规则追加新 revision/previousReceiptId，既有 APPLIED/STALE/CONFLICT 不改写。网络投递在锁外，禁止持业务锁跨服务等待。

本地映射不另设绕过原授权的HTTP入口。新增持久记录的读取、重派和恢复沿原受控 Owner command/query 能力，受众/权限/错误/幂等完整继承 OA §4/6/17；mappingVersion 未注册、原候选集合不完整、传输域不能无损承载时阻相关新发起。旧直接同库 callback/内存事件只有完成全部持久协议后才可接入新路由，不能与新worker重复应用。

## 2. SPACE → S5 准确消费

采用 `SPACE-S5-AUTHORIZATION/1`。这是新消费者映射，S5 对 Main/CRM 的答复不作为 Space 已部署证明。原 `AuthorizationVersion` 算法、Organization.SecurityStamp、Membership.SecurityStamp、GrantVersion、FieldPolicyVersion 与数据库 RowVersion 各自保留，不能拼成一个 int 或相互替代。

### 身份与资源

读取仅接受已认证身份唯一的 tenant_id、NameIdentifier 和有效外部成员关系；tenant_id 必须等于可信 TenantContext。外部 Portal 明确 subject_type=external，除组织选择入口外必须有可信 organization_context_id，并校验租户/组织/成员 Active 及完整成员 stamp。不能从请求body/URL任意选择另一个组织，不能从缺失 claim 推导默认租户。

`S5Binding` 保存 schema、准确 S5 provider/policy/field registry 版本及完整当前权限向量、Space原版本向量、主体/租户/组织/站点/用途、准确 grant clause 集合、读时点和有效期限。Space向量分别含组织/成员的准确ID+SecurityStamp+RowVersion，以及按grantId排序的全部 `{grantId,grantVersion,grantRowVersion,fieldPolicyId,fieldPolicyVersion,fieldPolicyRowVersion}`，不能拿一个Grant版本代表多条Grant。多个Grant共享同一Policy时其完整Policy三元组必须相等。原合法未指定Policy的Grant保fieldPolicyId/null、version0、rowversion/null及clause.fieldPolicyRef/null；配置了Policy却当前不可得/已撤回不能伪装为未配置。向量与完整clause集合按Grant身份双向相等，并绑定准确当前Grant成员集合证明；新增或删除Grant同样使旧向量过时，不能只检查旧集合内各项版本。新组合票据是独立合同，原 AuthorizationVersion 只是其中一项。rowversion保8字节Base64，原long在跨JSON传递时用十进制字符串；不能以JS Number承担完整int64。

资源映射固定三类 PublishedScene、Stock、Task；字段域来自固定源码 `SpacePortalFieldCatalog`，61项原键逐项映射到新注册的 `SPACE.PORTAL.<ResourceType>` 同名字段。注册状态仍 UNPROVEN。每条同时登记实际 DTO 路径、导出列及复合字段依赖；集合内每个成员沿同规则处理，不能仅过滤列表头。没有字段映射的版本禁止新路由输出，不凭名称猜映射。

### 权限和字段计算

先按 S5 当前原政策在真实存行聚合 MIN，再在**完整且已知**注册字段域补默认1：A缺+B3→3，A缺+B2→2，A2+B3→2，全缺且注册完备→1。先给各角色补1会扩大权限，明确禁止。未知role/registry/field/Access或不健康投影直接关闭相关输出。

动作、对象、行范围、用途、S5 online∩健康投影、Space完整grant clause再求交；同一匹配 clause 的 Site/资源/行/期限/字段策略必须一起满足，不能取甲grant的site、乙grant的资源、丙grant的CanExport拼授权。外部 write 永远false，Access1不赋予外部写权限。

S5 Access1/2/3 是读写/只读/隐藏；Space Masking None0/Partial1/Hash2/Redact3 是另一轴。先过读取门，再按原Space策略mask；Access3整个字段省略，绝不能直接强转成Masking3或输出CLR默认值。复合 position/dimensions/geometry 及其全部坐标受父字段同一门；其派生搜索、排序、聚合、导出列也不得旁路泄露。掩码规则有冲突时采用原Space已定义的组合，若不存在准确组合定义则拒绝输出而非猜“更高数值”。

### 当前性及失败恢复

每页、缓存命中、导出执行、下载和历史回执披露都做当前读取授权。缓存键纳入完整绑定版本和查询范围，缓存只是候选数据。游标冻结完整原cut及权限向量；权限变化拒绝续页并重新从头获取，不能旧cursor配新更宽权限。30分钟滑动缓存不是撤权承诺期限。

每个结果输出前比较组织/成员 stamp、grant/field policy/主权限向量；中途改变则丢弃待输出结果。导出文件绑定准确actor/org/用途/字段向量与不可变结果摘要，下载时仍重验，撤权后不凭旧URL或原成功回执放行；技术文件清理不抹掉原审计。跨库新业务效果仍需要准确共同最终门；未采用时 AUTHORITY_FENCE_UNAVAILABLE，不能靠读票据放开写入。

消费者内部返回闭合 `ALLOW_READ|DENY|UNAVAILABLE|STALE`，携本地审计Ref和允许字段/行的精确绑定；DENY不泄露隐藏数量/资源存在，UNAVAILABLE表示无法取得当前源而不是允许缓存兜底，STALE要求新读。原HTTP端点的401/403/404/412/503映射由此表固定：未认证401、明确禁止403（存在保密场景404）、续页向量变化412、原权威不可取503。它不新增外部修改接口。

## 3. ENG 多输出 Manifest

原 `plm.technical-manifest/1` 恰一个 PRODUCT、其 BOM/ROUTING 同 productCd；不修改原wire或旧签名黄金向量。新增两份独立不可变资产：`ENG-OUTPUT-RELATIONS/1` 与 `ENG-BASELINE-OUTPUT-BINDING/1`。

RelationSet 有准确 Scope、relationId/version、完整配置Ref、每个输入 occurrence 的原 ItemRevision/SpecRef，以及每个**输出 occurrence** 的 outputOccurrenceId、原产品/ItemRevision/SpecRef、生成 OperationOccurrence、配置Ref、inputOccurrenceIds 和准确关系证据。相同产品在两个工序产生两次也保两条 occurrence；不能按产品去重。所有被引用input/operation必须在完整注册集合内且配置精确一致；不靠输出行号反推工艺顺序，不凭字符串相似补关系。没有输入的合法生成操作必须有显式原工程依据，不默认为可空输入。

BaselineOutputBinding 固定新BaselineRef/version、原完整技术ManifestRef/digest（保持原bytes）、RelationSetRef/version/独立bodyDigest、准确FreezeCommand和完整技术配置证据。原 Manifest 的 PRODUCT 是兼容主产品，不代表所有输出；消费者解析多输出必须同时验证两份引用，旧消费者不能因已读原 PRODUCT 就声称支持PG03。

Binding与RelationSet的Scope/id/version/bodyDigest必须逐项相等；configurationEvidence须证明与新Baseline所属配置和每个生成操作/输出的配置相同。原兼容主PRODUCT必须准确对应RelationSet中至少一个输出的原productIdentity（允许同产品多次输出，不强行只一项），不能将别的产品的旧Manifest挂到该Baseline。缺这条身份关系不允许Freeze。

摘要各用本schema名加换行作为domain，再对闭合body（排除自身bodyDigest）做canonical JSON SHA-256。关系集数组按精确 occurrence 身份排序、输入引用数组排序并拒绝重复；保业务ID原字节。原Manifest仍用其自己的摘要算法，不能套新domain。存储原完整字节和长度，hash只作加速/完整性，身份比较保Scope+完整原Ref/version。

冻结在原ENG Freeze当前授权/Owner HoldExact/全技术头和Policy复核后，同事务追加新Baseline、原Manifest绑定、新RelationSet、BaselineOutputBinding、Receipt和Outbox；已有历史Baseline不附加一份新关系谎称原时点已有。旧Baseline需要补多输出能力时，生成合法后继Baseline并走原评审/变更/Freeze要求；不自动扩大原批准范围。

同命令同body返回原回执，异体409；来源或配置漂移412；缺映射/不支持的旧消费者422 REQUIRED_OUTPUT_MAPPING；Owner不可得503，无新Baseline。未知提交只查原Freeze命令，不重新Freeze。新读者按Baseline准确固定的两份digest读取；缺一项不返回不完整“全部输出”。这些错误为本适配能力目录的定义，既有封闭错误wire经有版本错误适配投影，不在旧枚举中暗加值。

## 4. 其余已有边界的处置

| 登记 | 本次明确采用边界 | 正向/反向验收重点 |
|---|---|---|
| PE-ACT-FILE-COMMIT-MODE | ACT仅LOCAL_AUTHORITATIVE_GUARD；不把通用远端租约当ACT已接入 | 真实本地最终门与同事务；仅远端reserve票拒绝 |
| PE-HANDOFF-NAMESPACE | 完整来源+合同名+版本+H号构成引用；采购H31与ENG H41分别解析 | 准确来源可解析；孤立H号跨册拼接拒绝 |
| PE-ENG-TOOL-OWNER-PARTICIPANT | ENG-TOOL每个真实Owner必须提供native schema与HoldExact；静态正面结论不替实例 | 全参与者同最终点；缺一个Owner仍FENCED |
| PE-CP22-EXACT-PROFESSIONAL-ADOPTION-NOT-RUNTIME | role15内PLM/CFG/ENG/DEV/REL专业设计ADOPT是后继；角色总数仍17，实际采用/真实provider仍0 | 有界角色15范围；不扩成18角色或运行已采用 |
| COM-EST-QTN-V2-DECODER | 原EstimateBasis wire保持；新decoder完整识别v2 ModelIdentity、FormulaDigest和Disclosure，按I03/I05原用途 | 同版本完整披露可消费；缺段或旧decoder读新体拒绝 |
| COM-IC-ORIGINAL-TRANSPORT | 新HO/OPP保完整原bytes、opaque version、封闭body，按新路由专业采用 | 原byte重放相等；旧C03敏感字段删改/int强转不能适配 |
| COM-PR-CP22-NATIVE-OWNER-ADOPTION | port14准确PR R3 native intake按后继ADOPT；Digest结构、原版本和Namespace保留 | 完整命名空间+原包；仅同字段名称不能证明实例 |
| COM-PRICE-EST-UNIT-SCALE | PRICE0..18和EST0..12为合法不同能力；EST不支持的精度显式拒绝或由原Owner给准确有版本转换 | scale12可直接；scale13不能静默截到12 |
| ROOT-X3-X4-ABC-01 | SLOT按加入前前缀<80/95且首项A；ANALYTICS按加入后累计<=80/95，保独立排序/分组/算法版本 | 单产品100%分别A/C均正确；不得用一个ABC函数覆盖两域 |

## 5. 验收和编码入口

机器资产固定原证据与当前源码SHA；新schema、映射和DEMO向量采用本次版本。撤回竞争、同Event异体、同决定异Message、旧轮次迟到、字段隐藏、整clause交集、撤权缓存/下载、重复产品多输出及旧Manifest摘要保持均列入全局验收目录。静态检查不证明OA引擎、Space身份源或ENG冻结事务已经运行。先实现完整注册/持久绑定，再接消费者，最后完成全部writer切换及并发回归；不以部分连接状态开放 Required 路径。
