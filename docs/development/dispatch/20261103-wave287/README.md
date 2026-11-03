# W287 START

原OA-DEF UC-D04/D05与OA-FORM提交：外部选项来源的明确版本合同、实际共享字典Owner适配、当前字段授权候选读取及最终服务端值准入；设计器和普通表单页面采用。

完整原OA-DEF R1、OA-FORM R1、S3 Common R1与WP12任务卡已核对。当前schema只验证静态options结构，个人表单select/radio/checkbox只核JSON类型；未采用外部选项声明或最终值资格。已有Sys_DictTypes/Sys_DictDatas是无TenantId的共享参考字典，现公共options读面已面向登录用户；新适配明确SHARED来源，不能伪装租户私有业务选项。现有FORM/IAM/字段/事务/原操作直接复用。

仅支持明确版本的共享字典参考选项，按准确类型ID定位，值保持原字符串；不接任意URL、SQL或跨业务Owner身份。当前候选与历史原值分开，来源缺失/停用/重复映射明确拒绝，不用空集合或静态options伪装来源成功。PostgreSQL并发保护只作用该共享源，新增迁移仅在隔离测试库执行；不迁移既有环境、不部署、不扩大已有共享字典可见范围。其他业务Owner专用选项与服务节点执行按实际余项保留。

W286 PR724/263与BUG725 PR726已远端采用、准确分支清理与官方finish。固定DEV1/DEV2复用，单人执行。预计0.75–1.75h低置信度；下一累计72000秒/20h校准，24h目标持续。
