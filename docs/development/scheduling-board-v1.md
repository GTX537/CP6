# 统一排程看板 v1

W244 补齐 MES-SCHED-01 RECOVERY-R2 的统一查询捕获、筛选/KPI、稳定分页和准确操作详情。生产业务验收可以使用模拟资料；本实现实际运行 Main Owner、PostgreSQL 和 HTTP，模拟输入及部署状态分别记录。

## 输入和权限边界

`GET /api/mes/scheduling/board-context` 返回当前 Planning/WorkOrder/Production 三个对象范围交集中的工厂、时区、完整 Process/Workgroup/Resource 引用和工厂本地当天至14日后的默认区间。首次访问明确选择工厂；只恢复当前仍授权的上次选择。Site 身份不由旧 BaseCd 猜测。

`POST simulation/board-sources` 在 SchedulingSimulation 开启、simulate-source 和完整 Scope 5 下接收 DataKind=SIMULATED、版本化来源引用、准确原生 Root/操作清单、Site/Zone、上游工单状态、可空交期 instant 和操作的工序/班组引用。历史操作可给出显式资源映射；已采用 Root 的 Site/Zone 必须匹配，资源与绑定直接采用当前 SCHED 合同，不能由此覆盖。来源初版为1，后继严格沿用原身份并加一，完整原来源保存到已有不可变 documents；不增加资格登记层。

已采用操作的计划、Occurrence/Snapshot/五域、数量及单位基准来自当前 Root，执行状态/实际时间来自当前观察，队列头来自 Root。来源缺失标为 UNKNOWN。旧原生操作只读，没有新 Owner 身份和单位基准时不制造 Snapshot/Occurrence/Expected/精确数量。旧工单状态6明确为 LEGACY_INSPECTED，不映射为 CLOSED。旧无偏移时间按显式工厂时区解释；遇到 DST 重复或不存在的本地时间保持未知，不猜偏移。

## 一次筛选、同一捕获

`GET /board` 必须传 siteId、from/to（带偏移完整时间）、zoneId、statuses（逗号分隔，ANY 必须单独明确指定）。可选 processRef/workgroupRef/resourceRef 是完整引用 JSON，sort 为 START_ASC、PRIORITY_DESC 或 WO_ASC，pageSize 为20/50/100。from<to，最长93日；初页不能带非零 cursor。

全引用筛选、状态和日期交集只应用一次，然后从同一结果计算 KPI 与页清单。日期条件是计划或实际区间与窗口重叠；两者均无的未排操作保留在未排区，不补造时间。稳定排序以 Root、路由次序和准确 OperationId 收尾；同工序代码的不同 Occurrence 不合并。

首请求持久保存 actor、租户/环境、规范查询、当前可见 Root 边界、queryScopeHash、asOf、全部结果和 KPI，30分钟到期。后续 captureId/cursor 请求采用相同查询和原结果；当前来源更新不会把新旧页混合。每次读取仍检查当前能力和原 Root 可见范围；撤权不返回旧捕获。过期返回409。已知工厂范围无匹配结果是 COMPLETE/空集合；无法读取不是空结果。

捕获复用已有 scheduling/authorization 围栏。旧原生两表另有独立写入口，因此投影读取期间短持 SHARE 锁，确保工单和操作读于同一切面；读取不修改业务事实。准确单操作详情 `GET /operations/{id}` 返回当前投影及已采用工单的完整 RootScheduleContext；历史资料没有可写根上下文。已采用的改期、排列、队列、换机协议沿用 W240–W243。

## 统计和冲突

FormulaVersion=BOARD_FILTERED_OPS_V1：工单数按筛选结果去重；执行中/完成工单按当前来源工单状态计算。操作完成率为 COMPLETED / 纳入操作数，取消和未知另列并不纳入分母；分母零显示 N/A。来源或执行覆盖未知时指标 value=null/UNKNOWN；工厂存在不可见 Root 时 value=null/PARTIAL，不返回隐藏总数。

延迟按筛选结果中的操作与明确交期比较：已完成用实际结束；未完成用计划结束与 asOf 的较晚值；取消操作不判延迟。工单取筛选操作中最晚结束，平均天数只平均延迟工单，按 decimal ticks/天计算并保留6位；没有延迟工单是 N/A，已完成却缺实际结束不能报告零延迟。缺失的来源与明确无交期分别处理。

资源重叠使用物理资源身份和计划/实际区间；可见对方可带准确 OperationId，隐藏对象只返回无身份的占用区间。执行中或未知状态缺实际占用时返回 RESOURCE_OCCUPANCY_UNKNOWN，不能声称不存在冲突。该只读提示不代替提交时 Owner 的当前资源检查，也不授予执行或改期权限。

## 页面与范围

`/mes/planning-board` 默认展示统一看板，原系统只读参考按需打开。日/周切换仅改变显示，日历轴按工厂时区推进日期，DST 保留实际23/25小时；计划与实际分层，交期独立标线，延迟为红边。缺计划进入未排区；实际未知不显示零。单操作详情按准确 ID 读取。

筛选请求序号阻止旧响应覆盖新查询；身份变化或授权失败清除旧私有结果。其他失败保留上次成功捕获并标 STALE/asOf；首次失败显示未知。分页过期保留旧数据，不拼接新捕获。统计与页使用同一 captureId/queryScopeHash。

本窗口不宣称完成原 D3 全部 AC01–36、拖拽草稿/全部独立详情路由、浏览器布局验收或跨阶段综合验收；这些继续按原任务逐项核对。完整目标不因本窗口关闭。实际结果和生命周期见 [W244 单份报告](evidence/W244-01/DELIVERY.md)；生产部署单独记录。
