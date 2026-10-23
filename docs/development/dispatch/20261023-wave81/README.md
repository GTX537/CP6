# W81 / 七天一期 D2：库龄与效期独立读取Owner及旧处置入口隔离

COORD本聊天单人，开工2026-10-23T21:33:46.385120+00:00；Main `c6de50305c82fb4e03664f92c4e5ab432cc60af0`，CRM `82d726fbc4ae3d90a0b9cfcbf24b78516829fa17`，Handoff `ce5783b5e078e4f9cbeb9b7eec7e120284ea6790`。W80 Main463/CRM120/Handoff56全部采用，Issue118关闭，14项相关本地用例及S01/revision只读检查通过；实际13.87min＋前置约4min。三准确分支删除、固定树干净detached、全部席位释放，Actions0、本轮无PG启动。W37–W80不重复。

## 前置、合同与限定范围

AGENTS/POLICY哈希未变，status/capacity无告警；原非allowlist两目录不动，无重复该类开放Issue，登记[Issue464](https://github.com/GTX537/CP6/issues/464)。总台账WP02-03仍为部分采用，WP10-24/WMS-DISP-01及其WP09-09/WP10-04等完整前置未交付。核对最新源码、WP10-24任务和WMS-DISP-01模块及原S1-WMS-DISP-3范围，不把旧负ADJ当正式处置。

正式DISP必须有不可变Basis/Review、准确Part/范围、独立批准、Stock/NCR/货权/用途/OA/IAM共同最终门与Finance准确原结果恢复；旧EXPIRY_DISPOSE/bridge Skipped只保历史，不冒已过账。到期只是候选原因，不自动批准报废。当前Stock没有币种，Warehouse没有批准时区；不从租户默认/当前机器或字符串猜正式来源。

本窗为两独立读取入口的当前Owner与安全显示，保留完整DISP未采用。StockDwellService直接读库存与客户名，ExpiryService读库存/单价并逐行调用独立Stock.Apply，现有Stock.adjust门不替代DISP批准，逐行可能造成部分效果。按精简规则只覆盖真实受影响路径，不实施完整WP10-24新写模型。

1. 两服务旧读与明确版本的新读走OperationsOwner当前原会话、各自view动作和源wms-stock:query。自身数据范围与源Stock范围/仓库区位取交集，scope1实际Creator，2–4复用stock UUID显式部门绑定，5仍核可信租户及仓库。复用已有原生授权/业务锁和最终重核；拒绝缺Owner、旧会话、外层事务及预跟踪业务，不让空结果绕过授权。
2. 库龄Amount/CustomerName、效期UnitPrice/Amount与源Stock.UnitPrice按当前MIN字段决策；隐藏不造0，客户名只从有权库存对应的同租户实际对象投影。必要的view目录只登记不授角色；既有库龄view不替换为新query。新的字段/范围登记和单个PG目录迁移本聊天串行。
3. 新版读DTO以字符串保数量/单价及有来源的参考算术值，复用InventoryDecimalValue准确中间运算，保decimal(21,8)数量与decimal(18,4)单价全域，不经JS Number或decimal乘法舍入。严格区分旧库存单价参考观察与原生Finance估值，币种/财务结果无来源时保未知，不硬编码货币符号、不把缺价/隐藏聚合成0；旧表示无法无损表达时明确要求版本读，不伪造精确。
4. 效期新版使用明确选择的观察日期，提供实际分页/总数，范围先于分页；原默认机器日期/500条只是历史入口行为，不能冒仓库批准时区或完整DISP候选。未知到期/损坏来源、正式币种估值及Review候选仍待原任务采用。页面说明观察范围，日期不是授权、版本或真实处置依据。
5. PG旧Dispose在任何库存查询/编号/审计/调用下游/通知前明确503 DISPOSITION_OWNER_REQUIRED，包括空请求。不新建队列等待自动唤醒，不以更换RelatedType触发财务桥。HTTP no-store与统一Owner错误，UI阻止未采用命令/旧选择提交，不报成功或自动重试；保非PG原历史行为和用例范围。
6. 两页面采用具名Owner响应，身份变化、乱序/失败清理数据；数值文本无损，展示未知/隐藏与未采用处置。相应API/types/相关页面行为用例一起闭环。

## 估时、验证和收尾

预计实际60–90min：Owner/DTO及精确读25–40、HTTP/页面15–20、PG/页面定向验证与集中审查/合并收尾20–30；独立前置约3min，W80内只读不重复计时。沿用七天ACTIVE目标。

真实PG验证当前身份/撤权、租户/本人/部门树/自定义/仓库范围交集、字段MIN与源单价、极值精度、缺价、分页/日期边界、早期零效果、原生锁/最终授权及实际Program/login/HTTP；UI核乱序/身份与失败/禁止旧处置。仅相关构建/测试和一次COORD SPEC/QUALITY，成功未变证据复用。自有PG夹具原生55435串行启动停止，保数据/红灯。无Actions、既有环境迁移、自动角色授权或生产部署。

DEV-1 `codex/bug-464-inventory-report-owner`，DEV-2 `codex/w81-handoff`，均固定目录，不新增代理/工作树。正常PR、远端采用、准确分支清理、manage finish释放。24/375整卡、125目标0完整交付、247正式运行NOT_RUN不提升；本窗不关闭完整WMS-DISP/Finance或CRM公开受理。W51指定停止data删除曾被自动审批以blocked by policy拒绝，保留不重试。

## W81验证完成

23独立.NET用例（17原生PostgreSQL＋6既有InMemory）及27Vue用例通过，无跳过；相关构建与最终vue-tsc类型检查通过。既有Dashboard库龄小组件直接消费改后的API/type，类型检查发现旧number处理；COORD同窗口扩展至该组件/解析器及两份相关测试，精确文本与epoch失效已验证，其余看板域不动。新版days0–365与旧有负天数/午夜过滤分别保真实合同，不把参考算术冒Finance估值。4个自有PG停止保留，进入两仓采用与同任务最终收尾，七天目标ACTIVE。
