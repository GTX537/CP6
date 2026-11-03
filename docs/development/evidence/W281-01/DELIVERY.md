# W281-01 原生收藏与统一授权历史

W281：原生幂等收藏、本人失效收藏取消与安全占位、当前授权统一提交/流程历史已实现；准确FORM/FLOW版本、租户时区、分页截点、只读字段投影及实际连续业务回执，真实Chrome/Vue→Main→PostgreSQL采用。

## 实现与合同边界

采用原OA-FORM UC-F01/02/F12/13及WFC原命令协议。收藏采用明确on布尔；同租户、环境、实际用户、formKey唯一。新命令赋值、重复请求原结果、同键不同raw冲突、取消不存在收藏与历史成功重放分别处理；历史重放不重新应用旧偏好。失效本人收藏仅保留原保存key，不能泄露缓存名称/schema或发起新提交；仍可显式取消。收藏不授提交、字段或文件权限。事务命令、审计、收藏及最终权限核对原子完成；PG约束触发器禁止绕过Owner直接写入。写入不可用不阻断合法原命令只读恢复，未知提交不伪装失败。

新增GET /api/oa/v2/catalog/favorites、PUT /api/oa/v2/catalog/{key}/favorite。复用现原结果/关闭入口，不另造证明层。页面单独保存收藏待确认意图，避免与草稿命令混用；敏感raw仍只在内存。断响应后必须恢复/重试原命令或明确关闭，身份变化清私有结果，迟到响应不恢复旧内容。

新增POST /api/oa/v2/query/search、GET query/context、GET query/instances/{id}与GET form-data/{id}。严格闭合筛选DTO、16KiB边界；当前oa-form-search:query权限与TASK代理范围在权威读入口核对，读取候选先于筛选、计数和分页。本人未绑定提交可读，TASK代理不扩大到他人未绑定提交。保留starter/handler/flow/keyword/status/date并增form/version/hasInstance；关键词仅流程代码/业务编号，不扩DataJson全文。已有旧查询仍独立展示，未声称全部旧caller已采用。

历史绑定准确不可变FORM/FLOW发布GUID、版本序号及名称；当前资源/字段权限投影schema、值、标题，历史详情只读。APP使用真实Intake时间来源，不编造FORM字段。可信Sys_Tenant.TimeZoneId将包含起止日的日历日期转换为UTC半开区间；无效、歧义或缺失时区明确失败，不回落服务器时区。分页按提交UTC/稳定ID排序，沿用首次snapshotAsOf；后续页要求原截点。新增query_available_at记录实际查询成员入库时间，避免后启动APP、后生成子流程或倒填业务时间的新记录进入旧分页。该列不冒充业务创建时间；既有行在未来正式安装迁移时才获得查询能力起点，本轮未迁移任何既有环境。

流程已通过与业务Applied分开：仅在当前业务读权通过后读取TxR的连续投影latest_receipt_id，不以乱序收件箱、决策ACK或重新执行业务推断结果。不重复验证内部回执哈希。显示实际应用状态、回执缺口与结果引用。

OA权限权威种子追加736:query，仅默认授管理员；其他角色由IAM明确授权。原SQL Server种子历史保留，新行为仅PostgreSQL。两份前向迁移在隔离全新PG18实际执行，现有SFS子流程及APP子流程定向回归通过。

## 实际验证和一次集中审查

本地29项唯一相关后端行为通过，含真实PostgreSQL/HTTP收藏并发、原命令恢复、原子性、当前租户/资源/字段权限、未绑定与已绑定历史、准确发布版本、租户日历/DST、分页截点、真实连续业务回执、SFS/APP子流程回归、OA权限种子与互锁，以及实际Chrome→Vue→Main→PostgreSQL收藏断响应恢复和历史详情。前端20项相关组件行为与应用vue-tsc通过；浏览器截图已查看。各来源中的失败仍保留，不将混合运行改称全绿。跨领域3项权限互锁失败登记独立P1 #715，相关源码与开工主线一致，尚未做这些端点的默认管理员HTTP复测。无Actions、生产部署或既有环境迁移。

逐行为来源：D:/CP6-local-test/wave281/foundation/final-validation.json。真实浏览器内部API未mock，仅在首次收藏真实服务端提交后丢弃响应，核对同一原命令恢复，再显式取消；最终收藏命令2、收藏行0，原FormData/instance数量未受查询影响。页面读取未绑定v1与已绑定v2原schema/值，PG核固定版本保持。模拟的是用户、租户、业务、政策和登记输入，实际运行产品Owner、HTTP、Chrome与PostgreSQL。

真实失败及修复保留：初始无收藏端点404；PG微秒精度与.NET时间不一致导致收藏约束拒绝，改用持久化时间；代理fixture非法空bizTypes，修正为合同允许省略；未来模拟Intake及延后启动暴露分页成员时间混用，新增真实可见时间列并移除临时QueryClock测试钩子；测试复用next命令发生raw冲突，改专属history-next；UI测试导入/选择器/stub修正后定向通过。最终snapshot-final运行8通过1fixture失败，review-fixes修正该fixture并验证收藏/OA权限，15通过3跨领域互锁失败；这两次整体运行均不称全绿。

额外互锁失败已登记[BUG #715](https://github.com/GTX537/CP6/issues/715)。对应WMS/ERP/MES控制器、种子和测试与开工主线逐文件一致；未擅自修入本功能分支，未改豁免或降低门禁。该问题按P1进入下一独立修复窗口。

作者一次任务级SPEC/QUALITY审查覆盖完整差异：收藏原命令/并发/原子性/撤权、闭合过滤、当前读权与租户/代理边界、准确版本/字段披露、时区/DST和成员截点、内部回执投影、页面身份与错误、迁移及实际证据范围。截图已检查；无额外代理和重复全仓回归。未涉及的成功证据直接复用，原失败记录不覆盖。

## 真实余项与交付状态

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不变。普通FORM文件准入及最终下载留W285，完整字段/图诊断留W284，FLOW原竞争与legacy来源/旧caller留W286；退回/转交/加签W282、期限/Worker W283及其他业务Owner采用仍待实施。新发现跨领域权限种子P1 #715按规则作为下一独立任务，之后接续W282。

普通FORM upload仍需实际FILE父级/字段授权，不用工程Revision父级或拒绝全部文件冒充附件完成。没有新增原R1未要求的批量导出，也未新增/by-command别名；现原命令协议可恢复本窗操作。当前只关闭收藏/历史子缺口，WP12-05整卡保持原状态。

总目标仍IN_PROGRESS，按3工作日约24h有效执行；排队间隔7558秒已排除，后续按运行时锚点计入实际连续执行。下一累计50400秒校准。本提交是合并前快照，Main/Handoff远端采用、准确任务分支删除、工作区干净与官方finish/席位释放以C:/Users/tt/CP6-planning-reports/20261017-foundation/wave281-final-verification.json核销。主目录用户资料保留，无Actions触发或取消、工作流变更、生产部署或既有环境迁移。
