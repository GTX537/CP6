# 第十二轮：真实服务令牌签发与租户有效性

COORD 已核对 Main `6e401945bc05647c94dbe948819cd4a14cd80fe6` 与 Handoff 收口 `e5b2dad2ccae76e67fe0469f36fb85bb8dbc6dd3`。A/B/C/D资料分支无新交付；第十一轮 PR237/238 已完成，不重做自撤销实现与已有效验证。

本轮优先 B / WP02-02：[执行计划](WP02-02.md)、[资源登记](dispatch.json)。服务自撤销已有真实租户事务，但 client_credentials 签发仍由默认连接读目录/保存记录；上一轮集成夹具仅租户活跃查询是替身。本轮补齐真实租户有效性、可信签发来源和同事务记录，使实际签发→自撤销能在明确的新 PostgreSQL profile 运行。权限、工作台与跨仓正向回退仍受各自前置约束，A/C/D不新增窗口。

唯一执行者是当前 COORD 内 `/root/wave12_issuance`。已收到真实只读准备，台账登记发布后才开始实现；原B对话不重复领取或并写。工作树 `D:/CP6-worktrees/wp02-02-identity-storage-20261015` 已干净核对，从最新 main 创建 `codex/wp02-02-service-issuance-20261016`，保留旧分支、其他worktree和根目录原七项未跟踪材料。派工合并后先快进到准入 main 再实施。

S02-00、WP02-01均DONE，正式0.10.3已由Main采用。现有池、目录、同事务guard及第十一轮安装/撤销复用；真实租户目录不是新的平行主账。控制/运行角色分离，拒绝未知布局，旧profile含义不变；仅自有空隔离库验证，无现有数据库迁移或环境切换。

独占PG55436，使用新的自有数据根和随机loopback HTTPS端口；占用时换空闲端口，不停止他人进程。执行者只写任务代码和一份报告；COORD同步台账、生成页及四状态文件，正常串行合并和远端核对。相关本地行为验证加一次集中SPEC/QUALITY审查；更正只复查受影响部分，不因SHA/文档重复产品测试。

完整任务24/375 DONE、业务0/125、247 NOT_RUN与41历史引用不变。WP02-02完整任务仍IN_PROGRESS/CONFIRMED；本轮未覆盖的原17PGT、全Main、全域Owner、消费者、既有库迁移及生产验收不能提升。basis v1.2无需重复采纳，独立verifier未配置限制保留。接手时Main queued/in_progress Actions均为零，无取消、触发变更或远程运行授权。

## 签名器范围补充（COORD自主决定）

真实HTTP验证发现，新host虽然在注册时复制签名配置，但controller首次解析仍强制构造共享legacy CrmOidcCrypto，原可变配置可导致首次请求失败；先warmup再变更只能掩盖缺口。现明确增加 `CP6.WebApi/Services/CrmOidcCrypto.cs` 及其独占资源，由同一B执行者添加新profile专用、注册时捕获的不可变签名快照依赖，使首次解析也使用相同issuer/keys。新类型在原已允许的ServiceTokenTenancy目录。

不改Program或依赖；未启用新profile时旧构造、Sign/Validate/Jwks合同保持。只增加首次解析前变更原options的真实HTTP签名/JWKS检查，以及旧构造兼容定向检查，原其他输入未变的成功证据复用。所有改动仍在同一次任务级集中审查中完成。此登记只扩展必要共享文件窗口，不提升实现或业务完成状态。
