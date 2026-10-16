# 第十一轮：一条服务令牌自撤销链路

2026-10-16。COORD 接任后已重新 fetch Main 和 Handoff：Main 仍为 `397e56abad335b28396d8dc30df6f6a9be4a49df`，第十轮收口后的新交付为零，未重复接收或执行已合并工作。Platform 远端 main 仍为 `1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`；继续消费 Main 已采用的正式 0.10.3，无新发包前置。

本轮只分配 **B / WP02-02**：[执行包](WP02-02.md)，[机器可读范围](dispatch.json)。目标是把既有 `/connect/service-revocations` 接到真实认证来源、可信租户连接和完整同事务写集中。A 的字段计算、D 的客户端状态和 C 的宿主身份已有交付保留；三者本轮不新增写入窗口，待真实服务消费边界形成后再派对应消费者与兼容验证。认证/存储/撤销共享同一入口，本轮由 B 一个 Owner 连续实施，避免并行创造权威定义。

## 核对结论

- [第十轮集成](../../evidence/wave10-integration-20261016/README.md)与唯一[台账](../../roadmap.json)一致：三个局部窗口 CLOSED；完整任务 24/375 DONE、业务 0/125、247 NOT_RUN 与 41 历史引用不变。basis v1.2 用户采纳已决定，独立 verifier 未配置的真实限制继续保留，不再索要原件或重复采纳。
- Handoff 固定收口 `5624bf0df3d1d88d533057993424430a24f5f87d`；A/B/D 资料分支仍为 `e870b32c284f4212b37a9064d4ecb2821e996b5f`、`a26168f7e8f4482bfa5359d06381df7dbfefcadf`、`b691f766ddfc48615e5b902db3707447dc596ed9`，C 仍为 `6730a91899ee56fcb79de33d500ecbf3810cbb22`。这些旧交付已处理，本次没有新 START。
- `CrmOidcController` 目前用严格 Basic 客户端认证，只接受一个规范 jti。`CrmServiceTokenRecordStore.RevokeAsync` 按 issuer/client/tenant/jti 锁定实际签发记录；`IdentitySnapshotWriter.RevokeTokenAsync` 写快照和优先 Outbox，快照 trigger 还更新租户 generation。有限写集可独立收敛，不需先映射全部 328 张表。
- 现有租户池、目录 fence、ERP/Identity 适配和同物理连接原语继续复用；实际 source verifier/binding authority 和这条 Core 写集的隔离仍缺失。旧 TenantMiddleware 的默认租户不能作为可信来源，四张 Identity 消息表的 RLS 不能代表 Core 业务表已隔离。

## 领取、资源与交付

B 的限定窗口 CONFIRMED，完整 WP02-02 维持 IN_PROGRESS/CONFIRMED；本轮尚未收到实际 START，不将派工登记写成代码开工或业务通过。其他任务状态、定义、依赖与历史交付不变。协调窗口只写台账、生成页、四份项目状态和本执行包；B 写实现及其单份报告。

B 工作树 `D:/CP6-worktrees/wp02-02-identity-storage-20261015` 已核对干净且旧 HEAD 在 main，从本次确认主线创建新分支 `codex/wp02-02-service-revocation-20261016`，保留原分支。派工正常合并后，COORD 将此无实现提交的分支快进到已核实 main；B 在 START 记录真正开工 SHA。其他工作树、C 的缓存及根工作区七项未跟踪内容保留。

独占 PG 55436，核对时无监听。执行者启用自有新 `D:/CP6-local-test/WP02-02/tenant-connections-<GUID>`，使用真实 PostgreSQL、已发行包及真实代码；新建隔离夹具的数据仍是夹具，不称生产业务验收。出现端口占用只换空闲端口并记录，不停止他人进程。更改范围、公共合同或共享配置时先给 COORD 具体差异；窗口内连续实现只发一次 START，不重复等运输 ACK。

沿用[精简规则](../../../../AGENTS.md)：受影响行为的本地验证加一次任务级集中 SPEC/QUALITY 审查，失败后只复查修改及影响；旧有效证据准确引用。任务默认一份 DELIVERY，原 17 PGT、真实跨仓投递、其他 Owner 与完整生命周期未执行则保持 NOT_RUN。没有 Actions、正式发包、既有业务库迁移或部署授权。

派工本身只做台账结构/生成一致性、必要链接和一次完整 diff 自查，不构建产品、不启动数据库。本轮接手查询 Main 排队/执行中的 Actions 均为零，未取消运行。每次 push/PR/merge 前按实际主线/候选工作流及依赖核对触发；普通入口手动、R2 只版本 tag，当前没有触发配置改动。Handoff 保持私有、Actions 禁用且无工作流；固定消息发布不自动唤醒对话。

台账生成/结构检查通过，完整任务状态、依赖与原验收未改变。独立工作树未复制未跟踪原件，其 basis 检查如实返回来源不可用；对拥有原件的 `D:/CP6` 只读核验仍为 coverage=true、adoption/runtime=false，原因 `INDEPENDENT_VERIFIER_UNCONFIGURED`。这是两处来源可用范围的区别，不是原件新丢失、要求重新采纳或业务验证通过。
