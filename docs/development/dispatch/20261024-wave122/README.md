# W122 原生询盘页面、当前动作展示与稳定重试

COORD本聊天单人；七天持续目标ACTIVE。W121 Main PR533/CRM PR147/Handoff PR97已远端采用、准确分支删除及官方finish，实际44.58分钟。依据final proof核销，不重复。

## 范围和前置

最新AGENTS/POLICY、总台账、Handoff与官方status已核对。Main463a843f354eed04032f4a3a03232ddbc15b6b25；CRM0c9e9a74b6b15ec2572f8f594e14f018703fe867；Handoff e30af151185e316255233a275ff5d12bf49a857a。复用DEV-1/DEV-2，缓存和用户资料保留，2+1配额不变。

Main读取用途按当前真实权限和完整六字段提供可选add/edit签名事实；原用途、完整来源、最终Bearer栅栏不变。CRM只在写Owner启用、当前精确资源范围、主管/部门/字段/逐动作权限与实际Consent/状态/期限均满足时展示动作。共享判断保持写Owner独立新鲜目的证明与最终事务检查；读证明不成为写权限。

原生队列/详情页面沿用运行时显式开关和原路由，使用冻结R3V15 submissionId/Retained/Reason1..2000 Unicode标量及原游标排序；保留旧页面回退，不窄化输入。/crm/session不含CSRF，新增仅供原生页面的最小上下文须经过同一当前读取Owner，返回原会话CSRF，不续期、不暴露token、不依赖旧workspace或configuration权限。

稳定待提交命令在未知结果时原key/body/IfMatch/CSRF显式重试；412刷新当前资源后由用户明确以新版本新命令提交。访问降低/退出/切换清理敏感内容；状态过滤/分页/保留期/遮罩、无假Lead页面链接。验证新行为的客户端/浏览器、实际PG/BFF及实际Main签发互操作；不为旧成功SHA变化重跑。

## 任务和预计实际执行

|任务|实际执行估计|
|---|---|
|W122-01 Main当前读取动作事实及来源验证|20–30分钟|
|W122-02 CRM当前动作交集与最小页面上下文|20–30分钟|
|W122-02 原生队列/详情/稳定重试页面|35–50分钟|
|相关本地验证与一次集中审查、三仓收尾|25–40分钟|

合计100–150分钟，登记起点 `2026-10-24T21:41:22.533153+00:00`，不叠加跨仓时间。PostgreSQL唯一新增数据库；自有隔离测试库，默认精简验证。当前无schema/包/工作流改变需求，不安装迁移既有环境、不开Actions、不改保护或部署。

- [x] 当前真实Main事实与CRM资源级动作展示。
- [x] 原生页面与原会话上下文、稳定重试。
- [x] 相关本地验证与一次任务级审查。
- [ ] 三仓远端采用、准确分支清理和席位释放。

完整WP03-02/03、隐私/保留Owner、实际Outbox发布与正式验收继续独立跟踪；24/375DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY不变。

## 实际交付

原生询盘队列/详情沿用原路由和显式运行时开关，冻结R3V15字段及1..2000Unicode原因，当前原会话CSRF最小上下文。Main读取用途补当前可选add/edit事实；CRM交集已启用写Owner、精确资源/逐动作范围/完整字段/原同意/状态/保留与审核期限，写入仍独立新鲜目的证明和最终事务检查。未知结果固定原key/body/IfMatch/CSRF显式重试；412读当前状态再明确新版本新命令；权限失效/显示期限/迟到响应清理敏感状态。服务器Date/耗时/单调时间及Retry-After按实际响应计算。原写事务和数据库合同未变。

CRM [PR148](https://github.com/GTX537/CP6.CRM/pull/148)采用 `e06bdedcb0cd31c17b15fb1064573f9075b83800`，源 `f885ec55c7b57fe15a5ef8da41c73ad7d1c64675`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/e06bdedcb0cd31c17b15fb1064573f9075b83800/docs/development/evidence/W122-02/DELIVERY.md)。101项唯一相关本地成功检查：Main28、CRM26（PostgreSQL/HTTP/9种实际浏览器场景）、Web47；实际Main读取用途HTTP原字节通过CRM持久PG信任，当前权限/CSRF、原命令恢复、412、访问失效、截止时钟、并发/CAS和原读取回归通过，必需0跳过。 证据D:/CP6-local-test/wave122/foundation/final-validation.json。桌面/390px截图已检查。实际Next SSR/生产TLS/真实业务正式验收未运行。原失败保留；到期夹具不得改不可变原收到/审核身份，替换为实际持久化图共享纯时钟谓词，不冒称过期HTTP/worker验收。

Main本轮有实际权限来源及4文件源码/测试变化，须独立正常PR/远端采用，不能由CRM合并推导Main采用；本轮最后统一核实。CRM项目仅增加一个本地浏览器夹具复制项，无包/工作流/数据库schema变化。无Actions、保护变更、既有环境安装迁移或部署。PG已停止，无新junction，缓存/失败日志/用户资料保留。

完整WP03-02/03、隐私/保留Owner、原生Outbox实际发布后继；24/375、0/125、247NOT_RUN不变，D6PARTIALLY_ADOPTED，七天ACTIVE。
Handoff [PR98](https://github.com/GTX537/CP6.AI-Handoff/pull/98)采用 `fa331f5750c6155d2d5a74908a8b3c9708aa562f`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟24.05分钟，原估100–150分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。

本提交时Main最终合并和席位收尾待；以官方finish与final_proof作最终证明，下轮统一核销，不额外创建记账分支。
