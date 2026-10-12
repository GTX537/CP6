# CP6 Development Roadmap Implementation Plan

> 执行方式：按用户批准的大任务粒度建立规划和跟踪文档。本任务由当前会话执行；未来业务切片按 executing-plans 逐项实施，是否委派另依当时授权。仓库任务级审查节奏优先于技能的逐小步骤模板。

**Goal:** 将125设计目标、14大任务及修订验收映射为可持续维护的开发总台账。

**Architecture:** roadmap.json单点登记，生成大任务/目标归属/进度三份视图。固定设计来源，区分现有代码入口、逐目标符合性评估、实际开发交付和真实运行验收；跨域场景按唯一ID统计。

**Tech Stack:** Markdown、JSON、Python标准库和Git；不引入业务运行依赖。

## 范围与计划粒度

用户先要求全量评估、拆大任务、按实际情况安排开发，再确认用于持续追踪进度。本任务交付完整范围规划和可更新台账；不宣称完成125目标逐方法语义审计、不启动业务编码。文件级代码步骤在各实施切片准入时编写。

## 任务及文件

- [x] 核对最新Main及CRM/Platform固定源码、现有实现入口、当前设计组合、125覆盖和288验收记录。依据：`docs/design-handbook/remediation/{00-disposition-register,08-acceptance-catalog,10-target-coverage}.json`及现有系统地图。
- [x] 创建`docs/development/roadmap.json`，14包分别写明成果、子范围、粗略规模及风险；125目标唯一归属；247运行场景和41历史静态记录分开登记。
- [x] 创建`docs/development/{README,01-work-packages,02-target-coverage,03-progress,04-batches}.md`，定义依赖、切片准入、状态和证据规则。
- [x] 创建`docs/development/tools/manage_roadmap.py`及定向回归，验证重复归属、缺失范围、无证据通过、静态/运行混淆及空验收假完成，生成三份视图。
- [x] 集中审查完整diff并定向复审修正：WP01关闭必须完成原验收盘点，源码入口计数采用目录全部受跟踪文件。18项工具回归及结构/生成视图检查通过，140个本地链接检查按未变更链接范围复用。

审查记录见`docs/development/evidence/task-review.json`。后续交付采用正常受保护PR流程；实际远端包含性回执另存，不以本地检查复选项推断远端状态。

项目状态入口同步更新`PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md`、`CHANGELOG-AI.md`及设计手册导航。原归档、R1资产与历史接受不修改。

## 验证与进度口径

运行`python docs/development/tools/manage_roadmap.py --write`生成；不带`--write`检查生成结果是否落后于JSON。运行`python -m unittest discover -s docs/development/tools -p "test_*.py"`验证防止虚假完成和重复计数的登记规则。

这些检查仅验证台账，不是业务验收。原247运行场景仍NOT_RUN；每个目标的原始验收清单需在WP01继续盘点。相对规模L/XL不折算工时或全系统百分比。当前从WP01评估开始，后续批次按实际依赖准入。

具体结果在`docs/development/evidence/`记录；未执行的业务/数据库/部署验证保持未执行。没有远程Actions运行、工作流触发调整或生产部署。
