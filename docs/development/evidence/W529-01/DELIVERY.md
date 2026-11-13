# W529-01 · PGT-011 后台重试与管理员跨租户范围

正式 PostgreSQL **25 PASS / 2 NOT_RUN**。本轮 PGT-011 通过，原询盘保持 **60/60**。基线 `1dd538e4a48f5d0f5f5e33b4fe0b7765a054709c`，测试源码 `23db035bd78e6e8f4698b34918139958b87edfea`，固定 DEV-1 和 `codex/w529-01`。

## 实际验收

在本机独占 PostgreSQL 18.6 和 .NET SDK 8.0.424 上执行。Core 使用实际 PostgreSQL 迁移，Platform 依赖按锁文件采用 0.10.4。租户、表单、申请人、订单和库存均为模拟业务数据。超时测试推进夹具时钟使原租约到期，保留真实任务、认领和事务。租户清单来自实际枚举器，夹具固定 A、B 顺序并保留暂停前快照。HTTP 使用实际 Main Kestrel 与登录 Cookie，Stock 外部传输由确认消息的模拟接收端承接。

| 验收行为 | 实际结果 |
| --- | --- |
| 两个独立 scope | 同一次 `NativeTimeoutWorkerV2.ScanOnceAsync` 依次建立 A、B 的独立租户和数据库上下文。进入每个 scope 及结束后 HTTP 上下文为空。实际后台审批执行无需交互身份，夹具若调用该身份会立即失败。 |
| A 原申请人撤权后重试 | 停用原申请人后，旧会话实际返回 401。A 使用原 execution ID 从 epoch 1 重领为 2，登记服务完成系统审批。SQL 连接原提交和后台命令，核对原申请人、执行服务、审计执行 ID 和原 tenant。审批完成一次，人工投票为零，超时通知一条。 |
| B 暂停 | 平台管理员通过版本化租户 API 暂停 B。旧扫描清单仍包含 B，Owner 重新校验并以 403 `TIMEOUT_TENANT_DISABLED` 拒绝。B 的原认领、待处理记录、结果、命令和扫描记录快照相等，实例、租户、申请人和待办状态保留。 |
| 原结果权限 | A 的当前登记服务按旧认领键读回已提交结果。停用该服务登记后，同键返回 403 `TIMEOUT_SERVICE_NOT_AUTHORIZED`。B 暂停后，原获准管理员通过管理回读入口获得同一暂停回执，数据库只保留这一条授权命令。 |
| 平台管理权限 | 真实平台管理员提交包含 A、B 合法订单行和原版本的批量改价，业务权限返回 403。两个租户的价格、金额、审批状态、行版本保持，工作流意图为零。真实签发的模拟会话可读取自身档案。请求旧平台暂停入口返回 403 `E-SEC-034`，版本化入口返回 401 `REAUTH_REQUIRED`，B 管理快照保持。 |
| Stock 技术投递 | 原库存操作与队列已提交。停用原申请人及投递授权后，投递被拒且发送为零。恢复投递授权后，A 发送一次租户提示 `{}` 并提交投递回执，重复扫描为空闲。B 暂停后仍为 Pending，没有投递 run 或额外库存流水。 |

最终 TRX 的 A 样本 execution 为 `3d3b40f5-c016-454e-8517-9a995aa6d9e7`，actor 为 `0701fefc-5b41-4870-9163-737d1ba9b3b3`，originalRequester 为 `24cb9120-1b1d-41b8-ae0f-6ab6cc935629`。B 原 execution `705cbeed-445b-408e-9aeb-37f4645c7128` 保持 epoch 1。独立 Stock 样本的派送 request 为 `e5475d47-7203-42e0-bada-121e066b036e`。 [本轮验收代码](../../../../CP6.Tests/Erp/BackgroundTenantAuthorityPostgreSqlTests.cs) · [正式矩阵](../../acceptance/postgresql-tenancy.json)

## 交付与范围

恢复锁定依赖并构建 WebApi、CP6.Tests。最终定向 **6 通过、0 失败、0 跳过**，包括两个新增 PGT-011 场景、既有超时租约竞争和未知提交、Stock 派送权限、Main 真实登录权限。证据在 `D:/CP6-test-artifacts/W529-01/`，最终结果为 `results/pgt011-final.trx`，构建与进程退出、端口关闭和本地二进制摘要见 `build-tests-03.log` 与 `terminal.json`。早期隔离目录命名及模拟会话 Cookie 装配失败分别保存在 `pgt011-first.trx` 和 `pgt011-02.trx`。集中审查检查了业务命令、当前授权、跨租状态和回读的可观察断言。

本轮提交测试及验收台账。WP02-04 整卡继续按原范围。PGT-017 仍需全 writer 登记及共同 fence 两种时序，PGT-025 仍需现行 Owner 的物理租户 schema、共享回执和无损重连恢复。GitHub Actions 启动、取消和部署均为零。远端采用和固定席位交付见本任务 PR 与 `D:/CP6-test-artifacts/W529-01/main-finish.json`。 [PGT-017 已有库存时序](../W435-01/DELIVERY.md) · [PGT-025 已有原字节证据](../W510-01/DELIVERY.md)
