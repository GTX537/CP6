# SQL 种子与辅助说明

[返回文档中心](../README.md)。本目录主要保存菜单、动作权限、多语言和模块辅助种子，按文件名前缀查找对应模块，例如 `wms-`、`space-`、`pur-`。

这些文件可能是特定阶段的一次性脚本。目录列表不是执行顺序，也不是数据库迁移入口。使用前阅读 SQL 内容，核对目标数据库、租户、依赖、幂等性和现有迁移；本次整理没有执行任何 SQL。

相关说明：[WMS 权限键](wms-permission-keys.md) · [WMS 菜单锚点](wms-key-menu-anchor.md)。

生产数据库初始化与前向迁移遵循 [R2 部署规范](../client/r2/03-compose-kubernetes-deployment.md)；项目级操作入口为 [DevOps](../devops/README.md)。
