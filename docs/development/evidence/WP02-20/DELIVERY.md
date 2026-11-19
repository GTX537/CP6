# WP02-20 客户端升级渠道、签名与失败恢复

**WP02-20 五项原验收通过。SYS-CLIENT-01 共 22/22 项通过，必需修订 0。** 产品候选 `cecb01120a2637a46b55cfb8129b311de4c3e7db`，工作基线 `04fc103b17ce8ea9bc55011c0f3b44962b979cd7`。执行人 `EXEC-WP02-14B`，Main `DEV-1`，分支 `codex/wp02-20`，工作区 `D:/CP6-worktrees/wave17-dispatch-20261018`。派工原件 `9162cd5b02f29b51e5ac84f57a35c27974999d60`，登记采用 `caf4d4496fc40b6fdce5f76d37d202e89ea27989`，两处原件 Blob 一致。 [派工 #331](https://github.com/GTX537/CP6.AI-Handoff/pull/331) · [START](https://github.com/GTX537/CP6.AI-Handoff/commit/008c7b862d5930b87402c44960878f621fa46f8f) · [本卡 PR](https://github.com/GTX537/CP6/pull/1271)

## 实施与适用条件

- Main 和客户端使用同一语义版本比较器，统一校验最低、最新与渠道。兼容的旧请求可以沿 `stable` 默认渠道，引导公开响应保留原 API 版本。受到强制升级的客户端从配置 Main 的 HTTPS 引导获取校验值，经 HTTPS 下载发行包。引导和下载均拒绝重定向。会话及业务调用沿现行传输层 与 Owner 授权。
- Android 将已核摘要、包标识、版本和签名指纹的 APK 交系统安装器。Windows 对直接 MSIX 检查平台标识、包版本、Windows 信任和发布者指纹。使用 `.appinstaller` 时外层由 `Sha256` 固定，内层 MSIX 另核 `PackageSha256`。经验证的包下载到本地私有目录。错误或取消时清理未交接文件。
- 检测到强制升级后进入升级页，阻断新业务请求。现有会话可从任务页复查版本后进入升级页，按同一来源、租户、主体、设备和准确的作业与完成键查原结果。该入口只有 GET。退出沿原 Native 撤销流程。安装交接显示待重启核实的状态，新的运行进程重新向 Main 引导报告版本。
- 发布方按已批准平台配置 `Security:NativeClient:{Android,Windows}` 的 `Channel`、`MinimumVersion`、`LatestVersion`、`DownloadUrl`、`Sha256` 和 `PublisherSha256`，仅 Windows 描述文件需要独立 `PackageSha256`。发布配置、推广、真实现场设备安装均按 R2 管理，本次采用为源码合并。 [开发卡](../../task-plans/WP02.md#wp02-20) · [R2](../../../client/r2/README.md)

## 五项实测

| 原 AC | 入口及实际结果 |
| --- | --- |
| AC-CL05-01 | 真实 Main 引导报告 stable、最低 2.0.0、最新 2.1.0。错误渠道返回 400，非法政策返回 503。客户端回归覆盖预发行版本的数字优先级、超32位数字、构建后缀、非法版本和低于当前的强制目标。 |
| AC-CL05-02 | AVD 从本地 HTTPS 发行入口传输实际签名包。拒绝明文引导、摘要错配、包版本错配、另一证书签名及渠道错配。拒绝后仍运行 1.0.0，同设备同旧包摘要。SDK 验证了换签包本身的签名。客户端核对测试策略里的包摘要后，发布者指纹检查拒绝了该包。正确包进入系统安装器。已升 2.1.0 时，低版本目标不提供下载，系统常规安装返回 `INSTALL_FAILED_VERSION_DOWNGRADE`。 |
| AC-CL05-03 | 现行客户端路径验证强制升级阻断新业务请求，包括完成写入及一般业务列表读取。同源、有当前权限并携带准确原键的结果查询可用。Android 以模拟用户从任务页进入强制升级、查询并退出。真实 Main 在隔离 PostgreSQL 返回原回执相同两次，拒绝过低版本 Required 新登录 403，旧版显式退出 200，退出后同一访问令牌读取 401。 |
| AC-CL05-04 | Android 保留模拟草稿文件、设备及原操作键。用当前生产客户端 Core 和模拟 WMS Owner 重建升级前、强制中、升级后和获准回退的会话，观察一次扫描、一次原完成、四次 GET。真实 Main 两次回查结果相同，数据库保持一条原命令回执、新扫描 0、新库存事务 0。 |
| AC-CL05-05 | 系统确认之前保持 1.0.0 并核对已安装的旧 APK 字节。系统更新后由包管理器独立读取 2.1.0/210，再启动实际进程，捕获本次 bootstrap 报告 2.1.0、设备 ID 和已安装 APK 的精确摘要。获准保留同一数据的开发回退后，同样实读 1.0.0/100。 |

### 下载、安装及实际进程

Android 15 / API 35 / x86_64 AOSP 模拟器 `WP0220Fixture`，测试应用 ID `dev.cp6.wp0220`，设备 ID `android-f409d71a1607403cbd9e0514302bfa2a`。使用产品客户端源码，测试构建仅替换包标识、显示版本、版本码和 APK 自身的局部测试 TLS 信任。两个包由本地测试证书签名，SHA-256 指纹 `85c56a8ee2ef241c188de0425a4f67f1dd3d19e124a4ea97ee85cf3f67c7ba19`。测试回退在调试虚拟设备显式授权，政策先将同渠道最低版本设为 1.0.0 并复验同一已批准旧包。常规客户端不会发起降级。

| 阶段 | 实际状态 |
| --- | --- |
| 下载 | 正确包传输完成，摘要 SHA-256 `45de6da59fdcdf8c097bb899db7d695522a0ef562d662dbb9348e6b6df3ac5d7`。系统确认页出现时，旧进程仍运行且包管理器保持 1.0.0/100，已安装摘要 `21e5f28a8cd28de72cdeb466a812b70b505927d184b22c1394a5ddd9ad2a80ae`。 |
| 安装 | 系统 `UPDATE` 确认 `2026-11-19T09:29:10.521637+00:00`，包管理器在 `2026-11-19T09:29:17.069216+00:00` 确认 2.1.0/210。 |
| 启动 | 新进程 PID `4699` 发送 stable 引导版本 2.1.0，已安装 APK 字节摘要 `45de6da59fdcdf8c097bb899db7d695522a0ef562d662dbb9348e6b6df3ac5d7`，原设备 ID 一致。 |
| 获准回退 | 新进程 PID `4888` 发送 stable 引导版本 1.0.0，包管理器为 1.0.0/100，安装摘要 `21e5f28a8cd28de72cdeb466a812b70b505927d184b22c1394a5ddd9ad2a80ae`。草稿原操作 `71cfda3c-70a8-4f71-b200-bd4a481d5d3f` 及文件摘要 `a23ac83ad4b81faabd255770f157e2f170d03f1ed899484c8bc268a36f0cf0e5` 在三阶段一致。 |

Windows SDK 用实际 Desktop 构建生成本地签名 MSIX 2.1.0.4，应用程序集版本 2.1.0，包 SHA-256 `7201b3de12d3c07e3c7fe1004c33403eea88400553652ff64e11718e8067e0c1`。实际 Windows 适配器认可包标识和版本，拒绝错误版本以及未受信任的测试签名。单元回归验证外层描述文件和内层包分别验摘要。系统安装与实际运行回读在 Android 虚拟设备完成。

## 关闭核对

按接受设计第 6 节、原目录及唯一台账逐条核对 ID、定义指针、候选输入、历史证据和现行回归。原档案 SHA-256 `e86bc515d64622afed314aa67776fa5b26e3c35e234ad1c0ffaa4d067da1f26d`。此前 17 项逐项复用，受影响的认证、设备和客户端入口以当前候选复验。现行必需修订清单及 `runtime_case_ids` 均为 0。 [设计入口](../../../design-handbook/modules/SYS-CLIENT-01.md) · [原目录](../../assessments/20261011/catalogs/foundation.json) · [唯一台账](../../roadmap.json)

| 原号 | 实际结果及证据 |
| --- | --- |
| AC-CL01-01 | PASS，双宿主并发消费同票据，仅注册一台设备，票据与绑定同事务。 [WP02-19](../WP02-19/DELIVERY.md) |
| AC-CL01-02 | PASS，过期、重复、错平台及坏公钥均拒绝，无残缺 Active 设备。 [WP02-19](../WP02-19/DELIVERY.md) |
| AC-CL01-03 | PASS，原设备丢失响应后可只读回原六字段，错键、租户、持钥、时钟及禁用均拒绝。 [WP02-19b](../WP02-19b/DELIVERY.md) |
| AC-CL01-04 | PASS，设备独立激活，无用户会话及 FullAuth，业务仍重新核权。 [WP02-19](../WP02-19/DELIVERY.md) |
| AC-CL02-01 | PASS，同 RowVersion 竞争单胜，原目的仓、库区及租户权限保持。 [WP02-19](../WP02-19/DELIVERY.md) |
| AC-CL02-02 | PASS，禁用与刷新撤销原子，保存故障整组回滚。吊销后刷新拒绝。 [WP02-19](../WP02-19/DELIVERY.md) |
| AC-CL02-03 | PASS，启用后旧会话及 refresh 仍拒，完整新认证后按原设备分区恢复。 [WP02-19b](../WP02-19b/DELIVERY.md) |
| AC-CL02-04 | PASS，换仓、换模式保持原在途 Owner 与键。真实读和拒绝、获准模拟 Owner 同键恢复。 [WP02-19b](../WP02-19b/DELIVERY.md) |
| AC-CL03-01 | PASS，RSA／ECDSA 只签原四字段，错签及陈旧时间不刷新成功观测。 [WP02-19](../WP02-19/DELIVERY.md) |
| AC-CL03-02 | PASS，双宿主共用 PostgreSQL，顺序和并发重复 nonce 各只有一条成功证据。 [WP02-19](../WP02-19/DELIVERY.md) |
| AC-CL03-03 | PASS，心跳 CurrentTaskNo 只作遥测，无任务或原命令写入。 [WP02-19](../WP02-19/DELIVERY.md) |
| AC-CL03-04 | PASS，时钟失配拒绝。离线切人与迟到心跳隔离，原主体可恢复原草稿。 [WP02-19b](../WP02-19b/DELIVERY.md) |
| AC-CL04-01 | PASS，引导、设备、认证和业务四轴独立。SSO 仍需本地因子，pending 无会话。 [WP02-19](../WP02-19/DELIVERY.md) |
| AC-CL04-02 | PASS，原可选路径保持，RegisteredDeviceGate 核对当前设备、租户、证明、版本并拒绝外租户。 [WP02-19b](../WP02-19b/DELIVERY.md) |
| AC-CL04-03 | PASS，Web family、无 family、Native 输出前后核权。拒绝只隔离原代次且无新 token。 [WP02-19](../WP02-19/DELIVERY.md) |
| AC-CL04-04 | PASS，完整认证开启 FullAuth，刷新和切换不续期，切人不会转移因子和草稿。 [WP02-19b](../WP02-19b/DELIVERY.md) |
| AC-CL04-05 | PASS，旧 Native 明确拒绝 pending 且无会话，现行端沿原交互。未知确认保原业务键。 [WP02-19b](../WP02-19b/DELIVERY.md) |
| AC-CL05-01 | PASS，共享 SemVer、最低与最新政策，非法政策 503、错渠道 400。回归覆盖版本序和稳定渠道。 [本卡实测](#五项实测) |
| AC-CL05-02 | PASS，Android 从指定 HTTPS 发行入口分阶段验证实际 APK，五类拒绝保旧态。Windows 实际 MSIX 验证包身份并拒绝不受信任的本地签名。 [本卡实测](#五项实测) |
| AC-CL05-03 | PASS，强制升级业务写入受门控，实际 Android 现有会话查原结果后退出。Main 真 PostgreSQL 撤销并拒绝重读。 [本卡实测](#五项实测) |
| AC-CL05-04 | PASS，批准测试回退保原设备和草稿/操作键。模拟 Owner 一次完成，真 Main 一条原回执且无新增扫描或库存事务。 [本卡实测](#五项实测) |
| AC-CL05-05 | PASS，Android 系统安装确认、包管理器与新进程独立证明两版实际运行、设备身份及 APK SHA-256。 [本卡实测](#五项实测) |

三张覆盖卡分别是 WP02-19、WP02-19b、WP02-20。WP02-19 的 #1266 采用 `05517ee4968a8bfd1c39d4dda0a92269b5af8783`，WP02-19b 的 #1269 采用 `89e61880597c111d908a8b10f30b074a107b419b`，直接前置 WP02-28 的 #809 采用 `90e69393ef976c82c871155a44d77cb56c3872c7`，均为开工时远端 Main 祖先。本卡采用、冒烟和席位处置依同一 PR、官方 `finish` 和通信分支 DELIVERY 回读。 [#1266](https://github.com/GTX537/CP6/pull/1266) · [#1269](https://github.com/GTX537/CP6/pull/1269) · [#809](https://github.com/GTX537/CP6/pull/809) · [本卡 PR](https://github.com/GTX537/CP6/pull/1271)

## 运行记录与模拟范围

本机 Windows，Main 使用 .NET 8 和真实 PostgreSQL 18.6。模拟认证主体、设备、业务作业及发布渠道。真实 Main HTTP 回查使用原命令回执夹具，由当前 Owner 授权和数据库对象响应。Android 系统安装及实际客户端进程为实测，用户、TLS 签发者、发布 API 和 WMS Owner 为本地夹具。业务写入计数在明确模拟的 Owner 与真实 PostgreSQL 原结果回查两侧分别记录。WP10-09 与 WP10-12 的正式移动写入和批量采用沿原归属。 [已接受的组合口径](https://github.com/GTX537/CP6.AI-Handoff/pull/326)

| 输出 | 结果 |
| --- | --- |
| `client-final.trx` | 114 项通过，零失败与跳过。 |
| `main-upgrade-green2.trx` | 68 项通过，包含实际引导、Native 退出和原库存操作。 |
| `main-device-adopted.trx` | 131 项通过，零失败和跳过。沿 WP02-19b 现行认证、Native 与设备过滤复验。 |
| `device-final-platform.log`、`device-release-proof.json`、`device-final-events.jsonl` | 实际签名包、失败保护、安装、启动、批准回退和分阶段摘要。 |
| `device-live-session-proof.json` | 现有会话复查版本，进入硬性升级，查回原结果并撤销。记录客户端和模拟 Owner 两侧的实际结果。 |
| `desktop-final.log`、`mobile-old.log`、`mobile-new.log` | Windows 与 Android 两个测试构建通过。 |

Main 两组共 198 项不同用例通过。原始记录和复现脚本保存在 `D:/CP6-test-artifacts/WP02-20/`。只引用不含可复用凭据的记录。普通远端 Actions 和生产推广未执行。

## 工时与处置

正式开工 `2026-11-19T03:52:34.764495-05:00`，本卡技术候选 `2026-11-19T04:34:01.287312-05:00`，有效工时 2486.523 秒。22 项及采用核对用时 114.204 秒，记入目标池。M 档预算 4 小时，6 小时为强制拆卡点。本卡无拆分，累计计划 20.491 小时。按 #331 将 WP02-21b 的正式收尾 377.416 秒入账一次，该卡技术与正式收尾共 3146.426 秒。WP02-29 依原裁决暂缓结案，本次下一候选为 `WP02-18`，实际领取依总控派工。 [收尾工时依据](https://github.com/GTX537/CP6.AI-Handoff/blob/4406558ee02ae635acec7651165f76144ba31271/handoffs/CP6-DEV-20261012/tasks/WP02-21b/replies/20261119T074320Z-coord-acceptance-ack/MESSAGE.md) · [#330 暂缓裁决](https://github.com/GTX537/CP6.AI-Handoff/pull/330) · [单卡计划](../../planning/20261117-card-execution.md)
