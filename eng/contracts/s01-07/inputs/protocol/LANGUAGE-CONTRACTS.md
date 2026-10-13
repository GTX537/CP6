# TypeScript、C#与数据库接口约束

CONSUMER-TYPES.ts.txt和CONSUMER-TYPES.cs.txt是由当前封闭JSON声明生成的可审阅语言设计，不是已编译SDK/运行parser。Owner/SourceQA/LegacyAdapter 的所有具名声明均已列出，语义标量与封闭 union converter 的实现另按本节验收；没有提供“看似可运行”却省略校验的伪handler。CONSUMER-WIRE及本规范是语义权威。

## L01 精确标量

- TS的Version/Sequence0/Decimal/Time/Evidence版本保持品牌string，比较整数用精确整数域；页面pageSize与上限内blockerCount可用number，不与业务revision混用
- C#同样保存语义string及准确UTF-8原bytes，精确数量可用decimal(21,8)运算副本，版本可用Int64解析副本；serializing原件永不以副本重建
- 新schema的Decimal规范不允许无意义尾零；原Observation value='1.0'按原专业Qty保持，不能拿候选Decimal parser强行规范化
- 时间解析验证真实日历、UTC和精度；原毫秒原字符串不能变成伪微秒。datetime2(6)仅存新真实派生时间，旧值原文另留
- Evidence OwnerRef id/version是字符串，不全强转UUID；只有schema指UUID的特定字段才如此。Case/Unicode不默默归一化后沿用原digest

## L02 封闭解析与枚举

TS readonly和C# required只表达设计形状，不算输入校验。未来parser必须对原UTF-8先拒duplicate keys/非法unicode/超界大小/深度，再封闭对象解析；所有必填nullable字段必须显式出现。C#构造器默认值不能补缺字段。所有判别oneOf只能中一个分支，code/status/retry三元组按ERROR-REGISTRY核。

public mesCompletionRef.owner=MES-WO，qaReinspectionRef.owner=QA-IPQC，其他ref按source职业owner校验；TS基础Evidence别名不足以承诺该const，必须由同schema语义校验器实现。Json schema通过不能代表actor有authority/current资格。

## L03 必需的应用/DAO接口（声明，不执行）

所有参数为命名参数，不位置拼接；AmbientUow是不透明的已登记同一DB连接+transaction对象，不能JSON化或跨HTTP传递。

| 接口 | 输入 | 输出 | 边界 |
|---|---|---|---|
| PreviewIntent | AuthContext, E, ncrId, IntentPreviewRequest | IntentPreview或Problem | 只读，无slot/Use/任务 |
| BindCompletion | AuthContext, ncrId, BindExecutionCompletionCommand | 存储Applied/Replay或Problem | T04全过程，G1 finalguard |
| BindReinspection | AuthContext, ncrId, BindReinspectionCommand | 同上 | T04全过程，G1+G2/IPQC/QAP |
| CloseComposite | AuthContext, ncrId, CompositeCloseCommand | 同上 | 全basis、全部members、one commit |
| ResolveCommittedIntent | AuthContext, E, operation, intent, digest, stableSelector | EXACT_BYTES/NOT_OBSERVED/typed mismatch | 当前read授权，原principal一致 |
| PrepareDependencyKeys | AuthContext, canonical business selection | complete key manifest+source refs | 只预读，无许可 |
| LockCompleteDependencies | AmbientUow, registered lock plan, manifest | locked resource cut或failure | rank10..60；禁止后补早锁 |
| ReadNcrTargetInUow | AmbientUow, E, exact decision/work/partition/cycle | Target+完整Continuity | rank60，无持久投影写 |
| GuardAdoptedExecutionCompletion | 原Owner EvidenceUseGuardRequest,AmbientUow | 原GuardResult | 无独立commit/token |
| GuardExecReinspectionMapping | 原Owner EvidenceUseGuardRequest,AmbientUow及同effect MES guard | 原GuardResult | 原IPQC/UoW与QAP新check |
| PersistConsumerEffect | AmbientUow,完整通过的语义图、result/head/Use bytes | 所有父子FK/唯一槽/CAS完成 | 不commit；失败全rollback |
| FinalizeConsumerV5 | AmbientUow,slot identity,exact result/head/use refs | 预存AppliedBytes/ReplayBytes | 新UTF-8域；不调用旧RESULT:4 |
| RecoverIntent | AuthContext, E, ncrId, RecoveryQuery | 精确Replay/NotObserved/Problem | 全图hash/FK；无guard/写 |
| ReadOwnerWorks | AuthContext,ncrId,query,cursor verified payload | OwnerWorkPage | SNAPSHOT/as-of、固定expiry |
| ReadEligibility | AuthContext,ncrId | Eligibility或Problem | 全cut零写，非guard |

内部receipt/capability/UoW对象不出public wire，浏览器也不能传入。当前完整新调用未实现/未运行；本表给未来实现逐项接口验收，不假称存在未读的CP8方法。

## L04 SQL设计落点

新schema/程序命名建议qa_ncr.R6S6_*，与R6S5原件并存；持久列、PK/UQ/FK/append-only/T04–T08完整规范在TRANSACTION-AND-STORAGE。这里不提供会被误执行的迁移脚本。新head必须同一旧aggregate并CAS原NcrHead，不能在新数据库维护一个不协调的“shadow closed”状态。

旧原head row与新HeadEvidence一一映射，旧phase保守映射，不写旧Decision或OwnerCommand。任何原核心实际约束与窄writer设计冲突需修受控后继实现和重审，不靠关闭约束/停用校验实现。此轮没有GRANT、DDL、运行或安全测试。

## L05 CP4 条件对象与封闭联合

四份语言文本分别为 CONSUMER-TYPES.ts.txt、OWNER-TYPES.ts.txt、LEGACY-ADAPTER-TYPES.ts.txt 和包含四namespace的 CONSUMER-TYPES.cs.txt。JSON Schema 是字段、条件、数量上限、nullable和额外成员拒绝的权威；声明形状不等于已经部署 parser。这里只检查文本符号和准确 schema/样例对应，编译及任何候选程序仍 NOT_RUN。

- ConsumerResult、Applied、Replay：三个operation分支，effectKey.operation与外层相同；ConsumerResult代码逐操作固定，closureBasisRef只在close非空
- Problem：每个ERROR-REGISTRY的code/status/retry是一个明确tuple分支，不能自由组合三个enum
- Eligibility：true分支blockerCount=0且blockers为空；false分支至少一个blocker，count==数组长度仍由封闭validator验证
- DisplayedObservation：数值value非空/code=null与分类value=null/code非空两个分支；精确原值和原QapUom两字段unitRef直接保留，不能借MES OwnerRef补owner
- DiscoveryState、ReferenceIdentity、IntentPreviewRequest、ReinspectionDiscoveryResult等oneOf均为具名封闭联合。TS不会生成never或unknown占位；外部Quantity明确从PinnedOwner解析
- TS UUID/Digest/Version/Sequence0/Decimal/Time是具名品牌string；C#持久/传输用string，schema验证语义。构造品牌或record不授currentness、权限或写许可

C#联合的具体子record均具名并在同交付文件声明。MutationResponse/RecoveryResponse等引用型联合使用 Case.Value 容器作为进程内类型；wire转换器必须按JSON顶层kind等判别选择白名单分支，直接读取/写出 Value 的底层原对象，绝不在线上添加“Value”包装层。record的CLR类名不作为JSON discriminator。没有声明默认System.Text.Json会自行得到该形状。

Converter闭合规则：先严格拒重复key/未知字段/缺必填，再确定唯一schema branch，验证全部const/tuple/nullable/集合及操作相关条件，最后构造允许的具体record。序列化只接受声明白名单的具体分支，输出规范字段名及原值，随后对同schema再核形状；nullable必填必须明确输出null。可空联合的null直接是JSON null；非null不包装。字节恢复始终使用原存bytes，不能以record重序列化替代历史bytes。

该转换器是详细实现义务，不是已编写或已运行的转换器。CLOSED-CONTRACT-INVENTORY 中逐个分支路径和精确schema结构可供未来实现逐项验收；任何语言shape无法表达的长度、数值、Owner或跨字段关系仍由同封闭schema/语义检查强制，不以as cast或默认null跳过。
