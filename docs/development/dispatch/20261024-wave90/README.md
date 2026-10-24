# W90 / 七天一期D2：套件作业独立Owner

COORD本聊天单人，2026-10-24T00:44:59.470277+00:00。Main `d65abaf82b6b0ac339d0fc3b6ce945dfc82ddf4f`，Handoff `d386156a175ccc0cdb70c5132a365cfc7a9d8343`。Issue [485](https://github.com/GTX537/CP6/issues/485)。W37–W88完成不重复；W89仅准备文档PR484采用，CRM未开工，跨仓bind仍待自动审批拒绝后的明确确认。W89 Main/Handoff原任务分支已推送保留、save-only finish deferred释放，不绕过被拒操作。

## 前置与范围

AGENTS/POLICY哈希不变，仅根AGENTS适用本产品路径；官方status/capacity无告警，D盘206.76GB/C盘104.15GB可用，根用户文件/非allowlist历史目录保持。DEV-1/DEV-2原仓复用，Main与Handoff均已官方start，不涉及跨仓bind或新增物理目录。

已完整读取当前KittingService、Controller、三实体、WMS-KIT-01及WP10-26/27任务边界，并检索活动Issue避免重复。现有GET只登录、服务直接返回实体；旧写服务无当前独立Owner，Execute运行时读可变当前配方并逐腿ApplyAsync，不证明原始Composition或全组原子。Issue485只处理实际旧入口安全边界，不借此提前放行正式套件业务。

1. 为主数据/组件与历史订单增加原生PostgreSQL只读Owner：实际可信会话和wms-kitting查询动作、租户、自有/部门/全部范围、真实KitMaster/KitOrder UUID及仓位范围，查询前过滤且锁内最后重核。两类对象各自可信部门绑定，不按SKU字符串/当前配方代替历史订单归属。
2. 精确数量使用字符串；组件数量保持原单位，不裸加总或据当前配方推算历史组成。分页真实总量及稳定排序，默认50/最大200。KitName、ComponentName、OperatorCd、Remarks和ExecutedTxnNos按当前字段最小权限输出；不透明旧流水仅在完整来源披露可证明时返回原文，否则null并解释。旧Executed/Cancelled只作历史标记，Composition/物理结果仍UNPROVEN；不将旧Draft无流水视作已证明零效果。
3. 所有旧PG查询经Owner后要求使用只读版本；所有旧写入先做当前动作/对象可见性，再以KIT_OWNER_ADOPTION_UNPROVEN拒绝，保证实体、序列、库存、消息零变化。原非PG实现私有保留其历史范围。正式版本定义、原工程Ref、准确换算、整组QuantityUoW、取消/恢复/物理闭合均留WP10-26/27。
4. 页面使用独立Owner读取当前主数据和历史订单，显示精确原值与未知边界；身份/权限/筛选/分页/模式变化清空旧数据，迟返不得回填。可信部门映射目录和最小seed/迁移仅登记能力，不给角色自动授权。

## 时间、验证、收尾

预计实际45–65min：Owner/绑定及旧写门20–30，页面10–15，PG定向验证/审查/两仓交付15–20。独立前置粗估4min，其余只读核对与W89保存过程重叠，不重复计。Main实际start `2026-10-24T00:43:24.568770+00:00`；等待W89确认不计为本窗执行耗时。

先实际PG复现缺Owner读取/创建/取消，再覆盖租户/主体/部门2–4、仓位、字段/来源披露、精确数量、真实分页、所有旧写零副作用、提交前故障、真实HTTP与原生锁；相关非PG历史用例和页面/类型检查按影响执行，一次任务级集中SPEC/QUALITY。自有PG使用现有隔离fixture（55435），结束停止并保留原日志与数据。无全仓回归/Actions/既有环境迁移/部署。

Main产品PR采用→Handoff交付合并/准确分支删除/finish→同Main任务状态收尾PR→远端采用/准确分支删除/finish；固定目录保留复用，报告分支、工作区与席位。W89保留分支不混入本任务；其确认到达后在安全任务边界续接。完整卡24/375、125目标0完整交付、247正式运行NOT_RUN以及KIT的K-G01–K-G06/15AC不提升，七天目标ACTIVE。

## W90验证完成

24独立.NET用例（16原生PG、7既有非PG套件行为、1两租户种子）及25Vue用例通过，无跳过；相关构建和vue-tsc通过，定向补验与原组去重合计49项。一次集中审查及有据定向补验完成，3个实际PG停止保留；49去重项不代表完整业务验收。正常采用与同任务收尾后继续，W89确认仍待答复，七天ACTIVE。
