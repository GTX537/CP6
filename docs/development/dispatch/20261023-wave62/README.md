# W62 / 七天一期 D7：实际 Main 来源到 CRM 持久发布集成

COORD 本聊天单人，2026-10-23T10:20:19.229178+00:00。Main `3a34065c2be0ac36b4a5494f2290c5f678cacc21`，CRM `42a40ed606c5f457b89fed5feec01274b9799158`，Handoff `b83442c4c421211c246266a4805db839d0448865`。W61 CRM106/Handoff37/Main430 全采用，19项不同相关行为有最终通过证据，实际登记30.48min、前置估计6min；三个准确分支删除、固定工作区干净detached、全部席位释放。持续目标ACTIVE，W37–W61不重复。

## 核对和限定范围

最新AGENTS/POLICY哈希未变，实时status/capacity通过，无重复W62任务PR。W52已有真实Main注册/认证/controller/普通PG身份→CRM独立进程/PG的完整基线验证；本窗复用驱动，不新造来源HTTP替身。Main相关源码与项目配置相对已通过W52 source `3892c606cef352d2eee65523c236e9e2f4af78b9`未变，可核原构建输入后复用本地制品执行本次集成。

CRM扩展原MainBaselineCompatibility场景，使用当前正式0.10.3依赖和已采用W56/W57恢复/发布实现，验证实际Main原来源全量采集、独立租户A/B/A、原始1MiB字节与摘要、完整来源图、原生调用方回滚/提交、重新调和撤下当前指针及旧凭据拒绝、错误来源/跨租户凭据不产生发布。保留原W52存储断言。仅测试及必要文档变化，发现实际产品缺陷时先登记Issue并记录扩大范围。

本地夹具的发行密钥、服务注册和数据均为合成；实际Main产品注册、JWT校验、目录controller及原生普通登录会真实运行。传输沿用显式loopback HTTP测试配置，不声称生产TLS/IdP登记、Main在线业务字段政策、真实Owner最终门、broker宿主或跨数据库原子提交。Published仍CanAuthorize=false，完整WP02-29与D5/D6缺口保持。

## 预计实际执行与出口

45–75min：测试扩展15–25；实际双PG/HTTP跨进程验证15–25；一次作者SPEC/QUALITY、三仓合并和生命周期收尾15–25。按Main start至finish记录，不重复累加子任务。只验证新增接点；已有90秒超时、原生守卫、事件撤销和当前身份单仓成功证据在输入未变时引用，不重跑全仓。

DEV-1负责总台账/四状态；DEV-2 Handoff START保存并finish deferred，再bind CRM实现/合并/finish，返回Handoff交付并finish，最后Main交付/finish。Main只读测试驱动在DEV-1执行，数据库路径独有；CRM PG端口55437，Main现有夹具固定端口55435（已核空闲；START草案的动态端口说明在此纠正）。全部自有PG正常停止留证，固定2+1席位复用。无Actions、既有环境迁移或部署。

24/375整卡、125目标0完整交付、247运行NOT_RUN不提升。W51准确data删除此前自动审批拒绝，原因仅blocked by policy；停止保留，无重试或绕过。

## W62实际交付

CRM [PR107](https://github.com/GTX537/CP6.CRM/pull/107)已采用，source `54a7886b68ef025f5058861a62abadb0f306247b`，远端main `6bb3b43e6d674148af470b4b44aa0b46917f1f47`，6任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/6bb3b43e6d674148af470b4b44aa0b46917f1f47/docs/development/evidence/W62-01/DELIVERY.md)。1项真实Main/CRM跨仓集成场景通过，父子驱动不重复计数、无跳过；当前两仓相关构建成功，完整Owner验收NOT_RUN，无Actions。

复用W52真实Main产品注册/JWT/controller/可信tenant目录及普通PG登录，CRM独立进程采用当前Collector、Authority和Publication，写入另一自有PG库。保留原A/B/A分页/1MiB原始字节与摘要检查，扩展本次lease采集、完整来源集合、实际rollback/commit、历史保留和当前指针失效。重新调和A后旧凭据及错误来源不能恢复发布，B当前发布保持原ID。

仅改CRM测试及必要文档；Main驱动和两仓产品/SQL/合同/依赖均不变。本次实际构建两仓相关项目；Main既有依赖和分析器告警保留，CRM构建0警告0错误。一次作者完整SPEC/QUALITY通过；W56/W57/W58未变单仓证据仅引用。Main端口55435（纠正START草案的动态端口说明）、CRM55437，两个自有PG均正常停止留data/log/TRX，未迁移既有库。

实际产品接点不等于生产身份：issuer密钥/服务注册/数据为合成，显式loopback HTTP；不证明生产TLS/IdP、在线字段政策、业务Owner共同最终门、broker宿主或跨数据库原子事务。所有结果CanAuthorize=false、PendingInspection。完整24/375整卡、125目标0完整交付、247运行NOT_RUN不提升。

CRM准确本地/远端分支删除、固定树干净detached、finish释放。七天目标ACTIVE，继续实际Owner前置与D6可独立公共支撑。无Actions、既有库迁移或部署。W51准确data删除此前自动审批拒绝，原因仅“blocked by policy”；停止保留，无重试绕过。

Handoff [PR38](https://github.com/GTX537/CP6.AI-Handoff/pull/38)已采用 `99db307c36bd5d5e9394e68bb1e84849a42566db`，准确分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约27.87min，原预算45–75min，最终以finish为准、子任务不重复累计；前置只读核对约6min。
