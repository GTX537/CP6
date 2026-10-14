# Wave 7 / lane A — WP01-01 准入与实质后继设计评估

固定Main：`1129953156b7775152a74ad2f51cb95ef430ee56`。本地main及origin/main均为此值；远端即时核对由COORD负责。本评估只读仓库，未执行测试、Actions、消息发布或数据库操作。

## 结论

建议派发 **WP01-01 技术决定收口与默认禁用的完成门适配候选**，写入新的任务私有目录。主任务继续 **PLANNED / NOT_CHECKED**，以assignment阶段表达本轮实质设计工作；不能重复生成上一轮矩阵或unsigned模板作为交付。

WP05-01设计任务DONE不等验收依据已采纳。原README §6明确普通“继续”/领取仅授权设计。本轮可在已有彻底修复设计/协调授权内作出准确技术决定候选，不补造用户签名、不倒填authority。先把实际决定包做成可审核结果，再由COORD处理最后的准确采用决定；当前不向用户追问。

## 已交付准备的固定身份

- 1.1.0组合manifest：`c5c926728287ac9ee0b5048300deefa789ec7c019ed88168bd74e120dc53ebf2`。
- 1.0.0原manifest：`979cb7f4fc779f6200e16b2919a89afc244681d122683036378e068931fcd3ee`。
- 577行矩阵：`0f6e160d49b9fb0305c94f9a3ccbfc71ebae312a26bd1613fd38ce4043b57063`。
- 准备核心manifest：`b9e26854717573ba4216e48cb2e576ff4440985b4e77f9605748cc6605e9ab26`。
- 准备候选`938f53eb44438c066bdc38a60e2d987264871803`，PR #205；规范读取pin `1ab2d031e3c501662ecb89a33839fac1cba55d33`。
- 221/295/43/8/577分别统计；旧30/48继续NOT_INVENTORIED / UNRECOVERABLE / UNPROVABLE，成员及缺件hash为null。
- 当前准备比较器只比较形状，不认证权威、证据内容或远端关系，不能直接接生产门。

## DP01–08：技术决定和真实剩余边界

### DP-ITER-01 — TECHNICAL_SUCCESSOR_DESIGN_PARTIALLY_DETERMINED

固化技术摘要、评审包摘要、aggregate摘要和snapshot摘要四种语义及版本身份。ITER对ENG原摘要保持opaque；R1已明确aggregate/snapshot域分离canonical、合法字符与无损别名规则。把每个wire字段映射到准确Owner原schema、算法版本及输入域；禁止用CD-ENG-01候选示例替代原已接受技术Manifest协议。

真实剩余：ENG共同序列化/修订格式及合法域必须按准确接受来源完成选择和无损对照。若索引未提供已接受算法，交付明确有版本的新技术决定候选与golden vectors，保留ADOPTION_PENDING；这不是缺一个人随意填写sha256的权限问题。

完成门影响：仅未注册算法/不无损表示的路由拒绝；不得把R1已明确部分继续笼统写未决，也不得将整个DP自动标已解决。

后继任务：WP05-03/WP05-19/WP05-12

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md:7-35`；`docs/design-handbook/modules/PLM-ITER-01.md:83-85`；`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1799cd319ae35310__03_ENG_当前阅读主册.md:1213-1300`

### DP-ITER-02 — TECHNICAL_POLICY_BINDING_DECIDABLE_ACTUAL_CONFIGURATION_REQUIRED

采用OA R2的绑定治理/唯一Owner/准确policy pin/可信actor/SOD、R1的roundBinding与rejectDisposition仅本地规则。写出项目成员来源、角色及代理授权证据、策略版本、冻结时点与应用时重核的输入合同，缺绑定/缺合格角色拒绝或按原OA Suspended，不默认全员审批或批准。

真实剩余：真实项目成员名单、角色映射、代理授予、自批例外及所选真实OA发布策略没有从资料自动得到批准。允许有版本配置输入和保守拒绝路径，不造租户业务政策。

完成门影响：设计框架可完成；真实送审/应用验收仍需具体配置证据。

后继任务：WP12-06/WP12-07/WP12-19/WP05-19/WP05-20

来源：`docs/design-handbook/modules/OA-APP-01.md:57-75`；`docs/design-handbook/modules/SYS-IAM-01.md:19,49-59,70`；`docs/design-handbook/remediation/07-shared-consumer-mappings.md:7-35`

### DP-ITER-03 — POSTGRESQL_PHYSICAL_DESIGN_AUTHORIZED_RUNTIME_ADOPTION_PENDING

按cp6-pg-tenancy-v1完成ITER/REL逐字段存储、原身份合法域、索引、唯一性、FK、CAS、写集与锁序设计。内部生成键可选自身UUID；外域ID/Ref保完整原字符/bytes/schema，不能仅hash或截断。共用TenantId/可信schema映射、同连接事务、角色/RLS、迁移lineage和恢复，不另造租户方案。显式区分业务修订与数据库并发令牌，PostgreSQL原生CAS不照搬SQL Server rowversion。

真实剩余：需要读取最新Main持久化/精确类型能力并给出具体无损映射；WP02-02目前BLOCKED，WP02-03/10/13/16尚PLANNED，不能声称这些设计已在真实Owner生效。precision/scale/外域限制若变更须有版本与无损证据。

完成门影响：本轮可完成可实现的DDL/CAS设计候选；不执行迁移、不改共享EF模型、不据文档声称PG通过。

后继任务：WP02-02/WP02-03/WP02-10/WP02-13/WP05-18..21/WP05-32..36

来源：`AGENTS.md`；`docs/design-handbook/architecture/02-postgresql-tenancy.md:1-155`；`docs/design-handbook/modules/PLM-ITER-01.md:31-48,87`；`CP6.Core/EFDbContext/CP6Context.cs`

### DP-ITER-04 — RETENTION_SAFETY_CONTRACT_DETERMINED_POLICY_VALUES_REQUIRED

明确关闭/否决不删除原工程证据；更正追加；缺有效policy/hold裁决不执行清理。采用SYS-PRIV准确scopeDigest、policyRef/version、批准范围、legalHold及Owner事务门，分类非PII证据/可处理PII/秘密/副本，冲突显示PARTIAL/UNKNOWN。

真实剩余：工程记录具体合法保留期、脱敏及合法清除条件没有已知获准值；不可自行填写30天，亦不能把无限保留当适用法律结论。新设计可固定政策输入及阻断行为，实际销毁另行按政策授权。

完成门影响：缺政策阻破坏性清理与对应验收，不阻只读、追加审计或其他已授权设计。

后继任务：WP02-13/相关SYS-PRIV任务/PLM-DOC Owner

来源：`docs/design-handbook/modules/SYS-PRIV-01.md:29-33,46-48`；`docs/development/evidence/WP01-01/preparation/semantic-audit.md`

### DP-ITER-05 — TECHNICAL_LIMIT_PROFILE_CANDIDATE_CAPACITY_EVIDENCE_REQUIRED

区分UI页长、API页上限、完整评审集合上限和OA运输硬边界；给出有版本容量profile、合法域/错误/分页稳定排序与配置变更影响。50/200/500/50/50原为建议，不能静默定生产上限；OA既有256KiB snapshot、1MiB body、1000 IDs继续有效。设计对不能无损承载的提交显式拒绝，不能截断候选集合。

真实剩余：生产SLO和特定业务规模容量结论需真实容量输入与专项验证；本轮可选明确候选profile并列验证方法，但不能以fixture通过批准生产参数。

完成门影响：配置/验证范围单独阻断，不把纯设计检查等同负载测试。

后继任务：WP05-18/WP05-19/后续容量验证

来源：`docs/development/evidence/WP01-01/preparation/decision.UNSIGNED.json:DP-ITER-05`；`docs/design-handbook/modules/OA-APP-01.md:46`；`docs/design-handbook/remediation/07-shared-consumer-mappings.md:17`

### DP-ITER-06 — CONTRACT_SEMANTICS_DETERMINED_PROVIDER_IMPLEMENTATION_MISSING

把VER/MES安全终结证明按原Plan/执行意图/根子集合/原请求键/水位/迟到封闭绑定成消费规则；UNKNOWN、timeout、NOT_FOUND、仅根Cancelled不能推出全部停止或释放。原操作恢复不新造Plan/Baseline/执行键。

真实剩余：真实PLM-VER/MES服务、根/子执行完整性与安全终结事实及联调未交付。这是provider实现/运行证据缺口，不是再请用户拍板即可消除。

完成门影响：仅依赖外部安全终结的取消/结案/释放受阻；设计可以明确完整合同，运行仍NOT_RUN。

后继任务：WP05-21/WP05-24/WP08-15/WP08-18

来源：`docs/design-handbook/modules/PLM-ITER-01.md:83-85`；`docs/development/task-plans/WP05.md#wp05-21`；`docs/development/task-plans/WP08.md#wp08-15`；`docs/development/task-deliveries/WP05-01/README.md:77-79`

### DP-ITER-07 — LANGUAGE_WORKFLOW_DETERMINED_TRANSLATION_CONTENT_PENDING

稳定字段ID/字典Value与可翻译Label分开，固定中文审阅来源。使用SYS-LANG的准确租户/key、编辑回draft、显式review集合/version、受权publish/不可变bundle及CAS回退；不将机器翻译直接发布。

真实剩余：日/韩等准确译文及所需审校记录尚未存在；语言发布Owner与客户端真实采用也待实现。无需把语言框架重新当未决定的技术问题。

完成门影响：阻相应未审译文发布，不阻稳定字段合同和其他语言已授权流程。

后继任务：WP02-17/各ITER-REL UI任务

来源：`docs/design-handbook/modules/SYS-LANG-01.md:29-38,40-48,54-62`

### DP-ITER-08 — CURRENT_SOURCE_AUDIT_CAN_BE_COMPLETED_IMPLEMENTATION_STILL_MISSING

对最新固定Main、实际Platform包与消费声明做符号/路由/模型/写者清点，形成REUSE/CHANGE/NEW与后继任务路径。当前Main未找到CP6.Core/Services/Plm及PlmEngineeringService；历史b7ec269候选只能参考。FormSubmissionService/ApprovalDispatcher与20261011审计同Git字节，后者仍FirstOrDefault同步直接OnApproved/OnRejected，不具新OA持久应用协议。

真实剩余：静态源码适配审计可本轮完成；真实服务、宿主、worker、DB、UI和实际采用仍由实现任务交付，不能把不存在符号或候选代码说成已实现。

完成门影响：可关闭本轮已核源码范围的未知项，留下明确实现差异，不提升业务状态。

后继任务：WP05-18..21/WP05-32..36/WP05-42/WP05-61

来源：`docs/development/assessments/20261011/implementation-assessment.json`；`CP6.Core/Services/Wf/FormSubmissionService.cs:29`；`CP6.Core/Services/Wf/ApprovalDispatcher.cs:23-41`；`docs/design-handbook/modules/PLM-ITER-01.md:130-134`

## 必须形成的新交付

- README.md and decisions.json: exact DP/subclauses, inherited normative decision vs new candidate choice vs configuration/runtime dependency
- postgresql-mapping.json and current-source-audit.json: actual identities, lossless physical/CAS design, current symbols and implementation tasks
- provider-consumer-closure.json: exact conditional edges and evidence duties
- basis-delta.json + manifest.json: changed obligations only, complete base/supplement references, formal adoption boundary and concrete residual choice list
- gate-design.md + gate-contract.candidate.json: target whitelist, task/target/package distinct completion predicates, immutable old-gap handling, WP01 123-original + 2-new-basis summary semantics without claiming 125 originals
- private read-only disabled adapter + meaningful tests + exact report; no import by production manage_roadmap.py
- delivery.md: exact diff, hashes, reviews and local validation/remaining runtime scope

A new candidate such as CP6-WP05-01-ACCEPTANCE-BASIS/1.2.0-proposed if obligations/decisions change; do not mutate 1.0.0/1.1.0 or preparation bytes.

Reuse prior checked source/matrix/hash inventory. Do not regenerate identical unsigned template or rename another preparation package as substantive delivery. Each DP must have a clause-level disposition and each unresolved item must name actual missing policy/evidence/provider.

## 跨仓闭包不能停在空泛UNRESOLVED

Known task implementation owners are Main, but current declared DAG has concrete Platform and CRM dependencies; baseline presence alone is not the reason. Resolve provider/consumer edges and conditional-consumer bindings individually.

- `WP05-18 → WP02-10 → WP02-07`
- `WP05-18 → WP05-14 → WP05-13 → WP03-17`
- `WP05-34 → WP02-12`
- `WP05-61 → WP05-60 → WP05-13 → WP03-17`

Per edge: provider/consumer repository, exact contract/profile/version/hash, task, routing condition, local owner atomic scope, required implementation/remote/package/consumer evidence and state. Do not remove existing declared dependencies as a private worker; propose exact COORD change only when a concrete source establishes narrower applicability.

这些声明DAG链不是每个运行场景都必须整包交付的结论；实际条件消费仍按任务卡绑定逐条核。工人不得自行删既有依赖或把Main-only模型当准确闭包。

## 写入和状态边界

新worker仅可写：
- `docs/development/evidence/WP01-01/decision-closure/`
- `eng/wp01-01/decision-closure/`

原preparation、WP05所有版本/adoption、生产manager、台账/定义/生成页、四份状态、原设计合同、产品/EF/迁移/依赖/工作流均只读。COORD另在串行窗口维护assignment与交付状态。若要修改公共规范，先交精确差异，不越界写入。

- Read-only task-private candidate; not imported or invoked by production manager. Default production_enabled=false and actual_adoption/actual_completion=false unconditionally.
- Unlike old shape comparator: validate exact Git/raw file bytes and path containment, source/target/obligation identities and no dropped subassertions; actual decision/evidence files, hashes and declared provider edges must be inspected, not accepted merely because strings are nonempty.
- Separate design resolved, adopted, configuration pending, implementation missing and runtime not run. A conditional exclusion requires exact authorized source and scope; no wholesale N/A or fulfilled-by-disable for target DONE.
- Synthetic tests may drive an isolated evaluate_candidate result; mark every fixture synthetic and never generate a signed/adopted production record. Unsupported authority authenticity/remote ancestry remains explicitly unverified and rejects actual use.
- Later COORD serial adoption/production wiring requires separate exact authority record, public edit window and targeted production gate regression. Approval alone does not make all business evidence PASS.

## 局部验证和复用

- Reuse wave6 preparation 3509 checks / comparator23 / bundle10 only at their recorded input hashes; label source time and no new run. Do not rerun simply because SHA changed.
- Check new JSON/schema, hashes, source line/pointer/edge integrity, path links, complete delta/no historical overwrite and git diff --check.
- For new adapter code only run private Python unit tests covering no adoption, forged/malformed authority, hash/source/path drift, wrong target/other123, missing obligation/subassertion, STATIC/REFERENCE_ONLY/synthetic/NOT_RUN/FAIL, conditional dependency omission, actual evidence file mismatch, provider receipt/remote binding, preserved old gaps and no production writes.
- A synthetic complete positive validates candidate algorithm only; actual no-adoption baseline always rejects. Separate WP01 task delivery from WP01 package inventory closure and business target completion.
- One consolidated task-level review before delivery; recheck affected fixes only. No full .NET build, business tests, database/migration, Office recomputation, Actions or deployment for this design/private-Python change.

## 状态建议

- WP01_01：Keep PLANNED / NOT_CHECKED. assignment.phase may say DECISION_CLOSURE_IN_PROGRESS or delivered equivalent and dispatch_scope DESIGN_ONLY_DISABLED_GATE_CANDIDATE; real worker START time only after receipt.
- WP05_01：Keep DONE as design-delivery; its acceptance adoption remains PROPOSED.
- targets：Keep 0/125 and all existing business/R1/historical states; other123 original gate unchanged.
- later：After concrete package review, COORD handles exact remaining approval choice/adoption and opens serial production gate window. Only then WP01-01 can enter its full post-adoption scope. No claim that WP01 package DONE follows from task candidate delivery.

## 关键源码事实和固定hash

最新Main `CP6.Core/Services/Wf/ApprovalDispatcher.cs:23-41`仍同步查首个callback并直接OnApproved/OnRejected；与20261011审计hash相同。当前未找到Main `CP6.Core/Services/Plm/`，历史b7ec269候选不能算已合入。具体运行能力全部未验证。

完整Git字节pin表在同名JSON的`inputs`；以下供快速核对：

| 路径 | SHA-256 |
|---|---|
| `docs/design-handbook/remediation/07-shared-consumer-mappings.md` | `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa` |
| `docs/design-handbook/architecture/02-postgresql-tenancy.md` | `dfcaea44365d92f7457db783908dd8eb6f1eaa7651c67016bdff68281a0148c3` |
| `CP6.Core/Services/Wf/FormSubmissionService.cs` | `9f2032ff02b31d01f919f487c4a70c5e53a384edb61d221ad5af0652f84e66b8` |
| `CP6.Core/Services/Wf/ApprovalDispatcher.cs` | `8a41944e80130f5124179e18a778a2310185977d0904b0eefa1786d8b2bcd3e8` |
| `docs/development/tools/manage_roadmap.py` | `e1a86a1c26cb2fc928d4eb2a5baf1261e46cd60ab9735846fdfc476a03db2e4a` |

本轮未取消Actions、未改触发器、未运行任何业务测试；所有原失败、未运行和未证明边界原样保留。
