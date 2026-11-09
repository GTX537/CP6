# W372-01 · Core RMA 托管提交

2026-11-09 10:11:57 UTC 在 DEV-2 开工，分支 `codex/w372-01`，基线 `1b3cc3238aba6c42f94ab35a23890bd5d4f1a5a6`。 [W364 草稿](../W364-01/DELIVERY.md) · [W361 原 journal](../W361-01/DELIVERY.md)

## 交付

`install-rma-core-submit-writer-v1 --stdin` 核对原物理库、完整目录、Ready/history、W361 原件和 W364 原权限目录。安装事务保存 W364 的原请求和变更前后完整目录摘要，以及提交 writer 的原回执。当前 CLI 仅接受这条确切的已采用权限变化并回放 W364 原字节。采用后旧 W364 CLI 实测返回 `draft-layout-mismatch`。 [安装与兼容](../../../../CP6.Core/Persistence/Tenancy/RmaTenantStorageV1.SubmitWriter.cs) · [CLI](../../../../tools/CP6.TenantStorage/README.md#hosted-core-rma-draft-submission)

`RmaReadTenancy:SubmitEnabled` 开启现有 `POST /api/wms/rma/native-applications-v1/{id}/submit` 的独立 `wms-rma:submit-write` 上下文。沿用原权限、If-Match、版本、字段与共享出库来源判断。普通事务取得原认证锁，授权连接保留共享读栅栏及最终当前权限检查。同键回放走原 `query` 权限，首次提交走原 `add`。 [Owner](../../../../CP6.Core/Services/Wms/RmaApplicationOwnerV1.cs) · [权限与锁顺序](../../../../CP6.Core/Services/Integration/OperationsOwnerV1.RmaDraft.cs)

同一 NOLOGIN 角色获得 head 版本列 UPDATE 和核对 DRAFT 所需的 7 列 SELECT。固定 writer 按租户、ID、预期版本和原生 DRAFT 状态更新 head，追加下一版 SUBMITTED，并通过 W364 原桥写 W361 原 journal。整个写入使用一个普通 PostgreSQL 事务，沿用提交或回滚确认不明时的同键恢复。归档只为已采用的提交 marker 接受 19 项精确且不可转授的权限，已采用的草稿仍按 11 项校验。 [事务](../../../../CP6.Core/Persistence/Tenancy/TenantRmaReadStore.Draft.cs) · [归档 ACL](../../../../eng/wp02-02/rma-tenant-archive/archive.py)

## 实际验证

同一个隔离 PostgreSQL 18.6 环回库，55567，库 OID 16386，system identifier 7706117870886737900。发布 W351 DatabaseInit `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` 实际 PID 16148 exit 0，迁移历史匹配。复用保存的 W353、W361、W364 可执行物实际安装旧步骤。A/B 登录、出库和载荷均为模拟。 [复现入口](../../../../eng/wp02-02/rma-tenant-migration/verify_submit.py)

| 场景 | 实测结果 |
| --- | --- |
| 现有托管请求 | A/B 真实 bearer 并发提交同 application 与 operation UUID，均 200，各有自己的 v2 和 journal。v1 payload 保持原值，原 ETag、历史 GET 及 8 位数量 `1.12345678` 保留 |
| 拒绝和原子性 | 缺 If-Match 428，错 ETag 和当前来源变更 412，重复提交已提交版本 409。原生直接尝试已提交 head 返回 false，且零 journal。直改 head、跨租和切换执行角色均 42501 |
| 故障及当前权限 | 私有故障触发器在原 journal INSERT 前实际看见已更新 head 和新 v2，然后报错。head、版本和 journal 均回到写前状态。普通 PID 55612 等待原认证锁持有者 50872，撤 add 提交后新请求 403 且零写入 |
| 原结果 | 丢弃首个 200 正文后，以原 If-Match 和同键在仅 query 时恢复原结果与 v2 ETag。同键改 If-Match 409，实际 journal 输入、摘要、结果和对象键字节精确 |
| 安装版本 | 缺 W364、错库 OID 和异键拒绝。旧 W364 在扩展前回原字节，扩展后 exit 1。当前 CLI 扩展前后均回 W364 原字节，同键回 W372 原件。旧 W361 可执行物实际回放其原 journal 与 W353 commissioning。两份 Ready/history 保持 |
| 归档 | 原 W363 式非 owner 角色 `w363_backup` 以 `pg_read_all_data` 实际导出 11 项草稿和 19 项提交权限的 v1。owner v2 通过原生 pg_dump、COPY 和校验，带 A 的一份 head、两版、一条出库引用及两个精确 COMMITTED 原件。额外 actor UPDATE 或破坏受保护变更绑定均拒绝且无 manifest |

完整原件位于 `D:/CP6-local-test/CP6-72H-20261109/W372-01/rma-submit-20261109T103302-90db8e21/submit`。`rma-submit.trx` 实际 1 通过、0 跳过，`native-submit-statements.log` 有普通登录提交与 journal 故障，`final.json` 于 10:34:12 UTC 写入。`source.json` 记录所有实际二进制 SHA-256。旧 W364 对新入口的实际 RED 为 exit 2，保存于任务根。

锁定还原复用全局包。物理余量下降至约 1.2 GiB 后错开编译，10:25:40 UTC 在 1654 MiB 物理和 20949 MiB 虚拟余量启动。CLI/Core 单节点 Release 5:41、宿主与测试复用该缓存 0:54，两次均 0 错误。只在任务根 `sealed-cli` 保存可运行 CLI 依赖闭包，宿主与测试保留在 DEV-2 可复用缓存，其摘要仍在原件。

实际 HTTP PID 25760 和 postmaster 22540 已停止，50781 与 55567 均无监听。实测故障为确定回滚与丢响应正文，未知提交沿用既有协议。正式恢复及生产采用各有对应范围。Actions 和部署均为零。候选分支及干净 DEV-2 交 COORD 处理集中审查、远端采用和席位释放。
