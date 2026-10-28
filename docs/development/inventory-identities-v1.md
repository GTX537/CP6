# 库存身份独立 Owner v1

W204 在 Main 提供 `Main.Wms.InventoryIdentity.v1`，入口 `/wms/inventory-identity`，API `/api/inventory-identities/owner-v1`。这是 D2 所需的真实身份资料和产品选项来源。本轮不写库存数量账，不构成 Transfer T01/T02/T04 的完整采用，不替代 MASTER、位置、货权使用、预留或实际库存写入的最终权限与共同事务。

## 记录与权威范围

- PARTY：SELF 明确保存法律主体名称、注册管辖地和登记号码，禁止以 TenantId 或缺失客户推断自有。CUSTOMER 引用真实 ERP BusinessPartner UUID/version/digest，要求其当前客户标识及完整字段读取权限。自有资料是系统内独立确认的身份配置，不宣称已完成外部登记机关核验、资产或会计认定。
- PURPOSE：显式选择 TRIAL、PRODUCTION 或 OTHER，并说明含义。它是带版本的原始用途定义，不修改已有 Stock 的原始用途，也不证明当前生产资格。
- PRODUCT：准确组合 ERP 产品原件、产品单位绑定、单位配置与基本单位、双 Owner 工程映射、已确认 PARTY/PURPOSE。KNOWN 引用真实冻结工程基线；NOT_APPLICABLE 需要明确依据，审批时还必须通过实际 ERP Product、PLM EngineeringProduct 的 `confirm-inventory` 行为及对象范围。UNKNOWN 不能成为正常新选项。

代码、记录种类及主体稳定身份不能原地替换。内容修订产生 DRAFT；另一位有权人员 APPROVE 后才可选择。DISABLE 保存后继版本，原版本仍可按版本读取。同一内容的 SAVE 不增加版本，但保留该次原操作回执。

产品目录使用既有 ProductOption 字段：productRef、label、baseUomRef、precisionPolicyRef、technicalIdentity、originPurposeRef、owner。KNOWN 只设置 revisionRef，N/A 只设置 applicabilityRef，后者指向经过双 Owner 确认的准确 PRODUCT 策略版本。单位及精度引用采用基本单位配置 UUID/version，精度策略作用于换算后的基本数量；输入单位配置另保留在来源原件中。例如整数输入单位的一件等于 0.5 基本单位时，不能误用输入单位的整数精度拒绝合法的基本数量。货权 CUSTOMER 返回原 ERP 客户引用，SELF 返回独立主体配置引用。目录同时返回准确目录引用及来源原件，并固定声明 `REQUIRES_FINAL_OWNER_GATES`。不以同名、同码、当前标签或候选默认值合并身份。

## 事务、权限与恢复

记录按受信环境、租户隔离。query/add/edit/approve/disable 单独授权，支持本人、部门、子部门、自定义部门及全部范围；新建使用当前创建权限版本和显式可信部门绑定。迁移只添加菜单/动作定义，不授予角色权限。

所有真实来源在调用者的 Main PostgreSQL 事务中读取和锁定，最终提交再检查实际权限。已保存确认人的身份仅从不可变原件提取，并重新检查其当前动作及对象权限；无关权限修订本身不作撤权。来源变更、停用或相关权限撤销会阻止新选择。历史披露同样要求当前来源对象及字段权限，不能利用聚合原件绕过客户字段隐藏。

KNOWN 原件包含受保护的基线评审链，当前操作人必须满足原 PLM 基线、工程项目成员及评审原件读取条件；仅有库存身份菜单权限不够。本入口不会自动给仓库用户增加工程项目成员身份。正式 Transfer 的最小选项投影和消费权威仍需在后续采用中单独落实。

数据库保留版本原件、SHA-256、单调 head、来源实际原件存在性及延迟原操作外键/回执约束。Owner 检查必需触发器实际存在、函数、类型和启用状态。原生冻结基线允许的大原件不会被此聚合层另设较小的 2 MiB 上限。

命令携带独立 OperationId、ExpectedVersion、Reason；只允许封闭 DTO。相同键和相同输入返回原结果；改用该键提交不同输入被拒绝。查询/关闭未知操作与写入在相同权威锁下竞争，关闭后不能迟到提交。浏览器只保存环境/租户/用户隔离的原操作 ID 和对象 ID，使用 Web Locks；未知结果不自动重发命令。身份变化、当前拒绝和迟到响应均清除或丢弃页面私有数据。

## 验证与剩余范围

本轮证据见 [W204 交付报告](evidence/W204-01/DELIVERY.md)。仅使用隔离 PostgreSQL、实际 Program HTTP、相关 Vue/类型/构建检查。DEMO 测试数据不构成真实业务验收。

仍需实现正式 Transfer 的上下文/MASTER/当前来源资格组合、T04 草稿预算的正式 Owner 采用、Stock 和相关 WMS 写入的真实库存身份及原子效果。WMS 独立货权用途配置本身不证明法律外部真实性、PLM 发布/验证或量产许可。七天阶段 D2 及完整 WP04-33/WP10-03/WP10-10 继续保留未完成状态；D3/D5/D6/D7 的原有出口也不变。
