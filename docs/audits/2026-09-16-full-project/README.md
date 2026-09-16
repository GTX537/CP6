# CP6公开源码盘点与完整报告入口

日期：2026-09-16。用户要求对整个CP6（含CRM及Platform）按真实代码盘点，为业务、架构和界面全面升级建立基线。

**完整跨仓报告已在私有CRM仓库整理；本目录只发布来自公开Core与Platform源码的盘点。** 交付前已确认CP6和Platform为公开仓库，CRM为私有仓库，因此没有将私有CRM实现、源文件清单或业务细节转存至公开仓库。

## 完整报告

- [全CP6跨仓总报告（需要CRM仓库权限）](https://github.com/GTX537/CP6.CRM/blob/main/docs/audits/2026-09-16-full-project/README.md)
- [CRM详细盘点（私有）](https://github.com/GTX537/CP6.CRM/blob/main/docs/audits/2026-09-16-full-project/03-crm.md)
- [业务、架构和界面升级工作包（私有）](https://github.com/GTX537/CP6.CRM/blob/main/docs/audits/2026-09-16-full-project/05-upgrade-backlog.md)

完整报告包含当前实现、部分闭环、空实现/演示、规划和未验证范围，另核对Portal规划边界与历史副本。它不将历史或模拟开发验收等同生产上线。

## 公开分报告

| 文档 | 范围 |
| --- | --- |
| [主系统业务与界面](01-core-business-ui.md) | ERP/PUR/PLAN/MES/WMS/FIN/OA/WF/SYS/PUB、Space、Web和原生客户端 |
| [主系统架构、数据与交付](02-core-architecture.md) | 注册与调用链、事务/租户/权限、数据模型、测试与DevOps |
| [Platform](04-platform.md) | 七个可发布共享库、认证/网关/消息/发布、宿主责任与历史证据 |
| [静态清单](data/README.md) | 公开源码项目、HTTP声明、DbSet、界面与主Web逐页清单 |
| [验证回执](VALIDATION.md) | 核对方法、结果、交付与未验证范围 |

固定公开源码基线：Core `5f39a020a9c535fb0bd515cc419bc0e99b90fe07`；Platform `30bd23af6808d217a23878bd9437513043c52834`。路径/行号绑定这些提交，不随报告提交变化。公开源码总计7,583个跟踪文件、58个csproj（含测试/工具/参考工程）；原始数量不是产品完成度。

## 主要发现

1. 主系统有真实且广泛的制造业业务，151个动态路由键，248个views Vue文件（含子组件）；并非只有菜单，但页面存在不代表每个业务动作闭环。
2. 旧Web拣货确认/短拣只更新本地状态，完成未提交实拣结果；包装页可本地随机生成跟踪号。应优先修复作业持久化并明确编号来源。
3. ERP WIP/PowerEgg/版模外部接口存在当前注册的NoOp；信用阈值和部分估价/成本有固定或标准回退。需先确认业务权威与缺数据时行为。
4. 既有best-effort桥与新事务消息并存；C02投影与C03专用命令Inbox语义不同。升级应保持幂等、版本及业务命令差异。
5. Space是复杂设计/发布/运行态工作台；Desktop/Android集中于WMS，Space.Client是SDK。不能将所有客户端视为全模块覆盖。
6. Platform是实际可消费的七个共享库，无独立业务UI或生产API宿主。公共协议有价值，仍须消费者落实业务授权、迁移和Worker。

证据和详细影响见分报告。推荐升级顺序是业务断点与对象权威、公共架构边界、代表旅程与一致界面，随后扩展及真实采用；具体工作包在私有完整报告中。

## 范围限制

本轮是全仓文件/声明扫描与关键调用链抽查，不是逐行人工代码审查或生产验收。未启动系统、连接数据库、执行业务构建/测试、浏览器/实机/视觉走查、性能/安全测试或生产部署；未运行Actions。相关结果均不宣称通过。
