# 原生工程基线 Owner v1

入口 `/plm/engineering-baselines`，API `/api/plm-engineering-baselines/owner-v1`。资源 `plm-engineering-baseline` 的 `query/freeze/cancel/successor` 独立授权；菜单迁移只登记动作，不授予角色权限。数据范围 1 按冻结人；2/3/4 采用父工程修订已有可信部门映射；5 为当前租户全部。每个原件来源仍独立核对当前读取权限。

## 冻结前置与身份

`POST /freeze` 接受封闭的 `EngineeringFreezeCommandV1`。客户端从 `/choices/{iterationId}` 的真实评审包选择 sourceCandidate，传准确 ITER 候选、轮次、submission、已应用 receipt、ENG candidate/revision/configuration、ExpectedETag、CheckRun、技术 Manifest digest 和评审包 digest。两份 digest 的来源和用途独立，不能替换。

共同事务读取实际当前迭代/输入/候选、已应用 APPROVED 原件、原生 FLOW 决定和专业人员当时证据；重新核对当前批准政策、角色、权限/组织版本和未关闭阻断问题。技术检查采用同一个 PostgreSQL UoW 的 HoldExact 和最终来源检查。完整基线、每个组成位置、技术 Manifest、评审原件、适用输出绑定、原操作/审计和持久结果意图一起提交。没有实际消费端回执时，持久意图不等于投递成功或 REL/VER 放行。

唯一键为 environment/tenant/ENG candidate ID+version/configuration ID+version，不按内容摘要全局去重。同一业务候选换操作键且准确绑定相同时回同一基线并登记本次原结果；改写准确绑定返回冲突。同一操作键同体回原结果、异体拒绝。当前读取权限被撤回后不能靠原操作键绕过披露检查。

批准分类政策允许 `NOT_APPLICABLE` 的购买成品不会虚构内部制造组成。INTERNAL 必须具有实际当前完整来源检查和完整输出关系。本轮不删除设备、工具、路线文件/冻结等现有缺项，也不把关系专业确认等同于完整内部制造冻结准入。

## 原件与输出绑定

`Main.Plm.EngineeringBaseline/1` 以原生 JSON bytes 和 SHA-256 固定 identity、命令、工作快照、完整 CheckRun、原评审 package/snapshot/submission/decision/evidence/application/receipt 和原 Manifest。Manifest 沿用 `main-engineering-technical-manifest/1`、`CD-ENG-01-Main/1`、SHA-256 的既有 bytes/hash，保留全部出现位置和语义顺序。

适用时，同事务固定整个 `ENG-OUTPUT-RELATIONS/1` wire 和 `ENG-BASELINE-OUTPUT-BINDING/1`。基线引用 digest 来自不含 binding 的不可变原件，独立 binding 引用该基线，避免自引用摘要。binding 用 schema domain + 换行 + 去掉自身 bodyDigest 的规范正文计算；原 Manifest 不改用新算法。准确配置须与整套关系一致，主产品须实际出现在关系输出中。

NativeReference 沿用 W201 的明确 Main 原生引用映射。ManifestRef 表示该准确 ENG candidate 原件导出的技术 Manifest，ManifestDigest 用封闭 SHA-256/value 映射固定原 digest；它们不冒充外部提供者注册或签名。输出绑定映射的单独验证不等于 INTERNAL 完整冻结已通过。

PostgreSQL forward migration `20261030004000_EngineeringBaselineV1` 登记不可变 baselines/members/intents/successors、唯一键、原生来源核对和延迟完整性约束。基线聚合容量为 256 MiB，覆盖已接受的 16 MiB 评审包及嵌套原件重复保存；超限整笔拒绝，绝不截断。新开发不增加 SQL Server 实现。

## 恢复、取消与后继

- `GET /operations/{operationId}`：UNKNOWN、CLOSED 或 COMMITTED + 原 ACCEPTED。未知不自动重发。
- `POST /cancel`：独立 OperationId，TargetOperationId，ExpectedState=`UNKNOWN` 和原因。取消先赢，原键持久关闭且迟到冻结被拒绝；提交先赢，返回原 ACCEPTED，不删除冻结事实。取消键可原体重试。
- `POST /successor`：准确父基线、冻结时修订/ETag、原因和独立操作键；还需要当前 `plm-engineering-revision:edit` 和父修订范围。只从仍为当前的冻结工作版本追加一次后继，并在同事务保存显式父基线关系。普通 PATCH 不能越过冻结；数据库也要求冻结后的下一工作版本具有原生后继关系。
- `GET /successor-operations/{id}` 与 `POST /successor-operations/{id}/close`：读取或关闭未提交的后继原操作。后继不继承旧 CheckRun/评审资格；旧基线原件不变。

这是同一工程修订的明确后继工作版本，不替代未交付的 ECO、正式变更批准、REL 或新修订来源 Owner。那些入口继续要求自身真实前置。

## 读取与页面

`GET /` 支持 page 和 revision；`GET /{id}` 回准确原件和独立当前 SourceStatus；`GET /diff?before=&after=` 比较同一工程修订的准确基线；`GET /{id}/where-used` 读取本 Owner 原生后继关系。REL/VER/WO/SCHED 尚未采用引用读取时明确返回 UNKNOWN，不能解释成完整零引用。

页面从实际迭代选择准确候选，分别显示技术检查、已应用评审、冻结、当前来源资格和用途状态。冻结后来源撤回可以使 SourceStatus 变化，不能改变历史 FROZEN。原件披露仍要求所有实际来源读权限。浏览器按 environment/tenant/actor 保存最小恢复键，使用 Web Locks，拒绝迟到身份响应；不保存完整技术原件或自动换键重发。原工程修订页面对已冻结工作版本禁用编辑，并指向明确后继流程。

## 验证边界

验证结果及失败保留来源集中记录于 [W202 交付报告](evidence/W202-01/DELIVERY.md)。原生审批、PostgreSQL、HTTP 和 UI 验证只证明对应本地行为；测试 FILE 内容/扫描替身不构成企业验收。完整 WP05-12、ECO/REL/VER/WO/SCHED、内部制造尚缺资格、跨提供者与正式业务验收仍按各自任务完成。没有运行 Actions、迁移既有环境或部署生产。
