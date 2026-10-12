# WP14-01 执行计划与准入记录

- 任务：全链验收映射、数据责任与证据索引；类型 DESIGN，无直接任务前置。
- 已确认 Main / origin/main 基线：`df5553fd04ec4fc516602a4e2843650c38ae5006`（2026-10-12 只读 fetch）。
- 分支：`codex/wp14-01-acceptance-preparation-20261012`；工作树：`D:/CP6-worktrees/wp14-01-20261012`。
- 私有写入范围：`docs/development/deliveries/WP14-01/`。只读任务卡、现行台账、原验收目录、修订目录、固定源码评估及其入口。
- 互斥资源：`coordination:acceptance-map`；资料准备不修改公共合同、EF 模型、迁移、业务写入入口或其他任务报告。
- 当前主线 assignment 为空、entry_gate 为 NOT_CHECKED；已请求确认协调对话归属。登记/共享状态编辑/合并窗口未确认前只形成私有准备材料，不自行写共享台账或宣称任务已领取/完成。

## 步骤

1. 固定输入版本与 SHA-256，保留 125 目标、123 完整原清单、ITER/REL 历史缺口、247 运行与 41 历史静态条目的边界。
2. 从原完整 definition_ref 建立逐目标、逐条目责任与证据索引；保留所有原文、refinements、连续步骤及当前解释的引用；按完整来源定位建立共享身份，所有消费者分别举证。
3. 定义跨域链、角色责任、独占 PostgreSQL 数据资源、候选输入、失败注入及证据字段；真实执行人/环境/版本未到齐不签收。
4. 集中验证覆盖、来源指针、计数、生成一致性和文档链接；审查整个任务 diff。普通文档不编译业务系统、不执行数据库测试。
5. 私有报告记录真实验证及限制；由唯一协调对话串行同步 assignment、台账、生成页和四份状态文件，并按正常受保护流程合并、核对远端包含性后登记 DONE。

## 已观察到的准备问题

- 首次 git worktree add 因 Windows 长路径失败；Git 自动撤销未完成工作树注册，保留新建分支。用单次 `git -c core.longpaths=true worktree add` 成功，无全局/仓库配置修改，无他人文件清理。
- 裸 python 指向旧解释器，台账检查因 WindowsPath.is_relative_to 不存在失败；按仓库已记录路径改用 CPython 3.13 后台账检查 PASS。不是业务失败或业务通过。
- 一次临时字段统计脚本误把 dict values 传入 Counter，TypeError；改为字段集合后完成只读统计，未写任何台账数据。
