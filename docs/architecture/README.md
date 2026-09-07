# 架构与代码导航

[返回文档中心](../README.md)。先用总地图理解分层，再进入模块源码地图。旧文档中的版本、文件数量和部署示例是历史快照；当前构建依据项目文件，部署操作从 [DevOps](../devops/README.md) 与 [R2](../client/r2/README.md) 开始。

| 文档 | 用途 |
| --- | --- |
| [CODEMAP 总地图](CODEMAP.md) | 请求链路、分层和模块位置 |
| [项目结构参考](PROJECT_STRUCTURE.md) | 代码架构、业务流程与数据模型；[Word 快照](PROJECT_STRUCTURE.docx) |
| [横切接线规范](00-横切接线规范.md) | 跨模块集成约定 |
| [设计系统 v1.0](CP6_Design_System_v1.0.md) | 界面设计基线 |
| [数据级多语言方案](i18n_数据级多语言方案.md) | 国际化设计决策输入 |
| [i18n 迁移指南](i18n_迁移指南.md) | 销售管理国际化迁移记录；[Word 快照](i18n_迁移指南.docx) |
| [历史代码盘点](../_inventory/README.md) | 按层盘点的存档，不能当实时统计 |

## 模块源码地图

| 领域 | 入口 |
| --- | --- |
| ERP 销售 | [codemap-erp](../codemap-erp/README.md) |
| MES 制造 | [codemap-mes](../codemap-mes/README.md) |
| WMS 仓储 | [codemap-wms](../codemap-wms/README.md) |
| 财务 | [codemap-fin](../codemap-fin/README.md) |
| 采购 | [codemap-pur](../codemap-pur/README.md) |
| 工作流 | [codemap-wf](../codemap-wf/README.md) |
| 公共平台 | [codemap-pub](../codemap-pub/README.md) |
| 计划中台 | [codemap-plan](../codemap-plan/README.md) |

各 `codemap-*` 目录保留稳定路径，在这里统一导航。用户操作见 [手册](../manuals/README.md)，学习讲解见 [全栈丛书](../learning/README.md)。
