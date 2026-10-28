# W207-01 库位层级策略与结构预检交付

COORD单人。W206 Main629/Handoff182已核实合并、分支删除和席位释放，本轮只归档一次；原七天目标继续。

## 交付

真实仓库独立层级策略Owner/API/UI，显式根与边集，独立当前发布权限及原生仓库/父对象范围，原生树兼容检查、原件/CAS/审计同事务，原生结构变化触发HierarchyRevision。名称变化不升结构版。查询/回查历史和UNKNOWN封闭不重新执行原写入。详见[合同](../../location-hierarchy-v1.md)与[范围](../../dispatch/20261028-wave207/README.md)。

## 验证与审查

45 distinct PG18/Program HTTP; 58 distinct Vue;types/build PASS;0skip。实际证据 `D:/CP6-local-test/wave207/foundation/final-validation.json` 列明每例的真实来源和最终源码绑定。第一轮45项PG18/实际HTTP通过；集中审查后仅重跑层级Owner受影响检查，旧仓库/草稿验证准确复用。前端第一次测试隔离遗漏vue-router传递依赖，未执行新页面用例；补全API mock后相关用例、类型和构建通过，原失败保留。一次作者完整SPEC/QUALITY审查；发现的原生代码比较问题统一按数据库实际语义核对，库位预检使用稳定ID。

相关覆盖：显式根/跨级边、循环、当前树不兼容、同值/旧CAS、独立1–5部门范围、部分区域不能控制整仓、撤权/原键恢复、原始篡改/缺捕获、真实原生结构变动和最终回滚、并发CAS/close、实际Program认证/CSRF/闭合输入/显式null、浏览器互斥/UUID恢复/晚响应清理。无Actions运行或触发变更。

## 交付状态与剩余

登记耗时15.75分钟（Main官方START至本快照，预计90–150；不叠加Handoff）。Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/183，source `677ff60d82d2799e7b8dac430404c90d5498d3c1`，目标 `c62cf6e8aa67139884204b979540366ede3631a0`，精确全文采用已核；本轮Handoff分支已删除、DEV-2已释放。Main待当前正常PR/远端采用与官方finish，精确分支codex/w207-01由本轮统一收尾。固定DEV-1/DEV-2复用，缓存和根目录用户资料保留，无新增工作树。

本轮完成的是层级策略和只读结构预检。没有创建新Location或证明InitializationFence；创建/启用许可始终false。旧Location/库存/预占/生命周期未重写，W206仓库使用围栏保留。后续先完成所有实际旧读取/写入/分配入口隔离，再接DRAFT+QUARANTINED/UNKNOWN初始化与当前层级策略；仍需完整规则/生命周期Owner保护、仓库可选组织关系与正式Transfer/Stock采用。原125目标、247正式验收、全部完整任务及其他七天出口均未据此改为完成。无部署、既有环境迁移或生产验收。
