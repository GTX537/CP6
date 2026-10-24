# W108 现有CRM业务图的PostgreSQL存储适配

COORD本聊天单人，沿用既有执行计划与技能，七天持续目标ACTIVE；不另开代理/聊天/定时任务。

## 前置与范围

Main `d7e5ff522282a593cc296670f2c287b59301e183`、CRM `a69098129ef64d1bd6ae0a96df8831890220ffce`、Handoff `88364f6475e7ab58cf09eac933922b264f2bf8a8`已核对。W107 Main519/CRM134/Handoff83正常合并，三条准确任务分支删除、固定目录干净detached、全部finish释放；实际17.36min，24项相关本地检查。AGENTS/POLICY、最新台账和实际席位核对；W103 junction已解决，缓存保留，不重复。

实际公开GET/IssuedAttempt和原Attempt/三维限流前置已采用。现存CrmRuntimeDbContext、CrmRuntimeGraphStore、CrmAuxiliaryStorageCodecs与CrmLeadStorageCodec负责完整规范化业务图；crm_v1/0.2.0 manifest含32组织表，Lead62列、PublicSubmission56列及真实复合FK。当前模型/脚本是SQL Server存储表示，无法原样作为PostgreSQL业务事务前置。Main实际签名权限生产者仅配置用途，完整H01服务权限及原编号回执仍待后继。

本窗采用原有行类型、codec、GraphStore及完整表关系，新增明确版本的PostgreSQL存储profile；不创建简化Submission替代原Owner。保留TenantId与复合FK，在原Context显式绑定可信租户、查询/写入范围；schema不作为权限证明。时间保留100ns源精度，原Unicode/NUL和加密列无损，金额与源版本不缩减；PostgreSQL自己的并发版本与约束替代SQL Server rowversion/方言。生成/安装与模型都从既有权威manifest取结构，未识别语义拒绝，不静默忽略check或nullable-target FK。

仅显式安装于本轮隔离新库；检查现有namespace/版本，禁止自动修补、迁移或切换真实宿主。当前新公开Bundle/签发与旧业务配置身份还需后继显式适配，不据存储测试宣称公开提交、H01、业务outbox投递或完整受理通过。原SQL Server历史资料不改动。

## 任务与实际执行估计

|任务|范围|预计实际执行|
|---|---|---|
|W108-01 Main|前置登记、总台账/四状态、三仓采用核对|10–20min|
|W108-02 CRM|原模型PG适配、完整图读写/精度/租户/并发/回滚、集中审查|60–100min|
|W108-HANDOFF及收尾|一次START、恢复交付、正常PR及准确分支/席位收尾|20–30min|

总估90–150min；Main START `2026-10-24T11:33:30.902614+00:00`，最终按finish墙钟计，不重复累加。目标CP6-7D-20261022保持ACTIVE。

## 验证与收尾

- [x] 显式PG存储版本及无损表示、原32表结构/复合约束/索引适配，安装不改已有环境。
- [x] 原codec和GraphStore完整Submission/Lead/子图实际PG往返、100ns/Unicode/NUL/加密/金额精度与真实租户隔离。
- [x] 原关系/并发版本、错误租户/非法引用/约束、事务故障无半个业务图，相关局部回归和一次集中审查。
- [ ] CRM、Handoff、Main正常PR、远端采用核对、准确分支删除、进程停止、官方finish释放固定席位。

固定DEV-1/DEV-2串行，隔离PG55446、D:/CP6-local-test/wave108；既有缓存复用。无Actions、生产部署、实际环境迁移或额外席位。整卡WP03-02与正式24/375、0/125、247NOT_RUN不提升。

## 实际交付与生命周期

CRM [PR135](https://github.com/GTX537/CP6.CRM/pull/135) 已采用 `dfeba8e44d58a668b8f721b605a425f1bc5279b7`，来源 `bd2c0ce23932b56ad78d46c020fa5cea7bd22459`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/dfeba8e44d58a668b8f721b605a425f1bc5279b7/docs/development/evidence/W108-02/DELIVERY.md)。

原CrmRuntimeDbContext的32组织表、完整行/codec/GraphStore及保留消息结构采用明确`crm-runtime-postgresql-v1`。UTF16LE bytea保留Unicode/NUL，100ns ticks与DateOnly保持原精度，精确numeric加原合法范围/scale检查拒绝静默舍入。原复合TenantId/Receipt FK、检查与索引保留；数据库字节相等语义有明确版本，未仿制SQL Server默认排序规则。

可信租户查询/ tracked写入检查不替代实际业务授权或RLS。显式ReadCommitted外层事务保证表锁后目录新快照，图更新先FOR UPDATE核对原8字节版本，再改子行；真实竞争等待后旧版本明确冲突。后段失败必须调用方整笔回滚，不catch后提交中间图。显式新库安装拒绝既有namespace及目录漂移，不迁移/切换宿主；运行身份无DDL、TRUNCATE、profile写入或序列setval。

19项最终适用的唯一相关本地检查通过、0跳过：13项真实PostgreSQL行为、6项原模型元数据回归；受影响编译通过。 原完整候选图、原生Lead成员/首次响应/并发更新和原生Submission同意字段/拒绝/保留期处理均实际执行；另有跨租户/Receipt FK、晚段唯一冲突整笔回滚、金额上限/超精度、日期与巨大JSON指数、安装/权限/漂移检查。6项原模型元数据成功结果按不受影响路径复用，未运行SQL Server数据库；全部32表安装不等于全表业务验收。原始失败与适用来源在`D:/CP6-local-test/wave108/foundation/final-validation.json`。

自有PG55446停止，数据/失败日志及既有依赖缓存保留，无新junction/源码副本。一次集中审查完成，无Actions、保护/依赖版本变更、已有环境迁移或生产部署。CRM准确任务分支已删除、固定DEV-2 finish释放。

新Bundle/IssuedAttempt与旧图的配置/表单身份仍须明确无损桥接，不能把任意精度版本缩成旧int；原Attempt消费/重放、真实H01、编号原操作回执及公开submit/receipt/page未完成。WP03-02只交付限定存储前置；24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR84](https://github.com/GTX537/CP6.AI-Handoff/pull/84) 已采用 `e10cab1497d1341d53a8538a5ab26eb93734a353`，原准确分支删除、固定DEV-2 finish释放。 CRM分支删除/席位释放已核对；Main本次正常PR及finish继续，当前START后墙钟24.52min（原估90–150），最终按Main finish计，不叠加各仓。
