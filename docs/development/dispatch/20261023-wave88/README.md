# W88 / 七天一期D2：VMI当前观察与历史估算独立Owner

COORD本聊天单人，登记开工2026-10-23T23:48:27.229303+00:00。Main `760fdd91e22d6a81fe66429f3d040f651bd7e71a`，CRM `1a7f0838c3db87d0b736fbe1c88d4ba9a0b1841f`，Handoff `49895cb45ffd949d2f6447365f9b0ec66ebb9ece`。W87 Main480/CRM122/Handoff63全部采用，33独立相关用例通过，实际18.06min＋前置3min；准确三任务分支删除、固定树干净detached、全部席位释放，自有PG停止，Actions0。W37–W87不重复。

## 前置与限定范围

AGENTS/POLICY哈希未变，官方status/capacity无告警，D盘约207GB、C盘约104GB可用；根用户文件及非allowlist历史目录保持。当前源码VmiController/VmiService/IVmiService/VmiBilling/Stock、页面与原两项测试已核；无重复VMI Issue，登记[Issue481](https://github.com/GTX537/CP6/issues/481)。核对当前WMS-VMI-01手册、R2原件b6a79341caa5168a的业务/精度/旧账/权限/验收、WP10-39/40及四类采用门。旧平均模型和Confirmed布尔不能证明正式计费/发送/Finance接受。

1. WP02-03有界窗口接入wms-vmi当前原会话、独立动作和字段MIN，可信tenant/self/部门2–4/全租户范围及原生权限/业务读围栏。当前库存观察取本Owner与当前Stock Owner的实际行交集，复用既有真实stock Id部门映射及仓区授权；不复制新的虚构客户/仓库Owner身份。历史账单采用实际vmi-billing UUID映射，Creator本人和部门/租户范围，不因当前Stock客户集合推断旧账单完整授权。
2. 具名VMI-OWNER/1接口与稳定分页50默认/100上限。当前库存仅为已有Stock行观察，按原OwnerCd/UnitCd分组；未知单位不合并出总量，跨单位不求和。数量精确字符串；跨行聚合使用PG精确numeric或不丢精度的整数运算，不能把decimal(38,8)塞回.NET decimal后溢出。AvailableQty只标旧物化差额，Free=null、availabilityKnowledge=NOT_EVALUATED；不冒充RequestedUse/Root/Slice/历史cut完整性。EstimatedValue=null/UNKNOWN，不把Stock.UnitPrice作为正式估值或缺值补0。
3. 历史账单保原Begin/End/Avg/Rate/Amount/Confirmed，标POINT_IN_TIME_ESTIMATE；缺UOM/currency保UNKNOWN，不补JPY/PCS，不把Confirmed当AR已接受。本Owner字段保护CustomerName/Remarks，费率与金额各自当前字段及独立read动作，不因calculate/confirm权限自动获得敏感读取。只登记query/amount-read/rate-read目录，不自动授予角色。禁止按隐藏金额/单价排序、筛选或汇总旁路。
4. PG旧读入口409，calculate/confirm在当前动作和可见对象核对后明确VMI_OWNER_ADOPTION_UNPROVEN，发生任何序号/账单/库存/Finance写入前拒绝；现有非PG两项历史测试保持真实旧算法范围。真实G1–G4按日历史/合同/换算/币种舍入/OA/Finance仍UNPROVEN，不提供旧估算正式确认旁路。
5. 真实HTTP/no-store与页面独立Owner读取，三列表真实分页，年月展示到YYYYMM唯一合法转换，历史状态准确显示。身份、源权限、客户、筛选、分页及失败清旧值，迟到响应不串客户；数字字符串原样显示，不JS浮点计算；不显示未采用写操作。

## 实际估时、门禁与收尾

预计实际50–75min：当前Owner/来源/原生范围20–30，精确读/分页/页面15–20，定向验证和一次集中审查/两仓收尾15–25；独立只读前置约3min，与W87收尾重叠的核查不重复计。原七天持续目标ACTIVE，D5仍缺真实权限生产者与Site/Form业务最终门。

使用现有自有PG夹具55435串行启停；先复现无Owner读/计算/确认，再验tenant/self/实际UUID部门/仓区、源行交集和撤权/字段MIN、混单位/未知量纲/精确数与聚合边界、超100分页、旧写零效果、原生读围栏及实际HTTP。相关Vue覆盖权限/失败清空、乱序与分页、精确字符串、无默认币种和不可用写按钮；仅必要构建/局部用例/类型检查，无全仓或Actions。

DEV-1 `codex/bug-481-vmi-owner`产品与协调，DEV-2 `codex/w88-handoff`沟通，固定席位复用，本聊天单人无代理。公共注册/映射迁移串行，正常PR、远端采用、准确分支定向删除、finish释放。24/375整卡、125目标0完整交付、247正式运行NOT_RUN保持；新接口是当前观察/历史读Owner，不是完整WP10-39/40。W51指定停止data删除曾由自动审批blocked by policy拒绝，保留不重试。

## W88验证完成

18独立.NET用例（15原生PG、2既有非PG VMI行为、1两租户启动种子）及22Vue用例通过，无跳过；相关构建和vue-tsc通过，最终权限故障定向补验1项并入原15项、不重复计数。一次集中审查完成，5个实际PG停止保留；40去重项不代表完整业务验收。正常采用与同任务收尾后继续，七天ACTIVE。
