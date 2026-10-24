# W100 Main/CRM精确Site/Form授权来源

本聊天COORD单人；Main `cd165466a9806861e70be5c57fea579d08172c56`、CRM `e86ca195470f8e4fad29aab474cfbef696dde132`、Handoff `ed4e49d9f68fd599d2c9dc29f0c5e77a6b538fd6`。W99 Main511/CRM126/Handoff75已全部采用，54项相关检查通过；真实执行15.85min＋前置约5min，准确分支删除、固定树干净、三个席位释放，不重复。最新AGENTS、POLICY、官方status/capacity、当前源码和台账已核对，无配额告警。

## 直接前置和实施决定

CRM-INQ RECOVERY-R3V15配置矩阵要求crm-site:query/configure与精确Site/Form；fields=[]。现有Main仅有crm-lead部门在线范围，C2 v1/v2权限事件也只有resource/action/scope/deptIds/fields。不能把部门、IsSupervisor或scope5自动转换为Site/Form授权。W89/W99仅提供真实身份/会话事务围栏；W87提供当前签名信任，均不是业务范围来源。

新增Main显式角色授权集合，项为同一tenant下的精确(siteId,formId)对，空集合撤销全部；不自动授予、不生成默认对象或角色。现有pub-data-scope query/edit作为范围管理权限，原IAM当前身份根、版本/CAS、原操作/审计与C2 snapshot/outbox同事务。角色身份完整int域和tenant绑定保留。对象存在与Site/Form父子关系在CRM真实Owner中按持久绑定复核；单独的UUID或跨配对集合不能授权。

Main-owned C2新增permission.changed.v3和bundle1.2，旧v1/v2 schema/examples字节保留；v3新增必须的精确对集合，身份/动作/部门/字段原意义不变。升级后的role aggregate不回退v2；清空/禁用/删除明确撤权。普通无新范围的历史角色仍按既有版本输出。PostgreSQL新增范围存储与写者门，不能提交未同步原permission snapshot的范围变更；不修改SQL Server或既有环境。

CRM精确采用同一新bundle，原始来源、基线/事件、当前publication和持锁核查保留完整配对、来源与当前角色状态。v1/v2不因携带同名可选扩展而获得新范围；旧identity健康/会话继续各自语义。范围来源不等签名权限生产者或配置Owner最终采用；后继直接接入真实签名生产者和业务Owner，不能仅凭五分钟有效期宣称跨仓撤权原子。

## 任务、验证和耗时

- W100-01 Main：权威存储/实际IAM API、原操作/审计/事件与v3合同；DEV-1 codex/w100-01，含总台账和四份状态同步。
- W100-02 CRM：精确合同与当前范围消费；DEV-2原Handoff START可靠保存并finish后官方bind CRM，CRM合并finish后bind回原Handoff。
- W100-HANDOFF：本轮一次START及最终交付，原任务分支收尾。

预计实际90–150min：Main来源及版本化合同35–55，CRM当前消费20–35，相关真实PG/HTTP/跨仓验证20–35，集中审查和全部合并收尾15–25；开工前独立分析约10min。实际START `2026-10-24T05:00:01.195758+00:00`，最终按主窗口finish计，不叠加CRM/Handoff。

定向覆盖：实际授权/CSRF/tenant/完整roleID、原键恢复与输入冲突、CAS/ABA、空集撤权、事务故障回滚、原snapshot/outbox一致、raw writer守卫、v1/v2兼容、v3精确pair/禁用/删除/当前发布变化。只运行相关本地验证和一次完整SPEC/QUALITY；真实失败保留。Main测试自有PG和CRM55446按实际实例标识停止，数据证据保留。无Actions、既有库迁移、生产部署。24/375整卡、125目标0完整、247正式运行NOT_RUN不提升；七天持续目标ACTIVE。

## W100实际交付与最后收尾

Main W100-01 本次提供实际 `api/iam/inquiry-scopes/{roleId}` GET/PUT及operations原键查询/close。现有 `pub-data-scope:query/edit` 当前权限、真实登录/CSRF、tenant及完整int RoleId边界保留；命令携带原operationId、角色version、referencesVersion和精确SiteForms。服务在原IAM同事务身份根内核对当前主体，冻结/排序精确配对，校验角色、CAS/ABA；原键与route RoleId绑定，返回原COMMITTED结果，关闭键禁止执行。原生参数化scope替换、原permission snapshot/outbox、审计和原操作结果在同一外层事务，结果写入失败全部回滚，提交ACK丢失从原键恢复。没有默认对象或自动授权。

PostgreSQL migration `20261024050000_InquiryRoleScopesV1` 新增tenant+role+site+form复合主键与tenant+role外键，沿用实际authentication写者门；禁止UPDATE/TRUNCATE，按原事务/角色合并一次延迟检查，scope变化必须与最终v3 snapshot精确匹配，否则整笔回滚。迁移只在自有本地测试库运行，未触及既有环境。DB owner/迁移管理员仍是可信边界；schema不替代tenant或权限。原EF模型和SQL Server实现不改。

Main-owned C2新增permission.changed.v3与bundle1.2.0。新集合要求非零UUID精确配对，旧动作/部门/字段和完整角色域不变；升到v3后角色聚合不降级，清空/禁用/删除明确撤权，未使用新范围的历史角色仍保持原版本。旧v1/v2 schema/examples字节不变，旧index保留，Main/CRM新资产逐字节一致。新index SHA256 `ff976a7acead6b563fa224af808cfa3900403194d81c103245dfa147cdf785da`，旧 `d06c2918d3aeee236a9ecb6b254911ad3a337f249fbac26c5237399644783e6a`。

CRM [PR127](https://github.com/GTX537/CP6.CRM/pull/127) source `43083d0e38970ae1172eb2e76cdd8947a4031c7f`已采用远端main `cab16a031c93d52da674f20984a2d503d35fc2a8`，24任务路径逐blob一致。[CRM单份交付](https://github.com/GTX537/CP6.CRM/blob/cab16a031c93d52da674f20984a2d503d35fc2a8/docs/development/evidence/W100-02/DELIVERY.md)。真实native ingress/原始基线/current publication精确保留配对、来源、版本与角色状态，原身份持锁期间实际撤销写者阻塞，外层结束后撤销生效并要求新发布。旧native注册保持原摘要并拒绝v3；旧合法未知扩展按原任意JSON形状忽略，原授权/发布不受误解析影响。没有静默升级持久Source，实际环境profile/基线版本切换须单独实施；更新Main合同包不代表旧CRM注册已迁移。

173项唯一相关本地检查通过：Main94（复用84项未变有效结果＋10项新行为），CRM79（41项native PG＋38项合同/发布/原权限）；0跳过。同一Main事件额外重放通过，不重复计数。 证据目录 `D:/CP6-local-test/wave100/foundation/`，最终索引 `final-validation.json`。Main相关输入未变，复用 `main-behavior-20261024T050634Z` 的84项成功结果；新增9项修正后 `main-reviewed-20261024T050824Z` 通过，加跨租户 `main-tenant-20261024T051243Z` 1项。CRM最终 `crm-stable-fixtures-20261024T051802Z` 41项、`crm-unit-20261024T051720Z` 38项、`crm-replay-20261024T051843Z` 重放1项通过。必要项目/依赖随测试编译，未跑全仓。

真实Main IAM/PG产生的原Outbox字节直接交给CRM native ProcessAsync和HTTP基线采集；同一实际事件可重复接收。配套目录、transport认证、在线身份为明确本地夹具，不是生产Main/OIDC/正式业务验收。一次COORD完整SPEC/QUALITY核对源码、合同、迁移/租户、事务/撤权、原键、输入与兼容，审查发现旧未知扩展误解析已修正并定向回归。初次编译泛型推断失败、Main测试漏tenant过滤、CRM夹具version不符及重复固定tenant时随机目录被原完整性守卫拒绝，全部原日志保留；修正均不放宽保护。两套CRM自有PG55446均实例校验后smart stop并保留数据，Main各测试实例也已退出，HTTP宿主释放。

完成的是明确Site/Form授权来源与当前消费；`CanAuthorize=false`，实际签名权限生产者、跨仓撤权最终门和配置Owner共同事务/真实业务采用仍须继续。不能把部门、supervisor或scope5转换成站点权限；配置矩阵fields=[]，不得引入线索PII；Owner必须核对实际tenant/Site/Form存在及父子绑定。无Actions、既有环境迁移、生产部署。整卡24/375、125目标0完整、247正式运行NOT_RUN保持；七天持续目标ACTIVE。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/76)已采用 `7ec07d382c98942481cea90f4a80aa6f4222d558`，准确分支删除、固定目录干净detached、官方finish释放。Main本次PR/finish尚待完成；当前实际24.21min，原估90–150min＋独立前置约10min；最终以wave100-final-verification.json的Main START到finish计，不叠加CRM/Handoff。
