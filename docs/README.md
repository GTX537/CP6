# CP6 文档中心

从这里按用途找文档。目录负责导航；模块自己的规范、批准记录和验收门禁负责定义行为与完成条件。

## 我现在要做什么？

| 目的 | 从这里开始 |
| --- | --- |
| 了解项目、搭建开发环境 | [项目首页](../README.md) → [开发指南](../DEVELOPMENT-GUIDE.md) |
| 接手任务、查看最新进展 | [项目记忆入口](project-memory/README.md) → [当前状态](project-memory/PROJECT_STATE.md) → [待办](project-memory/06-Todo.md) |
| 找产品蓝图、功能范围与路线 | [产品规划](product/README.md) |
| 看架构、源码位置和跨模块约定 | [架构与代码导航](architecture/README.md) |
| 查需求底稿和详细设计 | [需求资料](requirements/README.md) |
| 学习系统操作、准备用户培训 | [操作与培训手册](manuals/README.md) |
| 查设计任务、实施计划与测试记录 | [任务文档与 QA](superpowers/README.md) |
| 处理 CI、Release 或部署 | [DevOps](devops/README.md) → [WMS R2 生产规范](client/r2/README.md) |
| 学习技术、准备面试 | 本页的“学习与面试” |
| 查历史方案、文件迁移去向 | [历史归档](archive/README.md) · [盘点与迁移记录](_inventory/README.md) |

## 业务模块

| 领域 | 文档入口 | 内容边界 |
| --- | --- | --- |
| CRM | [CRM 文档](crm/README.md) | 产品基线、公开工程契约与审批状态；批准产品需求不等于允许开工 |
| 原生 WMS 客户端 | [Client](client/README.md) | Web、WPF、Android 边界与共享合同 |
| Space 数字空间 | [Space 总纲](space/README.md) · [需求拆分](space/requirements/03-epic-and-spec-backlog.md) · [验收资产](space/acceptance/README.md) | 建模、编辑、发布、WMS 接缝与独立验收条件 |
| 财务 | [Finance](finance/README.md) | 总账、应收应付、成本、资产、预算等设计 |
| 采购 | [Procurement](procurement/README.md) | PR、RFQ、PO、收货和三单匹配 |
| 审批与工作流 | [Approval](approval/README.md) | 工程设计与业务接缝；配套实现教材在 [OA](oa/README.md) |
| 公共平台 | [PUB](pub/README.md) | 组织、权限和公共能力 |
| ERP / MES / WMS / 计划中台 | [模块源码地图](architecture/README.md#模块源码地图) | 既有模块的代码导航；用户操作见手册，早期需求见需求资料 |

## 学习与面试

| 资料 | 适合什么场景 |
| --- | --- |
| [初学者补课](learning-basics/README.md) | 从基础概念开始，与进阶丛书对应学习 |
| [全栈进阶](learning/README.md) | 用 CP6 真实代码理解设计取舍 |
| [OA 实现教材](oa/README.md) | 理解低代码表单与工作流引擎的实现 |
| [按天面试备战](interview-prep/README.md) | 四天学习路线、项目叙事与历史面试记录 |
| [专题面试资料](interview-prepGPT/README.md) | 知识卡片、实验、项目复盘与开发工作簿 |

## 文件应该放在哪里？

| 目录 | 用途 |
| --- | --- |
| `product/` | 跨模块产品蓝图、功能盘点、路线及配套图源 |
| `architecture/` | 项目结构、总体代码地图、设计系统、横切规范与国际化方案 |
| `codemap-*/`、`_inventory/` | 各模块源码地图、历史代码盘点及文档整理记录 |
| `requirements/`、`detailed-spec/` | 早期需求底稿与逆向详细设计 |
| `crm/`、`client/`、`space/`、`finance/`、`procurement/`、`approval/`、`pub/` | 对应业务域的规范与说明 |
| `devops/` | CI/CD、发布流程、环境策略与发布决策 |
| `project-memory/` | 当前状态、完成记录、待办和接手上下文 |
| `manuals/` | 用户操作手册、培训材料和页面 SOP |
| `superpowers/` | 有日期的设计、实施计划与 QA 证据，完成状态需核对项目记忆 |
| `learning/`、`learning-basics/`、`oa/`、`interview-prep/`、`interview-prepGPT/` | 技术教材和个人学习资料 |
| `seeds/` | SQL 种子脚本与相关说明，使用前见 [种子目录说明](seeds/README.md) |
| `images/` | 已被文档引用的共享图片；模块专用图放在模块内 |
| `archive/` | 有明确历史日期的旧方案与任务记录 |
| `file/` | 原始设计资料与附件；已有文件中有 Git 跟踪资产，也有被忽略的本地资料，不能当缓存清理 |
| `_manual_render/` | 已被 Git 忽略的手册渲染中间文件；本地可能存在 |

## 哪份内容为准？

- 最新进展从 [PROJECT_STATE](project-memory/PROJECT_STATE.md) 和 [待办](project-memory/06-Todo.md) 获取；旧路线、代码数量和“已完成”措辞只代表原文编写时点。
- 业务行为以对应模块的当前规范和可执行合同为准。CRM 批准材料、R2 规范、Space 验收证据可能绑定摘要或固定路径，整理时需遵守原有治理流程。
- Markdown 是持续维护的文本入口。已有同名 Word、Excel、PDF 保留为导出或原始附件；未重新核验时，不承诺与 Markdown 同步。没有文本源的附件仍是独立资料。
- 新建、重命名、归档与链接检查遵循 [文档维护规则](CONTRIBUTING.md)。本次旧路径对应表见 [迁移清单](_inventory/docs-reorganization-20260907.json)。
