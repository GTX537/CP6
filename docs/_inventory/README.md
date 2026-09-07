# 盘点与整理记录

[返回文档中心](../README.md)。区分代码盘点快照与文档管理记录，两者都不能替代当前状态。

## 文档整理

- [2026-09-07 整理范围与验证](2026-09-07-docs-organization.md)
- [旧路径 → 新路径清单](docs-reorganization-20260907.json)
- [日常维护规则](../CONTRIBUTING.md)

## 历史代码盘点

[盘点总览](00-代码盘点总览.md) 按原文日期阅读。分层明细：

| 范围 | 文件 |
| --- | --- |
| 实体 | [entity](entity.md) |
| Core | [财务](core-fin.md) · [业务运行](core-ops.md) · [供应链](core-supply.md) · [系统](core-sys.md) |
| Web API | [Controller](webapi-controllers.md) · [基础设施](webapi-infra.md) |
| 前端 | [核心](web-core.md) · [页面](web-views.md) |
| 测试 | [tests](tests.md) |

## 原始资料与临时文件

`docs/file/` 包含已跟踪的原始设计书、附件、网页资源和源码样例，也可能有被忽略的本地文件。旧文档把整个目录描述为“仅本地”并不准确；以 `git ls-files docs/file` 与 `git status --short --untracked-files=all -- docs/file` 分别核对。本次不删除、搬迁或重新上传这些资产。

`docs/_manual_render/` 是已忽略的渲染中间目录，不是正式文档入口。
