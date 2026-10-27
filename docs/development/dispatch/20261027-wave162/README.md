# W162：修复实物登记原码点与版本化兼容

COORD 本聊天单人，CP6-7D-20261022 持续目标 ACTIVE，所有原出口保留。W161 已完整收尾，不重复。前置核对四仓远端、AGENTS、台账及固定席位通过。

## 依据与任务

[BUG #582](https://github.com/GTX537/CP6/issues/582)：W161 对 Evidence/SizeNote/Remarks 做 NFC，违反 WMS-TOOL-01 R2 第66行的字符串原码点要求，造成原输入丢失和同键异输入混同。遵循 BUG 优先规则先修复，再推进原可信关联范围。工作区任务 BUG-582-TOOL-REGISTRATION-UNICODE，分支 codex/bug-582-tool-registration-unicode（官方 manager 要求任务与分支规范一致）；台账窗口 W162-01。

新版 tool.registration.v2 仅新登记采用，字符串保留原码点、金额仍规范化。旧 v1 原件与恢复继续使用原编码，不能重写历史或宣称可恢复已丢失码点；在返回的来源/结果版本及页面上说明历史事实。独立 Owner 身份、原操作键、同事务权限和关闭规则不变。修复覆盖真实新建与旧记录初始化，隔离 PG 失败回归及真实 HTTP、页面验证。

## 实际执行预算

预计25–45分钟：失败回归及实现10–20，兼容性/页面验证8–15，集中审查与两仓合并收尾7–10。Main 官方 START 到 finish 计时，Handoff不重复相加；前置只读调查单列。

- [x] PostgreSQL 失败回归证实原码点丢失/输入混同。
- [x] 新编码保原文、旧编码真实恢复及版本展示，验证原操作/权限/关闭。
- [x] 一次 SPEC/QUALITY 审查、台账和四状态同步。
- [ ] 两仓正常 PR、远端采用、Issue处置、准确分支删除与官方 finish。

后续仍交付可信客户/产品/库位及工程候选。已找到实际 Space_Site.WarehouseCd 映射，需要对应当前权限和来源版本，不据此提前宣称已实现。完整WP10-48、D2/D3/D5/D6/D7、125完整目标和247真实验收保持未完成。仅本地隔离PostgreSQL验证；无Actions、既有环境迁移或生产部署。

## 实际交付

[报告](../../evidence/W162-01/DELIVERY.md)，登记26.02分钟；验证及集中审查完成。Handoff已释放，Main待正常PR/Issue处置/finish。原完整范围保留。
