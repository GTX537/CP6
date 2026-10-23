# W59 / 七天一期 D5：询盘权限投影真实验签与信任目录

COORD本聊天单人，2026-10-23T08:23:41.666227+00:00。Main `c16220a9134667336edbe801f9fccab3264992e3`，CRM `aaed1098b35cf74fd286099f2bc31b1b9b071f80`，Handoff `3aa2fac8dd49aca5bb0e2a08cd0b7034a864fcea`。W58 CRM103/Handoff34/Main427全部采用，130项相关本地用例通过，登记实际63.09min，前置约3min；准确任务分支删除、固定树干净detached、全部席位finish，任务Actions均0。七天目标ACTIVE；W37–W58不重复。

## 前置核查与范围

最新AGENTS/POLICY哈希未变；实时status/capacity、实际origin、固定树、W58最终证据和W59三仓重复PR均已检查，无席位或容量阻塞。S01-08原合同和WP02-07已采用；WP02-06仅局部基础、WP02-09整卡未完成。当前原PermissionProjectionVerifier只依赖IEd25519Verifier/IProjectionTrustStore，仓库没有具体实现或实际验签覆盖。

已实读CRM-INQ-01 §7、SYS-IAM-01 §5.2和S6 §3.6。Main字段来源/算法存在，但CRM完整字段注册映射尚未签认，不能虚构已完成映射、可信生产密钥或共同最终门。因此本窗先完成WP02-09可独立交付的签名投影验证前置：采用既有crm-iam-projection-v1及原规范化字节规则，接入真实Ed25519验证，按可信宿主配置核精确issuer/key/catalog、有效时间、撤销及轮换，拒绝未知/重复/冲突信任身份。信任快照不从请求payload/JWK/浏览器决定；目录更新与核查要有明确并发及失效语义，防止撤销后复用旧核查结果。

具体实现保留原tenant/subject/purpose和五分钟有效期约束、严格schema/重复未知字段拒绝；新结果封装不可变事实和信任版本，仅证明本次签名核查，不实现业务ALLOWED_HELD。后继Owner仍须核当前身份、原字段映射、动作/对象/PII交集并在共同事务内最终检查；本窗不签发生产许可、不配置生产信任、不切换Program/旧业务工厂，不发明新的公共wire合同。

密码实现使用维护的库，不手写密码算法。已核官方NSec文档：26.4.0要求.NET9，25.4.0支持当前.NET8；若采用固定25.4.0，则核其依赖和本地真实native装载，相关项目/锁文件串行更新。原S01冻结输入、生成器与生成资产默认不改；若实际验签暴露原适配缺陷，先保存失败，准确限定修正与合同依据，不悄悄改原件。

## 预计实际执行与验证

预计90–140min：35–55min真实密码适配与可信目录；25–40min时间、撤销、轮换、并发和严格输入验证；30–45min相关回归、一次作者SPEC/QUALITY及三仓收尾。计Main登记start至最终finish，不重复累计子任务，七天目标继续。

本地验证实际Ed25519标准向量/签名与篡改、错误密钥/issuer/catalog、未来/过期/超过五分钟、tenant/subject/purpose错绑、重复/未知字段、目录撤销/轮换/旧版本拒绝、输入输出集合变异；引用未变W58当前身份和PG撤销证据，不以新增用例数为目标。只跑受影响合同/签名/身份回归及必要项目编译；本窗原则上不改PG SQL，不新增迁移，不启用既有环境。原生库依赖改变按实际影响核宿主构建。

DEV-1维护本任务总台账与四状态，DEV-2先保存Handoff START并finish deferred，再bind CRM实施；CRM正常PR/远端采用/准确分支清理/finish后同席位返回Handoff交付，最后Main PR及finish。无新工作树或代理，无Actions、既有库迁移、发包或部署。完整24/375整卡、125目标0完整交付、247 NOT_RUN不提升。W51准确data删除此前被自动审批拒绝，仅“blocked by policy”；继续停止保留，无重试或绕过。

## W59实际交付

CRM [PR104](https://github.com/GTX537/CP6.CRM/pull/104)已采用，source `73163779f0a5d4d213cd500023f6629baa849c63`，远端main `611e95cfc83928c192070c5b101f2c6b9f202183`，10任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/611e95cfc83928c192070c5b101f2c6b9f202183/docs/development/evidence/W59-01/DELIVERY.md)。204项相关本地用例通过（18项签名/信任、186项既有询盘合同/终态）；API/Worker相关构建；真实Owner/宿主验收NOT_RUN，无Actions。

采用原PermissionProjection严格合同/规范化字节，补真实Pure Ed25519与可信密钥目录；精确issuer/key/catalog、tenant/subject/purpose、有效期和撤销检查。原子CAS更新目录使旧结果失效，防进程内密钥ID换材料/撤销回滚；轮换保留原签发时间语义。结果集合逐层只读，CanAuthorize=false，不能充当当前身份、Owner字段映射或共同事务最终门。

NSec25.4.0/libsodium1.0.20.1精确固定，nuget.org实际恢复并在Windows x64运行；原冻结S01/生成器/生成合同、八份PG SQL与Program不变。生产可信目录及跨进程/重启恢复仍需实际宿主采用；Main全字段映射未签认，真实业务验收不提升。本窗没有启动PG、迁库、Actions或部署。

一次作者完整SPEC/QUALITY通过；204项最终结果和初始RFC红灯/CS8604失败记录保留，不重复累计18项前次成功。未变W58当前身份/PG和W57发布证据仅复用。CRM准确本地/远端分支删除、固定树干净detached、finish释放。

七天目标ACTIVE。继续真实Owner必要前置与D6可独立公共支撑；24/375整卡、125目标0完整交付、247 NOT_RUN不提升。W51准确data删除此前被自动审批拒绝，原因仅“blocked by policy”；停止保留，无重试绕过。

Handoff [PR35](https://github.com/GTX537/CP6.AI-Handoff/pull/35)已采用 `4e6cb8a25ae06931ec590df95384c3abbbfa1f57`，准确本地/远端分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约23.17min，原预算90–140min，最终实际以finish为准，不重复累计子任务；此前只读前置约4min。
