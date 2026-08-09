# CP6 Space 技术决策记录

这些 ADR 冻结技术选择边界，不重新讨论产品范围。实际 SDK、Provider 和部署产品由试验数据决定，但必须遵守硬门槛、评分和回退规则。

| ADR | 状态 | 决定什么 | 不允许改变什么 |
|---|---|---|---|
| [ADR-0001 CAD 转换](./0001-cad-conversion-selection.md) | Accepted / Experiment-gated | SDK 或受控转换服务及兼容矩阵 | DWG/DXF 产品入口、统一 CAD IR、隔离解析 |
| [ADR-0002 AI Provider](./0002-ai-provider-selection.md) | Accepted / Evidence-gated | 首个外部 Provider、区域、SLA 和成本 | 原文件不外发、人工审查、确定性引擎边界 |
| [ADR-0003 Design V1 迁移](./0003-design-v1-migration-baseline.md) | Accepted | 候选工作树拆分、Migration 和切换顺序 | 不整体合并脏工作树、不长期双写 |
| [ADR-0004 性能环境](./0004-performance-acceptance-environment.md) | Accepted | 参考终端、测量方法和证据格式 | 已冻结的性能与容量门槛 |

任何破坏冻结契约的替代方案必须先提交 Scope Change RFC。

