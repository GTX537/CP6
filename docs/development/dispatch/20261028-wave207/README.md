# W207 库位层级策略与稳定身份预检

COORD单人继续CP6-7D-20261022 D2；WP10-01/WP10-10限定窗口。W206 Main629/Handoff182已完成，DEV-1/DEV-2复用。

## 实际执行预算

1. 显式层级策略Owner、版本化原件与实际既有树兼容检查：40–65分钟。
2. 稳定父ID预检、独立策略页面与原操作恢复：25–40分钟。
3. 定向PostgreSQL18/实际HTTP/UI验证、一次作者SPEC/QUALITY审查、合并及分支/席位收尾：25–45分钟。

合计90–150分钟，按Main官方START至finish计算，不叠加Handoff。七天持续目标ACTIVE。

## 已接受合同与限定边界

WMS-MASTER-01 v1.1.1 §6.2/6.2.1要求显式HierarchyPolicy，不能从DEMO推导生产根层级/跨级规则。本任务实现真实仓库作用域的显式策略发布：当前wms-warehouse:hierarchy-policy权限、当前仓库/部门/WMS完整仓域、根层级和边集输入、不可变版本/CAS/原键审计共同事务。无默认策略，无自动角色授予。拒绝与当前原生树不兼容的启用策略；结构变更有独立HierarchyRevision，名称变化不增。

按稳定父ID预检同租户同仓、可读父、无环及精确策略边；结果只是当时结构检查，明确非创建/启用许可。原生Location隔离读取和typed规则未齐，本轮不开放正式创建、不移除W206仓库使用围栏。后续先落实InitializationFence再接创建。保留当前所有库存/预占/生命周期。完整MASTER、Transfer/Stock及原七天出口不标完成。

精简本地验证：CAS/并发、当前撤权和部门/区域范围、历史/UNKNOWN恢复、原始篡改、真实原生树及HTTP闭合输入、Vue/类型/构建。无Actions、既有环境迁移或部署。

## 实际交付

[报告](../../evidence/W207-01/DELIVERY.md)，登记15.75分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
