# W286-01 定义并发与旧入口真实处置

W286：FLOW两真实作者同token/下一草稿竞争、旧定义写入与页面调用面处置、当前权限下的明确未核实历史来源已完成限定验收；缺版本绑定不再推导历史pin，办理在修改票据前快速失败。

最新确认Main基线 `53a805b15f04408547188247e730fd8896b20cb9`；COORD本对话单人复用DEV1/Main与DEV2/Handoff，无子代理。沿[24小时持续计划](../../planning/20261102-codex-24h-oa.md)，本报告只关闭本窗口，原整卡和正式验收状态不变。

## 原验收与产品行为

| 原OA-DEF-01 R1场景 | 本窗口实际结果 |
|---|---|
| AC-D01-03 两作者同旧token | 两名真实模拟IAM作者、独立PostgreSQL连接；观察实际数据库Lock等待，一胜一409，败方内容不覆写，命令同ID按实际作者分槽，只有赢家原件。 |
| AC-D03-04 旧helper/旧designer不得绕过 | 下表完整当前调用清单；所有旧定义writer明确拒绝，三个实际设计器入口改用现有v2草稿/发布，发布不启用。旧DTO不被悄悄扩展为v2。 |
| AC-D04-03 并发下一稿仅一版 | 两真实作者/连接争抢同Published源/token，唯一下一稿与版本号，源原文/发布时间不变、原件可恢复；同作者独立连接场景亦通过。 |
| AC-D04-04 当前head重建明确来源 | 新版本化只读接口/页面区别CURRENT_HEAD_RECONSTRUCTION、LEGACY_VERSION_SNAPSHOT、LEGACY_HEAD_BACKFILL；全部明确UNVERIFIED_LEGACY，只读，不制造历史发布人、时间或运行pin。 |

`GET /api/oa/v2/legacy-definitions/{FLOW|FORM}` 与指定key/可选versionId按当前租户、启用菜单、真实身份和字段权限读取；旧Creator只是自由文本，不能推断Owner/部门，因此要求当前该定义资源的全部数据范围(Scope5)。列表无Schema；详情只投影当前可读字段。旧版本仅包含当时确实存储的Name/Schema，不拼接今天的FormKey、身份码或分类。准确版本缺失404；分页保留101个历史版本。已知回填行不把回填时间/人展示成历史发布时间/人，底层记录不修改。

旧FlowDraft原协议不接收新创建意图（410），但严格原body/actor/operation/key的已提交结果仍可读取；撤销写权不抹去原事实，当前读权/字段/租户/身份仍裁决，隐藏字段不能通过不可裁剪旧DTO泄露。当前Actor和到期检查直接复用v2边界，不新增资格包装。页面遇410停止重试新写入并保留已有意图查询。

## 调用面逐项处置

| 当前入口/调用方 | 处置 |
|---|---|
| FlowDefService、FormService的SaveDef/SaveDraft/Publish；DesignerService Save/Clone；FlowAdminService SetEnabled | 服务边界立即DEFINITION_V2_COMMAND_REQUIRED，无跟踪改动/写库；不提供假成功或自动重解释。 |
| DefinitionController旧flow/form draft/publish/versions，DesignerController旧list/load/save/clone，FlowAdminController旧list/get/enable，Wf Flow/Form def GET/POST | 授权会话及既有CSRF之后410，给出明确错误和当前读/写入口；真实HTTP验证16个代表路径，其他同一Action路由由完整diff核对。ServiceCatalog继续原只读合同。 |
| FlowDraftOwnerV1 / draft-context、drafts、draft-operations | 新意图停用；只保留严格已有原件查询，当前IAM/字段权限采用同一边界。 |
| OA DesignerView、Wf FlowDesigner、Wf FormDesigner | 现有路由直接使用FLOW/FORM v2 Owner工作区；保留完整schema、精度、受控编辑、撤销/重做和原件恢复；关闭后可查看只读历史来源。 |
| NodePropertyPanel子流程目录 | 当前Owner资源/字段投影、分页、身份变化清理；不再读取旧Designer列表。准确发布时仍使用既有依赖pin合同。 |
| FlowAdmin页面 | 默认准确版本启停；保留绑定、FORM启停、触发器、连接器和批量转交，移除旧直接开关；增加只读历史来源。 |
| LegacyFormInitiate、LegacyInbox新建入口、LegacyFormQuery流程筛选 | 未带准确发布选择的旧填单链接明确停用并指向当前目录；历史信箱新建转当前目录；历史查询可输入原流程Key，不再静默吞掉旧定义列表410。既有历史查询/实例数据保留。 |
| 未挂路由的Wf TodoCenter及旧API客户端函数 | 源码历史保留；/wf/todo早已转/oa/inbox。若外部/旧客户端调用退役定义传输入口，仍精确410；不声称这些未采用页面可继续新写入。 |
| 内部GetDef/GetVersion/DefinitionVersionResolver及历史业务运行消费者 | 内部旧读DTO保留；GetDraft即使true也纯读。原始无pin实例及无准确子依赖立即UNVERIFIED_LEGACY，不从当前head/latest补造。办理先取准确源再改票据，并复用同一schema。已存准确pin运行/当前原生APP/FORM仍验证。 |
| OaP0MigrationService显式人工历史回填 | 保留历史迁移代码，未运行/未授权迁移；不把它算日常定义写入口或已通过生产迁移。读取其旧回填记录明确标识不可信来源。 |

## 实际验证与一次集中审查

33项唯一相关PostgreSQL/HTTP行为与2条实际Chrome/Vue→Main→PostgreSQL链通过，0跳过；52项相关Vue行为及vue-tsc --build通过。历史运行/规则行为489项通过或按真实来源复用；6项W283已记录的SQLite排序规则基线失败保留，不宣称全套通过。

逐行为最终来源和原失败见 `D:/CP6-local-test/wave286/foundation/final-validation.json`。第一条真实Chrome链通过已有三条设计器路径完成FLOW下一稿、修改、发布及FLOW/FORM历史只读展示，数据库核对原版本/历史记录不变，无业务实例或表单数据。第二条真实链覆盖FORM/FLOW发布、准确激活、普通表单丢响应原件恢复、提交、停新及独立代理办理，当前原实例pin不变。浏览器内部API无mock；仅丢响应场景在已真实完成请求后中断客户端接收，原件由真实Owner查询。

使用显式模拟IAM、业务/历史数据、政策和授权；实际PostgreSQL18/Main HTTP/Vue/Chrome。本地旧运行规则测试保留，夹具明确插入既存历史版本/依赖/pin，不再用被退役生产发布helper暗造前置；它们不充当发布/授权证据。SQL Server历史测试只适配夹具，未运行。6项SQLite Latin1_General_100_BIN2既有失败引用[W283基线记录](../W283-01/DELIVERY.md)，保留失败并按PostgreSQL基线交付，未删除/跳过测试或宣称该套全绿。

一次任务级SPEC/QUALITY集中审查覆盖全部新增/修改生产代码、调用面、相关测试与夹具和文档。发现并修正缺pin批准时先改内存票据的问题，真实PG先红后绿；修正旧原件读取重复身份边界并复用当前Owner到期/禁止冒充检查。初次历史夹具、重复scope登记、两处迁移测试断言及Chrome日期夹具格式失败均保留，修复后只跑相关范围；成功且未受影响的来源复用，不因提交SHA变化重复测试。未改变合法数字精度/租户/Owner/原子性，无新迁移、依赖或工作流。

## 剩余、时间与交付状态

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不改。WP12-01外部选项Owner、WP12-02实际服务节点Owner及其他整卡余项保留；下一窗口先核原卡和当前源码，不能重复W280/W281/W284/W285。

当前本地实现/验收/审查已完成，远端采用、准确任务分支删除、工作区干净和官方finish/席位释放仍由 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave286-final-verification.json` 最后核销。本提交是合并前快照，不先称已释放。两固定席位复用；用户主目录资料和可复用缓存保留。24小时持续目标仍IN_PROGRESS，排队/空等不计有效执行，下一累计20小时按真实耗时校准。无Actions触发/取消、生产部署或既有数据库迁移。
