# W143 CRM 实际运行程序集固定核验与启动门禁

COORD 单人继续原七天 D5 / WP02-29 的限定采用窗口，原完整任务未整体准入。W142 已从最终证明核销，不重复。START 2026-10-26T12:12:21.512494+00:00。DEV-1/Main；DEV-2/Handoff→CRM→Handoff，正常 bind 复用。

|任务|预计实际执行|
|---|---|
|实际宿主程序集闭包、独立固定输入与运行中内容核验|50–75分钟|
|启动拒绝、篡改与实际宿主/运营权限定向测试|30–45分钟|
|一次 SPEC/QUALITY、三仓正常 PR 与席位收尾|20–30分钟|

合计100–150分钟，不叠加跨仓时间。基线 Main ae0c5a5fcb3e30d3bb2463a9d1fccf999258007d，CRM 88328a6025527e3b7751d5d003dbadbf2f008066，Handoff 859a15d5538212643f89ae6a5b8feb1067e06f52；Platform a89e90eb4f078f37a6f8610882079073185fbcf8 不改包。四仓规则、远端与固定席位已核对，预检 wave143-preflight.json。

## 出口与真实前置

WP02-29 要求隔离 CRM 宿主实际加载清单一致，未知运行身份不能通过采用门。W132 已接入真实原生宿主运营/探针，但正式 RuntimeIdentityEvaluator 仍 Unproven；现有 OwnedModuleObserver 加载另一份镜像，不能证明当前宿主正在执行的代码。

本轮在 CRM 原生宿主增加独立配置的固定镜像清单门禁：从实际 API 程序集和其 CP6 引用闭包取得当前 Assembly，比较固定 PE 字节的 SHA256、原始元数据、方法 IL/异常区域、RVA 初始数据及嵌入资源。配置完整清单后，不匹配或不支持的镜像在启动时拒绝；无配置显式 Unconfigured。预期镜像只接受托管 IL，拒绝预期ReadyToRun/混合/动态等；实际ILOnly不证明无native/R2R/JIT执行，该部分明确未验证。可信 CLR/OS 边界、机器码/原生依赖/第三方/容器镜像、源码与发布签名和独立资格仍未证明。

接入受已有实际 Peer/Token/TLS 控制的运营路径，输出范围和剩余限制，不泄露本地路径。此限定核验不把正式平台健康、readiness 的 newEffectsAvailable 或生产 qualified 改为 Match。实际本机测试输入不得充当正式发布清单。

真实宿主正向与缺项/多项/错哈希、相同元数据下 IL/RVA/资源篡改、配置冻结及现有运营权限拒绝采用本地定向验证；无数据库结构/授权/业务 Owner 事务变更。允许仅为读取运行中原始元数据启用 API AllowUnsafeBlocks；无依赖版本变更，push/PR 前分析这一构建声明及实际工作流，禁止 Actions。一个任务级集中审查；失败原样保留。

D2 正式 Transfer T04 仍缺有版本且可信的 Product/UOM/技术身份原文，旧 ProductCd/QtyUnit 不得伪造为已批准 Owner。D3、D5 企业 broker/正式源与镜像资格、D6 实际政策/日历/注册/密钥、D7 最终集成均保留，完整阶段/卡/125目标/247验收不据本轮提升。原七天持续目标 ACTIVE；没有迁库、环境替换或生产部署。
