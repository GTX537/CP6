# W165-01：工程与实物准确版本关联

COORD 本聊天单人；[计划](../../dispatch/20261027-wave165/README.md)。Main基线 8a86cabdb45f84376e42341e504012c74b7bba7c，DEV-1/DEV-2固定目录复用。预计150–240分钟；Main官方START至本登记34.62分钟，最终以finish/proof为准，Handoff重叠不相加，前置只读核对单列。原CP6-7D-20261022持续目标ACTIVE。

## 实际行为与来源

落实accepted SUP-ACT-006-02的实例到准确技术版本关系、ENG-TOOL-COMPATIBILITY §7/10的INSTANCE本地双Owner采用。实物页按完整工程No与业务Rev查询实际、当前授权技术候选，显示Base、准确UUID和技术版本；必须显式选择，不按名称或省略Rev自动取最新。提案原件绑定可信Tenant、原技术UUID/No/Rev、Main.Plm.ToolSpecification.v1版本及摘要、Main.Erp.PlateMoldSource.v1原版本、Main.Wms.ToolInstance.v1实物UUID/版本、工程产品Ref、产品/工艺/配置/用途声明、生效区间、理由与显式后继关系。范围文本仍是明确工程声明，不伪造DOC/ROU/CFG资格。

两个实际本地生产Owner是Main.Plm.ToolInstanceConformance.v1与Main.Wms.ToolAssociationAdoption.v1；严格类型的当前命令分别由原工程确认人和实物责任人的独立权限plm-tool-specification:instance-associate / wms-plate-mold:engineering-link执行。旧add/edit不授予它们。能力种子为新租户定义动作，不自动给角色授权。浏览器不提交Owner决定回执或approved布尔值；它向真实Owner提出本方决定，Owner当前授权后生成不可变原决定。内部决定合同保存Owner/role/原receiptId/version1/hash、完整subjectDigest、proposal、round、generation、CONFIRM/REJECT/WITHDRAW、依据、原Actor、该Actor当次全部来源/action权限版本与组织版本、UTC时间；本地受信读取逐项核对，未实现外部Owner事件传输或通用自由JSON适配器。

提案内容不可变；改变技术/实物/范围必须新提案，已终结关系可显式supersedes。投影保留controlRevision、observationGeneration、projectionRevision、roundId、双方独立slot、everConfirmed和历史。两方对同一准确主体、同轮次/代际的当前决定才CONFIRMED；一方材料仍UNKNOWN。REFRESH代际递增、清空双方slot，旧轮提交保留applied=false的STALE_OBSERVATION原操作结果，不能复用旧单方确认。拒绝/撤回使从未确认关系REJECTED、曾确认关系REVOKED，终态不能复活。同实物最多一条已确认关系，由实际事务检查和PostgreSQL部分唯一索引共同保证；换新关系前需明确结束旧关系。多个实例可对应同一技术版本，但互不继承确认、维护计数或使用资格。

关联当前读取重新核对两Owner及全部来源query/对象权限，历史原件也受当前范围约束；分页前过滤不可见提案，原操作恢复不绕过撤权。重读决定时使用原决定人的真实受信权限读取，核对其全部原来源/action权限版本和组织/对象范围，不借用当前读取人的权限。双角色可由不同用户执行，实际验证覆盖工程责任人失去来源权限而另一用户仍有读取权限的情况。原来源/权限变化使当前关系UNKNOWN，历史CONFIRMED和everConfirmed保留；当前GET只报告准确knowledge/asOf，不写假事件或沿用缓存绿色。

当前技术草稿头、原生技术来源、工程产品、实物原来源、权限及本地决定/投影都在同一Main PostgreSQL事务中保护；最终提交再次核对准确头、原决定人权限/对象范围。真实其他连接的物理写入等待提交；同事务注入来源变化使决定、投影和journal一起回滚。并发/陈旧版本使用原If-Match；原operationId同输入重放原结果，异输入409，CLOSED不复活。提交响应丢失后按原键恢复，不再次产生决定。

主入口：GET/POST /api/wms/specials/v1/tools/{id}/engineering-links；GET /api/wms/specials/v1/tools/{id}/engineering-candidates?engineeringNo=&revision=；GET /api/tool-associations/owner-v1/{proposalId}（可准确历史version）；两个决定入口 /api/tool-specifications/owner-v1/instance-association-decisions 与 /api/wms/specials/v1/tools/engineering-link-decisions；原结果GET/close在 /api/tool-associations/owner-v1/operations/{operationId}。所有来源/关系读写经过实际Owner，no-store、身份与CSRF沿用实际宿主。

迁移20261028010000_ToolInstanceAssociationV1仅在隔离PG18执行；cp6_tool_association_v1含proposals、heads、versions、decisions，租户复合键、真实来源FK、原件SHA256、冗余列/JSON校验、不可变UPDATE/DELETE/TRUNCATE保护、连续版本及终态保护、PUBLIC权限撤销。不存在普通Stock/StockTransaction写入，不改变旧实物状态/UsedShots/原生版本，不生成技术新版或批准。

实际Vue实物页加入候选选择、关联列表/历史、独立双方确认/拒绝/撤回/重新核验和原操作恢复。显示准确两种版本、对应范围与生效区间、当前/历史知识，明确尚无技术批准或使用许可。412保留输入和原选择；权限或身份变化清除私密数据、迟到响应不得覆盖新对象。恢复记录只含operationId/objectId/proposalId，不缓存技术依据或业务内容。

## 验证和一次集中审查

51项PG18/真实HTTP及独立新租户能力检查全部通过，无跳过。12项新关联行为覆盖真实HTTP权限/CSRF/两入口采用/恢复、准确候选与同轮双方声明、不同角色和两个实际用户、权限/来源ABA与版本漂移、历史/分页范围/跨租户、独立实例、唯一当前关系、刷新与旧轮、终态及后继、精确原操作冲突/CLOSED/提交响应丢失；真实第二连接锁等待和同事务来源变更回滚。受影响既有Tool_instance_/Tool_specification_/Tool_linked_registration_回归通过。新租户种子单独在新隔离数据库执行，检查两动作定义且不额外授予角色。

16项Vue组件测试及vue-tsc/Vite通过，覆盖准确候选、独立权限/同轮输入、412保留、未知恢复只保存ID、错误提案回执拒绝、切换对象/身份迟到响应和既有实物页面。没有浏览器E2E、真实企业验收、Actions、既有环境迁移或部署。

证据D:/CP6-local-test/wave165/foundation/final-validation.json绑定实际源码和各次结果。association-red的真实HTTP404、association-first的名称冲突编译错误、association-compile的PostgreSQLUPSERT先触发首版校验失败均保留；已改为首版INSERT/后续准确UPDATE。集中SPEC/QUALITY补强原决定人的全部来源权限及提交前对象复核。最终受影响行为通过后复用成功输入，不因合并SHA改变重复验证。新租户检查追加测试方法后，既有测试方法及生产代码未改动。

## 未完成范围与收尾快照

Relation to an exact DRAFT is not technical approval, maintenance qualification, execution permit, external Owner inbox adoption, enterprise acceptance or complete WP05-06/WP10-48. No ordinary Stock/StockTransaction effects, Actions, existing-environment migration or deployment. Continue actual physical use/maintenance/discard and professional technical approval/qualification adoption, preserving all original D2/D3/D5/D6/D7 exits, Order request processing and formal Sales. 生产使用/维护/废弃、技术批准/冻结、DOC/VER/REL、ROU/BOM/WO/SCHED、跨仓宿主/企业来源及原七天其他出口继续开放。原WP05-05订单CHANGE/CANCEL仍为REQUESTED，WP04-32正式Sales仍开放。

W164归档CLOSED一次，仅关闭WP10-48.wave164_registration_links；W165记录wave165_instance_association。375正式卡24DONE/2IN_PROGRESS/349PLANNED，125完整目标0、247真实运行NOT_RUN、41历史静态保持。D1/D4完成；2/7=28.6%仅是阶段数，不能当实际工作量百分比。完整WP05-06/WP10-48与D2/D3/D5/D6/D7未关闭。

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/141，source 4a1952aa75b25ef90846fb74d533b198728f474c，目标 2878ad6702c1763504154786c1e8a3d014ceb1a9；准确本轮分支已删除，DEV-2已finish释放。Main本报告时待正常PR合并、远端采用、准确分支清理和官方finish；最终proof记录实际结果。固定目录、缓存和用户未跟踪文件保留。
