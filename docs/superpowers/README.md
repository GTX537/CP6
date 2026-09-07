# 任务文档与 QA

[返回文档中心](../README.md)。本目录按任务保留设计、执行计划和验证材料。目录名来自工作流工具，内容按以下用途阅读。

| 目录 | 放什么 | 如何判断状态 |
| --- | --- | --- |
| [specs](specs/) | 日期化设计与规格 | 先核对适用范围、原始日期及模块当前规范 |
| [plans](plans/) | 实施步骤与任务拆分 | 计划或勾选框不能单独证明已交付；对照提交和项目记忆 |
| [qa](qa/) | 场景验证记录、截图与复现材料 | 从对应任务的 README 读环境、步骤与证据限制 |

找一个任务时，在三个目录中搜索相同主题词，例如：

```powershell
rg --files docs/superpowers | rg 'wfs|space|tenant'
```

当前待办入口为 [06-Todo](../project-memory/06-Todo.md)，已完成入口为 [05-Completed](../project-memory/05-Completed.md)。正式 Space 验收仍由 [Space acceptance](../space/acceptance/README.md) 管理，生产发布仍由 [R2](../client/r2/README.md) 管理；这里的测试记录不自动构成正式接受证据。
