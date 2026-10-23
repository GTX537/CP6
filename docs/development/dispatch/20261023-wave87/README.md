# W87 / 七天一期D5：CRM签名信任目录持久化

COORD本聊天单人，2026-10-23T23:32:38.735500+00:00。Main `0041e410dba05fce60ae6bcbd7ec3b00b7fb142a`，CRM `3e69fa188a43d261660fe871103bc2b7b400a674`，Handoff `b20c534cad638b669272fd78fb804a86818dfb23`。W86 Main478/479、Handoff62已采用，Issue477关闭；41独立相关用例通过（PG最终批18/19后同编译单例补测1/1，保留连接失败），实际13.14min＋前置约2min，准确分支删除及席位释放已核实。W37–W86不重复。

## 前置和边界

AGENTS/POLICY、最新台账、工作树及容量已核对；WP02-09前窗W59全部收尾。核对WP02-06/09、CRM-INQ-01§7、原R3V15授权矩阵、冻结TrustedProjectionKey及W59实际源码，W58当前身份和W59真实Ed25519已远端采用。W59仅有进程内信任，持久目录和撤销重启恢复为当前可独立完成前置；它不替代真实Site/Form权限生产者和业务Owner最终门。

新PG namespace保存共享的可信签发者公钥配置，包含不可变发布全文、当前head、持久已知key材料与撤销墓碑。此数据为共享宿主信任配置，不是租户业务授权；不改变TenantId和当前组织/主体/用途绑定，不采用全库租户schema。没有私钥或客户端自带JWK，不部署生产配置、不迁移既有环境。

可信宿主在调用方持有的同连接同事务中发布，版本CAS，相同操作Id精确重放原发布，冲突拒绝；旧发布重放不重新激活。空目录显式拒绝所有签名，目录缺失不可用。原生锁序列化发布，撤销不可撤回，移除后重加或进程重启不能换材料或恢复撤销。完整目录以一条不可变正文保存，防止部分成员提交；所有失败/取消/外层回滚撤销写集，不隐式commit/retry。

保留Nullable ActiveToUtc合法开放区间；起止时间用UTC ticks无损存储，避免PG微秒截断.NET tick边界。正文确定性排序和摘要；数据库检查结构、精确材料和历史一致性，不能只依赖应用层。读运行身份仅获受限读取函数EXECUTE；SECURITY DEFINER固定search_path、schema限定、撤销PUBLIC权限，不为FOR SHARE赋运行身份UPDATE权限。发布权限与运行身份分离，所有授权由可信安装方显式授予。

运行验证在实际调用方事务内锁定当前head，读取数据库当前时钟，重用冻结严格合同和W59真实Ed25519。证据绑定实际连接/事务及目录版本，复用必须在同一仍有效事务中重新检查时间和版本。CanAuthorize=false；当前身份/会话/撤权、实际crm-site动作和Site/Form/字段范围、业务提交最终门仍必须后继接入，不能把DeptIds当SiteIds。

## 验证、时间与收尾

预计实际50–80min：持久化与原生约束25–40、定向PG/签名行为验证12–20、集中审查及三仓收尾13–20；独立前置约3min。登记start `2026-10-23T23:29:07.791928+00:00`，七天持续目标保持ACTIVE。

新自有PG复用空闲55446，验证真实重启/新实例、材料冲突、撤销/移除重加、空目录、CAS并发、读/发布互斥、原结果重放与零重新激活、事务回滚、实际运行角色不可写、合法时间精度/开放边界、真实签名绑定/过期及最终重查。安装/形状漂移fail closed；相关W59签名测试按输入影响复用或重跑。仅一次任务级作者SPEC/QUALITY，无全仓回归、Actions或生产验收。

DEV-1 Main协调；DEV-2 Handoff START保存/finish deferred后bind CRM，CRM合并/finish后bind回Handoff原任务。正常PR/远端采用/准确任务分支删除/finish释放；固定目录复用，不新增代理和工作树。总计24/375整卡、125目标0完整交付、247运行NOT_RUN保持。W51指定停止data删除曾由自动审批以blocked by policy拒绝，保留不重试。

## W87实际交付

CRM [PR122](https://github.com/GTX537/CP6.CRM/pull/122)已采用，source `f87d5dab2049bed802f77d1ee4e29e1a1122cd81`，远端main `1a7f0838c3db87d0b736fbe1c88d4ba9a0b1841f`，9条任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/1a7f0838c3db87d0b736fbe1c88d4ba9a0b1841f/docs/development/evidence/W87-01/DELIVERY.md)。33项独立相关用例通过（16原生PG＋17已有签名），无跳过；API及必要依赖构建通过，实际数据库重启恢复；真实Owner验收NOT_RUN，无Actions。

共享宿主公钥目录显式新PG namespace，完整不可变发布、当前head及持久已知材料/撤销墓碑。保留开放ActiveToUtc=null与有限UTC ticks精度；原生完整正文/材料/重复/冲突检查，移除重加或重启不可换材料、恢复撤销。版本CAS和原操作精确重放，历史重放不重新激活；所有写集同调用方事务，无隐式commit/retry。

运行身份只有受限读取函数权限，不为FOR SHARE授予UPDATE。读锁与发布锁共用原生head，锁后数据库时钟和冻结W59真实Ed25519；证据绑定实际连接/事务，重用再查当前时间/版本。CanAuthorize=false；当前身份、实际Site/Form动作/范围/字段和业务最终门仍需后继Owner。共享目录不是租户业务权限，未采用全库租户schema。

一次COORD集中SPEC/QUALITY发现摘要约束漂移漏检，真实红灯修复且保留；最终列/约束/trigger/函数体/安全属性检查通过。33独立相关用例包含实际PG stop/start后的原结果及撤销恢复、两个方向的原生锁阻塞、CAS单赢家、空目录/错绑、回滚/取消、运行角色不可写和数据库时钟过期。自有PG55446最终停止，数据/TRX/红灯/重启证据保留。CRM准确任务分支已删除、固定树干净detached、finish释放。

没有新依赖、Actions、既有环境迁移或部署。实际权限生产者、Site/Form Owner、配置Preview/Activate、公开Attempt/受理/隐私及真实投递仍待接。24/375整卡、125目标0完整交付、247运行NOT_RUN不提升，七天目标ACTIVE。W51指定停止data删除曾由自动审批以blocked by policy拒绝，保留不重试。

Handoff [PR63](https://github.com/GTX537/CP6.AI-Handoff/pull/63)已采用 `49895cb45ffd949d2f6447365f9b0ec66ebb9ece`，准确分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约15.99min，原预算50–80min，最终以finish为准、子任务不重复累计；独立前置约3min。
