# 公开盘点验证回执

日期：2026-09-16。对象为文档与静态清单，不是业务生产验收。

全盘点完成后核对GitHub可见性：CP6/Platform为public，CRM为private。完整跨仓报告转交CRM私有仓库；本公开目录只保留Core/Platform源码分项、其机器清单与私有报告入口。没有公开CRM源文件清单、实现摘录或完整升级工作包。

## 本地验证

- 原三仓报告完整引用/CSV/链接验证记录留在私有报告。公开衍生版另核对相对链接、源码路径/行号、CSV与JSON一致性、Python语法及git diff --check。
- 公开数据为58个项目、1,102条HTTP文本声明、318个UI文件、319处DbSet声明；主Web另有248条views、174条路由/布局、925条API调用行。统计口径见[data说明](data/README.md)。
- 集中交叉审查纠正C03专用CommandInbox与通用投影Inbox的差别，并完成局部复核；已抽查Web拣货、随机跟踪号、NoOp注册、成本回退与Platform版本引用。
- 普通文档按仓库规则不执行业务构建/测试、SQL、浏览器/实机、性能/安全扫描，不重建镜像；历史结果保留原执行范围，不改称本轮通过。

## Actions与Git

CP6主线13份workflow中12份仅workflow_dispatch，R2 candidate仅版本tag；Azure根桥trigger:none，DEV仍依赖成功上游。本任务只做文档push/PR，不触发上游或任何Actions，也未改变开关/保护/发布门禁。

只读核对CP6 main保护：enforce_admins=true，PR要求保留，required_status_checks=null，approving review count=0。使用codex/full-project-inventory-20260916正常PR交付，合并后核对远端main包含任务提交；不修改原本地main的分叉历史，不清理其他任务worktree。最终交付记录见PR与任务回复，不预写未发生的合并SHA。
