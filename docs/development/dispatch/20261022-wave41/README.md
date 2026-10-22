# W41-01：质量独立入口与关联事务

七天一期 D1；COORD 本聊天单人；[总计划](../../planning/20261022-seven-day-delivery.md)。基线 `f5d9a540513fa2fdea730bcad780cb3ae060cedd`，DEV-1/codex/w41-01。预计实际 4–6 小时，包括真实相关验证及采用收尾。W37–W40 不重复。

## 前置与边界

原会话/当前权限、可信字段/部门来源、W37–W40 OperationsOwner、序号事务和页面意图已采用。当前三质量服务仍缺独立 Owner；QC PASS 直接调用收货公共入口，MES NG 提交后才 best-effort 修改库存，必须核对共同事务。沿用正式合同/合法精度与租户约束；不新建双库实现或扩成全库 schema 改造。

首次 manage start 在 fetch 后被该调用自动启动的 Git repack 阻塞，180 秒超时；核实无任务登记后仅停止本次 pack-objects 子进程，现有引用/pack 保留。重新 start 采用进程级 maintenance.auto=false/gc.auto=0，已于 UTC 2026-10-22T04:10:19Z 领取，不修改全局配置。

## 实施步骤

- [x] 在 `CP6.Tests/Erp/QualityOwnersPostgreSqlTests.cs` 复现缺 Owner 的库存 QC 写入、入库检验读取、MES 品质读取；以现有隔离 PG/Harness 运行，保留红阶段。
- [x] `CP6.Core/Services/Integration/OperationsAccessV1.Quality.cs` 与 `OperationsOwnerV1.cs`：三个真实资源/动作，完整含软删除子图、原生关联键、可信范围与查询源、共享表 fence、最终检查。空/未知映射拒绝，数据权限筛选先于分页/统计。
- [x] `CP6.Core/Services/Wms/StockQcService.cs` / `.Owner.cs`：独立 set/by-work-order 的当前身份/库存和来源范围；关联库存按 warehouse/location/product/lot 的原生关系选取；全部授权后原子写，不接受伪造 userName。新增受限 internal 原语供已验证品质父 Owner 同事务使用。
- [x] `CP6.Core/Services/Wms/QcInspectionService.cs` / `.Owner.cs`、`InboundService.Owner.cs`：查询、from-inbound/direct、items、judge/cancel 全入口；判定/序号/收货/库存/最终授权同事务。内部收货原语限当前父 Owner 和准确输入，公共入口仍拒外来事务；通知只在确认提交后执行。
- [x] `CP6.Core/Services/Mes/QualityInspectionService.cs` / `.Owner.cs`：列表/详情/模板与 create/update；冻结输入，保留完整原生数量/测量值、实际 Actor/原 Creator；旧新关系和已有不良引用保护。创建时自动不良、工单状态和相关库存 QC 纳入当前 PG 事务。
- [x] 三个实际控制器接 query 权限、Owner 错误/no-store，保持 CSRF；Program 注册实际封存资源，无自动角色授权。
- [x] `cp6.web/src/views/wms/QcInspectionView.vue`、`mes/QualityInspectionEntryView.vue`、`mes/QualityInspectionListView.vue` 和实际库存 QC 页入口复用 `useOperationsIntent`；冻结写/确认目标，拒重复和 UNKNOWN 重放，隔离旧身份/目标/响应，保留已确认编号。新增相关 Vitest 行为覆盖。
- [x] 相关 PG/实际 Program HTTP 验证合法路径、越权/撤权、软删除子图、请求冻结、未知结果与序号/库存回滚、提交后通知；复用无影响的既有成功证据，运行受影响兼容测试和完整 vue-tsc，不做全仓回归。
- [ ] 一次集中作者 SPEC/QUALITY、单份 `docs/development/evidence/WP02-03/quality-owners/DELIVERY.md`、版本化合同、roadmap/四状态/生成页/Handoff；正常 PR、远端采用、准确清理与 manage finish。

测试入口复用已检查的 `C:/Users/tt/CP6-planning-reports/20261017-foundation/run_wave40.py`，仅更改本轮证据目录为 wave41；项目、NuGet 和编译缓存不复制。精确 filter/通过范围写入单份报告，不以数量为目标。

本窗口不宣称持久原结果/outbox、全 WP02-03、其他 WMS/MES 入口、真实 DEV/UAT 或 125/247 验收完成。W41 交付后继续七天目标 D2，无需用户再次请求下一轮。

实现、相关验证与一次作者审查完成；[DELIVERY](../../evidence/WP02-03/quality-owners/DELIVERY.md)。106项去重相关.NET通过（16项本轮PG/实际HTTP、67项既有Owner、23项兼容/权限），20项Vitest（10新增、10既有）及完整vue-tsc --build通过；所需范围0失败/跳过。正常PR/远端采用及原分支finish待执行。
