# QA-IPQC-01 关系约束与检验边界

本附册整理当前 RECOVERY-R4-SYSTEMIC-R4 类型及持久化规则，配合[模块主册](D:/CP6/docs/CP6_开发设计文档_20261010/modules/QA-IPQC-01.md)和[192项类型字段索引](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/QA-IPQC-01-类型字段索引.md)使用。前端动作、连续案例和回执原件另续；尚未把整个模块记为必要材料完成。没有执行归档 SQL、生成器或业务代码。

## 1. 开发时先区分这些身份

QAP 的普通引用为 owner/id/version；QapEvidence 将 digest 放在同层。EXEC 的 Evidence 则为 ref+digest，不能只改类型名就互转。SQL 内部 UUID 与 Owner 可见的字符串 id 也不等价，必须保留显式映射。所有业务环境键是 TenantId、EnvironmentId、Mode，真实与 DEMO 分离。

来源原文版本、当前来源头和映射头各自保存；Task 与 TaskRevision、Observation 与当前头、ResultSet 与当前头、Decision 与顺序流/当前头分层。新来源影响整族时保历史决定，新增重新评估义务并让当前可用性重新判断。不能以“历史曾通过”推当前仍有效。

ResultSet 绑定准确 TaskRevision、ObservationSetRevision、完整 slot、范围结果与冻结时间。Decision 再绑定 Task/ResultSet版本、Source/Mapping头、完整 partition、政策/计划、审批包/批准/权限和 writerEpoch。分区枚举严格为 ACCEPTED→ACCEPTED、REJECTED→REJECTED、HOLD→HELD或REWORK、PENDING→UNEXAMINED；缺测量覆盖不能投影为合格。

普通写入结果分 COMMITTED、REJECTED、UNKNOWN。COMMITTED 必须是该命令的 resultType，problem=null；后两支 result=null、problem为封闭Problem。C01 对20种命令逐一固定 requestType/terminalType，并返回保存的 requestBodyJcs、terminalBodyJcs和摘要；恢复不能拿最新页面状态重新构造旧结果。Stock 或 PLM 的未知、不可达和无权各自保留，NOT_OBSERVED 不能推断无效果。

## 2. 检验过程里的不可替代约束

ObservationInput 必须恰好提供数值或代码，另一个显式null；INITIAL、CORRECTION、RETEST 有各自事实身份，原记录不可覆盖。资格 cut 涵盖方法、规范、设备、校准、环境、人员权限，保存原事实/控制头/授权、有效期与覆盖。无截止日期需准确 noEndAuthority 原件；未知资格不能伪造成有效。

InitialClaim 的任务+slot唯一键解决双INITIAL竞争。复核原记录同时携带检验人与复核人，以及各自命令身份；SQL 检查 InspectorId≠ReviewerId。复测先提案、冻结审批包，再激活独立episode；重测不抹掉旧FAIL。ResultSetSlot及DecisionPartitionRange用复合外键同时绑定准确结果集、Outcome摘要、区间与质量，不能拼接别的任务/批次中恰好存在的ID。

QAP最后守卫与 UseFact 必须同真实业务事务提交，所核Family成员完整且当前头未变。检验批准 I18 与普通Stock采用 F15 分开：前者生成准确质量决定，后者另有 ConsumerResult、Guard和实际Effect；业务接收或质量通过不代表已移动库存。PLM的验证计划、试制授权、评价和文档四类读取均保留 FOUND/UNKNOWN/UNAVAILABLE/NOT_AUTHORIZED，不以未查到当无事实。

## 3. 关系模型的约束分工

| 关系组 | 开发约束 |
|---|---|
| [Command](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:88) | 完整环境+Kind+RequestKey唯一；保存请求原文、准确终态类型/原文/摘要和真实Actor。审计按Command唯一，不能把回执归到另一次动作。 |
| [SourceOriginalVersion](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:917) | Owner+OwnerId+OwnerRevision唯一；SourceHead的当前摘要外键指向准确原版本。映射版本另记ownerRevision，旧原文不可覆写。 |
| [FamilyDirectoryVersion](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:447) | 完整域+阶段+目录版本冻结成员与coverage；当前DirectoryHead和FamilyControlHead以rowversion CAS指向历史版本；所有成员变更参加同一门。 |
| [Observation](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:562) | 原测量、资格cut、检验命令均保留；当前ObservationHead只更新指针。MeasurementSlot唯一键包含需求、物理成员、episode、重复次序和方法摘要。 |
| [ResultSet](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:802) | 结果集与TaskRevision复合关联；Slot关联同时约束同Task及准确Outcome/digest，避免把另一个结果集的有效行拼入。 |
| [Decision](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:112) | 流内Sequence唯一；Task/ResultSet/Packet复合外键确保同一组合。Partition再强制与Outcome的from/to/quality逐值一致。 |
| [Approval](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:25) | I18命令种类和Actor、原AuthorityEvidence、ApprovalEvidence的决定/审批包/授权完整组合一起绑定；仅有批准ID不足。 |
| [Delivery](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:184) | 固定payload类型/摘要/注册版本、来源版本/摘要及可选Decision整组；Outbox复合外键与同一Delivery的Message/Payload/digest一致。Lease和ObservationHead独立CAS。 |
| [QapUseFact](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:767) | 采用记录关联准确Guard、FamilyCut及ConsumerResult摘要，保effect原文/摘要；实现必须明确 wire receipt/result 两种身份怎样落表，不能根据同名猜测。 |
| [StockConsumerResult](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:933) | 状态为APPLIED/BLOCKED/UNKNOWN；实际StockEffect关联准确结果摘要。阻断/未知记录不能假造Effect。 |
| [WriterEpochHistory](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bf/bfbd0d7b7c245820__QA-IPQC-01_SQLSERVER_DESIGN_RECOVERY_R4_SYSTEMIC.sql:1006) | PREPARED/ACTIVE/RETIRED历史与当前头分开；所有新writer须验证当前epoch。RowVersion为DB生成的8字节不透明token，绝不做版本算术或客户端赋值。 |

SQL的所有107条外键都是 NO ACTION。68表均含完整环境键；740列和96索引（其中2项含 INCLUDE列）、所有PK/UQ/CHECK/FK声明已与关系JSON逐项原文匹配，SQL剩余只有已读的schema创建和注释模式，没有执行SQL。数据库能否建成、并发锁序与真实持久化仍需实施阶段验证，文本相等不证明这些行为。

模型的 appendOnly 标志是设计语义，SQL没有凭它自动创建禁止UPDATE的运行保护；Mutable Head和租约必须CAS，准确历史同事务写入。状态流里哪些列能变更，要同时遵从主文与Owner动作，不能因存在主键或CHECK就假设已获得全部保护。

## 4. 已知类型和后继版本边界

旧 SourceEnvelope.output 与 TrialSourceReadResult.source 内嵌的 OutQualitySource 映射仍只有五成员，独立 OutQualitySource 的 receiptMappings 另有 receiptSubject。旧 QapResolution 是18字段，完整 QapResolutionEvidence 是27字段，后者加入 requirements/family/catalog/control/validity 等信息且 planRef 可空。它们不能以相同 schema 字样直接当完全相同正文或删除字段重算摘要。

NCR R12 的准确后继采用记录给出保存原件并作无损投影的方案，见主册§11；本整理保留旧IPQC字节。独立TS中 Evidence/Uom 名称引用的 OPS-IF-015仍保留，不能由本页自动补alias。生成前端/客户端必须按准确 canonical 与Owner采用版本核对，而非机械复制类型名。

## 5. 全部关系字段查阅

下列S固定展开为TenantId uniqueidentifier NOT NULL、EnvironmentId uniqueidentifier NOT NULL、Mode nvarchar(8) NOT NULL；PK/UQ/FK/INDEX中的S代表同样顺序的三列。APPEND是原模型标志；类型代号逐一映射如下，NULL与NOT NULL不省略。

```text
T0 = uniqueidentifier NOT NULL
T1 = nvarchar(8) NOT NULL
T2 = bigint NOT NULL
T3 = nvarchar(64) NOT NULL
T4 = nvarchar(96) NOT NULL
T5 = nvarchar(max) NOT NULL
T6 = char(64) NOT NULL
T7 = nvarchar(16) NOT NULL
T8 = datetime2(7) NOT NULL
T9 = nvarchar(160) NOT NULL
T10 = nvarchar(32) NOT NULL
T11 = char(64) NULL
T12 = nvarchar(256) NOT NULL
T13 = nvarchar(128) NOT NULL
T14 = rowversion NOT NULL
T15 = decimal(21,8) NOT NULL
T16 = nvarchar(24) NOT NULL
T17 = uniqueidentifier NULL
T18 = bigint NULL
T19 = nvarchar(128) NULL
T20 = nvarchar(64) NULL
T21 = datetime2(7) NULL
```

```text
AdapterRegistration APPEND=True
COL S; RegistrationId:T0, Revision:T2, Owner:T3, Protocol:T4, Destination:T4, BodyJson:T5, BodyDigest:T6, State:T7, RecordedAt:T8
PK S,RegistrationId,Revision
UQ S,Owner,Protocol,Revision
UQ S,RegistrationId,Revision,BodyDigest
CHECK State IN ('READY','UNPROVEN','WITHDRAWN')
INDEX IX_AdapterRegistration_State(S,Owner,Protocol,State) UNIQUE=False

Approval APPEND=True
COL S; DecisionId:T0, DecisionVersion:T2, ApproverId:T9, AuthorityDigest:T6, ApprovedAt:T8, StreamId:T0, PacketId:T0, PacketVersion:T2, ApprovalCommandId:T0, ApprovalCommandKind:T10, AuthorityOwner:T3, AuthorityId:T9, AuthorityVersion:T2, AuthorityRefDigest:T6, ApprovalRefOwner:T3, ApprovalRefId:T9, ApprovalRefVersion:T2, ApprovalRefDigest:T6
PK S,StreamId,DecisionId,DecisionVersion
UQ S,ApprovalRefOwner,ApprovalRefId,ApprovalRefVersion,ApprovalRefDigest
FK FK_Approval_Decision S,StreamId,DecisionId,DecisionVersion -> Decision(S,StreamId,DecisionId,Version) DELETE NO ACTION
FK FK_R4_Approval_Packet S,PacketId,PacketVersion -> ApprovalPacket(S,PacketId,Version) DELETE NO ACTION
FK FK_R4_Approval_Command S,ApprovalCommandId -> Command(S,CommandId) DELETE NO ACTION
FK FK_R4_Approval_DecisionPacket S,StreamId,DecisionId,DecisionVersion,PacketId,PacketVersion -> Decision(S,StreamId,DecisionId,Version,PacketId,PacketVersion) DELETE NO ACTION
FK FK_R4_Approval_CommandActor S,ApprovalCommandId,ApprovalCommandKind,ApproverId -> Command(S,CommandId,Kind,ActorId) DELETE NO ACTION
FK FK_R4_Approval_AuthorityEvidence S,AuthorityOwner,AuthorityId,AuthorityVersion,AuthorityRefDigest -> AuthorityEvidence(S,AuthorityOwner,AuthorityId,AuthorityVersion,AuthorityDigest) DELETE NO ACTION
FK FK_R4_Approval_ApprovalEvidence S,ApprovalRefOwner,ApprovalRefId,ApprovalRefVersion,ApprovalRefDigest,ApproverId,StreamId,DecisionId,DecisionVersion,PacketId,PacketVersion,AuthorityOwner,AuthorityId,AuthorityVersion,AuthorityRefDigest -> ApprovalEvidence(S,ApprovalOwner,ApprovalId,ApprovalVersion,ApprovalDigest,ApproverId,StreamId,DecisionId,DecisionVersion,PacketId,PacketVersion,AuthorityOwner,AuthorityId,AuthorityVersion,AuthorityDigest) DELETE NO ACTION
CHECK ApprovalCommandKind = 'I18'
CHECK AuthorityDigest = AuthorityRefDigest
INDEX IX_Approval_Approval_Decision(S,StreamId,DecisionId,DecisionVersion) UNIQUE=False

ApprovalPacket APPEND=True
COL S; PacketId:T0, Version:T2, TaskId:T0, TaskRevision:T2, ResultSetId:T0, ResultSetRevision:T2, ContentDigest:T6, State:T7, PreparedAt:T8
PK S,PacketId,Version
UQ S,PacketId,Version,TaskId,TaskRevision,ResultSetId,ResultSetRevision
FK FK_Packet_TaskRevision S,TaskId,TaskRevision -> TaskRevision(S,TaskId,TaskRevision) DELETE NO ACTION
FK FK_Packet_ResultSet S,ResultSetId,ResultSetRevision -> ResultSet(S,ResultSetId,Revision) DELETE NO ACTION
INDEX IX_ApprovalPacket_Packet_TaskRevision(S,TaskId,TaskRevision) UNIQUE=False
INDEX IX_ApprovalPacket_Packet_ResultSet(S,ResultSetId,ResultSetRevision) UNIQUE=False

AuditEvent APPEND=True
COL S; AuditId:T0, Sequence:T2, CommandId:T0, ActorId:T9, Action:T3, SubjectDigest:T6, BeforeDigest:T11, AfterDigest:T6, OccurredAt:T8
PK S,AuditId
UQ S,Sequence
UQ S,CommandId
FK FK_Audit_Command S,CommandId -> Command(S,CommandId) DELETE NO ACTION
INDEX IX_Audit_Command(S,CommandId,Sequence) UNIQUE=False
INDEX IX_AuditEvent_Audit_Command(S,CommandId) UNIQUE=False

Command APPEND=True
COL S; CommandId:T0, RequestKey:T12, Kind:T10, RequestCanonicalUtf8:T5, InputDigest:T6, TerminalBody:T5, TerminalBodyDigest:T6, TerminalState:T7, RecordedAt:T8, RequestType:T13, TerminalType:T13, ActorId:T9
PK S,CommandId
UQ S,Kind,RequestKey
UQ S,CommandId,Kind,ActorId
CHECK TerminalState IN ('COMMITTED','REJECTED','UNKNOWN')
CHECK DATALENGTH(RequestCanonicalUtf8) > 0
CHECK DATALENGTH(TerminalBody) > 0
INDEX IX_Command_RequestReplay(S,Kind,RequestKey) UNIQUE=True INCLUDE=InputDigest,RequestCanonicalUtf8,TerminalType,TerminalBodyDigest

Decision APPEND=True
COL S; StreamId:T0, DecisionId:T0, Version:T2, Sequence:T2, TaskId:T0, TaskRevision:T2, ResultSetId:T0, ResultSetRevision:T2, SourceOwnerRevision:T2, BodyJson:T5, BodyDigest:T6, RecordedAt:T8, PacketId:T0, PacketVersion:T2
PK S,StreamId,DecisionId,Version
UQ S,StreamId,Sequence
UQ S,StreamId,DecisionId,Version,TaskId,TaskRevision,ResultSetId,ResultSetRevision
UQ S,StreamId,DecisionId,Version,BodyDigest
UQ S,StreamId,DecisionId,Version,PacketId,PacketVersion
FK FK_Decision_Stream S,StreamId -> DecisionStream(S,StreamId) DELETE NO ACTION
FK FK_Decision_TaskRevision S,TaskId,TaskRevision -> TaskRevision(S,TaskId,TaskRevision) DELETE NO ACTION
FK FK_Decision_ResultSet S,ResultSetId,ResultSetRevision -> ResultSet(S,ResultSetId,Revision) DELETE NO ACTION
FK FK_Decision_Packet S,PacketId,PacketVersion -> ApprovalPacket(S,PacketId,Version) DELETE NO ACTION
FK FK_R3_Decision_SameTaskResult S,ResultSetId,ResultSetRevision,TaskId,TaskRevision -> ResultSet(S,ResultSetId,Revision,TaskId,TaskRevision) DELETE NO ACTION
FK FK_R3_Decision_SamePacketTaskResult S,PacketId,PacketVersion,TaskId,TaskRevision,ResultSetId,ResultSetRevision -> ApprovalPacket(S,PacketId,Version,TaskId,TaskRevision,ResultSetId,ResultSetRevision) DELETE NO ACTION
CHECK Sequence > 0
INDEX IX_Decision_Decision_Stream(S,StreamId) UNIQUE=False
INDEX IX_Decision_Decision_TaskRevision(S,TaskId,TaskRevision) UNIQUE=False
INDEX IX_Decision_Decision_ResultSet(S,ResultSetId,ResultSetRevision) UNIQUE=False
INDEX IX_Decision_Decision_Packet(S,PacketId,PacketVersion) UNIQUE=False

DecisionHead APPEND=False
COL S; StreamId:T0, DecisionId:T0, DecisionVersion:T2, Sequence:T2, RowVersion:T14
PK S,StreamId
UQ S,StreamId,Sequence
FK FK_DecisionHead_Decision S,StreamId,DecisionId,DecisionVersion -> Decision(S,StreamId,DecisionId,Version) DELETE NO ACTION
INDEX IX_DecisionHead_DecisionHead_Decision(S,StreamId,DecisionId,DecisionVersion) UNIQUE=False

DecisionPartitionRange APPEND=True
COL S; DecisionId:T0, DecisionVersion:T2, SegmentId:T0, OutcomeId:T0, FromQty:T15, ToQty:T15, Quality:T7, ProjectionKind:T7, StreamId:T0, ResultSetId:T0, ResultSetRevision:T2
PK S,StreamId,DecisionId,DecisionVersion,SegmentId
FK FK_DecisionRange_Decision S,StreamId,DecisionId,DecisionVersion -> Decision(S,StreamId,DecisionId,Version) DELETE NO ACTION
FK FK_DecisionRange_ResultSet S,ResultSetId,ResultSetRevision -> ResultSet(S,ResultSetId,Revision) DELETE NO ACTION
FK FK_DecisionRange_Outcome S,OutcomeId -> EvaluatedRangeOutcome(S,OutcomeId) DELETE NO ACTION
FK FK_R3_DecisionRange_SameResultOutcome S,ResultSetId,ResultSetRevision,OutcomeId -> EvaluatedRangeOutcome(S,ResultSetId,ResultSetRevision,OutcomeId) DELETE NO ACTION
FK FK_R4_Partition_ExactOutcomeSemantic S,ResultSetId,ResultSetRevision,OutcomeId,FromQty,ToQty,Quality -> EvaluatedRangeOutcome(S,ResultSetId,ResultSetRevision,OutcomeId,FromQty,ToQty,Quality) DELETE NO ACTION
CHECK FromQty >= 0 AND ToQty > FromQty
CHECK (Quality='ACCEPTED' AND ProjectionKind='ACCEPTED') OR (Quality='REJECTED' AND ProjectionKind='REJECTED') OR (Quality='HOLD' AND ProjectionKind IN ('HELD','REWORK')) OR (Quality='PENDING' AND ProjectionKind='UNEXAMINED')
INDEX IX_DecisionPartitionRange_DecisionRange_Decision(S,StreamId,DecisionId,DecisionVersion) UNIQUE=False
INDEX IX_DecisionPartitionRange_DecisionRange_ResultSet(S,ResultSetId,ResultSetRevision) UNIQUE=False
INDEX IX_DecisionPartitionRange_DecisionRange_Outcome(S,OutcomeId) UNIQUE=False

DecisionStream APPEND=False
COL S; StreamId:T0, FamilyId:T0, Stage:T16, ScopeVersion:T2, StreamKeyDigest:T6
PK S,StreamId
UQ S,StreamKeyDigest
FK FK_DecisionStream_Family S,FamilyId -> FamilyControlHead(S,FamilyId) DELETE NO ACTION
INDEX IX_DecisionStream_DecisionStream_Family(S,FamilyId) UNIQUE=False

Delivery APPEND=True
COL S; DeliveryId:T0, Destination:T3, Protocol:T3, CanonicalKey:T12, Sequence:T2, MessageId:T0, PayloadId:T0, PayloadType:T3, PayloadDigest:T6, SourceRefDigest:T6, DecisionRefDigest:T11, RegistrationRefDigest:T6, FrozenState:T7, CreatedAt:T8, DecisionStreamId:T17, DecisionId:T17, DecisionVersion:T18, SourceInternalId:T0, SourceOwnerRevision:T2, RegistrationId:T0, RegistrationRevision:T2, PreviousDeliveryId:T17
PK S,DeliveryId
UQ S,Destination,Protocol,CanonicalKey,Sequence
UQ S,MessageId
UQ S,DeliveryId,MessageId,PayloadId,PayloadDigest
FK FK_Delivery_Payload S,PayloadId -> DeliveryPayload(S,PayloadId) DELETE NO ACTION
FK FK_Delivery_Registration S,RegistrationId,RegistrationRevision -> AdapterRegistration(S,RegistrationId,Revision) DELETE NO ACTION
FK FK_Delivery_SourceVersion S,SourceInternalId,SourceOwnerRevision -> SourceOriginalVersion(S,SourceInternalId,OwnerRevision) DELETE NO ACTION
FK FK_Delivery_Decision S,DecisionStreamId,DecisionId,DecisionVersion -> Decision(S,StreamId,DecisionId,Version) DELETE NO ACTION
FK FK_Delivery_Previous S,PreviousDeliveryId -> Delivery(S,DeliveryId) DELETE NO ACTION
FK FK_R3_Delivery_ExactSourceDigest S,SourceInternalId,SourceOwnerRevision,SourceRefDigest -> SourceOriginalVersion(S,SourceInternalId,OwnerRevision,OriginalDigest) DELETE NO ACTION
FK FK_R3_Delivery_ExactPayload S,PayloadId,PayloadType,PayloadDigest,RegistrationId,RegistrationRevision -> DeliveryPayload(S,PayloadId,PayloadType,PayloadDigest,RegistrationId,RegistrationRevision) DELETE NO ACTION
FK FK_R3_Delivery_ExactDecisionDigest S,DecisionStreamId,DecisionId,DecisionVersion,DecisionRefDigest -> Decision(S,StreamId,DecisionId,Version,BodyDigest) DELETE NO ACTION
FK FK_R3_Delivery_ExactRegistrationDigest S,RegistrationId,RegistrationRevision,RegistrationRefDigest -> AdapterRegistration(S,RegistrationId,Revision,BodyDigest) DELETE NO ACTION
CHECK FrozenState IN ('PENDING','APPLIED','REJECTED','UNKNOWN')
CHECK Sequence > 0
CHECK (DecisionId IS NULL AND DecisionVersion IS NULL AND DecisionRefDigest IS NULL AND DecisionStreamId IS NULL) OR (DecisionId IS NOT NULL AND DecisionVersion IS NOT NULL AND DecisionRefDigest IS NOT NULL AND DecisionStreamId IS NOT NULL)
INDEX IX_Delivery_CanonicalSequence(S,Destination,Protocol,CanonicalKey,Sequence) UNIQUE=True
INDEX IX_Delivery_Decision(S,DecisionStreamId,DecisionId,DecisionVersion) UNIQUE=False
INDEX IX_Delivery_Delivery_Payload(S,PayloadId) UNIQUE=False
INDEX IX_Delivery_Delivery_Registration(S,RegistrationId,RegistrationRevision) UNIQUE=False
INDEX IX_Delivery_Delivery_SourceVersion(S,SourceInternalId,SourceOwnerRevision) UNIQUE=False
INDEX IX_Delivery_Delivery_Previous(S,PreviousDeliveryId) UNIQUE=False

DeliveryAttempt APPEND=True
COL S; DeliveryId:T0, AttemptId:T0, LeaseEpoch:T2, State:T7, AttemptedAt:T8
PK S,DeliveryId,AttemptId
FK FK_DeliveryAttempt_Delivery S,DeliveryId -> Delivery(S,DeliveryId) DELETE NO ACTION
INDEX IX_DeliveryAttempt_DeliveryAttempt_Delivery(S,DeliveryId) UNIQUE=False

DeliveryLease APPEND=False
COL S; DeliveryId:T0, WorkerId:T9, LeaseEpoch:T2, LeaseUntil:T8, RowVersion:T14
PK S,DeliveryId
FK FK_DeliveryLease_Delivery S,DeliveryId -> Delivery(S,DeliveryId) DELETE NO ACTION
CHECK LeaseEpoch >= 0
INDEX IX_DeliveryLease_DeliveryLease_Delivery(S,DeliveryId) UNIQUE=False
```

```text
DeliveryObservationHead APPEND=False
COL S; DeliveryId:T0, ObservationRevision:T2, State:T7, RowVersion:T14
PK S,DeliveryId
FK FK_DeliveryObservationHead_Revision S,DeliveryId,ObservationRevision -> DeliveryObservationRevision(S,DeliveryId,ObservationRevision) DELETE NO ACTION
INDEX IX_DeliveryObservationHead_DeliveryObservationHead_Revision(S,DeliveryId,ObservationRevision) UNIQUE=False

DeliveryObservationRevision APPEND=True
COL S; DeliveryId:T0, ObservationRevision:T2, State:T7, OwnerResultDigest:T11, ObservedAt:T8
PK S,DeliveryId,ObservationRevision
FK FK_DeliveryObservation_Delivery S,DeliveryId -> Delivery(S,DeliveryId) DELETE NO ACTION
INDEX IX_DeliveryObservationRevision_DeliveryObservation_Delivery(S,DeliveryId) UNIQUE=False

DeliveryPayload APPEND=True
COL S; PayloadId:T0, PayloadType:T3, PayloadBody:T5, PayloadDigest:T6, CreatedAt:T8, SchemaType:T13, RegistrationId:T0, RegistrationRevision:T2
PK S,PayloadId
UQ S,PayloadDigest
UQ S,PayloadId,PayloadType,PayloadDigest,RegistrationId,RegistrationRevision
FK FK_Payload_Registration S,RegistrationId,RegistrationRevision -> AdapterRegistration(S,RegistrationId,Revision) DELETE NO ACTION
CHECK PayloadType IN ('MES_OUTPUT_QUALITY_TASK_RESULT_V1','MES_OUTPUT_QUALITY_DISPOSITION_V1','MES_EXEC_QUALITY_TASK_RESULT_PROPOSED_V1','MES_EXEC_QUALITY_DISPOSITION_PROPOSED_V1','PLM_RAW_EVIDENCE_HANDOFF_V1')
INDEX IX_DeliveryPayload_Payload_Registration(S,RegistrationId,RegistrationRevision) UNIQUE=False

DependencyWake APPEND=True
COL S; WakeId:T0, SubjectDigest:T6, Reason:T3, State:T7, CreatedAt:T8
PK S,WakeId

DispositionDraft APPEND=True
COL S; DraftId:T0, Revision:T2, TaskId:T0, ResultSetId:T0, ResultSetRevision:T2, BodyDigest:T6, RecordedAt:T8
PK S,DraftId,Revision
FK FK_Draft_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
FK FK_Draft_ResultSet S,ResultSetId,ResultSetRevision -> ResultSet(S,ResultSetId,Revision) DELETE NO ACTION
INDEX IX_DispositionDraft_Draft_Task(S,TaskId) UNIQUE=False
INDEX IX_DispositionDraft_Draft_ResultSet(S,ResultSetId,ResultSetRevision) UNIQUE=False

EligibilityCut APPEND=True
COL S; CutId:T0, CutVersion:T2, TaskId:T0, BodyJson:T5, BodyDigest:T6, ObservedAt:T8
PK S,CutId,CutVersion
FK FK_EligibilityCut_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
INDEX IX_EligibilityCut_EligibilityCut_Task(S,TaskId) UNIQUE=False

EligibilityCutFact APPEND=True
COL S; CutId:T0, CutVersion:T2, FactOrdinal:T2, Kind:T10, FactDigest:T6, ControlHeadDigest:T6, AuthorityDigest:T6
PK S,CutId,CutVersion,FactOrdinal
FK FK_EligibilityFact_Cut S,CutId,CutVersion -> EligibilityCut(S,CutId,CutVersion) DELETE NO ACTION
INDEX IX_EligibilityCutFact_EligibilityFact_Cut(S,CutId,CutVersion) UNIQUE=False

EvaluatedRangeOutcome APPEND=True
COL S; OutcomeId:T0, ResultSetId:T0, ResultSetRevision:T2, FromQty:T15, ToQty:T15, Quality:T7, BodyDigest:T6
PK S,OutcomeId
UQ S,ResultSetId,ResultSetRevision,OutcomeId
UQ S,ResultSetId,ResultSetRevision,OutcomeId,BodyDigest
UQ S,ResultSetId,ResultSetRevision,OutcomeId,FromQty,ToQty,Quality
FK FK_EvaluatedRangeOutcome_ResultSet S,ResultSetId,ResultSetRevision -> ResultSet(S,ResultSetId,Revision) DELETE NO ACTION
CHECK FromQty >= 0 AND ToQty > FromQty
CHECK Quality IN ('ACCEPTED','REJECTED','HOLD','PENDING')
INDEX IX_EvaluatedRangeOutcome_EvaluatedRangeOutcome_ResultSet(S,ResultSetId,ResultSetRevision) UNIQUE=False

ExportReceipt APPEND=True
COL S; ExportId:T0, SubjectOwner:T3, SubjectId:T9, SubjectVersion:T2, SubjectDigest:T6, Format:T1, State:T7, BodyDigest:T6, CreatedAt:T8
PK S,ExportId
CHECK Format IN ('JSON','CSV','PDF')
CHECK State IN ('READY','PENDING','REJECTED')
INDEX IX_Export_Subject(S,SubjectOwner,SubjectId,SubjectVersion) UNIQUE=False

FamilyControlHead APPEND=False
COL S; FamilyId:T0, ControlRevision:T2, State:T16, RowVersion:T14
PK S,FamilyId
FK FK_FamilyControlHead_Version S,FamilyId,ControlRevision -> FamilyControlVersion(S,FamilyId,ControlRevision) DELETE NO ACTION
INDEX IX_FamilyControlHead_State(S,State,FamilyId) UNIQUE=False
INDEX IX_FamilyControlHead_FamilyControlHead_Version(S,FamilyId,ControlRevision) UNIQUE=False

FamilyControlVersion APPEND=True
COL S; FamilyId:T0, ControlRevision:T2, State:T16, BodyJson:T5, BodyDigest:T6, RecordedAt:T8, DomainKeyDigest:T6, Stage:T16, PreviousControlRevision:T18, AuthorityOwner:T3, AuthorityId:T9, AuthorityVersion:T2, AuthorityDigest:T6
PK S,FamilyId,ControlRevision
UQ S,FamilyId,ControlRevision,BodyDigest
FK FK_FamilyControl_DirectoryHead S,DomainKeyDigest,Stage -> FamilyDirectoryHead(S,DomainKeyDigest,Stage) DELETE NO ACTION
CHECK State IN ('REGISTERED_NO_TASK','OPEN','DISPOSED','WITHDRAWN')
INDEX IX_FamilyControlVersion_FamilyControl_DirectoryHead(S,DomainKeyDigest,Stage) UNIQUE=False

FamilyCutEvidence APPEND=True
COL S; CutId:T0, CutVersion:T2, DirectoryRevision:T2, Coverage:T7, BodyJson:T5, BodyDigest:T6, ObservedAt:T8, DomainKeyDigest:T6, Stage:T16
PK S,CutId,CutVersion
FK FK_FamilyCut_DirectoryVersion S,DomainKeyDigest,Stage,DirectoryRevision -> FamilyDirectoryVersion(S,DomainKeyDigest,Stage,DirectoryRevision) DELETE NO ACTION
INDEX IX_FamilyCutEvidence_FamilyCut_DirectoryVersion(S,DomainKeyDigest,Stage,DirectoryRevision) UNIQUE=False

FamilyDirectoryHead APPEND=False
COL S; DomainKeyDigest:T6, Stage:T16, DirectoryRevision:T2, RowVersion:T14
PK S,DomainKeyDigest,Stage
FK FK_FamilyDirectoryHead_Version S,DomainKeyDigest,Stage,DirectoryRevision -> FamilyDirectoryVersion(S,DomainKeyDigest,Stage,DirectoryRevision) DELETE NO ACTION
INDEX IX_FamilyDirectoryHead_CurrentRevision(S,DomainKeyDigest,Stage,DirectoryRevision) UNIQUE=True

FamilyDirectoryMember APPEND=True
COL S; DomainKeyDigest:T6, Stage:T16, FamilyId:T0, AddedRevision:T2, DirectoryRevision:T2
PK S,DomainKeyDigest,Stage,DirectoryRevision,FamilyId
FK FK_FamilyDirectoryMember_Version S,DomainKeyDigest,Stage,DirectoryRevision -> FamilyDirectoryVersion(S,DomainKeyDigest,Stage,DirectoryRevision) DELETE NO ACTION
INDEX IX_FamilyDirectoryMember_Family(S,FamilyId,DirectoryRevision) UNIQUE=False
INDEX IX_FamilyDirectoryMember_FamilyDirectoryMember_Version(S,DomainKeyDigest,Stage,DirectoryRevision) UNIQUE=False

FamilyDirectoryVersion APPEND=True
COL S; DomainKeyDigest:T6, Stage:T16, DirectoryRevision:T2, BodyJson:T5, BodyDigest:T6, MembershipDigest:T6, Coverage:T7, RecordedAt:T8
PK S,DomainKeyDigest,Stage,DirectoryRevision
CHECK Stage IN ('IPQC','FQC','TRIAL_INSPECTION')
CHECK Coverage IN ('COMPLETE','PARTIAL','UNKNOWN','CONFLICT')
INDEX IX_FamilyDirectoryVersion_Digest(S,BodyDigest) UNIQUE=False

FamilyObligationSegment APPEND=True
COL S; FamilyId:T0, ObligationId:T0, SegmentId:T0, FromQty:T15, ToQty:T15, State:T7, ControlRevision:T2
PK S,FamilyId,ControlRevision,ObligationId,SegmentId
FK FK_FamilyObligation_ControlVersion S,FamilyId,ControlRevision -> FamilyControlVersion(S,FamilyId,ControlRevision) DELETE NO ACTION
CHECK FromQty >= 0 AND ToQty > FromQty
CHECK State IN ('OPEN','CLOSED','TRANSFERRED')
INDEX IX_FamilyObligationSegment_FamilyObligation_ControlVersion(S,FamilyId,ControlRevision) UNIQUE=False

ImpactCase APPEND=True
COL S; ImpactId:T0, Revision:T2, TriggerDigest:T6, ScopeDigest:T6, State:T7, CreatedAt:T8
PK S,ImpactId,Revision

Inbox APPEND=True
COL S; Owner:T3, CanonicalKey:T12, Sequence:T2, MessageId:T0, OriginalDigest:T6, State:T7, ReceivedAt:T8
PK S,Owner,CanonicalKey,Sequence
UQ S,Owner,MessageId

InitialClaim APPEND=True
COL S; TaskId:T0, SlotId:T0, ObservationId:T0, ClaimedAt:T8
PK S,TaskId,SlotId
FK FK_InitialClaim_Slot S,TaskId,SlotId -> MeasurementSlot(S,TaskId,SlotId) DELETE NO ACTION
FK FK_InitialClaim_ObservationHead S,ObservationId -> ObservationHead(S,ObservationId) DELETE NO ACTION
INDEX IX_InitialClaim_InitialClaim_Slot(S,TaskId,SlotId) UNIQUE=False
INDEX IX_InitialClaim_InitialClaim_ObservationHead(S,ObservationId) UNIQUE=False

InspectionTask APPEND=False
COL S; TaskId:T0, TaskKeyDigest:T6, Stage:T16, SourceInternalId:T0, PolicyVersionDigest:T6, CreatedAt:T8
PK S,TaskId
UQ S,TaskKeyDigest
FK FK_Task_SourceHead S,SourceInternalId -> SourceHead(S,SourceInternalId) DELETE NO ACTION
INDEX IX_InspectionTask_Task_SourceHead(S,SourceInternalId) UNIQUE=False

LegacyMapping APPEND=True
COL S; LegacyOwner:T3, LegacyId:T9, MappingDigest:T6, WriterEpoch:T2, State:T7, WriterName:T4
PK S,LegacyOwner,LegacyId
FK FK_LegacyMapping_WriterEpoch S,WriterName,WriterEpoch -> WriterEpochHistory(S,WriterName,Epoch) DELETE NO ACTION
INDEX IX_LegacyMapping_LegacyMapping_WriterEpoch(S,WriterName,WriterEpoch) UNIQUE=False

MeasurementSlot APPEND=True
COL S; TaskId:T0, SlotId:T0, RequirementId:T9, MemberId:T9, EpisodeId:T9, ReplicateOrdinal:T2, MethodDigest:T6
PK S,TaskId,SlotId
UQ S,TaskId,RequirementId,MemberId,EpisodeId,ReplicateOrdinal,MethodDigest
FK FK_MeasurementSlot_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
FK FK_MeasurementSlot_Member S,TaskId,MemberId -> PopulationMember(S,TaskId,MemberId) DELETE NO ACTION
CHECK ReplicateOrdinal > 0
INDEX IX_MeasurementSlot_MeasurementSlot_Task(S,TaskId) UNIQUE=False
INDEX IX_MeasurementSlot_MeasurementSlot_Member(S,TaskId,MemberId) UNIQUE=False

Observation APPEND=True
COL S; ObservationId:T0, Revision:T2, TaskId:T0, SlotId:T0, Kind:T7, ValueText:T19, Code:T20, BodyJson:T5, BodyDigest:T6, ObservedAt:T8, EligibilityCutId:T0, EligibilityCutVersion:T2, InspectorId:T9, SourceCommandId:T0
PK S,ObservationId,Revision
FK FK_Observation_Slot S,TaskId,SlotId -> MeasurementSlot(S,TaskId,SlotId) DELETE NO ACTION
FK FK_Observation_EligibilityCut S,EligibilityCutId,EligibilityCutVersion -> EligibilityCut(S,CutId,CutVersion) DELETE NO ACTION
FK FK_R4_Observation_SourceCommand S,SourceCommandId -> Command(S,CommandId) DELETE NO ACTION
CHECK (ValueText IS NULL AND Code IS NOT NULL) OR (ValueText IS NOT NULL AND Code IS NULL)
CHECK Kind IN ('INITIAL','CORRECTION','RETEST')
INDEX IX_Observation_Observation_Slot(S,TaskId,SlotId) UNIQUE=False
INDEX IX_Observation_Observation_EligibilityCut(S,EligibilityCutId,EligibilityCutVersion) UNIQUE=False

ObservationHead APPEND=False
COL S; ObservationId:T0, Revision:T2, TaskId:T0, SlotId:T0, RowVersion:T14
PK S,ObservationId
FK FK_ObservationHead_Revision S,ObservationId,Revision -> Observation(S,ObservationId,Revision) DELETE NO ACTION
FK FK_ObservationHead_Slot S,TaskId,SlotId -> MeasurementSlot(S,TaskId,SlotId) DELETE NO ACTION
INDEX IX_ObservationHead_ObservationHead_Revision(S,ObservationId,Revision) UNIQUE=False
INDEX IX_ObservationHead_ObservationHead_Slot(S,TaskId,SlotId) UNIQUE=False

ObservationReview APPEND=True
COL S; ReviewId:T0, Revision:T2, TaskId:T0, ReviewerId:T9, AuthorityDigest:T6, BodyDigest:T6, ReviewedAt:T8, TaskRevision:T2, ReviewCommandId:T0
PK S,ReviewId,Revision
FK FK_Review_TaskRevision S,TaskId,TaskRevision -> TaskRevision(S,TaskId,TaskRevision) DELETE NO ACTION
FK FK_R4_Review_Command S,ReviewCommandId -> Command(S,CommandId) DELETE NO ACTION
INDEX IX_ObservationReview_Review_TaskRevision(S,TaskId,TaskRevision) UNIQUE=False
```

```text
ObservationReviewItem APPEND=True
COL S; ReviewId:T0, Revision:T2, ObservationId:T0, ObservationRevision:T2, Action:T7, TaskId:T0, InspectorId:T9, ReviewerId:T9, ObservationCommandId:T0, ReviewCommandId:T0
PK S,ReviewId,Revision,ObservationId
FK FK_ReviewItem_Review S,ReviewId,Revision -> ObservationReview(S,ReviewId,Revision) DELETE NO ACTION
FK FK_ReviewItem_Observation S,ObservationId,ObservationRevision -> Observation(S,ObservationId,Revision) DELETE NO ACTION
FK FK_ReviewItem_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
FK FK_R4_ReviewItem_ObservationCommand S,ObservationCommandId -> Command(S,CommandId) DELETE NO ACTION
FK FK_R4_ReviewItem_ReviewCommand S,ReviewCommandId -> Command(S,CommandId) DELETE NO ACTION
CHECK InspectorId <> ReviewerId
INDEX IX_ObservationReviewItem_ReviewItem_Review(S,ReviewId,Revision) UNIQUE=False
INDEX IX_ObservationReviewItem_ReviewItem_Observation(S,ObservationId,ObservationRevision) UNIQUE=False
INDEX IX_ObservationReviewItem_ReviewItem_Task(S,TaskId) UNIQUE=False

ObservationRuling APPEND=True
COL S; TaskId:T0, RulingId:T0, ObservationId:T0, ObservationRevision:T2, AuthorityDigest:T6, RecordedAt:T8
PK S,TaskId,RulingId
FK FK_Ruling_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
FK FK_Ruling_Observation S,ObservationId,ObservationRevision -> Observation(S,ObservationId,Revision) DELETE NO ACTION
INDEX IX_ObservationRuling_Ruling_Task(S,TaskId) UNIQUE=False
INDEX IX_ObservationRuling_Ruling_Observation(S,ObservationId,ObservationRevision) UNIQUE=False

Outbox APPEND=True
COL S; MessageId:T0, DeliveryId:T0, PayloadId:T0, PayloadDigest:T6, State:T7, LeaseEpoch:T18, NextAttemptAt:T8
PK S,MessageId
FK FK_Outbox_Delivery S,DeliveryId -> Delivery(S,DeliveryId) DELETE NO ACTION
FK FK_Outbox_Payload S,PayloadId -> DeliveryPayload(S,PayloadId) DELETE NO ACTION
FK FK_R3_Outbox_SameDeliveryPayload S,DeliveryId,MessageId,PayloadId,PayloadDigest -> Delivery(S,DeliveryId,MessageId,PayloadId,PayloadDigest) DELETE NO ACTION
CHECK State IN ('PENDING','LEASED','SENT','UNKNOWN','APPLIED','REJECTED')
CHECK LeaseEpoch >= 0
INDEX IX_Outbox_Dispatch(S,State,NextAttemptAt,MessageId) UNIQUE=False INCLUDE=DeliveryId,PayloadId,LeaseEpoch
INDEX IX_Outbox_Outbox_Delivery(S,DeliveryId) UNIQUE=False
INDEX IX_Outbox_Outbox_Payload(S,PayloadId) UNIQUE=False

OwnerObservationHead APPEND=False
COL S; Owner:T3, CanonicalKey:T12, Sequence:T2, ObservationRevision:T2, State:T16, RowVersion:T14
PK S,Owner,CanonicalKey,Sequence
FK FK_OwnerObservationHead_Revision S,Owner,CanonicalKey,Sequence,ObservationRevision -> OwnerObservationRevision(S,Owner,CanonicalKey,Sequence,ObservationRevision) DELETE NO ACTION
INDEX IX_OwnerObservationHead_State(S,Owner,State,CanonicalKey) UNIQUE=False
INDEX IX_OwnerObservationHead_OwnerObservationHead_Revision(S,Owner,CanonicalKey,Sequence,ObservationRevision) UNIQUE=False

OwnerObservationRevision APPEND=True
COL S; Owner:T3, CanonicalKey:T12, Sequence:T2, ObservationRevision:T2, MessageId:T0, State:T16, OriginalDigest:T6, BodyJson:T5, BodyDigest:T6, ObservedAt:T8
PK S,Owner,CanonicalKey,Sequence,ObservationRevision
FK FK_OwnerObservation_Original S,Owner,CanonicalKey,Sequence -> OwnerResultOriginal(S,Owner,CanonicalKey,Sequence) DELETE NO ACTION
CHECK State IN ('APPLIED','REJECTED','UNKNOWN','UNAVAILABLE')
INDEX IX_OwnerObservation_Message(S,MessageId,ObservationRevision) UNIQUE=False
INDEX IX_OwnerObservationRevision_OwnerObservation_Original(S,Owner,CanonicalKey,Sequence) UNIQUE=False

OwnerResultOriginal APPEND=True
COL S; Owner:T3, CanonicalKey:T12, Sequence:T2, OriginalBody:T5, OriginalDigest:T6, RecordedAt:T8
PK S,Owner,CanonicalKey,Sequence
FK FK_OwnerResult_Inbox S,Owner,CanonicalKey,Sequence -> Inbox(S,Owner,CanonicalKey,Sequence) DELETE NO ACTION
INDEX IX_OwnerResultOriginal_OwnerResult_Inbox(S,Owner,CanonicalKey,Sequence) UNIQUE=False

PlmEvidenceHandoff APPEND=True
COL S; HandoffId:T0, Revision:T2, BodyJson:T5, BodyDigest:T6, State:T7, CreatedAt:T8
PK S,HandoffId,Revision

PopulationMember APPEND=True
COL S; TaskId:T0, MemberId:T9, FromQty:T15, ToQty:T15, EvidenceDigest:T6
PK S,TaskId,MemberId
FK FK_PopulationMember_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
INDEX IX_PopulationMember_PopulationMember_Task(S,TaskId) UNIQUE=False

QapConsumerResult APPEND=True
COL S; ConsumerOwner:T3, ConsumerResultId:T9, ConsumerResultVersion:T2, ResultBodyJson:T5, ResultBodyDigest:T6, RecordedAt:T8
PK S,ConsumerOwner,ConsumerResultId,ConsumerResultVersion
UQ S,ConsumerOwner,ConsumerResultId,ConsumerResultVersion,ResultBodyDigest

QapGuardEvidence APPEND=True
COL S; GuardId:T0, Revision:T2, RequestKey:T12, ResolutionOwner:T3, ResolutionId:T9, ResolutionVersion:T2, ResolutionDigest:T6, FamilyCutId:T0, FamilyCutVersion:T2, RegistrationId:T0, RegistrationRevision:T2, BodyJson:T5, BodyDigest:T6, Decision:T7, CheckedAt:T8
PK S,GuardId,Revision
UQ S,RequestKey
FK FK_QapGuard_FamilyCut S,FamilyCutId,FamilyCutVersion -> FamilyCutEvidence(S,CutId,CutVersion) DELETE NO ACTION
FK FK_QapGuard_Registration S,RegistrationId,RegistrationRevision -> AdapterRegistration(S,RegistrationId,Revision) DELETE NO ACTION
CHECK Decision IN ('ALLOWED','BLOCKED','UNKNOWN')
INDEX IX_QapGuard_Resolution(S,ResolutionOwner,ResolutionId,ResolutionVersion) UNIQUE=False
INDEX IX_QapGuardEvidence_QapGuard_FamilyCut(S,FamilyCutId,FamilyCutVersion) UNIQUE=False
INDEX IX_QapGuardEvidence_QapGuard_Registration(S,RegistrationId,RegistrationRevision) UNIQUE=False

QapUseFact APPEND=True
COL S; UseKey:T12, GuardId:T0, GuardRevision:T2, ResolutionOwner:T3, ResolutionId:T9, ResolutionVersion:T2, ResolutionDigest:T6, ConsumerReceiptOwner:T3, ConsumerReceiptId:T9, ConsumerReceiptVersion:T2, ConsumerResultDigest:T6, EffectBodyJson:T5, EffectBodyDigest:T6, FamilyCutId:T0, FamilyCutVersion:T2, CommittedAt:T8
PK S,UseKey
FK FK_QapUse_Guard S,GuardId,GuardRevision -> QapGuardEvidence(S,GuardId,Revision) DELETE NO ACTION
FK FK_QapUse_FamilyCut S,FamilyCutId,FamilyCutVersion -> FamilyCutEvidence(S,CutId,CutVersion) DELETE NO ACTION
FK FK_R3_QapUse_ConsumerResult S,ConsumerReceiptOwner,ConsumerReceiptId,ConsumerReceiptVersion,ConsumerResultDigest -> QapConsumerResult(S,ConsumerOwner,ConsumerResultId,ConsumerResultVersion,ResultBodyDigest) DELETE NO ACTION
INDEX IX_QapUse_Resolution(S,ResolutionOwner,ResolutionId,ResolutionVersion) UNIQUE=False
INDEX IX_QapUse_ConsumerReceipt(S,ConsumerReceiptOwner,ConsumerReceiptId,ConsumerReceiptVersion) UNIQUE=False
INDEX IX_QapUseFact_QapUse_Guard(S,GuardId,GuardRevision) UNIQUE=False
INDEX IX_QapUseFact_QapUse_FamilyCut(S,FamilyCutId,FamilyCutVersion) UNIQUE=False

RequirementOutcome APPEND=True
COL S; ResultSetId:T0, Revision:T2, RequirementId:T9, State:T7, CoverageDigest:T6
PK S,ResultSetId,Revision,RequirementId
FK FK_RequirementOutcome_ResultSet S,ResultSetId,Revision -> ResultSet(S,ResultSetId,Revision) DELETE NO ACTION
INDEX IX_RequirementOutcome_RequirementOutcome_ResultSet(S,ResultSetId,Revision) UNIQUE=False

ResultSet APPEND=True
COL S; ResultSetId:T0, Revision:T2, TaskId:T0, TaskRevision:T2, ObservationSetRevision:T2, BodyJson:T5, BodyDigest:T6, FrozenAt:T8
PK S,ResultSetId,Revision
UQ S,ResultSetId,Revision,TaskId,TaskRevision
UQ S,ResultSetId,Revision,TaskId
FK FK_ResultSet_TaskRevision S,TaskId,TaskRevision -> TaskRevision(S,TaskId,TaskRevision) DELETE NO ACTION
INDEX IX_ResultSet_ResultSet_TaskRevision(S,TaskId,TaskRevision) UNIQUE=False

ResultSetHead APPEND=False
COL S; TaskId:T0, ResultSetId:T0, ResultSetRevision:T2, RowVersion:T14
PK S,TaskId
FK FK_ResultSetHead_ResultSet S,ResultSetId,ResultSetRevision -> ResultSet(S,ResultSetId,Revision) DELETE NO ACTION
FK FK_ResultSetHead_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
INDEX IX_ResultSetHead_ResultSetHead_ResultSet(S,ResultSetId,ResultSetRevision) UNIQUE=False
INDEX IX_ResultSetHead_ResultSetHead_Task(S,TaskId) UNIQUE=False

ResultSetSlot APPEND=True
COL S; ResultSetId:T0, Revision:T2, SlotId:T0, OutcomeDigest:T6, TaskId:T0, OutcomeId:T0
PK S,ResultSetId,Revision,SlotId
FK FK_ResultSetSlot_ResultSet S,ResultSetId,Revision -> ResultSet(S,ResultSetId,Revision) DELETE NO ACTION
FK FK_ResultSetSlot_Slot S,TaskId,SlotId -> MeasurementSlot(S,TaskId,SlotId) DELETE NO ACTION
FK FK_R3_ResultSetSlot_SameTaskResult S,ResultSetId,Revision,TaskId -> ResultSet(S,ResultSetId,Revision,TaskId) DELETE NO ACTION
FK FK_R4_ResultSetSlot_ExactOutcome S,ResultSetId,Revision,OutcomeId,OutcomeDigest -> EvaluatedRangeOutcome(S,ResultSetId,ResultSetRevision,OutcomeId,BodyDigest) DELETE NO ACTION
INDEX IX_ResultSetSlot_ResultSetSlot_ResultSet(S,ResultSetId,Revision) UNIQUE=False
INDEX IX_ResultSetSlot_ResultSetSlot_Slot(S,TaskId,SlotId) UNIQUE=False

RetestEpisode APPEND=True
COL S; TaskId:T0, EpisodeId:T0, PacketDigest:T6, State:T7, ActivatedAt:T8
PK S,TaskId,EpisodeId
FK FK_Retest_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
INDEX IX_RetestEpisode_Retest_Task(S,TaskId) UNIQUE=False

ReviewHead APPEND=False
COL S; TaskId:T0, ReviewId:T0, ReviewRevision:T2, RowVersion:T14
PK S,TaskId
FK FK_ReviewHead_Review S,ReviewId,ReviewRevision -> ObservationReview(S,ReviewId,Revision) DELETE NO ACTION
FK FK_ReviewHead_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
INDEX IX_ReviewHead_ReviewHead_Review(S,ReviewId,ReviewRevision) UNIQUE=False
INDEX IX_ReviewHead_ReviewHead_Task(S,TaskId) UNIQUE=False

SampleSelectionRoster APPEND=True
COL S; TaskId:T0, RequirementId:T9, MemberId:T9, RankHash:T6, SelectionDigest:T6
PK S,TaskId,RequirementId,MemberId
FK FK_SampleSelectionRoster_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
INDEX IX_SampleSelectionRoster_SampleSelectionRoster_Task(S,TaskId) UNIQUE=False

SourceAlias APPEND=True
COL S; SourceInternalId:T0, AliasOwner:T3, AliasId:T9, AliasVersion:T2, AliasDigest:T6
PK S,SourceInternalId,AliasOwner,AliasId,AliasVersion
FK FK_SourceAlias_SourceHead S,SourceInternalId -> SourceHead(S,SourceInternalId) DELETE NO ACTION
INDEX IX_SourceAlias_SourceAlias_SourceHead(S,SourceInternalId) UNIQUE=False

SourceHead APPEND=False
COL S; SourceInternalId:T0, OwnerRevision:T2, MappingRevision:T2, State:T7, RowVersion:T14, SourceDigest:T6
PK S,SourceInternalId
FK FK_SourceHead_Original S,SourceInternalId,OwnerRevision -> SourceOriginalVersion(S,SourceInternalId,OwnerRevision) DELETE NO ACTION
FK FK_SourceHead_Mapping S,SourceInternalId,MappingRevision -> SourceMappingVersion(S,SourceInternalId,MappingRevision) DELETE NO ACTION
FK FK_R3_SourceHead_ExactDigest S,SourceInternalId,OwnerRevision,SourceDigest -> SourceOriginalVersion(S,SourceInternalId,OwnerRevision,OriginalDigest) DELETE NO ACTION
CHECK State IN ('CURRENT','BLOCKED','REASSESS_REQUIRED')
INDEX IX_SourceHead_State(S,State,SourceInternalId) UNIQUE=False
INDEX IX_SourceHead_SourceHead_Original(S,SourceInternalId,OwnerRevision) UNIQUE=False
INDEX IX_SourceHead_SourceHead_Mapping(S,SourceInternalId,MappingRevision) UNIQUE=False
```

```text
SourceMappingVersion APPEND=True
COL S; SourceInternalId:T0, MappingRevision:T2, CanonicalSourceDigest:T6, ScopeDigest:T6, RecordedAt:T8, OwnerRevision:T2
PK S,SourceInternalId,MappingRevision
FK FK_SourceMapping_SourceOriginal S,SourceInternalId,OwnerRevision -> SourceOriginalVersion(S,SourceInternalId,OwnerRevision) DELETE NO ACTION
INDEX IX_SourceMapping_OwnerRevision(S,SourceInternalId,OwnerRevision) UNIQUE=False

SourceOriginalVersion APPEND=True
COL S; SourceInternalId:T0, OwnerRevision:T2, Owner:T3, OwnerId:T9, OriginalBody:T5, OriginalDigest:T6, RecordedAt:T8
PK S,SourceInternalId,OwnerRevision
UQ S,Owner,OwnerId,OwnerRevision
UQ S,SourceInternalId,OwnerRevision,OriginalDigest

StockConsumerResult APPEND=True
COL S; ConsumerResultId:T0, ConsumerResultVersion:T2, DecisionStreamId:T0, DecisionId:T0, DecisionVersion:T2, DecisionDigest:T6, GuardId:T0, GuardRevision:T2, State:T7, ResultBodyJson:T5, ResultBodyDigest:T6, RecordedAt:T8
PK S,ConsumerResultId,ConsumerResultVersion
UQ S,ConsumerResultId,ConsumerResultVersion,ResultBodyDigest
FK FK_R3_StockResult_Decision S,DecisionStreamId,DecisionId,DecisionVersion,DecisionDigest -> Decision(S,StreamId,DecisionId,Version,BodyDigest) DELETE NO ACTION
FK FK_R3_StockResult_Guard S,GuardId,GuardRevision -> QapGuardEvidence(S,GuardId,Revision) DELETE NO ACTION
CHECK State IN ('APPLIED','BLOCKED','UNKNOWN')

StockEffect APPEND=True
COL S; EffectId:T0, ConsumerResultId:T0, ConsumerResultVersion:T2, ConsumerResultDigest:T6, FromQty:T15, ToQty:T15, EffectBodyJson:T5, EffectBodyDigest:T6, RecordedAt:T8
PK S,EffectId
FK FK_R3_StockEffect_ConsumerResult S,ConsumerResultId,ConsumerResultVersion,ConsumerResultDigest -> StockConsumerResult(S,ConsumerResultId,ConsumerResultVersion,ResultBodyDigest) DELETE NO ACTION
CHECK FromQty >= 0 AND ToQty > FromQty

TaskHead APPEND=False
COL S; TaskId:T0, TaskRevision:T2, State:T16, RowVersion:T14
PK S,TaskId
FK FK_TaskHead_Revision S,TaskId,TaskRevision -> TaskRevision(S,TaskId,TaskRevision) DELETE NO ACTION
INDEX IX_TaskHead_State(S,State,TaskId) UNIQUE=False
INDEX IX_TaskHead_TaskHead_Revision(S,TaskId,TaskRevision) UNIQUE=False

TaskRevision APPEND=True
COL S; TaskId:T0, TaskRevision:T2, State:T16, BodyJson:T5, BodyDigest:T6, RecordedAt:T8
PK S,TaskId,TaskRevision
FK FK_TaskRevision_Task S,TaskId -> InspectionTask(S,TaskId) DELETE NO ACTION
CHECK State IN ('OPEN','MEASURING','REVIEWING','RESULT_FROZEN','DISPOSED','REOPENED')
INDEX IX_TaskRevision_TaskRevision_Task(S,TaskId) UNIQUE=False

WriterEpochHead APPEND=False
COL S; WriterName:T4, Epoch:T2, RowVersion:T14
PK S,WriterName
FK FK_WriterEpochHead_History S,WriterName,Epoch -> WriterEpochHistory(S,WriterName,Epoch) DELETE NO ACTION
INDEX IX_WriterEpochHead_Epoch(S,WriterName,Epoch) UNIQUE=True

WriterEpochHistory APPEND=False
COL S; WriterName:T4, Epoch:T2, State:T7, PreviousEpoch:T18, AuthorityOwner:T3, AuthorityId:T9, AuthorityVersion:T2, AuthorityDigest:T6, ActivatedAt:T8
PK S,WriterName,Epoch
CHECK Epoch > 0
CHECK State IN ('PREPARED','ACTIVE','RETIRED')
INDEX IX_WriterEpochHistory_State(S,WriterName,State,Epoch) UNIQUE=False

ResultInvalidation APPEND=True
COL S; InvalidationId:T0, TaskId:T0, ResultSetId:T0, ResultSetRevision:T2, CauseRefDigest:T6, State:T7, InvalidatedAt:T8
PK S,InvalidationId
UQ S,TaskId,ResultSetId,ResultSetRevision,CauseRefDigest
FK FK_ResultInvalidation_ResultSet S,ResultSetId,ResultSetRevision -> ResultSet(S,ResultSetId,Revision) DELETE NO ACTION
CHECK State IN ('INVALIDATED')
INDEX IX_ResultInvalidation_Task(S,TaskId,InvalidatedAt) UNIQUE=False

AuthorityEvidence APPEND=True
COL S; AuthorityOwner:T3, AuthorityId:T9, AuthorityVersion:T2, AuthorityDigest:T6, ActorId:T9, Action:T3, Stage:T16, ScopeDigest:T6, ValidFrom:T8, ValidTo:T21, ControlOwner:T3, ControlId:T9, ControlVersion:T2, ControlDigest:T6, State:T7, BodyJson:T5, RecordedAt:T8
PK S,AuthorityOwner,AuthorityId,AuthorityVersion
UQ S,AuthorityOwner,AuthorityId,AuthorityVersion,AuthorityDigest
CHECK Action IN ('APPROVE_DISPOSITION','ORDINARY_STOCK_USE')
CHECK State = 'CURRENT'
INDEX IX_AuthorityEvidence_ActorAction(S,ActorId,Action,Stage,State) UNIQUE=False

ApprovalEvidence APPEND=True
COL S; ApprovalOwner:T3, ApprovalId:T9, ApprovalVersion:T2, ApprovalDigest:T6, ApproverId:T9, Action:T3, Stage:T16, StreamId:T0, DecisionId:T0, DecisionVersion:T2, PacketId:T0, PacketVersion:T2, SubjectDigest:T6, AuthorityOwner:T3, AuthorityId:T9, AuthorityVersion:T2, AuthorityDigest:T6, BodyJson:T5, RecordedAt:T8
PK S,ApprovalOwner,ApprovalId,ApprovalVersion
UQ S,ApprovalOwner,ApprovalId,ApprovalVersion,ApprovalDigest
UQ S,ApprovalOwner,ApprovalId,ApprovalVersion,ApprovalDigest,ApproverId,StreamId,DecisionId,DecisionVersion,PacketId,PacketVersion,AuthorityOwner,AuthorityId,AuthorityVersion,AuthorityDigest
FK FK_ApprovalEvidence_Packet S,PacketId,PacketVersion -> ApprovalPacket(S,PacketId,Version) DELETE NO ACTION
FK FK_ApprovalEvidence_DecisionPacket S,StreamId,DecisionId,DecisionVersion,PacketId,PacketVersion -> Decision(S,StreamId,DecisionId,Version,PacketId,PacketVersion) DELETE NO ACTION
FK FK_ApprovalEvidence_Authority S,AuthorityOwner,AuthorityId,AuthorityVersion,AuthorityDigest -> AuthorityEvidence(S,AuthorityOwner,AuthorityId,AuthorityVersion,AuthorityDigest) DELETE NO ACTION
CHECK Action = 'APPROVE_DISPOSITION'
```

阅读与逐声明核对见[专项证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/QA-IPQC-01-supplement-reading.json:1)。
