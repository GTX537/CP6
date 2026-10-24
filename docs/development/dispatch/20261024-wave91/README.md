# W91 报表中心独立Owner实施计划

COORD本聊天单人，沿用writing-plans/executing-plans及项目精简节奏。Main `9991c1734520bf7d9b421ad865485b27cc97d84d`，Handoff `194b2cdecad3322d7f5a87b1d5caf747913c7cc8`，Issue[488](https://github.com/GTX537/CP6/issues/488)。目标是修复当前实际入口权限缺口，完整WMS-ANALYTICS-01仍按WP10-44/45推进。W90已全部收尾，19.77min＋独立前置约4min，Issue485关闭；W89两原分支保存、deferred释放，跨仓重绑待已有确认。

## 前置和设计

AGENTS/POLICY哈希不变，官方status固定两树干净无进程，原仓start，无新目录/bind。最新远端Main已fetch，活动同任务Issue/PR未发现重复。当前服务/接口/Controller与WMS-ANALYTICS-01手册、WP10-44/45完整任务跨度已读；旧页面/实体/相关测试核对。未声称新全文阅读所有原始附件。旧五类报表和十条HTTP/CSV只登录后读数据库，缺当前独立Owner；月报混历史量与当前Allocated/均价，ABC缺完整发生身份，呆滞用CreateDate补证，旧流水截5000。

复用原生OperationsOwnerV1身份/业务表锁与最终重核，wms-report-center动作与Stock query来源按实际原UUID交集。report与source各自本人/部门2–4/全部及实际仓区过滤，源库存和源流水分别原对象绑定。新版本仅返回当前可披露的原记录，逐行原数量精确字符串、原单位/货主状态，未知不补默认，不造历史cut、正式Free、估值、排名、发生次数或范围呆滞。原有完整业务输入/存储不收窄，版本化查询与旧合同拒绝分开。

## 实施与验证顺序

- [x] 在`CP6.Tests/Erp/ReportCenterOwnerPostgreSqlTests.cs`写真实缺Owner三红灯：旧报告即使空也拒绝、原流水不泄露、通用CSV不在PG直接导出。命令`run_wave91.py owner-red Report_center_owner_missing --pg`，预期先3失败，保存原日志。
- [x] 新增`CP6.Entity/DTOs/Wms/ReportCenterOwnerV1.cs`、`CP6.Core/Services/Wms/ReportCenterService.Owner.cs`、`CP6.Core/Services/Integration/OperationsAccessV1.Reports.cs`。默认50/最大200，过滤与稳定排序后分页/真实总量；库存和流水分模式，不跨单位汇总。日期只过滤原TxnDateTime轴（date-only含末日并保留末日亚秒），不宣称Site时区或knowledgeCut。OperatorCd/OwnerCd/Remark当前字段MIN；跨业务RelatedNo等缺来源证据不披露，原值保留。
- [x] 修改`ReportCenterService.cs`/接口/Controller/Program、WmsMenuSeed/WmsPermissionSeed及单一PG目录迁移：481锚定wms-report-center，新增query/export定义不授角色。五旧PG报告Owner后409；旧HTTP CSV先独立export门再503，通用同步CSV入口PG直接拒绝以免逃逸验证。新只读GET no-store，不将Owner错误转400。非PG旧实现语义保留。
- [x] 修改`cp6.web/src/api/wms/reportCenter.ts`、新增`types/wms/reportCenterOwner.ts`并采用于`views/wms/ReportCenterView.vue`：当前库存/原流水两模式，精确原值与未知语义，身份/权限/筛选/模式/分页变化立即清空，迟返/失败/卸载不回填；正式报告/CSV未满足采用门不提供成功入口。新增真实页面行为测试`ReportCenterOwner.spec.ts`。
- [x] 原生PG覆盖源Owner交集、租户/部门/仓区/软删除、字段/无权来源、精度/未知、跨5000分页、亚秒/日界、参数冻结与最终权限故障、实际HTTP/CSV、原生SQL写锁；复用既有同对象IAM目录证明，仅按受影响范围新增行为覆盖。相关旧非PG报告和两租户种子、Vue与类型检查。一次作者完整SPEC/QUALITY集中审查，有明确风险才补局部验证。
- [ ] Main产品PR精确采用→Handoff DELIVERY/合并/准确分支删除/finish→原Main任务分支状态收尾PR→远端采用/准确分支删除/finish→Issue488关闭。每次push/PR检查实际工作流内容/路径，禁止触发Actions。PG55435隔离、自有进程停止、数据和失败证据保留。

预计45–70min：服务/权限/原生验证25–40，页面10–15，集中审查/采用/收尾10–15；独立前置粗估4min，上一轮重叠只读不重复算。实际Main start `2026-10-24T01:06:50.244228+00:00`，finish后据实校正。固定DEV-1/DEV-2保留，报告分支、工作区、席位。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN不变。N-G1–5、18AC及正式冻结报告/排名/估值/导出仍UNPROVEN/NOT_RUN；旧分支、原数据/非PG历史保留。七天ACTIVE。无Actions、既有环境迁移或部署。W89仅阻其已被自动审批拒绝的跨仓重绑，不重试/绕过；继续当前独立Main范围。

## 验证完成

24独立.NET（16原生PG、7既有非PG报告行为、1两租户目录种子）与9Vue通过，无跳过；相关构建及vue-tsc通过，定向补验去重合计33项。一次集中审查和最终源权限故障补验完成；PG均停止，待正常采用/收尾。

## 实际采用与最后收尾

Main [PR489](https://github.com/GTX537/CP6/pull/489) source `121dc62f27288eca82ad1ca455560e280eaa3d5a`采用远端main `5dfa31efd70989d645162964e70ee5d3f208a1aa`，27任务路径逐blob核对；Handoff [PR66](https://github.com/GTX537/CP6.AI-Handoff/pull/66)采用`567f465bac4180d7d3ebf3bad54627343f80f9e0`，准确分支删除、固定树干净detached、finish释放。17个产品路径与已测提交逐blob一致，复用有效结果，最后文档diff/台账生成单独检查。

Main同任务最终状态PR/finish及Issue488最后关闭待完成；当前约13.41min，原估45–70min＋独立前置4min，实际按finish不重复计Handoff。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave91-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续剩余独立Owner及D5/D6真实前置，正式Analytics WP10-44/45仍未采用。
