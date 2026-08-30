# 项目当前状态

## Space Editor 工具反馈增强已验收（2026-08-30）

- 空间编辑器五种工具现提供持续上下文提示、明确光标和 `aria-pressed` 状态；旋转模式使用高对比度圆形手柄，反向建模在未单选货架时保持可点击并解释前置条件，导出完成后提供页面内成功反馈。
- 场景加载期间选择的工具会在 `InteractionManager` 初始化后重放；组件卸载早于异步加载完成时不再写共享 Store、创建 Stage/InteractionManager 或遗留文档事件。旋转手柄只在恰好单选一个有效货架时出现，多选不会预览或提交部分旋转。
- 新增工具文案使用稳定语义键并提供 `ja`、`zh-CN`、`zh-TW`、`en`、`ko` 本地兜底，后端语言包仍可覆盖。最终 Web 门禁为 179 个文件 / 943 项测试、严格类型检查和生产构建通过；本地 CP6DB 环境的隔离 Chrome 验收确认五工具、撤销/重做、导入取消、导出、反向建模与单选/多选旋转。登录后的编辑器验收动作阶段 `consoleErrors`、`pageErrors`、POST/PUT/PATCH/DELETE 均为 0，且未点击保存。用户已于 2026-08-30 确认验收。
- 本任务 Git diff 只涉及前端、测试及文档，不含 Docker、数据库、API、DTO、迁移或部署文件；执行过程未运行 Docker、数据库管理或迁移命令。反向建模对话框既有的字面量 `{diff}` 未插值问题不属于本次回归，已保留为独立 P1 跟踪项。

## CRM Platform P07 已冻结可消费（2026-08-29 UTC）

- `GTX537/CP6.Platform` 通过实现 PR #15 与发布 PR #16，在 `main@329bf8ee82091de569cb80f1e83fc5d518f74068` 完成 code-owned YARP allowlist、外部身份头清理、按连接来源分区的固定窗口限流、后端独立认证边界及 loopback E2E；实现 main run 33261055327 attempt 2、发布 main run 33262410890 与 exact-main publish run 33262569274 成功。
- publish run 发布五个不可变 `0.7.0-alpha.1` 包和 artifact 9717721544；artifact SHA-256 为 `bf6487e55d8345b1dfbe39cedc2afcb3e365c9bcdd36bbec17841996cd4e88a0`。逐包 SHA-256：Contracts `609c4f562858be6cfe45fc2d51c9eaeceb50a392ce28135a4a9ea644416054d9`、Abstractions `7149940ddb817145fe615a51c8e517fe1915516bf9188b6e2f084e21d7738479`、AspNetCore `85f0742253ebed8adaad2fcf63f2166545e5c4cf35adf739eb87c1eed63fd010`、EntityFramework `a16de9d73df5dda91c4be9e667c4352fad354c5425fb207ae922148d864b7a0e`、Messaging `b385cebe84ebd6dafac723724b273a8660999042bc95f3e6dd88af433cbbd7fd`。
- `GTX537/CP6.CRM` PR #31 固定消费 `0.7.0-alpha.1`，以 11/11 真实 loopback 测试证明未匹配 route 不触达后端、伪造身份头被清除、429 在第二次后端调用前返回，且直连/代理均不能绕过后端认证；PR run 33264347561 与合并后 `main@02f7078de6a67e7f3fded6df6a84b9f6fb712a84` run 33264676796 通过远端包恢复、真实 SQL 与完整门禁。
- CRM PR #32 将 locator 冻结为 `Frozen / Consumable`；PR run 33265394681 与最终 `main@467d95e46625d4db0bb7aa0932aff5464f64a01b` run 33265702772 的 P01–P07 locator、真实 SQL、40/40 M0、39/39 .NET、39/39 Web、production build 与 3/3 Chromium smoke 均通过。
- 该结论只关闭 P07 通用 Gateway 合同与固定版本消费。C01/C02/CRM03、实际 CRM public route、P09 后端网络隔离、运行时 Gateway/auth 注册、Secret、云资源和部署仍未实现或授权。

## CRM Platform P06 已冻结可消费（2026-08-29 UTC）

- `GTX537/CP6.Platform` 通过实现 PR #12 与发布 PR #13，在 `main@3b1669a05f9b265f9b3fb14ade4d656018cbf6b5` 完成 EF Outbox/Inbox、lease、retention、DLQ/replay、真实 SQL Server 与保留的真实 Dapr/Kafka 回归门禁；实现 main run 33241821365、发布 main run 33242125202 和 exact-main publish run 33242264497 成功。
- publish run 发布五个不可变 `0.6.0-alpha.1` 包和 artifact 9711742920；artifact SHA-256 为 `44431d7f359ea524ba9dc438f6f70d24bf34be69411729a2fe1953e3039b3b86`。逐包 SHA-256：Contracts `acb42d617635ed6ba484edf1281a6c3a049c209d0c861015e5a9e269141722a4`、Abstractions `004ff6d528e7d15a2887df51f42035105804988d49e387d87aea6f0555e4b759`、AspNetCore `1104e5319195a2ff8a59a4cf3893766fa959e8733a10486cf260998c61c020fb`、EntityFramework `63491b51b6c0302b0ec662341764181665952c36788f1ef4dcd1424ef75777e7`、Messaging `5bcbb2bec969ac463876b84c87c36d6a93b316642943ab5f3ded2103d8c6c410`。
- `GTX537/CP6.CRM` PR #29 固定消费 `0.6.0-alpha.1`，复用 P04/P05 失败关闭并在 pinned SQL Server 中证明 invalid、duplicate、payload conflict、out-of-order、failed handler 与 poison DLQ 行为；PR run 33243227124 attempt 3 和合并后 `main@910804f5e7fa02569da958ae325997e10c0ffbc0` run 33244344319 通过远端恢复、真实 SQL 和完整门禁。
- CRM PR #30 将 locator 冻结为 `Frozen / Consumable`；PR run 33244749522 与最终 `main@744ca5d9d06db4470d18a4d8ce3ecfbae42f1d2c` run 33245027773 的真实 SQL、40/40 M0、28/28 .NET、39/39 Web、production build 与 3/3 Chromium smoke 均通过。
- 该结论只关闭 P06 通用事务消息 substrate 与固定版本消费。CRM-F3-CONTRACT/C02 业务事件、`CP6.CRM.Worker`、运行时订阅、Secret、云资源、迁移和部署仍未实现或授权。

## CRM Platform P05 已冻结可消费（2026-08-28 UTC）

- `GTX537/CP6.Platform` 已通过实现 PR #9 与发布 PR #10，在 `main@7acb658e001e2bea4e567feeb4e0f7fb1e47eae6` 完成 Dapr service invocation、structured CloudEvent Pub/Sub、Kafka topic/partition-key 约定及真实 Dapr 1.18.2/Kafka 4.3.1 容器门禁；main run 33192565859 与 exact-main publish run 33192773875 成功。
- publish run 发布四个不可变 `0.5.0-alpha.1` 包和 artifact 9694537167；artifact SHA-256 为 `d2135e57ffcd47695165808c5ce841a6aca6afb8641cb49a499f9b1af23d96db`。逐包 SHA-256：Contracts `b574da91d427a2573c7afd55e686bee590ab401550c057a3436db94c79c81aae`、Abstractions `add6d9e136629791a99e93522557466d4328b4ecdee2ae711f4c57dbf20cf5c0`、AspNetCore `142c7a6eabaa888603471550cc5b74b208c365a631a0cd6f6668cd8e59b6fe6e`、Messaging `3a2e934abeb419861ee8958e52a1c878c9ed988c703839c7225039981ec44fc3`。
- `GTX537/CP6.CRM` PR #27 固定消费 `0.5.0-alpha.1`，组合 P04 envelope/region/legacy 与 P05 topic/key 校验并在副作用前失败关闭；PR run 33194075874 与合并后 `main@76cf6e6eef5dd835e5d2005d9d1e22b69654c759` run 33194583713 通过远端包恢复、28/28 .NET、39/39 Web、production build 与 3/3 Chromium smoke。
- CRM PR #28 将 locator 冻结为 `Frozen / Consumable`；最终 `main@75fa59ffd9e31c9bffb3ec4f8dd27b996cb49c0f` run 33195879078 通过。Platform PR #11 同步三仓闭环证据后，最终 `main@b0a5b472cf9554f01969f93d671658ad1aca8752` run 33196852469 的 Windows、Linux 与真实 Dapr/Kafka 三项门禁通过。
- 该结论只关闭 P05 通用传输合同与固定版本消费。CRM Worker、运行时订阅、P06 Outbox/Inbox、C02/CRM-F3-CONTRACT 业务事件、Secret、云资源和部署仍未实现或授权。

## CRM Platform P04 已冻结可消费（2026-08-28 UTC）

- `GTX537/CP6.Platform` 已通过实现 PR #6 与发布 PR #7 合并 `main@2c4c601228d81b300659b7773748da2e995ce433`；Windows/Linux main run 33167738330 与 exact-main publish run 33167927567 成功，发布四个不可变 `0.4.0-alpha.1` 包和 artifact 9684391334。
- 包 SHA-256 已固定：Contracts `b41b6f65507fc1c1db9db7c6213b793787af17e53c6f3d5c8debac7a7606b278`、Abstractions `d867e6ad43355113ab29a775e0801f11643c0f60ae4c65e58fa41d9646423139`、AspNetCore `8749825d6cfa0d899ef2ab21421818bf1bfdc36e0a31b4607b60de71bda7c5c5`、Messaging `50fd23395f49d14ec22619cdddce8006f2b5ec33c465787496f5c5582a74d762`；发布 artifact SHA-256 为 `07eae751d6288cf8f8d81561ae77ac4ab452d62610966a9c69cad995a05bea3e`。
- `GTX537/CP6.CRM` 已通过 PR #25 固定 Abstractions/AspNetCore/Messaging，复制发布包内 bundle，并在副作用前委托 Platform Schema validator 后拒绝旧别名、未知区域与区域不一致；PR run 33169553326 attempt 2 及合并后 main run 33170491020 通过远端包恢复、40/40 M0、23/23 .NET、39/39 Web 与 3/3 Chromium smoke。
- CRM PR #26 已把机器 locator 冻结为 `Frozen / Consumable`；`main@2a728411c6becd437bb0e1f7f4ead680a0947c52` run 33171913476 再次通过完整门禁。Platform PR #8 同步闭环文档后，`main@313f8090a0a806ab961e3590939b07fab878a77b` Windows/Linux run 33172549942 通过。
- 该结论只关闭通用 P04 CloudEvents 1.0 structured JSON、Draft 2020-12 bundle 与固定版本消费。P05 Dapr/Kafka、P06 Outbox/Inbox、C02/CRM-F3-CONTRACT 业务事件和运行时订阅仍未实现；没有创建 Secret、云资源或部署。

## CRM Platform P03 已冻结可消费（2026-08-28 UTC）

- `GTX537/CP6.Platform` 已通过 PR #4 合并 `main@1a39711423dbae7d869031b5d8570e649ccee8f4`；PR run 33154975361 与双平台 main run 33155123120 成功，publish run 33155315173 从该提交发布三个不可变 `0.3.0-alpha.1` 包。
- 包 SHA-256 已固定：Contracts `4eb84481db0fc6e30104295f485d7f59c2c1fbde1a45b15cb9f84c8340efa8ca`、Abstractions `04d40464f7e9cccaf6288ea97ed715677efa8e76426ad5b5356e60104711ea66`、AspNetCore `91e4e24b011dcd2aff4ba985b58e52ceaa6bb12ad3abc495f6bda43c4c2f4202`。
- `GTX537/CP6.CRM` 已通过 PR #23 合并 `main@16e9955b24a5c8c412839301b12d8a9275250746`，固定 Abstractions/AspNetCore 版本，复用 RS256/JWKS validator 与 RFC 9457 writer，并保持 `CRM_* / crm.error.*` 错误命名。
- CRM consumer PR run 33156706503 与 main run 33157212604 均通过远端私有包恢复、15/15 .NET、39/39 Web 契约和 3/3 浏览器冒烟；P03 状态为 `Frozen / Consumable`。
- 该结论只关闭 P03 合同。C01 issuer/Discovery/JWKS、P07 Gateway、C02、CRM03、真实登录和业务切片仍需各自实现与授权；CRM `Program.cs` 未启用认证，没有创建 Secret、云资源或部署。

## Space Studio V1 Core GA 100% 结案（2026-08-28）

- 受控发布演练绑定应用提交 `21a81767a0155a8cc92325acc3e3cdcc076ee930`、冻结 Source Set/Golden Dataset/Worker Environment 及 10 个实现源 Git Blob；WP2/WP4/WP7/WP5 正式基线均重新校验通过。
- SQL Server Express LocalDB `17.0.4025.3` 上实际执行 Publish/WMS/Published 隔离/恢复 8/8、0 failed、0 skipped；产品 `Cp6SpaceWmsAdapter` 使用 `Real` 数据源分类，自动恢复 5 秒，CHECKSUM 备份恢复 3.6480777 秒，`DBCC CHECKDB`、Published Hash、WMS 写入计数和临时资源清理均通过。
- Kestrel loopback + 验签 JWT 1/1 通过；Customer/Supplier/3PL 控制面全部 403，Published Portal 只读、写入拒绝，内部主体可用。没有把 Mock、未签名角色头或合成数据作为正式安全/生产验收。
- WP6、WP8 已 Complete/Accepted，唯一 DeliveryOwner `BUBAO.GAO` 已 Signed；两类外部输入、WP0～WP8 九个 Gate 和签署均无 Pending，Core GA 派生状态为 `GaReady` / 100%。
- 本次仍是本机受控非生产结案：`productionDataClaimed=false`、`productionWmsClaimed=false`、`productionDeploymentPerformed=false`。生产 Release/CD、生产 WMS 窗口、现场 Pilot 和 Backup Provider 是 GA 后独立事项。

## Space WP5 Viewer 正式接受（2026-08-28）

- 生产 Viewer 的 Current Published Design Revision 边界通过聚焦 3 文件 12/12 和浏览器请求级验证；Draft、错误 Authority、内容漂移、运行态混入和 Legacy/可变 Scene 路径继续失败关闭。
- 正式标准仓为 500 货架/10,000 库位，30/30 冷启动、3,000/3,000 拾取命中、0 失败、0控制台错误；硬件 WebGL2/RTX 3060 全程一致，交互/帧/标签/拾取/着色/Draw Call/标签量全部低于冻结门槛。
- 生产 Canvas、工具栏和楼层列表已补齐键盘与可达语义；1440×900、1280×720、键盘、Chromium Accessibility Tree 和关键控件 4.5:1 对比度 4/4 通过。UI fixture 明确为 Simulated，不计生产数据。
- WP5 已 Complete/Accepted。Core GA 保持 72% / `NoGo`：0 个外部输入、WP6/WP8 共 2 个 Gate 和 1 个最终签署 Pending；下一主线是一次受控 SQL Server/WMS/恢复/安全发布演练，未执行生产部署。

## Space WP2 CAD Start 正式接受（2026-08-28）

- 新增独立受控验收运行器，从仓库外复算授权 `L1-C01.dwg` / `L1-C02.dxf`，验证冻结 `cp6-autocad-worker/1.0.0` Release 和 AutoCAD Core `25.0.58.0.0` 后，在 SQL Server Express LocalDB `17.0.4025.3` 调用产品 Preparation/Parse 服务。
- 两格式均完成 Floor/Unit/Transform/Mapping 显式选择、Preview 零 Draft 写入、sealed Preparation、Parse Start 和幂等重放；篡改请求以 `SPACE_CAD_PREPARATION_INVALID` 拒绝且 Job 保持 2→2，Worker 原始 CAD/Attempt 残留为 0。
- 后端聚焦 21/21、Web Wizard/API 14/14、严格类型检查、正式 Manifest、16 个失败模式和总 GA 52/52 均通过；WP2 已 Complete/Accepted。
- Core GA 固定保持 72% / `NoGo`：0 个外部输入、WP5/WP6/WP8 共 3 个 Gate 和 1 个最终签署 Pending。未执行生产部署、生产 WMS 或远程 Worker 部署；下一主线是 WP5 Viewer 结果接受。

## CRM Platform P02 已冻结可消费（2026-08-28 UTC）

- `GTX537/CP6.Platform` 已通过 PR #3 合并 `main@6004decd2a4e41d9d502738dc5d9063bef9b37b7`；双平台 main run 33144068629 与 publish run 33144178346 成功，发布 `CP6.Platform.Contracts`、`CP6.Platform.Abstractions`、`CP6.Platform.AspNetCore` 三个 `0.2.0-alpha.1` 包并记录逐包 SHA-256。
- `GTX537/CP6.CRM` 已通过 PR #21 合并 `main@72c405b4e6ab0ab708cfa1b579b8821a1402dcfe`，固定消费 `CP6.Platform.Abstractions [0.2.0-alpha.1]`，精确映射 TenantId→OrganizationId 且不允许默认租户。
- CRM consumer PR run 33144894103（attempt 2）与 main run 33146816096 均使用短期、只读 `GITHUB_TOKEN` 完成私有包恢复、构建、12/12 .NET、39/39 Web 契约和 3/3 浏览器冒烟；P02 状态为 `Frozen / Consumable`。
- 该结论只关闭 P02；P03 的后续关闭由上方独立记录追踪。P04 CloudEvents、CRM-F3-CONTRACT、登录和业务切片仍需各自依赖与独立授权；未创建云资源、Secret 或部署。

## Space WP1 手工建模正式接受（2026-08-28）

- 正式 Manifest 绑定已合并的 `main@b0164a15cc7d0ad5716973323fcac27325bcfd5e` 和 8 个测试源 Git Blob/SHA-256；由唯一 DeliveryOwner `BUBAO.GAO` 完成可重复自验收，不设置第二人或独立 QA 门禁。
- SQL Server Express LocalDB `17.0.4025.3` 最终运行 20/20、0 failed、0 skipped；覆盖 Blank/显式 Floor、System/Tenant Template、模板应用、完整编码仓库、Preview 零写入、Apply、Lease/Revision/Idempotency 与失败批次原子性。首次缺少环境变量的 17 skipped 运行已作废且未计证据。
- Web 6 个聚焦文件 25/25；新增 WP1 正式协议、12 个独立失败模式和总 GA 组合验证，`manual-modeling-formal-evidence-v1.0.0.json` 已 Pass/Accepted。
- Core GA 仍为 72% / `NoGo`：0 个外部输入、WP2/WP5/WP6/WP8 共 4 个 Gate 和 1 个最终签署 Pending。下一主线是 WP2 CAD 起始向导结果接受。

## Space WP0 基线与治理正式接受（2026-08-28）

- 唯一 DeliveryOwner `BUBAO.GAO`、Kickoff `2026-08-27`、目标 GA `2026-09-27`、两类外部输入 Owner 与 WP0～WP8 Gate Owner 已固定；单人交付不设第二人、独立复核、角色配额或多人签字门槛。
- 正式 Manifest 绑定 `main@162d110829780e0f1a9c16e4d5b576158e03c849`、两类 Complete 输入及已接受的 WP3/WP4/WP7；PR #59 7/7 必需检查、合并后 WP4 11/11 与总 GA 42/42 均通过，工作区干净且未执行生产部署。
- 新增 WP0 独立失败关闭校验器 9/9，并把模板、未见证清单、缺前置、输入/Owner/日期/Commit 漂移纳入总 GA 47/47 组合门禁；WP0 已 Complete/Accepted。
- Core GA 仍为 72% / `NoGo`：0 个外部输入、WP1/WP2/WP5/WP6/WP8 共 5 个 Gate 和 1 个最终签署 Pending。下一主线是 WP1 手工建模结果接受。

## Space WP4 三路径正式接受（2026-08-28）

- 复用 WP7 冻结 Source Set 与精确 AutoCAD Primary，授权真实 `L1-C01.dwg`、`L1-C02.dxf` 的 CAD IR Package 哈希已绑定；Excel–CAD 使用产品自身生成、Open XML 校验并解析的 12,466-byte 标准 XLSX，手工路径使用受控 PDF、PNG 与空白画布。受控资产均明确 `productionDataClaimed=false`。
- 完整 `CP6.Space.IntegrationTests` 在 SQL Server Express LocalDB `17.0.4025.3` 上执行 `465 passed / 0 failed / 0 skipped`；CAD、Excel–CAD、底图/空白画布均证明 Preview 不改 Draft、显式 Apply、Typed Changeset、Lease、Revision 与 Idempotency 语义。
- 新增正式三路径协议、失败关闭校验器、11 个专项正反向测试和总 GA 组合门禁；`three-path-formal-evidence-v1.0.0.json` 通过并由 `BUBAO.GAO` 接受，WP4 已 Complete/Accepted。
- Core GA 仍为 72% / `NoGo`：两类外部输入均 Complete，剩余 WP0～WP2、WP5、WP6、WP8 共 6 个 Gate 和 1 个 DeliveryOwner 签署 Pending；未执行生产部署、生产 WMS 联调或 WP8 发布演练。下一主线是把可独立复核的 WP0～WP2 逐项接受，再收口 WP5/WP6/WP8。

## Space WP7 正式黄金 CAD 接受（2026-08-28）

- 同一冻结 Source Set 的 20 份 `ApprovedOriginalWork` CAD 已完成正式业务评估：Overall 覆盖率 99.0224%、准确率/高置信精确率 98.7008%、Wilson 下界 98.1717%、人工操作下降 96.9043%；Out-of-sample 五项分别为 99.2352%、98.9828%、98.9828%、98.3541%、97.5781%，Holdout 未报告 Blocking 遗漏为 0。
- 精确 50 MiB 的授权原创派生性能包络执行 1 次预热和 20 次稳定观察；可审查提案 P95 0.038715917 分钟、首次 Ready P95 0.032280823 分钟、0 失败。证据明确不把 DXF `999` 注释填充冒充客户复杂几何，也没有伪造确认、Apply 或 Draft 写入。
- `golden-cad-formal-evidence-v1.0.0.json` 通过失败关闭校验，Primary 资格分 86、`releaseEligible=true`；WP7 已 Complete/Accepted。原始 CAD、完整评估请求、性能 CAD 和 Worker 二进制仍在仓库外。
- Core GA 仍为 72% / `NoGo`：两类外部输入均 Complete，剩余 WP0～WP2、WP4～WP6、WP8 共 7 个 Gate 和 1 个 DeliveryOwner 签署 Pending；未执行生产部署。下一主线转为 WP4 三路径的受控真实端到端接受，并复用 WP7 的同一 Primary/Source Set，不再重复黄金集评估。

## CRM Platform P01/P10 签名里程碑对齐（2026-08-28 UTC）

- CRM V1 公开执行规格已消除 P01/P10 的签名冲突：P01 以空模板编译、可重复 pack、runner contract、无跨仓 ProjectReference 和“不发布空包”作为完成证据；正式 NuGet/镜像签名候选仍属于 P10 发布治理。
- 该修正只对齐已批准的里程碑边界，不新增运行时能力。P01 可保持 `Frozen / Producer Ready`，P02–P10 仍须分别以代码、测试、版本化产物和消费证据关闭。

## Space AutoCAD Primary V1 资格接受与 WP3 结案（2026-08-28）

- `BUBAO.GAO` 已确认 V1 本机受控边界：OS Firewall 出站禁网不再是本地阻断门禁；无业务凭据、无网络监听、临时 CAD 强制删除和可审计报告仍为硬边界。该确认不授权生产、公共 SaaS、远程托管或 Autodesk 软件再分发。
- 正式 `1.0.0` Worker、20 份受控 CAD Release 评测和六维评分已绑定；AutoCAD Primary 得分 86/100，`qualify-providers` 返回 `cadGaReady=true`、唯一 Primary、0 Blocking Code，选择 SHA 为 `d7b9645d915f28e165209b71f69386305711301a6a2fecf7422c15cbcc2a0faa`。
- `PRIMARY_PROVIDER_AND_ISOLATED_WORKER` 外部输入已 Complete，WP3 已 Complete/Accepted；Backup 保持可选且不阻断 V1。评分工具未修改 Site 配置，精确 Provider Version 的实际 Site 写入须继续遵守权限、Revision Fence 和发布演练边界。
- WP7 仍诚实保持 Partial/Pending：转换覆盖、确定性、性能和删除已证明，但业务总体准确率、高置信精确率/Wilson 下界、人工操作减少率及受训用户首次 Ready 时间尚未接受。正式 Core GA 仍为 72% / `NoGo`（0 类输入、8 个 Gate、1 个签署 Pending），没有执行生产部署。

## Space AutoCAD Primary 正式 Release 绑定评测（2026-08-27）

- Release 绑定评测器已由 PR #53 在 7/7 required checks 后合并；从精确 `main@d2d0a0d1b0978a4283bd9387f4120eefe10a135d` 构建并封存正式 `1.0.0`，完整 Worker Release SHA 为 `c794e9c0ebbb2c736866827e07e6682347992dd5a672218efddfe6ff5c0f202e`。
- 正式 20 份受控 CAD 双跑 20/20、确定性 20/20；14,699 总实体中 14,659 支持、支持率 99.727873%，缺失 SourceRef/Blocking/残留均为 0，P95 4.281 秒。版本化报告 SHA 为 `97a9ff7f7cbd60f2c2ea34a5b16e0d645823d94980cd43581dca7129e0373350`。
- 正式报告完成 Release 身份、转换质量、确定性、性能和清理证据，但仍诚实记录 `outboundNetworkPolicy=NotVerifiedAtOsBoundary`；没有生产 mTLS/Firewall、Site 认证或生产部署声明。WP3/WP7 继续等待隔离审批、资格评分和完整业务准确率/操作时长证据。
- 正式 Core GA 仍为 72% / `NoGo`（1 类输入、9 个 Gate、1 个签署 Pending），原始 CAD 与 Worker 二进制保持仓库外。

## Space AutoCAD Primary 批准与单 Provider Ready（2026-08-27）

- `BUBAO.GAO` 已批准当前 AutoCAD 2025 Core Console 作为 V1 唯一 Primary；批准范围限于本机受控 CP6 开发、验证和 Release Rehearsal，不代表 Autodesk 软件再分发、公共 SaaS 托管或生产部署授权。
- 当前 Core Console `25.0.58.0.0`、SHA-256 `d1fd7232893094234f31c65445d0ec9259ffc1df17fb15aad99373e31545cefb`、Autodesk 签名有效，Licensing Service 正在运行；真实安装型合同/Worker 测试 2/2，4,424/4,422 实体，残留 CAD/Attempt 为 0。
- 修正 Lean Schema 3 的运行时漂移：一个满足全部硬门禁、资格分至少 80 且唯一最高分的 Primary 即可通过 `qualify-providers`；机器规则显式升级为 `cad-provider-adr-0001-v2`，Site `CadGaReady` 也只由合格 DWG/DXF Primary 派生。Backup 可选且不阻断 Core GA。
- Primary 选择和受控使用范围已批准，正式 SemVer Worker 与同一 20 份 CAD 的 Release 绑定转换评测也已完成；完整外部输入仍 Pending，下一步是补齐隔离/安全依据并用正式报告生成资格评分。当前仍为 72% / `NoGo`，没有生产部署。

## Space Lean Core GA Schema 3（2026-08-27）

- 首版 Core GA 已移除独立 Backup Provider、Greenfield/Retrofit 双仓、各 14 天 Pilot、客户来源 CAD 和额外人员确认等过度流程门禁；Backup 与现场 Pilot 转为 GA 后韧性/推广增强，不再阻断单人开发结案。
- 硬质量门禁不降：20 份冻结 CAD 与 Holdout、防泄漏、单一 Primary 的真实许可和隔离 Worker、资格分/质量/Wilson/人工操作/性能阈值，以及 SQL Server + CP6 WMS + Published-only Viewer + 恢复 + 安全发布演练全部保留。
- GA 索引升级为 Schema 3：外部输入由三类收敛为 `AUTHORIZED_GOLDEN_CAD_CANDIDATES` 与 `PRIMARY_PROVIDER_AND_ISOLATED_WORKER` 两类，WP3 改为单 Primary，WP8 改为一次内容哈希固定的受控发布演练和单一 DeliveryOwner 签署。
- 当前 CAD 输入已 Complete；剩余 1 类外部输入、9 个正式接受 Gate 和 1 个签署 Pending，派生状态保持 72% / `NoGo`。本次没有伪造 Provider 批准、发布演练或生产部署。依据见 `docs/space/reports/2026-08-27-space-lean-core-ga-reset.md`。

## Space 原创黄金 CAD 候选完成（2026-08-27）

- 单人开发场景已正式采用 `ApprovedOriginalWork`，不再要求不存在的客户来源或第二复核人，也不虚构客户、地址或授权关系；`BUBAO.GAO` 是 20 份原创 CAD 的作者、授权人和实名复核人。
- AutoCAD 2025 原生引擎已在仓库外受控证据区生成并冻结正好 20 份唯一 CAD：10 DWG + 10 DXF，Calibration/Validation/Release Holdout 为 10/5/5，L1～L5 各 4，均为 AC1032；合计 14,659 个 Model Space 图元和 2,455 个带 Handle 的逻辑标准答案元素。
- 每份文件均具有源 SHA-256、原创授权、脱敏证明、单位/坐标系/布局、DWG/DXF 版本、标准答案/预期问题、Mapping Profile/规则版本及实名复核。Source Set SHA 为 `7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955`，Golden Dataset SHA 为 `2b9438e09e2953b169770d0ee9292d8f9cc9ed697337111bcb61b913484b1f15`。
- 产品 Converter Contract Runner 对 20/20 文件验证通过。仓库只保存脱敏 Manifest、哈希和受控 URN，原始 DWG/DXF 不入 Git；三类外部输入中的 `AUTHORIZED_GOLDEN_CAD_CANDIDATES` 已为 Complete。
- 这不等于 WP7 接受：按后续 Lean Schema 3，单一 Primary 批准/评分、质量与性能、受控发布演练和最终签署仍未完成。正式 Core GA 保持 72% / `NoGo`，剩余 1 类外部输入、9 个 Gate 和 1 个 DeliveryOwner 签署 Pending。依据见 `docs/space/reports/2026-08-27-space-original-work-golden-cad-candidates.md` 与 Lean 重置报告。

## Space Studio Development V1 100%（2026-08-27）

- `CP6_SPACE_STUDIO_DEVELOPMENT_V1` 已在 RepositoryAndDevelopment 范围达到 `DevelopmentComplete` / 100%：统一 Draft/模板、CAD/Excel/手工三路径、编辑器、2D/3D Viewer、开发 CAD Worker，以及发布/WMS/安全/恢复仓库门禁共六项全部通过；唯一 Owner 为 `BUBAO.GAO`，没有多人门禁。
- 20 份合成 DXF 重新生成与完整性审计通过，L1～L5 各 4，覆盖五个冻结 DXF 版本；53,190,207 bytes / 670,000 实体与 79,517,079 bytes / 1,000,000 实体两档压力资产通过。29 个文件的唯一字节差异是 Windows CRLF 检出，换行归一化后完全一致。
- 开发版验收索引与失败关闭校验器强制 `formalGaEligible=false`、`countsTowardProductionGa=false`，并禁止开发数据/报告进入正式 accepted evidence。正式 E02 Ready 负向审计仍因 0 正式黄金、无 10/5/5、无 DWG/版本矩阵以退出码 3 失败。
- 双轨状态固定为：Development V1 = 100% / `DevelopmentComplete`；正式 Core GA = 72% / `NoGo`。后者的 3 类外部输入、9 个接受 Gate 与 1 个签署没有被开发版替代。依据见 `docs/space/reports/2026-08-27-space-development-v1-acceptance.md`。

## Space GA 单人 Owner 与外部输入盘点（2026-08-27）

- `BUBAO.GAO` 已登记为唯一 DeliveryOwner、三类外部输入和 WP0～WP8 的责任人；Kickoff 为 `2026-08-27`，目标 GA 为 `2026-09-27`。WP0 仓库实现改为 Complete，接受与唯一签署仍 Pending，不存在第二人或多人门禁。
- `D:\CP6` 跟踪 CAD 共 28 份，全部为 DXF、DWG 为 0；其中正好 20 份的 `development-v2.0.0` 明确是 1.6～3.9 KiB 合成 DevelopmentSeed，`countsTowardReleaseGate=false`。其余为旧 seed/测试 fixture，均不能计入授权黄金集；授权/脱敏只找到空模板，没有正式编号、逐文件证明和 10/5/5 Manifest。
- `CP6_SPACE_ODA_LICENSE_PATH` 未配置，常见目录没有 ODA Drawings SDK 包；`tmp` 内只有历史 File Converter 27.1 非商业示例边界，不是 Backup SDK/生产授权。Greenfield、Retrofit 和 WMS 窗口也未提供。
- 三类外部输入、WP0～WP8 接受和 DeliveryOwner 签署均保持 Pending；整体继续 72% / `NoGo`。完整依据见 `docs/space/reports/2026-08-27-space-ga-owner-and-cad-intake.md`。

## Space AutoCAD 候选 Worker 不可变 Release 身份（2026-08-27）

- 可运行 Worker Host 不再以 development Provider 启动；发布后的可执行文件生成 Schema 1 Release Manifest，固定全部 Payload 文件、源提交、Runtime、真实 Core Console 完整哈希/文件版本和 DXF Converter 版本，启动时再由外部 SHA-256 锚定并逐项复核。
- 通过后派生 `cp6-autocad-worker/{semver}+worker.{manifest-prefix}.autocad.{core}.dxf.{parser}`；远程协议升为 Schema 2，部署批准 Manifest 的完整 Worker Release SHA 会进入每次 API 请求、在 Worker 落盘前核对并由响应回显验证，12 位版本前缀不能替代完整哈希。`/health/live` 同时返回完整 SHA、源提交和 Runtime；每次 DWG 转换前还会复核 Core Console 完整哈希。
- 真实本机 `win-x64` 发布演练封存 18 个文件，Schema 机器验证通过，Core Console 为 `25.0.58.0.0`；完整 CAD Experiment 含真实安装门禁 57/57、0 skipped，远程协议专项 6/6，`CP6.Tests` 2,939/19/0，整仓 0 warning / 0 error，残留 CAD/非空 Attempt 为 0。
- PR #46 已在 7/7 required checks 后合并为 `main@4375c7c2fc1e297bf3fe845873b1af5af2cb5d66`；合并后用 `0.0.0-rehearsal.postmerge` 从该精确提交重建 18 文件 Payload，Schema 通过，完整 Worker Release SHA 为 `c51c2ce8925f7bf2bf647dd2d958270d7903e6adc212eee37a668bfe9d82dc84`。这只证明主干可重现密封，不是正式 Release/批准证据；仍须用批准 SemVer 重建、取得许可证/Site 批准、部署生产等价隔离环境、接入独立 Backup 并运行授权黄金集。WP3/整体继续 Partial/Pending、72% / `NoGo`。

## Space 托管 DXF Parser 50 MiB 容量（2026-08-27）

- DXF Parser 从 25 MiB 整文件 byte[]/整文本/Split 数组升级为严格 UTF-8 逐行解析，底层流同步执行原始字节计数与 SHA-256；无语义的 999 注释会完整验证但不驻留，当前失败关闭上限为 64 MiB。
- 精确 50 MiB 合法 DXF 合成容量包络通过并生成 1 个实体；64 MiB+1 seekable 输入在解析前拒绝且没有工件。Converter 升为 `cp6-development-dxf/1.1.0`，AutoCAD 组合 Provider Version 自动变为 `{core}+cp6-dxf-1.1.0`，旧认证不能静默复用。
- 完整 CAD Experiment 在真实 Core Console 环境为 47/47、0 skipped；既有 DWG 指标保持 29/19/4,424/4,422，测试根残留 CAD/Attempt 为 0。
- 50 MiB 文件是有效 DXF + 999 注释的合成容量包络，不是正式黄金集性能结果。按 Lean Schema 3，仍需 Primary 的真实 50 MiB/Ready P95、正式 Release、批准和受控发布演练；WP3/整体继续 Partial/Pending、72% / `NoGo`。

## Space AutoCAD 候选 Worker DWG/DXF 双格式（2026-08-27）

- AutoCAD 隔离 Worker 候选现以组合链 Provider 身份同时接受 DWG/DXF：DWG 经精确 Core Console 后进入托管 DXF Parser，原生 DXF 直接进入同一 Parser且不会启动 AutoCAD；外层和内层 Converter 均经统一合同执行器。
- Provider Key 改为 `cp6-autocad-worker-development`；组合 Version 当前已由上方容量任务升为 `{core}+cp6-dxf-1.1.0`（初始双格式里程碑为 `1.0.0`），任一链版本变化都强制重新评分与 Site 认证。
- 候选聚焦 4/4、真实安装环境完整 CAD Experiment 45/45、0 skipped；Core Console 样例仍为 29 图层/19 块/4,424 实体/4,422 支持实体，原生 DXF 断言 0 次 Exporter 调用，测试根残留 DWG/DXF 与 Attempt 均为 0。
- 这只关闭 Primary 候选的 DXF 仓库路径。原生 DXF 仍是 fixture；Parser 容量和不可变非 development Release 机制虽已由上方任务补齐，但正式合并提交 Release、授权 20 份黄金集/真实 50 MiB、独立 Backup、许可证/Site 批准和生产 mTLS 隔离/Failover 仍缺。WP3 保持 Partial/Pending，整体保持 72% / `NoGo`。

## Space Studio WP3 远程隔离 CAD Worker Provider（2026-08-27）

- 已补齐生产侧远程 CAD Provider 接入：Design API 不加载供应商 SDK 或启动 CAD 进程，只通过 mTLS 向隔离 Worker 发送原始 CAD、源 SHA-256、格式和精确 Provider 身份；Tenant/Site/用户/模型/数据库/Mapping/Object Storage 身份均不跨边界。
- 运行注册默认关闭；只有外部哈希固定且未过期的部署批准 Manifest、精确 Provider/版本/格式/部署与数据边界、有效客户端证书、CA/主机名验证和服务端证书 SHA-256 Pin 全部通过时才启动。Mapping Profile 精确版本、完整 Override Replay、语义、诊断和 PreviewSet 仍在 CP6 内生成，Worker 不能选择 Mapping 或写 Draft。
- 新增可运行的 AutoCAD Core Console DWG 候选 Worker；本机真实调用 `accoreconsole.exe 25.0.58.0.0` 处理 Autodesk 样例，得到 29 图层、19 块、4,424 实体/4,422 支持实体，Provider 身份和 Attempt 原始/派生清理合同通过。该样例不是授权黄金 CAD，只计开发候选证据。
- 远程 Provider、路由、候选 Worker、Space Unit/Integration 和完整 solution 门禁已通过；后续任务又补齐 DWG/DXF 双格式、50 MiB Parser 容量和不可变 Release 身份机制。仓库生产接入层与候选 Host 已完成，但许可证/客户/Site 批准、真实 mTLS 无出口部署、从合并提交生成的正式 Release、独立 Backup、20 份黄金集评分和生产等价 Failover 尚缺；WP3 保持 Partial/Pending，整体保持 72% / `NoGo`。

## Space Studio WP1 仓库实现闭环（2026-08-27）

- WP1 Design V1 手工建模的仓库实现已从 Partial 推进为 Complete：同一向导支持 Blank、Published、System Template 和 Tenant Template，模板模式按密封预览幂等初始化全部楼层，版本持久保存来源、模板版本和内容 SHA-256。
- 当前生产 Draft 可先零写入预览，再通过仓库人员表单创建租户私有不可变整仓模板；空白首层显式要求宽度/深度，未完成模板初始化的重试不会覆盖后来修改过的楼层。
- SQL Server LocalDB 纵切覆盖 2 楼层、500 货架、10,000 库位及 Tenant Scope；Version Clone 全类 17/17、OpenAPI 57/57、Web 聚焦 19/19、完整 solution 0 warning / 0 error，EF/SDK/type-check/production build 门禁通过。
- 这只关闭 WP1 的实现缺口，不构成正式接受：`acceptanceStatus`、3 类外部输入、WP0～WP8 和 DeliveryOwner 签署仍 Pending；整体继续为 72% / `NoGo`，下一工程主线是需要真实授权与环境的 WP3 Provider/隔离 Worker。

## Space Studio 单人交付门禁（2026-08-26）

- 本节记录 Schema 2 当时口径；当前阻断项已由本文顶部 Lean Core GA Schema 3 取代。
- 核心 GA 治理已从多人角色门禁改为单一 `DeliveryOwner`：同一实名开发者可兼任产品、开发、QA、UX、架构、安全和 WMS 联调，自验收并签署；不再要求 2 Backend + 2 Frontend3D + 1 QA、五角色实名签字、不同复核人或第二人审批。
- `ga-evidence-index.json` 与开工/黄金 CAD Manifest 升为 Schema 2；外部输入由 5 类减为 3 类（授权 CAD、Provider/隔离 Worker、双仓/WMS 窗口），签字由 5 个角色减为 1 个 DeliveryOwner，黄金 CAD 每份样本由一个实名 `reviewedBy` 复核。
- Schema 2 当时仍要求主备 Provider 与两仓 14 天 Pilot；Schema 3 已把它们改为单 Primary + 一次受控发布演练，同时保留数据、质量、性能、恢复和安全结果门禁。
- 自动化已证明单一 Owner 可以拥有并接受全部外部输入、复核全部黄金样本并签署 GA；模板、fixture、原始客户 CAD、哈希错误、占位身份和未达技术指标仍会被拒绝。本文下方及历史报告中旧的 2+2+1、五方签字、双标注/独立 QA、独立 UX/安全或第二人审批表述只保留作当时审计，均由本节与 Schema 2 合同取代，不再是当前门禁。

## Release/CD 仓库与平台工程结案（2026-08-26）

- PR #32 已把 Azure Release Shadow S0 合入 `main@9009abe687c693fdcbd650261f39b56cf8ccf8fb`；GitHub required checks、额外 Space/CRM 守卫和本地 1 个有效/10 个失败关闭场景全部通过。
- Azure 已创建 `CP6 Release Shadow` Definition #5，只定向授权现有 GitHub endpoint 与 `Default` queue；Run #145 绑定上述完整 SHA，在无 Variable Group、无 Registry/Environment 权限下输出 `Authority=Shadow`、`Deployable=false` 的辅助 Artifact `cp6-release-shadow-s0-145`，结果为 `Succeeded`。
- Phase 1 PR 归属固定为 GitHub，Azure 保持 `pr: none`；OpenAPI/SDK、Android、WMS SQL、候选 E2E/供应链与 Space 独立门禁的责任矩阵，以及 self-hosted Agent 更新/磁盘/离线/单并发/身份隔离规则均已固化。
- Release/CD 的工程建设状态改为 `Complete`；首个生产发行仍为独立的 `No-Go` 执行门禁。实时审计确认 GitHub 无 R2 Release、受保护版本 Tag、R2 workflow Run、Environment 或仓库 Secret，`v1.0.0` 仍是 Draft 且 20 项输入 Pending，Freeze gate 按预期拒绝。
- Phase 3 S1 和 Phase 4～7 只在真实候选、外部环境、批准人或 Pilot 到位后按事件重开单任务卡；不得以 fixture、空 Environment、Azure DEV 成功或工程结案冒充生产上线。完整依据见 `docs/devops/RELEASE-CD-ENGINEERING-CLOSEOUT.md`。

## CRM V1 PRD 完整脱敏产品基线批准（2026-08-26）

- `docs/crm/CRM-V1-PRD.md` v0.2 已迁移到最新 `main` 基线，并移除商业 cohort 数量/地域/名单、精确推广时间表、私有数值商业 KPI、数值 Pilot UAT 和采用门禁；候选期措辞改为不随状态漂移的批准规则，批准的 payload SHA-256 为 `5e646cc8e394c74c35f9716216be1d12fa5f4f7210e42d8d52ab9b86f4528a3a`。
- 合并前全披露面复核进一步清理 `CRM-M0-READINESS.md`、`CRM-PRODUCT-FRAMEWORK.md` 和 `CRM-V1-EXECUTABLE-SPEC.md` 中遗留的 Pilot 样本量、采用窗口和数值 KPI，只保留公开证据类别、技术 SLO 与产品行为；`docs/crm/**` 的全部现有文件由自动发现清单和规范化 SHA-256 失败关闭，新增未登记 CRM 文档会直接阻断。
- `crm-v1-prd` 门禁区分 PR head 诊断与受保护基线验证：当前引导 PR 的 head check 不冒充独立信任边界；工作流进入 `main` 后，required context 只由 `pull_request_target` 中的受保护 base validator 对精确 PR head 数据产生，并以只读权限运行。
- 三次未合并的预审批尝试因脱敏声明不完整而作废；相关证据仅保留在未合并 PR 审计轨迹中，新的干净分支不继承旧敏感提交祖先，也不把旧对象引用写入公开产品基线。
- 当前状态为 `Approved product requirements baseline`；批准绑定候选 commit `00fa3aea66045cb2b949b691824f0fbb830cc739`、PRD blob `b6f8da119bf700340616e8a2d3cc01ceb0dd38d6`、PR #35 ProgramOwner 评论正文摘要 `4092bc5ec3338be408292c5f240579ed036dcd1033858b4d237dc38d39608de1` 和 append-only 历史摘要 `76b3d5d481ad6c128f70abc7ceb770e430907fed97ca8bdd986873dc492720b3`。Public Contract Sync 为 Complete，M0 仍为 No-Go；没有创建业务代码、云资源、Secret、数据库、迁移、候选制品或部署，也不解锁 CRM01。

最后更新：2026-08-27

## Azure Release Shadow S0 仓库合同完成（2026-08-26）

- 新增独立 `azure-pipelines-release-shadow.yml`，固定手动 `trigger: none` / `pr: none`；只运行仓库内固定 `v1.2.3` fixture 合同并发布辅助 `shadow` Artifact，没有 Variable Group、Service Connection、外部下载或自动触发。
- 新增 `Test-Cp6ReleaseShadowCandidate.ps1`，严格验证 Schema 1 candidate result、Schema 2 release manifest、Schema 1 freeze snapshot、version/Tag/完整 Git SHA、manifest/freeze/spec SHA-256、GHCR repository allowlist/完整 digest、三类原生签名制品、供应链证据和 ForwardOnly db-init 元数据。
- Shadow 报告语义在解析器内固定为 `Authority=Shadow`、`Deployable=false`；候选不能通过输入覆盖。合同包含 1 个有效场景与错误来源、版本、SHA、可变 Tag、manifest/spec hash、repository、digest、freeze 绑定和 Deployable 越权共 10 个失败关闭场景。
- `test-r2-source-gate.ps1` 已纳入 S0 PowerShell 解析与行为合同。静态门禁拒绝 Build/Push/Pull/Tag、ACR、外部 fetch、Service Connection、自动 trigger 和部署命令。当前未访问真实 R2/GHCR、未拉取镜像、未创建 Azure Pipeline/连接、未部署；下一单任务卡是 S1 真实候选只读元数据。

## 发布权威与 Registry 决策完成（2026-08-26）

- `ADR-DEVOPS-001` 已固定当前 CP6 唯一候选 Registry 为 GHCR、唯一候选/部署权威为 GitHub R2；API/Web 只由受保护 `vX.Y.Z` Tag 触发的 R2 candidate 一次构建，Schema 2 `release-manifest.json` 与 `candidate-result.json` 构成唯一候选链。
- Azure 下一阶段改为非权威 Release Shadow：只读验证同一 Tag、完整 Git SHA、manifest/freeze/spec/证据哈希和 GHCR digest，输出固定 `Authority=Shadow`、`Deployable=false`；禁止重新 Build、Push、签名、生成第二份清单或部署。
- R2/Azure 等价矩阵、只读 Service Connection 权限、S0/S1/S2 影子期、30 分钟回退和未来 ACR 硬门禁已记录；当前不创建 ACR、不购买资源、不接触生产 Secret/环境。
- Phase 3 S0 仓库合同已完成；下一张单任务卡是 Shadow S1 真实候选只读元数据，需先审批最小只读身份，仍不得 Build/Push/Tag/Deploy。

## DEV 自动发布稳定性闭环完成（2026-08-26）

- #129 以 31 次 1328～1861 MiB 采样证明 2048 MiB + 3 次连续 SQL readiness 会在备份前失败关闭；依据本机约 8 分 40 秒恢复实测，等待窗口保持安全阈值不变并扩为 600 秒。
- #131 attempt 1 的 61 次采样仅 1254～1756 MiB，SQL/备份前安全失败；同 Run 重试 `DeployDev` 后 readiness、CHECKSUM/VERIFYONLY 备份、迁移、健康和 `main@50a1db6d...` 身份均成功。最终失败仅因 attempt 1/2 复用固定 `cp6-dev-evidence` 名称；PR #30 改为 `cp6-dev-evidence-attempt-$(System.StageAttempt)` 并增加合同回归。
- PR #30 合入 `main@08813896d80cc11d7829194e432ae0fbcfa243f6` 后，GitHub client-contract/SQL 通过；Azure 基础 CI #132 以 `individualCI` 成功桥接同 SHA Artifact，并由 `pipelineTriggerType=PipelineCompletion` 自动创建 DEV #133。
- #133 readiness 三次为 2184/2383/2411 MiB 且 SQL=True；第 7 份备份 `CP6_DEV_20260826_042513_866_887d307c_UTC.bak` 为 2,600,960 bytes，SHA-256 `af4f48fd19daeeb2461411a4210a1cb384c649a4fd01322b82b74555d804c9de`，`BACKUP CHECKSUM` 与 `VERIFYONLY` 均通过。
- #133 API/Web 均为 `0.0.0-dev.08813896d80cc11d7829194e432ae0fbcfa243f6` 且 Healthy，image ID 分别为 `sha256:ccfcb019...633f00` / `sha256:16f44b80...c50149`；`cp6-dev-evidence-attempt-1` 发布成功。根 `cp6-api`/`cp6-db` 的 ID、StartedAt、RestartCount `3/2` 未变；自动开关保持 `true`、公网验证保持 `false`，GitHub R2/GHCR 生产权威未变。

## DEV 自动发布开启与首次验收（2026-08-25）

- 手动 DEV 验收 3/3 后，用户明确接受继续完成自动闭环；Azure `CP6 DEV CD` 的 `CP6_DEV_AUTO_DEPLOY_ENABLED=true`，`CP6_DEV_PUBLIC_VERIFICATION_ENABLED=false`，因此自动模式已开启但本次没有切换 `cp6.uk`。
- 基础 CI #124 成功后约 6 秒自动排队 DEV #125 / `dev-20260826.1`。Azure REST 确认 `reason=resourceTrigger`、`pipelineTriggerType=PipelineCompletion`、来源 `GTX537.CP6` #124 / `main@ecbad9e1...`；Classify、587 文件 Artifact 校验、runtime-only Package、锁内 freshness、Backup、Deploy 和证据发布全部 Succeeded。
- #125 生成第 5 份备份 `CP6_DEV_20260826_012133_290_2a3d7daf_UTC.bak`，2,572,288 bytes，SHA-256 `bcd9f2282bd747b61d292852570fdc8df3e7329e012473de6a1ad6171ba3a574`；Pipeline 日志记录 `BACKUP CHECKSUM` / `verifyOnly=passed`，本机重新计算哈希一致，`cp6-dev-evidence` 成功归档 2 个文件。
- 当时 DEV API/Web 均为 `0.0.0-dev.ecbad9e1...` 且 Healthy，运行 image ID 分别为 `sha256:c8d2a559...145d2` / `sha256:d9d03015...24b4c`，最新迁移为 `20260811030108_CrmFoundation`。根 `cp6-api`/`cp6-db` 的容器 ID、StartedAt、RestartCount `3/2` 完全不变，旧 `cp6-cloudflared` 继续运行。任何旧版本手动回退前仍必须先关闭自动。

## GitHub 远程构建与 Azure 轻量 Artifact 桥（2026-08-25）

- 本机完整编译在 #113/#115 仍触发宿主内存或 SQL 门禁；Azure 组织 #110 又无 hosted parallelism，因此基础编译迁至 GitHub `client-contract.yml`。它在 GitHub-hosted Runner 完成 .NET、客户端、OpenAPI、Web、Android 与 R2 source 门禁，生成名称含完整 SHA、内部含逐文件 SHA-256 的 3 天 Runtime Artifact。
- Azure `azure-pipelines.yml` 现只执行合同、使用授权 Checkout 凭证下载、核对工作流来源/事件/结论/完整 SHA/归档 SHA-256、验证 ZIP 路径与内部 manifest，再发布 Azure `cp6-dev-runtime`；不在本机运行 .NET/Node，也不部署。
- GitHub Run 32879704210 在 `f18e4610...` 首次完整成功并生成约 49.8 MiB 产物。Azure #116 因错误查询非仓库专属 extraheader 在下载前失败、Publish skipped；修复为仓库专属键后，GitHub Run 32881647447 在 `489c99be...` 完整成功，Azure #117 随后成功完成下载、双层验证与发布。SQL 与公网七容器 ID/StartedAt/RestartCount 不变。
- PR #24 已合入 `main@a5c6b5fa...`；GitHub client-contract 与 Azure #118 成功产出/桥接同 SHA Runtime Artifact，自动 completion #119 在开关关闭时安全跳过。
- Manual #120/#121 各自完成独立分类、Artifact 验证/封装、CHECKSUM/VERIFYONLY 备份、迁移、健康/身份验证和 `cp6-dev-evidence` 发布；备份由 2→4。#120 备份 SHA-256 `c90a3db2...19a3a`，#121 为 `9fc35ca1...414fb`。最终 API/Web 版本 `0.0.0-dev.a5c6b5fa...59e6`、live/ready Healthy、最新迁移 `20260811030108_CrmFoundation`。
- 手动验收现为 **3/3**。最终 8/8 SQL 查询成功、近 45 分钟无 701/17300，公网七容器 ID/StartedAt/RestartCount 全部不变；只有旧 `cp6-cloudflared` 运行。自动/公网开关继续为 `false`，GitHub R2/GHCR 生产权威未变。

## DEV Manual Run #98/#101/#107 内存失败与 CI Artifact 隔离（2026-08-25）

- Artifact 主线 CI #109/#111 均证明 `Default` Pool 的默认 MSBuild 并行度会在本机 SQL/Docker 共存时形成不可接受的内存竞争：#109 在 API build 工作集约 2.97 GiB、宿主可用约 0.96 GiB时取消；用户释放内存后重跑的 #111 仍达到约 4.63 GiB、可用 1.24 GiB，按门禁取消。两次都没有 Artifact 或 DEV completion/deploy，备份仍为两份，根 API/DB 与旧 DEV API 的 ID、StartedAt、RestartCount 不变，SQL 随取消立即恢复。
- Microsoft-hosted 探测 Run #110 正确选到 `Azure Pipelines` hosted Pool，但组织未获赠或购买 hosted parallelism，Job 在 Checkout 前以 `No hosted parallelism has been purchased or granted` 失败；没有本机负载或环境副作用，也未擅自启用计费。低内存分支 Run #112 随后把 restore 固定为非并行、build/test 固定为单 MSBuild 节点并关闭持久/共享编译服务器、Vue 单测限制两个 worker；完整 CI 于约 9 分钟内成功并发布 `cp6-dev-runtime`，最低观测宿主可用内存约 2.22 GiB，SQL 全程可查询且三项受保护容器重启数不变。分支 Artifact 不可部署，仍须合入后取得成功的 `main` CI Artifact。
- Artifact 方案首次主线 CI #108 在真正 restore/build 前失败：`test-azure-ci-runtime-artifact-contract.ps1` 已打印 passed，但 Azure Windows PowerShell 5.1 继承了进入 Step 前的非零 `$LASTEXITCODE`，随后 YAML 把这一陈旧值误判为脚本失败。根因不是合同断言、YAML 或产物逻辑；Run 无编译、Artifact、completion DEV 或部署副作用，根 API/DB 与旧 DEV API 三项元数据均不变，`CP6_DEV` 仍 ONLINE。修复为 PowerShell 脚本依靠 terminating error/`ErrorActionPreference=Stop`，禁止用继承的外部进程退出码判断 `.ps1` 成败，并对基础 CI/DEV 两个合同 Step 增加静态回归。
- 继续 2/3、3/3 验收前，宿主机仅余 1.09 GiB / 6.9% 内存，预检按门禁拒绝排队；关闭 Chrome 后，12/12 次 `CP6_DEV` 独立 SQL 连接/查询通过，最低可用内存 3.57 GiB / 22.6%，无新增宿主 SQL 701/17300。
- Manual Run #98 正确选择成功的 CI #96 / `main@c9b02c82`，但 API Docker `dotnet publish` 期间 Agent 报宿主内存已使用 96.03%。Run 在 Build 阶段人工取消，Deploy 为 Skipped，没有新备份、迁移或 DEV 候选切换，不计手动验收。
- Docker 事件证明根 `cp6-db` 被 OOM kill 后自动重启一次，`cp6-api` 因数据库恢复累计重启两次；两者与其余根容器当前均恢复 Healthy，但“根环境未受影响”门禁已失败，不能只凭容器 ID 未变化放行。
- 低并发修复合入 `main@76d0832e` 后，自动 CI Run #99 与关闭状态的 completion Run #100 均成功；Manual Run #101 / `dev-20260825.7` 正确选择该提交，但 Docker VM 在串行 publish 时仍达到 95.83%。任务在 Build 阶段安全取消，Deploy Skipped，无新备份、迁移或 DEV 镜像切换；根 `cp6-db` RestartCount 由 1→2、`cp6-api` 由 2→3，因此仍不计验收。当前根基线分别为 StartedAt `15:06:55Z` / `15:07:03Z`。
- DEV 候选构建已改为部署 Agent 在宿主机使用 .NET 8/Node 22 串行生成 API publish/Web dist，再由新增 runtime-only Dockerfile 封装；Web Node 堆限制 768 MiB，两个镜像仍各只构建一次并以 `--iidfile` 固定身份。提交 `72ec0e70` 的完整本机验证生成 API `sha256:bc681051...293d92` 与 Web `sha256:c81ae3ce...77e05`，专属临时目录清零，Docker VM 采样保持约 1.9 GiB 以上可用，根 API/DB 三项元数据未变且宿主 SQL 无新增 701/17300。六组契约、PowerShell 解析、差异与凭据扫描通过；GitHub R2/生产 Dockerfile 未改。自动与公网开关仍为 `false`，手动成功计数保持 1/3。
- 合入后 CI #102、completion #104 和 Readiness #105 成功；错误资源版本的 Manual #106 在 YAML 解析前失败，无 Job/副作用。Manual #107 正确绑定 CI #102，但宿主 `dotnet publish` 工作集约 4.18 GiB、可用内存降至约 0.62 GiB并导致 `CP6_DEV` 新连接超时，按门禁取消；Deploy Skipped、备份仍两份、根 API/DB 基线不变，但旧 DEV API RestartCount 16→17，因此仍不计验收。
- DEV 候选改为下载所选成功 CI 的 `cp6-dev-runtime`：基础 CI 对已通过测试的 API/Web 输出写入版本、完整 SHA、逐文件长度/SHA-256，DEV 重新验证完整集合后只做 runtime-only Docker 封装，不再运行 .NET/Node 编译。真实 145,966,387 bytes API + 7,473,275 bytes Web 本机实测共 587 个哈希文件，约 17 秒生成 API `sha256:10a68d65...9573c6` 与 Web `sha256:23d01b7b...94b9ff`；根 API/DB 与旧 DEV API 三项元数据不变，`CP6_DEV` ONLINE/最新迁移不变。生产 R2/GHCR 未改，自动/公网仍为 `false`，成功计数保持 1/3。

## Azure CI 与首次本机 DEV 发布外部闭环（2026-08-25）

- Azure 基础 CI `GTX537.CP6` Run #92 在 `main@47ca8441898af69d1e66bc1acb6c51129dbe9c18` 完整成功。此前固定失败的 `.NET Restore` 根因是通用 `CP6-Windows` Agent 从 PowerShell 7 启动后继承 `PSModulePath`，使 Windows PowerShell 5.1 重复加载类型数据；清空父进程该变量后同一提交通过。新增 `Start-Cp6CiAgent.ps1` 固化 `CP6-Windows` / `Default` 校验和前台隔离启动，不注册服务、不改电源设置。
- `CP6 DEV CD` Definition ID `4`、`cp6-dev-secrets`、`CP6-Deploy` 与 `cp6-dev` 已按 Pipeline 定向授权；Environment 配置 Exclusive lock，两项非 Secret 开关均为 `false`。Readiness Run #89 由 `cp6_deploy_agent` 服务身份成功验证 Docker、SQL TCP、`sqlcmd` 和备份目录；`cp6_dev_backup` 及锁定 Secret 已完成最小权限验收。
- completion-trigger Run #93 成功完成分类并明确跳过 Build/Deploy，证明自动关闭门有效。首次 Manual Run #94 在生成并 VERIFYONLY 备份后因宿主 `KOUSQLSERVER` 已发生 701/17300 内存耗尽、实例退化而在 db-init 元数据查询超时；API/Web 未启动，失败未冒充成功。管理员重启 SQL 数据引擎并停止当前未使用的 PolyBase/Launchpad 依赖后，`CP6_DEV` ONLINE 且 128 条迁移历史可立即读取。
- Manual Run #95 成功发布 `0.0.0-dev.92`：API/Web 均对应完整 SHA `47ca8441898af69d1e66bc1acb6c51129dbe9c18`，`19991` live/ready 与 `18080` release identity 一致，最新迁移为 `20260811030108_CrmFoundation`。新备份 `CP6_DEV_20260825_123030_332_9b7cd05d_UTC.bak` 为 2,453,504 bytes，SHA-256 `58c6ff73...5079c23`，CHECKSUM/VERIFYONLY 通过；`cp6-dev-evidence` 已发布。
- 根 `cp6` 七个容器 ID、Docker `CP6DB` 与既有 Cloudflare connector 全程未变。当前口径是手动 DEV 验收 **1/3**；自动与公网验证仍关闭，未切换 Tunnel，另两次成功 Run、宿主机内存治理和公网身份验收仍是后续门禁。

## CRM 公开产品对比与 PRD v0.2（2026-08-25）

- `docs/crm/CRM-COMPETITIVE-ANALYSIS.md` 已对比 Salesforce、HubSpot、Dynamics 365、Pipedrive、Zoho、Odoo、SAP Sales Cloud、纷享销客和销售易，按获客、Lead 执行、客户/商机、报价成交、ERP 权威、治理扩展及商业模式建立同口径分析。
- 竞品研究没有扩大 V1：`CRM-COMP-001`～`007` 固定 Lead Pilot 行动优先、稳定对象/状态、来源与 SLA、CP6 ERP/ExternalEvidence 成交权威，并把公海、企微/钉钉、邮件/日历、移动拜访、评分和 AI 保留为后续受控扩展。
- `CRM-V1-PRD.md` 已升为 v0.2，并把竞品结论映射到目标、旅程、前端、后端、升级和验收合同。该 2026-08-25 里程碑当时仍是产品评审草案；当前批准状态与 Public Contract 状态以上方 2026-08-26 记录为准，M0 始终保持 `No-Go`。

## 本机 DEV 外部首次运行就绪审计（2026-08-25）

- 只读审计确认 Docker Desktop 29.3.1、Compose 5.1.1、`MSSQL$KOUSQLSERVER` 和专用 Azure Agent `vstsagent.gaobubao.CP6-Deploy.LAPTOP-3QQ44FJS` 正常运行；`CP6_DEV` 已存在，根 `cp6` 七个容器、`CP6DB` 与 `cp6_cp6-db-data` 保持运行且未被修改。
- `sqlcmd.exe` 已安装于 ODBC 17 标准目录，但该目录不在机器级 `PATH`；交互用户能发现它不代表 `cp6_deploy_agent` 服务身份也能发现。备份脚本和 Readiness YAML 现同时探测 PATH、Go sqlcmd、ODBC 18 与 ODBC 17 标准目录；新增 7 场景行为回归覆盖 PATH、显式路径、全缺失、标准目录回退、Secret 前置门与失败后 `SQLCMDPASSWORD` 恢复，相关数据安全/Readiness/DEV CD 合同测试通过。
- 已创建 `C:\CP6Backups\CP6_DEV` 并关闭继承的宽泛修改权限；SQL Server 服务身份具有 Modify，部署 Agent 只有 Read/Execute，当前管理员维护身份、SYSTEM 和 Administrators 保留 Full Control。独立 SQL 登录 `cp6_dev_backup`、Azure 锁定 Secret 和最小权限已在同日后续外部闭环完成；Run #94/#95 已生成并验证真实备份。
- 微软 Azure CLI 2.89.1 与 Azure DevOps 扩展 1.0.6 已安装并完成设备登录。Pipeline/Variable Group/Environment 定向授权、Exclusive lock 和两项 `false` 开关已完成；手动 Run 当前完成 1/3，Tunnel 切换仍待验收。

## 本机 DEV 双模式发布闭环（2026-08-25）

- `azure-pipelines-dev.yml` 统一支持手动和 completion-trigger 自动模式；`CP6_DEV_AUTO_DEPLOY_ENABLED` 初始为 `false`。两种模式都只接受 Azure 回读为 `completed/succeeded` 的 `GTX537.CP6/main` Run；过期自动 Run 安全跳过，自动开启时禁止选择旧 Run 手动回退。
- 候选使用 `0.0.0-dev.<CI Run ID>` 与完整 SHA 镜像 Tag，从所选提交的隔离 worktree 构建；编排逻辑始终来自当前 `main`。构建同步捕获 Docker 不可变 image ID，部署不依赖可能被并发重写的本机 Tag，并逐容器核对实际运行 `.Image`。deployment job 进入 `cp6-dev` 顺序锁后会二次淘汰过期自动 Run，并受 Windows 全局互斥锁兜底；之后先对 `CP6_DEV` 做 COPY_ONLY/COMPRESSION/CHECKSUM 备份及 RESTORE VERIFYONLY，再停止旧 Web/API、执行一次性前向迁移、依次验证 API 与 Web，并归档触发/备份/镜像/健康证据。
- 根 Compose `cp6`、Docker 数据库 `CP6DB` 与命名卷 `cp6_cp6-db-data` 明确排除在 DEV CD 之外。新增手工导出与旁路导入工具；导入只允许新的 `CP6DEV_IMPORT_yyyyMMdd_HHmmss`，拒绝目标已存在且不含 `WITH REPLACE`，不会自动合并到 `CP6DB`。
- 新增独立 `cp6-public-tunnel` Compose，只加入 `cp6-dev_default`。控制器要求旧 `cp6-cloudflared` 已被用户显式停止，避免同一 Tunnel 双 connector 分流；DEV CD 不执行一次性公网切换。Azure Secret/Exclusive lock 已配置且首次手动 Run 成功；另两次手动 Run、Tunnel 切换和公网身份验收尚未执行。

## 登录体验恢复与可访问性闭环（2026-08-24）

- 登录页已恢复为包装制造运营入口，保留原有账号密码、租户自动识别/显式选择、SSO、2FA 跳转、动态菜单和登录后路由合同，并提供 zh-CN、zh-TW、en、ja、ko 五语言产品说明及桌面/移动响应式布局。
- 浏览器根因复核发现，单靠 CSS 折叠的 Tenant 输入仍会进入键盘 Tab 顺序；折叠区现同时使用 `inert` 与 `aria-hidden`，展开或后端返回 `needTenant` 时会把焦点移到组织输入。语言选择器改为普通按钮组语义，密码登录与 SSO 共享互斥忙碌状态，避免并发发起两条认证流程。
- 顶栏不再在没有健康检查请求时显示“系统服务正常”或使用 `role=status`；改为中性的安全访问入口，不伪造实时运行状态。
- 验证为组件聚焦 10/10、Web 全量 176 文件/902 测试、Vue 类型检查、production build，以及 Chromium 桌面 1440×900/移动 390×844 的无横向溢出、折叠 Tenant Tab 跳过和展开聚焦验收。浏览器使用无后端的本地 Vite 时语言 API 返回 502 属测试环境缺少 API，不是本组件运行异常。

## Kafka 生产者安全退出修复（2026-08-24）

- `KafkaProducerService` 作为 DI Singleton 由 Host 在进程退出时释放。关闭流程现在只执行一次：先最多等待 5 秒刷新在途消息，再无条件尝试释放 producer handle；`Flush` 抛错不再跳过真正的 `Dispose`。
- 刷新异常、释放异常和超时后仍在队列中的消息数都会记录 Warning，但旁路操作日志通道的关闭异常不会反向阻断 WebApi Host 退出。正常 Publish、Kafka 配置和运行期降级语义不变。
- 新增 4 个行为回归，覆盖刷新异常仍释放、剩余消息告警、释放异常不外抛和重复调用幂等；`CP6.Tests` 全量 2,938 passed、19 项既有 SQL 环境门禁 skipped、0 failed。

## 日期时间规范化恢复与 P4/P5 决策（2026-08-24）

- 旧 WIP 的 P4 `env.d.ts` 通配 `declare module '*.vue'` 不恢复：最新 `main` 使用 Vue 3.5、TypeScript 6 和 `vue-tsc` 3.2，在没有该声明时完成干净 `vue-tsc --build`。旧声明中的 `DefineComponent<{}, {}, any>` 会弱化 SFC 类型检查，只保留归档证据，不进入主线。
- 日期时间恢复分支把 OA/PMS/WMS/Space 与通用列表中散落的原始 ISO、字符串截断和浏览器直出统一到共享 `formatDateTime`/`formatDateTimeCell`；`VolTable` 与 `CpListPage` 增加显式 datetime 列合同，高精度 .NET 输入不再直接暴露。
- P5 精度合同固定为普通业务 UI 显示本地日期及“时:分”。没有产品需求时不得在全局 `long` 格式加入秒或小数秒；将来若审计/追踪确需更高精度，必须使用独立命名格式和独立验收，不能扩大所有调用方。
- 回归覆盖 zh-CN、zh-TW、en、ja、ko 五种语言及高精度/空值/非法值；Web 全量 175 文件、892 测试通过，Vue 类型检查和 production build 通过。

## 白天临时家庭测试服务器（2026-08-24）

- 当前阶段选择由本机 Docker Desktop 承载 CP6，Cloudflare Tunnel 仅把容器内的 Web/API 转发到 `cp6.uk` 与 `api.cp6.uk`；它是供同事白天临时测试的开发环境，不是云主机或正式生产部署。
- 新增根目录 `cp6-daytime-server.bat` 和 `scripts/Invoke-Cp6DaytimeServer.ps1`：支持复用现有镜像启动、显式重建后启动、状态检查、仅关闭公网 Tunnel，以及安全停止全部 Compose 服务。全部停止使用 `docker compose stop`，保留容器和 SQL Server/Redis/RabbitMQ/Kafka/i18n 命名卷。
- 流程不会修改 Windows 睡眠、电源计划或计划任务。电脑睡眠、关机、Docker Desktop 停止或网络中断时，`cp6.uk` 暂时不可访问是预期行为；恢复后由用户手动启动/检查。
- 2026-08-24 只读实机验收确认 7 个服务运行，DB/Redis/MQ/Kafka 健康；本机 Web/API 与公网 Web/API 四个地址均为 HTTP 200。为保护正在使用的环境，本任务没有执行重启、重建或停止。
- Cloudflare Workers 中名为 `estimate` 的 Git 集成仍是独立外部清理项，不参与本机 Docker + Tunnel 运行链，也不因本流程交付而视为已修复。

## CRM V1 产品需求草案（2026-08-24）

- 新增 `docs/crm/CRM-V1-PRD.md` v0.1，状态为 `Draft for Product Review`。文档以 2026-08-14 私有 `GTX537/CP6.CRM` Frozen SaaS V1 为长期产品目标，并把已批准的 Lead Pilot C 分栏工作台定义为第一可见交付。
- PRD 已统一 Website/Manual/Import Intake、Lead/Opportunity/PublicSubmission/IntegrationProcess 状态、22 个权限动作、DataScope/PII/Entitlement、幂等/ETag/412、CP6 ERP 与 ExternalEvidence 两种成交权威、Site/CMS、Portal、移动端、KPI、采用门禁和未来升级接口。
- 当前代码事实不变：`main` 只有 20 表 Foundation、状态机、迁移、6 个禁用菜单和 22 个动作；没有 CRM Controller、独立 API、Next.js/React Native 客户端或可用 CRM 页面。私有 CRM 仓当前为 docs-only。
- 本条记录 2026-08-24 的草案事实；Public Contract Sync 后续已由 PR #8 完成。PRD 产品批准仍不把 M0 从 No-Go 改为 Go，只有 M0 Go 后才可按 Lead Pilot 切片拆实施票。

## Space GA 退出码假红修复（2026-08-24）

- Attestation、Pilot、Golden CAD、Kickoff 和 Development Personnel Seed 五个负向测试套件都会在末个预期失败的 validator 子进程后把 `$LASTEXITCODE=1` 留在调用方全局作用域；GitHub Actions 的 `pwsh` 包装器因此把断言全绿的测试误判为失败。
- 五个测试辅助函数现在都只在完成正/负向退出码与错误码断言后清除已消费的子进程状态，并在各自套件汇总前断言全局退出码必须为 `0`。没有放宽任何 GA 证据规则、错误码或 `NoGo` 条件。
- 本地按 Actions 顺序运行全部 Space GA 门禁：当前状态校验通过，Attestation 36/36、Pilot 21/21、Golden CAD 31/31、Kickoff 28/28、Development Personnel Seed 8/8；全部独立 `pwsh` 进程退出码为 `0`。

## CP6 SaaS V1 公开工程契约同步完成（2026-08-26）

- 私有 `GTX537/CP6.CRM` 的 Frozen 产品 merge commit 为 `07a7bb0b50f33b0cb70c18c14f83be77c725626d`，产品摘要为 `e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b`；R00 `CP6-SAAS-R00` 摘要为 `64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03`。
- 公开 `CP6-SAAS-V1-PUBLIC-CONTRACT` 的规范化摘要固定为 `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`；ProgramOwner 已在 PR #8 对该精确摘要批准，append-only 记录绑定评论、证据 commit/blob 和私有源摘要，公开合同与 R00 镜像同步为 Complete。
- 公开文件只同步四仓职责、领域/API/事件、安全、发布与 M0 开工规则；价格、支付供应商、商业 cohort、私有个人审批身份和内部风险记录不公开。M0 保持 No-Go；DEC-001、DEC-003 至 DEC-009、专业证据、Critical/High、私有仓库分支保护和必需检查仍不可豁免。

## 仓库分支整顿与当前开发基线（2026-08-24）

- 分支整顿当时的集成基线为 `main@0a14581f87ac1955678bdb664911183fc5a2a2a1`；根工作区已从落后 172 个提交且含 43 个 tracked 修改/4 个 untracked 文件的旧 WIP 分支切回干净主线。整顿前完整引用、脏 worktree patch、原始未跟踪文件与校验清单保存在本机 `D:\CP6-archives\2026-08-24-branch-consolidation`，不得在完成独立备份前删除。
- 远端 72 个旧非 `main` 分支中，61 个已被 `main` 包含、9 个经内容审计后只需归档，均已删除远端引用；陈旧 PR #3 已关闭。两个 CRM 草稿分支已合并当前 `main` 后继续保留为 Draft PR #7/#8，不视为已获产品批准。
- 根目录混合 WIP 当时按职责拆成登录体验、日期时间规范化和 Kafka Dispose 三条续开发分支；三项现均已完成独立评审、自动化和合并流程，旧混合根分支只保留在整顿归档中，不再整体回灌主线。
- 仓库保持 GitHub Public 是本阶段的明确决定；未执行生产部署。`main` 已启用严格分支保护：管理员同样必须经 PR，分支必须基于最新主线并通过 `windows-and-web`、`android`、`sql-integration`，禁止 force-push/删除且要求解决会话。下一治理项是单独修复 Space GA 证据测试脚本“36/36 通过但进程退出 1”的 CI 假红。
- 完整分支处置、恢复入口、验证证据和续开发顺序见 `docs/project-memory/11-Branch-Consolidation-20260824.md`。

## Space Studio v1.3 核心实现（2026-08-12）

- Tenant 私有整仓模板已形成 Design V1 完整纵切：内部编辑者通过带 `Idempotency-Key` 的唯一创建接口提交类型化 Floor/Zone/Aisle/Rack 计划，服务端规范化父链、编码、尺寸、逐层规格、命令数与 100,000 库位上限，计算内容 SHA-256 并保存当前租户不可变 v1；System 模板仍为代码内置只读。`Space_WarehouseTemplate`/`Version` 使用复合租户外键、租户内编码唯一索引、查询过滤和同步/异步不可变护栏；目录、密封 Preview 和既有 Lease/Floor/Content Revision Apply 同时支持 System/Tenant，工作台标识作用域并拒绝把旧模板 Preview 用于新选择。全量 Space Integration 456/456（0 skipped）、Space Unit 549/549、CP6.Tests 2,934 passed / 19 项既有环境门禁 skipped、Web 884/884、Space Studio Playwright 26/26；OpenAPI/权限聚焦 96/96、EF/双 SDK drift、Vue TypeScript、生产构建和完整 solution Release 均通过。该纵切关闭 Tenant 模板持久化、版本不可变、目录、跨租户隔离和按层 Apply；面向仓库人员的模板制作表单、Blank/Published/System/Tenant 四模式统一创建向导及 Template 创建来源持久化仍待后续任务，LM-FR-001/WP1 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 历史 CAD 审核结果目录已接入 Design V1 与 Space Studio：楼层级只读接口只枚举同 Version/Floor 的成功 DWG/DXF Parse Job，并把启动时冻结的 Base Content Revision/Hash 与当前 Draft 比较；只有当前 Revision、来源仍为 PreviewReady 且 PreviewSet Artifact 存在的候选可直接加载。旧 Revision 结果继续可见，但工作台只允许带原 Source 进入 CAD 起始向导重新解析，不能直接 Review/Apply；选择候选会先清理旧 CAD/Excel/Preflight/Match 路由与本地状态。只读用户可以查看目录，编辑用户才能触发重新解析，用户全程无需填写 SourceId/JobId 等内部标识。Space Integration 15/15、OpenAPI/权限 95/95、Web 882/882、Space Studio Playwright 26/26、Vue TypeScript、生产构建与双 SDK drift 通过。该纵切关闭“历史 CAD Source/Parse Job 可发现与显式重新关联”的仓库 UI 边界，不替代真实 Provider/文件/WMS/黄金集/Pilot 接受，WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 当前 CAD + Excel 工作会话已形成统一前端闭环：只有与当前 Floor/Draft Revision 一致的 CAD Review Workspace 才启用入口；用户上传 `.xlsx` 后，工作台等待受控来源扫描、选择服务器 Mapping Profile、自动轮询并展示行/列级预检，Blocking 失败关闭，显式确认后绑定 CAD Source/Parse Job、Floor 和 Content Revision 启动权威匹配，再进入既有 Lease/Revision/Artifact 保护的 Apply 与统一撤销/重做链。Excel Source/Preflight Job 写入 URL 可刷新恢复，匹配 Job 自动轮询；删除依赖来源会清理失效路由链。Web 878/878、Space Studio Playwright 25/25、Vue TypeScript 和生产构建通过。该纵切关闭当前工作会话的统一 Excel 上传 UI；历史 CAD 候选目录已由上一条后续纵切闭环。它们都不替代真实 DWG/DXF+Excel、Provider、WMS、黄金集和 Pilot 接受，WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- LM-FR-019/019A 的仓库主链已完成深审和加固：CAD Job 成功后仍自动从受控 Artifact 加载绑定 Base Content Revision/Hash 的只读 Workspace，前端现在对新增、修改、删除、冲突、低置信度、未识别六类变更显示独立计数和筛选，并拒绝汇总、选择状态或可 Apply 类型不自洽的响应；替换 Workspace 会清除旧选择。静态设计元素使用专用内部 CAD 命令入口，单次可原子确认最多 10,000 项，公开手工命令仍保持 100 项上限；101 项服务纵切证明只推进一次 Floor/Content Revision。Zone/Aisle/Rack 不伪装成通用元素，审核面板会把 `SPACE_CAD_REQUIRES_RULE_ONLY_REVIEW` 显式交接到既有 RuleOnly → Proposal Review → Atomic Apply 主链并预选当前 CAD 来源。Space Integration 15/15、Space Unit 546/546、CP6.Tests 2,933 passed / 19 environment-skipped、Web 873/873、OpenAPI 55/55、Space Studio Playwright 24/24、生产 Web 构建、完整 solution Release 0 warning / 0 error 和 SDK drift 均通过。LM-FR-019/019A 仓库实现闭环，但真实 DWG/DXF 三路径、Provider、黄金集与 Pilot 仍未接受，核心 GA 保持 72% / `NoGo`。
- LM-FR-010～011 的仓库主链已闭环：浏览器与服务端都显式接受 `.dwg`/`.dxf`，服务端以扩展名、声明 MIME 和真实文件签名三重校验后才进入隔离扫描；两种格式继续归一到同一 CAD IR/Preparation/Parse 合同。解析前，工作台现在直接展示自动建议单位、每来源单位对应毫米、原始 X/Y/宽高、自动换算毫米范围、比例/范围是否异常及稳定问题原因，并继续要求用户分别确认单位/坐标转换和映射语义；任一输入变化都会使确认失效。Space Unit 546/546、Web 869/869、Vue TypeScript、生产构建和完整 solution Release 0 warning / 0 error 通过；安装型 AutoCAD 2025 Core Console 真实 DWG 合同用例 1/1 通过，只计开发转换链。生产主备 Provider/Site 审批仍未关闭，核心 GA 保持 72% / `NoGo`。
- LM-FR-014～016 的 CAD 语义与质量诊断已完成仓库复核和补齐：既有解析器覆盖墙、柱、门、月台、区域、巷道和货架，并为每个自动提案保留 SourceRef、命中规则、几何规则、置信度和画布定位；未映射/规则冲突继续由 Mapping 诊断进入问题空间。零尺寸与无法闭合现在使用独立稳定代码，越界除了全图 Blocking 还逐对象保存 SourceRef，并通过 Preparation 合同与 CAD 起始向导直接展示；同目标 Polygon/Circle 的真实面积重叠会为双方生成可定位 Warning，边界接触、不同目标包含关系和无可靠面积的降级块不会误报。Space Unit 544/544、CAD Preparation/Parse/BuildScene/Excel 集成聚焦 37/37、CAD 实验工具常规门禁 39 passed / 1 个安装环境用例 skipped、OpenAPI 55/55、CAD 向导 4/4、CP6.Tests 2,933 和完整 solution Release 0 warning / 0 error 通过；配置安装环境后 AutoCAD Core Console 真实 DWG 用例另行 1/1、0 skipped。LM-FR-014～016 仓库实现闭环，真实黄金 CAD/主备 Provider/Pilot 未因此关闭，WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- LM-FR-013 的 CAD Mapping Profile 已形成完整租户权威：System Profile 继续只读；租户可在 CAD 起始向导中复制系统方案、结构化编辑最多 500 条图层/块规则、启停方案，并以 RowVersion + Idempotency-Key 追加不可变新版本。`Space_LayerMappingProfile`/`Version` 保存规范 JSON、Definition SHA-256、复制来源、创建审计和跨租户外键，版本更新/删除由 `SpaceContext` 同步与异步双路径拒绝；管理 API、Preparation Catalog、OpenAPI/双 SDK 与工作台使用同一版本。真 SQL Space Integration 453/453（0 skipped）、Space Unit 540/540、CP6.Tests 2,933、Web 866、生产构建、SDK/EF 漂移和完整 solution Release 0 warning / 0 error 通过。LM-FR-013 仓库实现闭环；真实 DWG/DXF/Provider/黄金集/Pilot 未因此关闭，WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- LM-FR-012 的 CAD 来源结构审核已接入起始向导：Design V1 Preparation Preview 在不暴露原始 CAD 字节、块引用明细或外部引用路径令牌的前提下返回完整图层/块清单，工作台可搜索并查看图层名称、颜色、线型、可见性、对象/支持/未支持计数，以及块定义、引用和属性引用计数。用户可逐图层选择沿用 Profile、忽略或覆盖目标，并显式调整几何规则和置信度；单位、坐标、Profile 或 Override 变化都会使旧 Preview 失效，必须重新由服务端密封后才能启动 Parse。LM-FR-012 仓库实现闭环；后续租户 Profile 权威见上一项。
- LM-FR-005 来源移除引用预检已交付：Design V1 在提交前分类展示活动阻断与历史保留引用，Apply 绑定双 Revision Fence、幂等键和 Serializable 事务；活动 Job/Generation、底图、当前设计对象或元数据会零写入阻断，终态 Job/工件/问题/标定/导入审计继续保留。确认只软删除来源，物理文件继续走 Retention/Tombstone 权威，工作台不级联删除。全量门禁为 Space Unit 540/540、Space Integration 真 SQL 447/447（0 skipped）、CP6.Tests 2,932、Web 862、OpenAPI/双 SDK/EF/生产构建及完整 solution Release 0 warning / 0 error。WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- Space Studio CAD 与 PDF/图片底图上传已显式消费服务端 `Reused`：只有后端按 SHA-256 确认复用时才提示用户已复用受控文件或当前来源，新文件仍显示普通成功提示；底图复用后继续走同一 Clean/Scanning/Rejected 与挂接链。客户端不计算权威哈希、不绕过隔离扫描。Excel 后端/SDK 的 Reused 合同继续由服务器决定；当时缺失的当前 CAD + Excel 上传向导及历史 CAD 候选目录均已由同日后续纵切补齐。WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- Space Studio 活动 Draft 摘要已补齐来源、创建者、创建/更新时间、状态和 Open Blocking 数量；Design V1 列表与详情按同一合同返回，Blank 与 PublishedVersion 来源由现有版本创建权威确定，创建者缺失时不伪造姓名。真 SQL 验证只统计 Open Blocking，不混入已解决问题或 Warning；OpenAPI、双 SDK 和前端同步。未来 System/Tenant Template 创建来源仍须随四模式向导持久化，创建者显示名解析也未在本卡猜测实现；LM-FR-001/WP1 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- System 整仓模板现在可在 Space Studio 按模板楼层原子写入既有 Draft Floor：服务端只接受内置不可变计划，绑定 Site、模板版本、Proposal Hash、页面租约、Floor/Content Revision 和幂等 CommandBatch，并把 Zone/Aisle/Rack/逐层规格/Location 与目标 Floor 边界放进同一 Serializable 事务。标准 F1 真库一次生成 3 区、10 巷道、250 货架和 5,000 库位；错误租约、过期 Proposal、Revision 冲突与命令失败均零写入，完成态重放不重复。工作台提供密封预览、楼层显式选择、数量确认、窄屏/只读保护和未知网络结果的同批安全重试。Space Unit 537/537、Space Integration 真库 443/443、CP6.Tests 2,925、Web 856/856、OpenAPI/SDK/EF/生产构建门禁通过。Tenant 私有模板与 Blank/Published/System/Tenant 四模式统一向导仍缺，LM-FR-001/WP1 保持 Partial/Pending；核心 GA 仍为 72% / `NoGo`。
- 平台整仓模板目录与实例化预览纵切已交付：Design V1 暴露内部可读的 System/Tenant 过滤合同，首份内置 System 模板固定 2 层、7 区、20 巷道、500 货架和 10,000 库位，并以稳定模板/版本/内容 SHA 和完整父级引用返回机器可读预览。工作台可浏览和预览，且持续标明 `writesDraft=false`；外部主体、旧版本和非法 scope 失败关闭。租户私有模板、Template → Draft Apply 与四模式统一向导仍缺，因此 LM-FR-001/WP1 保持 Partial/Pending，GA 保持 72% / `NoGo`。
- Design V1 Floor shell 与项目入口纵切已交付：Space 首页可按 Site 打开 Space Studio，读取活动 Draft 与设计楼层；没有 Draft 时显式创建 Blank，没有 Floor 时必须填写编码、名称、层级、标高和层高。新 GET/POST Floor 合同使用内部读/编辑权限、Version 级 SQL 锁、Expected Content Revision、Serializable 事务和 Idempotency-Key，创建后以服务端 LogicalId 进入既有租约工作台。真 SQL 聚焦 4/4、Space Unit 534/534、Space Integration 真库全量 441/441、CP6.Tests 2,923 通过、Web 全量 848/848、契约/SDK/EF/GA 证据门禁与生产构建通过；完整 solution Release 0 warning / 0 error。整仓 System/Tenant 模板及四模式统一向导仍缺，因此 LM-FR-001/WP1 保持 Partial/Pending，GA 保持 72% / `NoGo`。
- LM-FR-001 的空白版本初始化纵切已交付：Design V1 `CreateVersion` 现在支持 `Blank` 与既有 `PublishedVersion`；Blank 强制无 `BasedOnVersionId`、不继承线上内容、不移动 Published 指针，并以唯一活动 Draft 槽、初始化 Operation、完成态 Job/Attempt、SQL 事务和 Idempotency-Key 失败关闭。真 SQL 2/2、领域聚焦 7/7 通过。该版本纵切自身不创建 Floor；Floor 初始化/选择现由上一条独立纵切补齐，平台/租户整仓模板仍缺。LM-FR-001 与 WP1 均为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-025～029 的仓库实现与自动化证据已闭环：保存后的 Design Scene 无需第二次建模即驱动 2D/3D；选择与逐楼层 3D 视角跨模式保留，未保存重画不再因切换 3D 被静默丢弃，3D 中禁止误提交且回到 2D 可继续；首次四步清单默认展开、可折叠重开并具有 44px 热区及非颜色状态；问题可按 Blocking/Warning/Info 筛选并定位选择；低于 1280px 保持只读 3D、版本和问题。WP4 仍为 Partial/Pending，WP5 仍为 Complete/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-021 的仓库实现已闭环：底图标定现在按 P1 原点、P2 比例点、独立验证点 V 的顺序选点，用户输入真实距离、P1 世界原点、旋转和 V 世界坐标，工作台确定性生成服务端实际接收的 P2 世界毫米坐标。预览展示比例、原点、旋转、验证误差及 `max(50mm, 实距×0.2%)` 阈值，误差超限时禁止保存；既有 Lease、Floor/Content Revision、幂等 CommandBatch 和可逆历史权威不变。LM-FR-021 只代表仓库能力完成，WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-022 的仓库实现已闭环：构件库在既有 Zone/Aisle/Rack/Location Design Layout 权威链之外，补齐墙、柱、门、月台、托盘及输送线、AGV、叉车、工作台、电子秤、充电站六类静态设备。预设固定领域类型、默认尺寸、编码前缀和 `design` 属性，只表达静态几何/编码/自定义属性。创建复用租约、Floor/Content Revision、Content Hash 与幂等 CommandBatch，并进入公共撤销/重做历史；Playwright 验证托盘与六类设备的 2D/3D 同源清单。LM-FR-022 只代表仓库能力完成，WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-020 的仓库实现已闭环：工作台“图层”模式现提供可访问的底图显示/隐藏、0～100% 透明度和锁定控件，直接驱动真实 Konva 栅格图层；锁定会阻止标定入口，新挂接自动解锁、标定保存后自动锁回。显示偏好按版本/楼层保存在当前浏览器标签页，兼容旧 floor view schema v1，损坏或越界状态失败关闭，不修改 Draft 或制造第二套设计权威。单测、类型检查和 Playwright 已覆盖实际画布变化及重载恢复。LM-FR-020 只代表仓库能力完成，WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-024 的仓库实现已闭环：PDF/图片底图的挂接、同页替换、标定与显式移除现均受页面租约、Floor/Content Revision、数据库 UTC 和幂等 CommandBatch 保护，并以既有不可变 Command Record 密封前后态；工作台将它们接入与 CAD、Excel–CAD、普通编辑相同的撤销/重做栈。Undo/Redo 只提交原批次、方向和历史 Hash，由服务端复核当前底图/标定指针并生成新的不可变补偿批次。真 SQL 已覆盖挂接、标定、替换、移除、会话隔离、历史篡改和恢复旧标定指针；OpenAPI/双 SDK、Web 与 Playwright 同步。LM-FR-024 只代表仓库能力完成，WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- Excel–CAD 权威确认现已接入统一撤销/重做：v2 Apply 结果从实际不可变 Command Record 密封历史 Hash/数量，工作台只保存公开历史引用；服务端 Undo/Redo 复核同一页面租约、双 Revision、内容 Hash、原 Apply 工件链、密封历史和当前 Rack/层/库位/绑定/属性/Source 状态，并以新不可变批次原子补偿。介入编辑、历史篡改或旧 v1 结果均失败关闭，OpenAPI/双 SDK、真 SQL和 Playwright 已覆盖。LM-FR-024 现只剩底图挂接/标定可逆合同；WP4 和 72% / `NoGo` 状态不变。
- 详细 Spec LM-FR-024 的 CAD 确认批次已接入统一撤销/重做：CAD Typed Changeset 显式 Apply 后，服务端以实际提交结果密封 Create/Delete/Modify 的补偿命令和修改前完整快照，幂等回放保持同一历史；工作台验证白名单与数量后写入既有历史栈，异常响应保护性切为只读。Excel–CAD 历史已由上一条后续纵切闭环，LM-FR-024 只剩底图挂接/标定可逆合同；WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-018 的人工校正保护已补齐：CAD 来源通用元素可在既有 Design V1 `UpdateProperties` 命令中原子锁定/解除锁定，持久保存单调校正版本、最后操作者和 UTC 时间；锁定后的人工编辑继续递增版本。重新解析命中锁定 SourceRef 时，修改/删除统一变为不可应用的 Blocking Conflict，审核空间可定位并展示版本；服务端 CAD Changeset Apply Fence 再次阻止任何锁定对象写入。迁移、版本克隆、OpenAPI/双 SDK、真 SQL、前端与 Playwright 已覆盖。LM-FR-018 仓库实现闭环；WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-023 的“复制”已补齐：批量检查器可复制 1–100 个 Active 通用元素/货架并允许混合选择，显式确认后把 `CreateElement` 与 `GenerateRackArray` 放入同一 Design V1 原子命令批。通用元素分配新 LogicalId、保留几何/类型/父级/属性并清除唯一业务编码、业务链接和 CAD 来源；货架复制 Active 层与 Generated/Unbound 空编码库位并生成 Zone 内新编码。撤销/重做只 Delete/Restore 已分配身份。真 SQL 混合批、前端单测和 Playwright 已覆盖；LM-FR-023 的对齐、等距、复制、旋转、阵列仓库实现现已闭环。LM-FR-018 已由上条后续纵切关闭；WP4 仍为 Partial/Pending，下一步审计 LM-FR-024 统一撤销/重做及其余三路径细项，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-017 的 CAD 异常对象“画布重画”已完成可逆 Design V1 纵切：单个 Active 非资产通用元素可在 2D 画布采集 3–100 个世界毫米顶点，重复、零面积、自交和 Int32 越界在确认前失败关闭；本地绘制期间 Draft 零写入，显式确认后用同一 LogicalId 的 `UpdateProperties` 原子替换为规范多边形，保留类型、业务链接、属性和 CAD 来源。R/Enter/Backspace/Esc、未保存状态、2D/3D 同源、撤销/重做、真 SQL 审计和 Playwright 均已覆盖。LM-FR-017 五项仓库能力现已闭环；WP4 仍为 Partial/Pending，等待其余详细条目审计及真实多路径/Provider/WMS 接受证据，核心 GA 仍为 72% / `NoGo`。
- 详细 Spec LM-FR-017 的 CAD 异常对象“拆分”已完成可逆 Design V1 纵切：单个 Active 非资产组合可拆成 2–100 个独立元素，首部件保留当前 LogicalId，其余部件分配新 LogicalId并继承类型、父级、业务链接、设计属性和 CAD 来源。组合整体移动/旋转会按渲染器同一局部到世界变换展开；保存/撤销/重做分别使用 Update+Create、Update+Delete、Update+Restore，重做不重复创建。2D/3D 等价、真 SQL 原子与零写入、工作台确认和 Playwright 已覆盖；重画已由上条后续纵切关闭，WP4 继续 Partial，核心 GA 仍为 72% / `NoGo`。
- 详细 Spec LM-FR-017 的 CAD 异常对象“合并”已完成一条可逆 Design V1 纵切：2–20 个同类型、同父级、同业务链接和同设计属性的 Active 非资产元素可合并，保留首选 LogicalId，其余元素标记删除；`group` 组合几何逐部件保存来源、位置、旋转、尺寸和原始几何，并以 100 部件/8 层上限失败关闭。2D/3D 同源渲染、聚合选择边界、工作台确认、撤销/重做、真 SQL 原子批和 Playwright 已覆盖；拆分与重画已由后续纵切关闭，WP4 继续 Partial，核心 GA 仍为 72% / `NoGo`。
- 详细 Spec LM-FR-017 的 CAD 异常对象“改类型”已完成一条 Design V1 纵切：通用元素在同一 `UpdateProperties` 命令内改型，保留 LogicalId，并继续受 Lease、Floor/Content Revision、幂等与原子审计保护；资产实例禁止改型。工作台提供受支持类型选择，并以补偿命令支持撤销/重做。真 SQL 1/1、Space Unit 全量 526/526、OpenAPI 44/44、前端全量 780/780、类型检查和 Space Studio Playwright 全量 15/15 通过。该切片复核时把 WP4 从过宽的 Complete 校正为 Partial；合并、拆分与重画已由后续纵切关闭，核心 GA 仍为 72% / `NoGo`。
- 单人开发阶段已新增 `00001`～`00005` 五个 `DevelopmentSeed` 虚拟人员，覆盖本地 Product/Backend/Frontend3D/QA/WMS/Architecture/Security/DevOps 流程视角；专项机器门禁强制一个真实操作者、五个唯一编号、无生产访问且无正式签字资格。总 GA 与开工证据校验同时拒绝纯数字及开发/测试身份进入正式 Owner、接受人和签字人字段。该能力只解除开发测试角色不足，不证明 2+2+1 真实投入，不改变 72% / `NoGo`。
- 本机 AutoCAD 2025 Core Console 开发转换链已接入实验型 `ICadConverter`：原始 DWG 经只读哈希校验后只在 D 盘唯一 `attempts` 目录导出 ASCII DXF，再通过既有确定性 DXF→CAD IR 与共合同执行器；子进程不经 Shell、绑定精确文件版本、超时杀进程树并清理中间源。Activity Insights 长期锁定的 Autodesk 自身运行包与原始数据分离到 D 盘持久缓存，缓存内出现 DWG/DXF 即失败关闭。Autodesk Floor Plan 样例两次得到相同 CAD IR SHA，4,424 个实体中 4,422 个受支持，安装型测试 1/1 通过。该链未注册 Site Provider、未做网络隔离/客户批准/主备评分，样例不计黄金 CAD，WP3/WP7 和 72% / `NoGo` 均不变；GUI `acad.exe` 的本机签名为 `HashMismatch`，正式使用前仍需 Autodesk 修复或批准例外。
- GA 总索引已更新到 2026-08-15，仓库完成度审计登记为 WP0 实现证据，AutoCAD 开发转换报告登记为 WP3 实现证据；两者没有进入 `acceptedEvidence`。证据 JSON 兼容层修复 PowerShell 7.6 自动把 ISO 时间转换为 `DateTime` 后合法 UTC 证明被误拒的问题，PowerShell 5.1/7.6 仍执行同一严格字符串、UTC 和未来时间校验。详细 Spec 复核后 WP0/WP3/WP4 为 Partial，WP7/WP8 为 ExternalExecution，WP1/WP2/WP5/WP6 仅为仓库实现 Complete，所有接受状态继续 Pending。
- WP0 核心 GA 证据索引已建立：`docs/space/acceptance/v1.3-ga/ga-evidence-index.json` 固定 72%→100% 口径、五类外部输入、WP0–WP8 九个不可删除 Blocking Gate 和产品/QA/WMS/架构/安全五个实名签字角色；仓库实现、真实环境接受与正式签字分开记录。`tools/Test-SpaceGaEvidence.ps1` 校验结构、证据路径与派生状态，`-RequireGaReady` 当前按设计以退出码 2 失败。当前仍为 `NoGo`：5 项外部输入、9 个接受门禁和 5 个签字均 Pending，未填写虚构 Owner、Site、Provider 或证据。
- WP0 证据证明链已加固：Signed/Complete/Accepted 统一验证受控 URI、实际本地文件 SHA-256、非占位的实名字段与 ISO-8601 UTC 时间，拒绝越界/不存在/哈希不一致证据、不安全 URI、原始 DWG/DXF 仓库路径和角色/团队/占位人名；GitHub Actions 工作流覆盖 16 个正反向自测。该加固没有填写任何真实证据，不改变 72% / No-Go 状态。
- WP0 五类外部输入现已增加结构化开工 Manifest 与语义门禁：五角色实名/审批权、2 Backend + 2 Frontend3D + 1 QA 及共享角色、20 份授权 CAD 候选、至少两条不同 Provider Key 的 `ICadConverter` 审批链与隔离 Worker、Greenfield/Retrofit 双仓和覆盖完整 Pilot 的 CP6 WMS 窗口都必须在对应分区中成立。每个 Complete 输入还必须证明 Manifest 自身哈希、Owner 与索引一致；签字人登记逐角色回绑总索引。专项 26/26、总 GA 证明链 34/34。此项只关闭证据内容误报，未填写真实外部输入，当前仍为 5 Pending。
- M0 基线 PR #4 已验证并以 merge commit `9c320a74` 合入；WP1 已完成 Design V1 布局创建/修改/删除、工作台接入和批量编码，WP2 CAD 起始向导也已合入远端 `main`。WP3 当前已完成 Site 级 Provider 认证与主备路由的仓库基础，但真实 ODA/APS 适配器、隔离 Worker 注册和客户批准证据尚未完成。
- WP3 新增按 Tenant/Site 版本化、追加式的 CAD Provider 配置：每个配置最多一个 Primary 和一个 Backup，记录部署模式、数据边界、DWG/DXF 范围、有效期、审批证据引用及 Secret 引用；认证明细不可修改，历史配置只允许从 Current 变为 Superseded。独立 `space:model:provider:manage` 权限维护配置，`space:model:read` 只读能力接口不返回 Secret 内容。
- WP3 Provider 资格选择已按 ADR-0001 和冻结执行计划补齐服务端约束：新认证必须绑定四项硬门禁、80–100 总分、评分规则版本、黄金集 SHA、冻结环境 SHA 与资格证据引用；两条链必须共享同一评测基线，Primary 分数必须严格高于 Backup，低于 80、基线混用或并列均零写入。历史认证不会被迁移默认值冒充合格，缺少资格证据时能力接口和执行路由均失败关闭。该项只完成仓库规则，不代表真实 ODA/APS、Site 审批或黄金集评分已经取得。
- WP3 Provider 评分已新增 `CP6.Space.CadExperiment qualify-providers` 机器入口：严格校验六维权重、四项审批证据、Preflight/黄金集/冻结环境哈希、80 分门槛和唯一第一/第二名，只在完整 Pass 时输出受报告 SHA-256 绑定的一主一备认证输入；No-Go 时认证输入为空，且工具从不修改 Site。聚焦测试 34/34 通过；当前尚无真实候选评分和客户接受证据，WP3 与核心 GA 状态不变。
- WP3 Provider 版本身份已从评分证据贯通到执行链：Site 认证、运行时注册、只读能力、Preparation 输出和新 Parse payload v5 都绑定规范 `ProviderVersion`。认证/注册版本不一致时能力与路由失败关闭，Provider 输出版本漂移在 sealed Preparation 前拒绝，v5 Parse 只接受当前仍认证的 Key + Version；历史认证迁移为空版本并失去资格，不猜测回填。评分工具产生的认证输入携带原候选版本，CAD 向导显示主备版本。该项关闭仓库版本漏洞，不代表任何真实 Provider 版本已认证，WP3 仍为 Partial/Pending。
- WP3 `ICadConverter` 共合同执行器已补齐：所有正式调用入口必须经 `SpaceCadConverterContractRunner`，统一保护隔离 Source 只读/不转移所有权、流式 CAD IR 顺序与唯一性、汇总计数、单次完成，以及 Result 与 Sink 实际提交的 Source/Provider Version/Artifact SHA/Summary/Issues 绑定；开发转换入口已接入，适配器吞掉 Source/Sink 异常也会失败关闭。该项只关闭供应商无关执行协议，不代表 ODA、APS 或替代者已实现/认证，WP3 和 72% / No-Go 状态不变。
- WP3 Provider 认证真库门禁已在 SQL Server 17.0.4025.3 LocalDB 运行 3/3、0 skipped：覆盖并发 Replace 只允许一个成功、唯一 Current Revision、历史追加、认证证据不可变、幂等记录、三份迁移脚本重复执行，以及旧认证缺资格或 Provider Version 时能力与路由失败关闭。该项关闭仓库 SQL 自动化 skip，不代表真实 Provider、Site 审批、黄金 CAD 或冻结 Worker 已取得，WP3 仍为 Partial/Pending。
- WP3 Preparation → Parse 的确定性映射输入已补齐：Ready Preparation 保存服务器生成、规范 JSON、SHA-256 密封的 Mapping Replay Snapshot，绑定 Tenant/Source/不可变 Profile、Inventory/Structure/Preview Hash 与完整 Layer Overrides；历史 payload v4 首次携带该快照，当前 v5 在同一快照上再封存 Provider Version，启动服务和 Worker 分别在入队和 Provider 调用前验证。缺失/篡改当前快照或版本零 Job/零 Provider 调用，历史 v2–v4 显式兼容读取。该项只为真实适配器提供重放合同；真实 Provider 仍须加载冻结 Profile、执行覆盖重放和 `ValidateReplay`，因此 WP3 仍为 Partial/Pending。
- CAD Preparation/Parse 现在统一经 `SpaceCadProviderRouter`：只选择当前 Site 已认证、有效、格式和 Provider Version 均匹配且在当前部署注册的 Provider；Primary 仅在可重试资源故障时切到同配置 Backup，不会跨到未认证链，Preparation 已由 Backup 密封时也不会反向切回 Primary。Preparation 保存 Provider Key + Version，Parse payload v5 绑定 Provider Key、Version、Semantic Preview Hash 和 Mapping Replay Snapshot；旧 v2–v4 显式兼容，v1 继续拒绝。
- Space Studio CAD 起始向导先读取 Site CAD 能力，展示配置 Revision、主备链、认证版本和阻断码；没有有效运行链时禁止扫描轮询和 Preview。`CanPrepareCad` 只代表当前至少一条资格完整、已批准、认证/注册版本相同且运行可用的链可用于内部准备，`CadGaReady` 只有主备两条链资格完整、同评测基线、排名有效、认证有效、版本匹配、运行可用且同时覆盖 DWG/DXF 才成立。
- WP1 已形成独立 Design V1 `layout-commands` 原子写链：在不写入通用 `Space_Element` 的前提下，按租约、Floor Revision、Content Revision 和命令幂等 fence 创建、修改、删除 Zone、Aisle、Rack，并由 Rack 逐层规格确定性协调 RackLevel/Location；Space Studio“构件”上下文已接入三类创建表单、画布坐标、逐层规格和库位数预览，右侧属性面板可修改三类布局对象并显式确认级联删除。货架修改保留既有层/库位 LogicalId、编码与绑定，新增库位保持未编码。
- WP1 批量编码任务卡已实现 Design V1 `location-codes:preview` → `location-codes:apply`：服务端按 Zone → Floor → Tenant 默认规则选择，只修改 Active/Unbound/Generated 库位，保护 WMS Bound、Adopted、Imported 和 Manual 编码；Preview 绑定 Floor/Content Revision、规则集和完整 Proposal Hash 且零写入，Apply 再在同一楼层锁与 Serializable 事务内复算，并受租约、双 Revision、Proposal Hash 与命令幂等保护。Space Studio 批量域可选择填空/重建与库区范围，展示修改/保持/保护项，显式勾选后才写当前 Draft；失败包保留原幂等标识，Published/WMS 不被直接修改。
- 低成本 3D 建模 Spec 已在完整保留 v1.2 详细正文的基础上增量修订为 v1.3；RFC-003 明确为“产品决定已冻结、跨职能批准 Pending”，外部 AI 独立 Beta、Viewer 性能门槛收紧、Supplier 不参加现场 UAT，`DesignUnderlayView` 成为单一页面权威。
- Space Studio 已形成冻结四栏壳层，包含 44px 标题栏、60px 命令栏、52+244px 左侧模式/上下文、主 2D/3D 画布、324px 属性/批量/问题检查器和 30px 状态栏；小于 1280px 自动只读。
- WP5 仓库内可达性切片已闭环：检查器采用标准 `tablist/tab/tabpanel` 与方向键、Home/End 漫游焦点；工作台补齐工具状态、快捷键声明、全局清晰焦点环、状态播报和 2D 画布键盘焦点。`G` 可按 Blocking → Warning → Info 循环定位 Open 问题，窄屏定位仍保持只读 3D；右侧 CAD/Excel/属性/WMS 面板统一消费 `--space-studio-*` token，正文/问题说明不低于 16px、元数据 13–14px，主要控件热区不低于 44px。该项是自动化可验证的工作台行为完成，不替代 WCAG 独立审计、真实键盘/辅助技术验收或人工 UX 签字。
- WP5 2D/3D 交互切片已闭环：草稿 3D 通过参数化场景映射做对象拾取，Element/Zone/Aisle/Rack 回到同一 Design LogicalId，RackLevel 归一到所属 Rack；3D 点选与既有 2D/问题选择共用 `selectObjects`，Ctrl/Command 支持切换选择，Orbit 拖动不会误触。2D 平移/缩放、当前投影模式及 3D camera/target 按 Version+Floor 保存在当前标签页，切层或刷新后恢复；损坏、越界或旧 schema 状态会被拒绝，新楼层无保存状态时重新 framing。该项不替代生产 Published-only Viewer 边界核验或独立 UX 验收。
- WP1 2D 画布拖动精调已接入现有 Design V1 命令权威：Rack/Element 按世界毫米直接拖动，已选对象保持多选整体移动，Zone/Aisle 继续走 Layout 属性合同；落点和撤销均通过带 Lease、Floor Revision、Content Revision/Hash 和幂等批次的 `MoveObject` 写入，失败重绘权威场景。前端全量 780/780、Space Studio E2E 14/14、拖动重复 5/5、type-check 和 production build 通过；独立 UX/Pilot 仍为接受门禁。
- WP7 正式黄金 CAD 已建立结构化证据门禁：恰好 20 份唯一真实授权样本、10/5/5、L1～L5≥4、DWG+DXF、双标注/独立 QA 仲裁、冻结版本/Worker/Source Set、Primary/Backup 两份 release-eligible 报告、主 Provider 严格高分、两者使用同一 50 MiB 标准 CAD、Overall/Out-of-sample 五项质量指标、Holdout 零未报告 Blocking 以及 50 MiB/Ready P95 都进入同一 Manifest。WP7 Accepted 前还必须完成授权 CAD、Provider/Worker 两类外部输入和 WP3 验收，并由 Gate 证明 Manifest 自身哈希；模板/fixture/原始 CAD 路径均拒绝。专项 31/31、当前组合证明链 34/34。该项只关闭证据格式和误报，真实 20 CAD 与 Provider 运行仍未发生，WP7 保持 `ExternalExecution/Pending`。
- WP8 双仓 Pilot 已建立结构化证据门禁：最终包必须恰好包含 Greenfield/Retrofit 两个不透明 Site、各至少连续 14 个日历日并逐日列出不可重复/不缺日的记录，零 S1/S2、每个 S3 都有可用绕行并在签字前关闭、2D/3D/对象清单与 WMS 100% 一致、15/240 分钟恢复、旧 Published 持续服务、Published-only Viewer、无长期双写，以及客户仓库代表和实施负责人的独立实名确认。未来日期或 Pilot 结束前预签的证据也会失败。通用 GA 校验器只在五个内部签字人均 Signed 且每份签字证明的接受人与登记姓名一致后允许 WP8 Accepted，并且只接受被自身哈希证明、通过专项校验的真实 Manifest；模板、Manifest 或嵌套证明中的测试 fixture 均拒绝。专项 21/21、组合证明链 23/23。该项只关闭证据格式和误报漏洞，两仓 Pilot 仍未发生，WP8 保持 `ExternalExecution/Pending`。
- WP5 Viewer 当前仓库性能门禁已在代码提交 `bd206ff8` 的干净跟踪工作区正式复验：Windows 11 / Chrome 151 / Intel Iris Xe 31.0.101.4502 / D3D11 / WebGL2 / 1920×1080，确定性 500 货架/10,000 库位场景执行 1 次单独预热和 30 次冷 Context。30/30 运行、3,000/3,000 次实际拾取命中，0 console errors、0 软件渲染；可交互 P95 62.3ms、帧时间 P95 8.2ms、拾取 P95 0.3ms、10,000 库位着色并提交渲染 P95 2.0ms、36 draw calls，全部通过冻结门槛。原始样本、环境、输入文件哈希和截图见 `docs/space/reports/2026-08-14-space-viewer-v13-ga.md`；该证据不替代生产 Published-only Viewer、独立 QA/UX 或 Pilot。
- WP5 生产 Viewer 的仓库权威边界已闭环：新增 Site 级 `published-scene` 只读合同，只从 `CurrentPublishedVersionId` 解析 Production/Published Design Revision，且明确排除 runtime overlay；单层 Viewer、跨层 Viewer、Control Tower 与楼层列表均不再读取可变旧 `/space/floor/*/scene`/floor API。客户端再次绑定 Site/PublishedVersion/状态并按 RackLevel 确定性投影可拾取 Location，Draft 注入、身份漂移、缺失几何或跨层任一楼层失败均整体失败关闭，不留下半仓画面。OpenAPI、双 SDK、权限和结构守卫已同步；Published-only 真库用例已在 SQL Server LocalDB 实际通过，并证明楼层必须在版本发布前封存、发布后不可追加。生产等价部署、真实发布数据和独立 QA 复核仍是门禁。
- WP4 仓库内路径闭环新增底图标定与 Excel–CAD 可发现性：图片上传并挂接后，上下文面板明确提供“标定底图/重新标定底图”入口；带 `matchJobId` 进入工作台会自动切到问题域并加载权威匹配，定位 Rack 后仍须显式确认才 Apply。浏览器自动化分别覆盖图片上传→标定、Excel–CAD 审核→确认，以及 DWG、DXF 两种格式各自经服务器 Preview 和双确认启动解析。上述证据使用受控 API fixture，只证明 UI/合同路径，不替代真实 Provider、真实文件、Excel 数据与 CP6 WMS 环境闭环。
- WP6 发布 Warning 认领已形成独立 fence：Publish Preview 返回与 ValidationRun 和排序后的 Warning Issue 集绑定的 SHA-256；存在 Warning 时，发布页要求独立逐项确认并把哈希带入 Publish Attempt。缺失确认稳定返回 422，集合或 Run 变化返回 409，历史重发遇到 Warning 会停在生成的 Ready 版本等待人工预览确认，不再静默继承通用审批。该项关闭仓库内合同/服务/UI 行为，不替代真实 CP6 WMS 故障恢复、对账、监控告警、运行手册或双仓 Pilot。
- WP6 发布恢复可观测性基础已交付：跨租户聚合器以不可变 Publish Audit 时间为主，按固定 `waiting_retry`、`manual_intervention`、`reconciliation_required` 状态输出活动数量、最老等待时长、SLO 超时数量和目标秒数，不暴露 Tenant/Site/Version/Attempt；Prometheus 规则覆盖 15 分钟自动恢复、4 小时人工恢复/对账及指标缺失，运行手册固定旧 Published 连续服务和正式 Retry/Reconcile 路径。仓库规则和 Mock 自动化不替代真实告警加载/通知或 SQL/WMS 演练。
- WP6 发布恢复指标已在本机真实 SQL Server LocalDB 暴露并修复 EF 翻译缺口：原复合键 GroupJoin 在 WMS 超时进入 `WaitingRetry` 后无法生成 SQL，现改为显式 TenantId + AttemptId + AttemptStatus 相关子查询；恢复指标单测 6/6、发布编排真库 3/3 通过，超时期间旧 Published 保持且重试完成。该证据只证明真实 SQL 引擎查询与编排，不替代生产等价 SQL/WMS、告警通知和恢复时限接受。
- Space Integration 已在 SQL Server 17.0.4025.3 LocalDB 完整运行 426/426、0 failed、0 skipped。首次 424/426 暴露并关闭发布恢复指标 SQL 翻译与 Published Viewer 夹具先发布后写快照两项问题；最终复跑覆盖全部原环境门禁。该结果关闭本机真实 SQL 自动化 skip，不替代生产等价拓扑、真实 CP6 WMS/IdP/告警链、独立验收或 Pilot。
- CP6.Tests 中属于 Space/WMS 的额外 SQL Server 集合已在同一 LocalDB 独立运行 15/15、0 skipped：覆盖 Location 过滤唯一索引、两阶段换码、rowversion、Control Tower SQL 翻译、WMS Move/Replenish/Serial/LPN/Feature Flag 事务，以及 Integration Event UTC 回填的 Session applock。全套 CP6.Tests 启用 SQL 后另有两个 OA/PUR 共享隔离 Stage 门禁按设计拒绝普通临时库；它们不计入 Space/WMS 结果，也未被绕过。
- WP6 外部主体仓库矩阵已闭环：Design V1 授权阶段在权限属性、模型绑定和 Controller 之前拒绝 Customer/Supplier/3PL 访问 Draft、Source、Upload、Lease、Validate、Publish 和 AI；Published-only Portal 是唯一显式例外，反射守卫禁止扩张放行面。该边界不替代真实 IdP HTTP 负向、生产 SQL 跨租户、独立渗透测试或安全签字。
- 新增 Floor 编辑租约：数据库唯一槽、数据库 UTC、90 秒租期、30 秒前端续租、释放、同用户不同浏览器会话隔离、过期重申请、带双权限和原因的强制接管、不可变接管审计。编辑命令请求新增必填 `leaseId`，保存与租约写入共享 Floor applock，Revision/命令/幂等失败关闭。
- CAD Parse 成功后可由 Job/Source 路由自动读取并校验 PreviewSet SHA、Tenant/Source/Job/Floor 与解析启动时 BaseContentRevision；审核空间输出带基线与哈希的 typed 新增/修改/删除/冲突/低置信度/未识别变更集，经用户勾选后通过租约、Revision、ContentRevision 与幂等 fence 原子合入 Draft，stale 或工件链异常均零写入。CAD 起始向导现会轮询并同步安全扫描终态，要求用户显式确认当前楼层、来源单位、原点、旋转和服务器已知 Mapping Profile；服务端在受控 `ISpaceCadPreparationProvider` 边界内检查原始 CAD，生成坐标、库存、映射和语义预览，并保存绑定 Source SHA、Profile/Transform/Preview Hash、BaseContentRevision/Hash 与两小时有效期的 sealed Preparation。唯一解析启动接口新增必填 `preparationId`，伪 Profile、伪 Hash、过期或 Draft 已前进均拒绝且零 Job/Draft 写入。前端只有在两项显式确认后才能启动解析，本地 JSON 仅保留为高级回退。
- 空白画布/底图路径已可直接创建墙、柱、门、月台和静态设备；Zone/Aisle/Rack 可在工作台创建、选择、修改和显式级联删除，并使用同一租约、Revision、幂等和恢复状态。三类布局对象与 Element 一起进入共享参数化渲染计划，2D/3D 机器清单一致；库位批量编码已在同一批量检查器闭环。工作台“运行校验/校验并发布”会携带 Site/Version 进入正式发布控制面并自动发起 Validation，发布本身仍要求 Preview、审批确认和 `space:model:publish`，不会自动执行。
- 当前累计仓库门禁：完整 Release solution 0 warning / 0 error；Space Unit 531/531、CAD Converter/IR 聚焦 23/23、Provider 评分与 CAD Experiment 工具 34/34、Provider SQL 3/3、CP6.Tests 默认门禁 2,916 passed / 19 environment-gated skipped，另有 Space/WMS SQL 聚焦 15/15、Client 71/71、Space Integration 在 SQL Server LocalDB 426/426（0 skipped）、Web 794/794、Space Studio Playwright 17/17、Provider/Preparation/Parse 聚焦 33/33、OpenAPI 聚焦 44/44、Published Viewer 聚焦 12/12、Viewer 性能聚合单测 5/5、Viewer CPU 性能 1/1、Iris Xe 正式冷运行 30/30、发布管理聚焦 5/5、恢复指标/告警合同 6/6、外部主体矩阵 30/30、权限/OpenAPI/主体边界聚焦 112/112、GA 证据自测 36/36、Vue type-check、production build、OpenAPI/C#/TypeScript SDK drift、EF pending-model 和 diff whitespace 通过。LocalDB 真库证据不等于生产 SQL/WMS 接受；合并后仍执行聚焦冒烟。
- 本项完成的是仓库核心实现、自动化、Published-only 代码边界和当前 SHA 的 Viewer 硬件性能门禁，不代表 GA：真实主/备 DWG Provider、20 份授权黄金 CAD、生产等价部署与真实发布数据复核、独立 UX/辅助技术验收、两仓各 14 天 Pilot、WMS 恢复演练和五角色签字仍未完成。
## CRM V1 T1 对抗审阅收口（2026-08-13）

- T1 继续保持纯规范范围；没有创建 `CP6.CRM` 仓库、业务代码、数据库、云资源、迁移或部署。新仓只允许由满足 T1/M0/P01 前置的 `CRM01-S01` 创建。
- PublicSubmission 已形成独立 Intake 闭环：Quarantined 只能竞争性 release/reject/expiry，匿名化是 PII 生命周期；复用既有 22 个权限，并把首次响应 SLA 固定在原 ReceivedAt，Owner 30 分钟从 release 起算。
- 公开写入固定为浏览器到同源 BFF，再以服务 JWT、Dapr mTLS AppId、workload identity/NetworkPolicy 调 CRM；稳定 attempt 绑定 payload/browser，回执 Cookie 按最终编码 3800 bytes 控制。
- 生产连续性固定 Azure SQL Database GP vCore zone-redundant、GZRS/35-day PITR，以及受控 Azure Storage ZRS Emergency Intake；AZ 与逻辑损坏分别以 RPO/RTO 和季度恢复证据验收。
- System Release Manifest 新增 previous digest 与 DB/OpenAPI/Event/Dapr 兼容范围；默认系统整体回退，组件级例外必须有签名证据，Schema/业务数据永不回退。
- 本地工程/设计与合入前 fallback 复核修正了 Dapr 调用图、IntakeDeptId/PII 权限、实际 migration ID 和首次切换回退边界，修正后无剩余 Critical/High；正式 gstack 交互审阅因当前宿主缺少技能强制的 AskUserQuestion 接口未签发，不能冒充技能门禁。CRM Foundation 16/16、Markdown 相对链接和 `git diff --check` 通过。

## OpenAPI 原生客户端漂移门禁修复（2026-08-13）

- `client-contract` 的 OpenAPI 指纹曾同时在 `main@c68d9b53` 和 CRM PR #5 失败；两者均为期望 `D305...`、实际 `774F...`，确认不是 CRM 文档分支引入。
- 根因有两项：PowerShell 5.1/7 的 JSON 序列化仍会产生不同字节；旧门禁还把全部全局 schema 纳入原生客户端指纹，使 CRM、Space、Finance 等无关 API schema 也触发客户端漂移。
- 门禁现由 Node.js 对 JSON 键作稳定排序，只保留原生客户端路径及其递归可达 schema；PowerShell 入口、失败关闭语义和受控 `-Update` 流程保持不变。新增 4 个 Node 单测，Node 20/22 均通过，当前指纹为 `A49DB452941BF554AEAD66E35C41A7013CB280F7B5A918E41195C4C0FF44A637`。
- 本地完整门禁通过：OpenAPI live check、CP6.Tests 2859/2859、Client 71/71、Web 719/719、类型检查、生产构建与 R2 source gate；19 个 SQL/环境门禁保持基线 skip。本任务不改 API、客户端行为、数据库、部署或生产资源。

## CRM V1 规范批准基线（2026-08-12）

- 规范任务从 `main == origin/main == c68d9b53b4cf3adb5925b8258c36969fdebda753` 创建独立 `codex/crm-v1-spec-approval-20260812` 分支；只修订 CRM 规范与项目记忆，不修改旧根工作区、业务代码、仓库、云资源、数据或部署。
- `CRM-PRODUCT-FRAMEWORK.md` 与 `CRM-V1-EXECUTABLE-SPEC.md` 已升级为 Approved implementation-planning baseline。审阅证据为工程/设计计划 `C8574D3BE11C5492C2CFFA8797917FE4898328E16B25A832267714C719701A08`、QA 计划 `1A6995F45DAD2CD4DD511B7D6CF2E5FA760123C63DD597D1E0F5975D91C5F281`、采用/产品设计 `C60FA78E3F876D0682CB39814EEB9383FBAEC17D1E285DC59D2BB9256C322DF7`。
- 本轮冻结 Lead 创建/Assignment/Activity/Merge 的幂等与并发、412 保文/差异/显式重试、4 个租户业务小时 SLA、C 分栏 Pilot、公开站点 IA/视觉/受控 CMS、加密有界回执 Cookie、真实隔离 ERP UAT，以及 Pilot/CRM12 分层性能门禁。
- Observation、Pilot UAT、Lead Adoption 和 Full Journey Adoption 都是不可豁免硬门禁；整改窗口与重新立项/终止条件保留在私有 Adoption Manifest。CRM V1 唯一 Registry/候选权威固定为 GHCR/GitHub R2，Azure 只能做 CI、DEV 学习、影子验证或消费同一 digest；ACR 迁移独立立项。
- 该状态只表示 T1 规范批准，不表示 CRM 端到端实现完成。下一张票据是 M0/R00 DevOps ADR；Sponsor、Product/Sales Operations/Security/ERP/Data/SRE/Release Owner、Pilot cohort 和 Observation 证据缺失时自动 No-Go。
- 最新基线复核确认 20 个 CRM DbSet、无 CRM Controller/前端路由、JWT 仍为 HMAC SHA-256 且旧租户上下文仍回退 A1；`dotnet test CP6.Tests/CP6.Tests.csproj -c Release --filter "FullyQualifiedName~CP6.Tests.Crm" --nologo` 为 16/16 passed、0 failed、0 skipped。Markdown 相对链接和 `git diff --check` 通过。

## CRM 产品框架与三仓可执行 Spec（2026-08-11）

- 已从 `main == origin/main == f149c75e` 的干净独立 worktree 核对 CRM Foundation：聚焦回归 16/16 通过。当前主线实际只有 20 张 CRM/CMS 表、状态机、迁移和菜单权限种子，没有 CRM Controller、应用服务或前端路由；现有 JWT 仍是 HS256、租户上下文仍有默认 A1，均不得复制进新服务。
- 新增 `docs/crm/CRM-PRODUCT-FRAMEWORK.md`，把 CRM 定位为包装/制造企业从获客到 ERP 订单的行业化售前工作台，冻结角色、22 个权限动作、获客渠道、端到端旅程、V1/VNext、信息架构、UX、指标和产品验收场景。
- 新增 `docs/crm/CRM-V1-EXECUTABLE-SPEC.md`，固化 `CP6`、`CP6.Platform`、`CP6.CRM` 三仓边界，以及 Dapr/Kafka/YARP、RS256/OIDC/JWKS、Next.js、独立 CRM 数据库、CloudEvents/JSON Schema、Outbox/Inbox、20 表一次性迁移和 build-once 发布方案。
- Spec 已覆盖领域/状态机、数据/API/事件、权限/PII/租户隔离、ERP 集成、SLO/威胁模型、测试/发布门禁、里程碑/任务依赖和 Definition of Done。该条记录的是 2026-08-11 初稿状态；2026-08-12 已完成上方规划批准，但 named 开工/生产 Owner 审批仍待完成；未创建新仓库、未改业务代码、未迁移数据或部署。
- CRM 实施必须按 Spec 的 Platform P01–P10、CP6 C01–C04、CRM01–CRM12 依赖图拆分小分支。2026-08-12 已固定 CRM V1 使用 GHCR/R2；R00 只负责在 ADR 中记录该权威、候选清单与 Azure 非权威边界，仍是 P09/P10/CRM12 的硬前置。

## Azure DevOps CI/CD 项目记忆（2026-08-11）

- 已交付本机学习环境的 `azure-pipelines-dev.yml`：仅由 `GTX537.CP6` 在 `main` 成功后的 pipeline completion 触发，在 `CP6-Deploy/LAPTOP-3QQ44FJS` 上核对完整 Git SHA，构建一次 commit-addressed API/Web 镜像，再用 `cp6-dev` deployment job 执行 db-init、服务启动、健康/发布身份验证和非敏感证据归档。
- Azure Variable Group `cp6-dev-secrets` 已由 2026-08-11 外部截图确认存在，四项 DEV SQL/RabbitMQ/JWT 变量均为锁定 Secret。部署脚本新增进程环境 Secret 模式，同时保留人工 DPAPI 模式；只有 deployment task 接收 Secret，RabbitMQ 使用独立 `cp6-dev_rabbitmq-data-azure` volume，不覆盖原人工 Lab 数据。
- 上述段落记录仓库配置交付时的边界；同日后续已创建 `CP6 DEV CD`，完成 `CP6-Deploy`、Variable Group、`cp6-dev` 定向授权，并由 Run #95 取得首次成功 Run、Environment deployment history 和 Artifact 证据。该本机镜像缓存方案仍不适用于 UAT/PROD-LAB，也不关闭 Registry/发布权威门禁。
- Azure `CP6-Deploy` 专用 Pool 已建立；Agent `LAPTOP-3QQ44FJS` 以非管理员本机账号 `cp6_deploy_agent` 作为延迟自动启动 Windows 服务运行，Azure 截图显示版本 `5.277.0`、Online/Idle。通用 CI Agent `CP6-Windows` 仍留在 `Default` Pool。
- 手工、无 Secret、无 Checkout 的 `azure-pipelines-deploy-agent-readiness.yml` 已在 Azure Build ID `10`（Run `20260811.1`）成功运行；截图与 Worker 日志确认专用 Job 身份、非管理员边界、Docker Desktop Linux engine、Compose 和 `KOUSQLSERVER` TCP 门禁通过。Pipeline 后续已重命名为 `CP6 Deploy Agent Readiness`，并由 Build ID `89` 补齐 `sqlcmd` 与备份目录服务身份验收。
- 已交付本机 `cp6-dev`、`cp6-uat`、`cp6-prod-lab` 三套隔离 Compose project；每套包含 Redis、RabbitMQ、Kafka、API、Web，分别使用 `CP6_DEV`、`CP6_UAT`、`CP6_PROD_LAB` 和独立 migrator/runtime SQL 登录。三套环境均为 5/5 容器健康，live/ready Healthy，API/Web 版本与 Git SHA 一致。
- 新增 `deploy/lab/` 与 `Invoke-Cp6LabEnvironment.ps1`，从现有 SQL DPAPI note 临时渲染最小权限 Secret；RabbitMQ/JWT Lab 密钥保存在额外 DPAPI vault，明文临时文件在每次命令结束后删除。`KOUSQLSERVER` TCP `50286` 由工具自动发现，不写入 Git。
- 修复 API Dockerfile 未在 restore 层复制 Space 项目文件，以及 Web Docker 构建上下文无法读取仓库级 TypeScript SDK 的缺陷；Lab、根 Compose 与 R2 candidate 现统一使用可复现的构建边界。
- 2026-08-11 用户提供的 Azure DevOps 列表截图已确认 `cp6-dev`、`cp6-uat`、`cp6-prod-lab` 三个逻辑 Environment 存在，且当时均为 `Never deployed`。2026-08-25 已补齐 DEV 的定向 Pipeline permissions、Exclusive lock 与首次 deployment history；UAT/PROD-LAB 仍未部署，详见 `docs/devops/AZURE-ENVIRONMENTS-SETUP.md`。
- 新增 `docs/devops/` 项目级文档入口，整理当前 Azure CI、目标 Release/CD、Build once、环境策略、发布步骤和分阶段路线；`AGENTS.md` 与根 README 已接入，Codex 后续无需从聊天记录猜测上下文。
- 当前仓库事实是：`azure-pipelines.yml` 在 `main` 提交上运行 `Default` self-hosted pool，完成 .NET 8/Node 22 的后端/客户端测试和 Web 类型/单测/构建；`pr: none`。另有 DEV 学习链在部署 Agent 本机 Build/Deploy，但尚无 Azure Registry、不可变候选或生产环境部署。
- 现有 `.github/workflows/r2-*` 已实现更完整的受保护版本、GHCR 镜像、SBOM/漏洞扫描、签名、不可变证据和 digest 部署，因此在 Azure 门禁等价并显式切换前继续作为生产发布权威。
- 聊天规划中的 ACR 被记录为候选目标而非当前事实。当前只新增本机 DEV 学习流水线，没有创建 Registry/云资源或执行生产部署；CRM V1 已在 2026-08-12 固定沿用 GHCR/R2，R00 记录候选清单、Azure 非权威影子期和回退。ACR 或其他产品的长期 Registry 迁移另行决策。

## CRM V1 Foundation（2026-08-10）

- 已确认 CRM V1 产品边界并形成 `docs/crm/CRM-V1-SPEC.md`：官网/人工线索、线索池、负责人和协作人、活动、企业/联系人、商机、报价接受、ERP 订单赢单，以及独立轻量营销官网。
- Foundation 已落地 20 张 CRM/CMS 实体表和 EF 迁移，覆盖租户隔离、租户内唯一约束、公开路由先解析租户、PII 擦除标记、聚合外键与固定状态机。Won 只有在订单已创建时才合法，Accepted 要求已接受报价。
- 已登记 6 个菜单节点和 22 个动作，所有租户管理员获得权限；在 Vue 页面交付前菜单保持禁用，避免 admin 看到不可用入口。本里程碑是基础设施完成，不代表 CRM 端到端功能已经完成。
- 下一阶段为 Intake：人工/公开线索接入、线索池、分配与协作、SLA、重复候选和活动时间线 API；其后依次交付转化/ERP、前端体验、营销官网和运营任务。

## Space 单格货位码 Zone 级 rackSeq（2026-08-10）

- `CodeEngineService.GenSingleAsync` 已从目标 Zone 加载完整货架集合，并与批量 `GenerateAsync` 复用同一套 `(X, Y, Id)` 确定性排序；非首架单格生成不再被错误简化为序号 `1`。
- 相同几何坐标时以货架 `Id` 作稳定兜底。回归用例证明第二货架的单格编码与整层批量重建一致，且不会和首架编码重复。
- 本切片没有改变规则模型、API、数据库、迁移或前端。门禁为 CodeEngine/LocationPublish 聚焦 55/55、CP6.Tests 2843 passed / 19 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error；新增排序路径覆盖率审计 8/8，任务 diff 与新增行格式检查通过。

## FIN BudgetLine 版本级并发控制（2026-08-10）

- `BudgetVersion.RowVersion` 已经是预算行聚合并发边界。新增、编辑、删除和 Excel 确认导入都从客户端接收版本令牌，每次行写入都会推进该令牌；因此两个用户即使修改不同预算桶，旧快照也会统一失败为 `E-A5-CONCURRENCY-001`。
- API 对缺失/非法令牌失败关闭；前端在新增、行内编辑、删除、确认导入后同时刷新版本和行数据。单行的行头/12 期明细与 Excel 整批确认分别在单一事务中持久化。
- 本机 `KOUSQLSERVER` 原生 `rowversion` 证明 1/1，覆盖不同行的第二写者拒绝、刷新重试和陈旧删除回滚。其余门禁为 FIN 303 passed / 1 个既存 SQLite 限制项 skipped、前端令牌合同 3/3、Vue type-check、WebApi Release 0 warning / 0 error。

## PLAN/PUB Attachment 宿主业务权限补强（2026-08-10）

- Attachment 保持横切组件架构，不新增没有页面可锚的 `pub-attachment` 暗菜单；`Attachment:EnforceBizPermission` 缺省改为 true，显式 false 只保留为受控兼容开关。
- list/upload/download/preview/delete/rebind 六个入口统一按请求或持久化 `BizType` 回查宿主菜单。下载/预览在授权后才打开物理流，删除在授权后才调用服务；rebind 还要求当前用户是 draft token 下全部附件的上传人，并拥有全部宿主菜单。
- `PubUpload` 新增必填 `writePermission` 宿主 action key；无写权限时隐藏上传与删除，但保留下载/预览。前端只是 UX，后端宿主菜单回查仍是安全边界。
- 门禁：Attachment/PLAN-PUB 聚焦 21/21、OpenAPI 30/30、CP6.Tests 2841 passed / 18 environment-gated skipped / 0 failed、PubUpload 3/3、前端全量 716/716、Vue type-check 与 production build 通过、WebApi Release 0 warning / 0 error。

## 分支优先规范与本地配置优先级（2026-08-09）

- `e4e33364` 已把仓库级“分支优先、验证后合并”规范合入并推送 `main`；`AGENTS.md` 和 `DEVELOPMENT-GUIDE.md` 约束每个任务使用独立分支/必要时使用 worktree、只暂存任务文件、必需门禁通过后再合并与推送。
- 配置任务提交 `e3bf2420` 把 `appsettings.Local.json` 插入逻辑从 `Program.cs` 提取为可测试组件，并只匹配 `Prefix: null` 的无前缀环境变量源；`DOTNET_`/`ASPNETCORE_` 主机源不会被误认，环境变量与其后的命令行源继续覆盖本地 JSON。
- `.claude/settings.local.json` 已作为机器本地文件忽略；任务没有提交 `localhost\\KOUSQLSERVER` 等机器专属 `launchSettings` 配置。
- 验证：配置聚焦 4/4、OpenAPI 契约 30/30、CP6.Tests 2832 passed / 18 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error、C# whitespace 与 `git diff --check` 通过。

## WF 通知定向推送与遗留广播清理（2026-08-09）

- 运行链路已收敛为事务内 outbox：`PersistentWfNotifier` 只根据用户偏好写通知意图，不再持有数据库、邮件或 SignalR 直发依赖；提交后由 `WfNotificationDispatchWorker` 统一派送。
- 实时通知使用 `Clients.User(row.UserId.ToString())`，目标值与 JWT `ClaimTypes.NameIdentifier` 的用户 GUID 一致；`NotifyHub` 继续要求认证。未注册的 `SignalRWfNotifier` 与四段永远不可达的 `Clients.All` 回退代码已删除，避免旧广播路径被重新启用。
- 项目记忆中“WF 仍广播”的 API TODO、Todo 和 KnownIssue 已同步关闭。门禁为通知聚焦 13/13、CP6.Tests 2832 passed / 18 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error。

## E13 无锁 Zone 父关系确定性推导（2026-08-09）

- 功能提交 `d19a5300` 基于 `main@6bbdd760` 交付 `warehouse-rule-only-v2`：Aisle/Rack 没有人工 `relations.zoneSourceKey` 锁时，只在同一权威 CAD Semantic Preview 中恰有一个 Zone Polygon 被证明完整包含其确定性几何时写入父关系；来源为 `DeterministicRule`，证据码为 `RULE:ZONE_GEOMETRY_CONTAINMENT_V1`。
- 零候选与多候选继续以 Blocking `SPACE_RULE_ONLY_PARENT_REQUIRED` 失败关闭，细节分别为 `no-containing-zone` / `ambiguous-containing-zones`；凹多边形路径按完整线段而非端点或 Bounds 判断。人工锁仍优先，冲突 AI Relation 被拒绝并保留融合问题，已解析关系进入环检测。
- `warehouse-rule-only-v1` 的冻结 Run 与恢复链保持旧行为，不被新算法静默改写；不同 SourceHash 的匹配、建议继承和人工确认仍是独立产品卡，不在本切片内。
- 门禁为融合聚焦 16/16、BuildScene 3/3、Space Unit 492/492、默认 Integration 288 passed / 95 SQL 环境门禁 skipped、完整 Release/AOT 0 warning / 0 error。无 Migration、HTTP/OpenAPI/SDK、前端、Provider、Usage、High Accept 或 Draft 写入变化。完整证据见 `docs/space/reports/e13-deterministic-zone-parent-inference.md`。

## `main` 同步、P2.5 受控整合与分支策略（2026-08-09）

- PR #2 已以 `8045d872` 把原 `integration/space-v1-20260730@f8c3bae8` 受保护合入 `main`；5 个预期冲突按权威边界人工解决，Docker 本地 HTTP Cookie 修复以 `0fc6f529` 等价纳入。
- P2.5 Analytics Control Tower 随后以 `030a97b9` 在当前 E10 Runtime/Viewer 真相源上选择性整合；保留独立控制塔、实时脏库位批处理、分析配置、定时 ABC 快照、容量发布和共享 ABC 分类器，不整段摘取历史分支。
- 历史迁移 `20260720035903` 未进入主线；替代迁移 `20260809092206_SpaceAnalyticsControlTowerCurrent` 基于当前 ModelSnapshot，仅新增两张分析表和三个索引。`b2a91680` 已对齐 Space 权限、菜单种子与配置文档。
- 本次文档同步以远端 `main@e4e33364` 为基线；该提交通过 `04eaf42d` 明确分支优先规则。后续开发、修复、重构和文档任务必须从最新 `main` 建独立分支；根工作区有未提交改动时使用独立 worktree。
- 合并前门禁包括 OA 2/2、前端 711、CP6.Tests 2816、Space Unit 487、默认 Integration 288、客户端 71 与心跳重复 50、EF drift、完整 Release/AOT 0 warning / 0 error。14 个 Core + 36 个 Space 幂等迁移已从 main 基线在 LocalDB 双执行，51083/51000/51020 失败关闭通过；正式生产仍需备份恢复副本演练。完整评估见 `docs/space/reports/2026-08-08-main-merge-readiness.md`。
- 主线代码不代表正式 CAD/Provider/S14/S15/S18/S19、R2 标签或生产部署获批；这些门禁继续独立失败关闭。

## E13 RackGenerationProfile 权威版本链（2026-08-08）

- 功能提交 `19d32650` 在基线 `d0d1c713` 上新增独立的 RackGenerationProfile 头/不可变版本存储、System/Tenant 可见性、Tenant-only 幂等创建、列表与精确版本读取；未复用渲染资产冒充生成规格。
- Generation Run 首建会冻结经 Active/Ready 与租户校验的精确版本；RuleOnly Worker 把该版本显式绑定到权威 Preview 的 Rack 项并确定性生成 RackLevel/Location。Web 提供显式可清空选择，不自动推断默认；未选择继续产生 Blocking。
- Migration `20260808164544`、幂等 SQL、三条 API、读写审计、Problem Details、OpenAPI 118 operations、C#/TypeScript SDK 已同步。真实 SQL 迁移/双执行/隔离/约束 1/1，前端聚焦 9/9、全量 711/711，Space Unit 487/487，默认 Integration 288 passed / 95 skipped，CP6.Tests 2816 passed / 17 skipped，完整 Release/AOT 0 warning / 0 error。
- 本切片不启用 Provider、网络、Secret、Usage、High Accept 或 Draft 自动写入。现有方案追加 v2、System 配置入口和完整管理 UI 尚未实现；下一项内部优先级转为无锁父关系确定性推导或不同 SourceHash 人工确认。完整证据见 `docs/space/reports/e13-rack-generation-profile-authority.md`。
- 功能/报告提交 `19d32650` / `6f12a19e` 已通过 no-ff 提交 `70dd670d` 进入远端集成分支；合并态前端 9/9、OpenAPI/权限 63/63、SDK drift 复验通过。祖先链确认后已删除本地/远端临时分支；清理 38 个可再生成目标和 29,418 个文件，释放 1,985,000,330 bytes（约 1.85 GiB），`main` 未修改。

## E13 Generation Run 建模 Web 入口（2026-08-08）

- 功能提交 `52bb3a29` 与验证报告提交 `282d4e54` 已通过 no-ff 提交 `2871df1b` 进入 `integration/space-v1-20260730`；编辑器可从已确认 DWG/DXF Preview 启动 RuleOnly Run，并在同一决策面板显示排队/进度、审核、Apply 和 Failed/Stale replacement 恢复，旧 recovery 合同已移除。
- Run 公开 DTO 补齐冻结 Source/Mapping/Rack Profile 标识，恢复请求重新读取当前 Draft RowVersion/ContentRevision，并发送 `If-Match`、旧 Run RowVersion、BasedOn 血缘和稳定 Idempotency-Key。Queued 等中间态不再提前读取尚未物化的 Review/Proposal。
- 门禁：前端聚焦 3 files / 11 tests、全量 133 files / 710 tests、type-check/production build、OpenAPI/审计 31/31、Space Unit 484/484、默认 Integration 283 passed / 94 skipped、CP6.Tests 2812 passed / 17 skipped、SDK strict/drift 与完整 solution Release/AOT 0 warning / 0 error。无 Migration、Provider、外部网络、Usage 或 Draft 自动写入。
- 下一项内部缺口优先为权威 RackGenerationProfile 版本存储/读取，随后可独立处理无锁父关系推导或不同 SourceHash 人工确认；外部 Provider 与正式 CAD/黄金集证据仍独立存在。完整边界见 `docs/space/reports/e13-generation-run-web-entry.md`。
- 合并态前端聚焦 11/11、OpenAPI 合同 29/29 与 SDK drift 通过；清理 38 个可重建目标和 29,416 个文件，释放 1,982,552,577 bytes（约 1.85 GiB）。远端祖先链确认后已删除本地/远端临时分支，`main` 未修改。

## E13 首次 Generation Run 创建入口（2026-08-08）

- 功能提交 `770bdc96`、验证报告提交 `bbcaf6fe` 已通过 no-ff 提交 `9d0971f4` 进入 `integration/space-v1-20260730`；合并后聚焦 9/9、OpenAPI/审计 31/31 复验通过，`main` 未修改。
- 在集成基线 `54f1cda7` 上以功能提交 `770bdc96` 把 `POST /versions/{versionId}/generation-runs` 收敛为统一 `CreateGenerationRun`：无 BasedOn 首次创建，有 BasedOn 保留 Failed/Stale replacement Run；必须提交 `If-Match`、ContentRevision 与 Idempotency-Key，继续使用 `space:model:generate-ai` 和显式审计。
- 首建只放行 RuleOnly；Tenant Disabled 的 AiAssisted 返回 `SPACE_AI_DISABLED`，已启用但生产 Provider-backed BuildScene 未配置时返回 `SPACE_AI_PROVIDER_UNAVAILABLE`。创建过程零 Provider、零 Usage、零 Draft 写入，不注册 Mock/Local Provider 冒充生产能力。
- Version/Source/Clean file/SourceHash/坐标/Floor/Mapping/PreviewSet 全部重新校验；业务键固定 Preview Artifact ID 与文件 SHA，Worker 和后续 replacement Run 精确消费同一 Preview。未经权威存储验证的 RackGenerationProfile GUID 拒绝进入 Run。
- 公开 create 幂等域覆盖首次与恢复：同键同请求重放、同键不同请求冲突、不同键相同业务输入复用 Current Run。OpenAPI 与 C#/TypeScript SDK 已同步，operation 总数仍为 115。
- 门禁：聚焦 9/9、OpenAPI/审计 31/31、Space Unit 484/484、默认 Integration 283 passed / 94 skipped、CP6.Tests 2812 passed / 17 skipped、C# SDK 与 TypeScript strict、SDK drift、完整 solution Release（含 Desktop/Android AOT）全部通过；最终构建 0 error / 7 条未改动测试文件既有 warning，C# SDK 为 0 warning / 0 error。无 Migration。
- 下一个内部缺口优先补权威 RackGenerationProfile 版本存储/读取，或把本 API 接入建模 Web UI；无锁父关系推导、不同 SourceHash 人工确认、外部 Provider 与正式 CAD/黄金集证据仍独立存在。完整边界见 `docs/space/reports/e13-generation-run-create-production.md`。
- 合并后清理当前隔离工作区 36 个可重建 `bin/obj` 目录、8,622 个文件，共 1,666,117,627 bytes（约 1.55 GiB）；临时功能分支待远端祖先链确认后删除。

## E13 纯规则 BuildScene 生产执行链接线（2026-08-08）

- 功能提交 `36cc0241`、验证报告提交 `89c6fb2a` 已通过 no-ff 提交 `9e7f7e0a` 进入 `integration/space-v1-20260730`；合并后规则/融合 21/21、BuildScene 与默认注册 3/3 复验通过，`main` 未修改。
- 在集成基线 `4d9bc3f6` 上以功能提交 `36cc0241` 实现生产默认 `SpaceBuildSceneJobStepExecutor`：`RuleOnly` recovery 现在能从私有、哈希和血缘校验通过的 CAD PreviewSet 走完 12 步 BuildScene，持久化只读 Proposal/Issue 并把 Run 推进到 AwaitingReview；Provider-backed 模式继续 `SPACE_AI_PROVIDER_UNAVAILABLE` 失败关闭。
- local-only 特征快照使用稳定 `Source SHA + SourceRef` SourceKey、Run 隔离 correlation 和零 Provider 调用；同 SourceHash 的已确认 E13-S10 locked facts 会从旧 Proposal SourceRef 重映射并参与融合，覆盖名称、allowlisted 类型属性和 Zone/Aisle/Wall 父关系。不同 SourceHash 仍不自动继承。
- Proposal/Issue 落库使用 Serializable 和逐字段重放校验；元素尺寸、Aisle/Rack 父关系和 RackProfile 缺失均 Blocking。执行器不写 Draft、不创建 AI Usage、不启用 High Accept；Draft 继续只能经人工 Decision 与原子 Apply 修改。
- 门禁：规则/融合聚焦 21/21、BuildScene 端到端与外部模式关闭 2/2、Space Unit 484/484、默认 Space Integration 277 passed / 94 SQL-gated skipped、CP6.Tests 2811 passed / 17 environment-gated skipped、完整 solution Release（含 Desktop/Android AOT）0 warning / 0 error。
- 本切片关闭“生产 BuildScene 全部占位失败”和“同源 LockedFacts 未接 Worker”的内部缺口，但尚无首次 Generation Run 创建 API；当前入口仍是 Failed/Stale Run 的 RuleOnly recovery。外部 Provider、不同 SourceHash 人工确认继承、权威 RackGenerationProfile、确定性无锁父关系和正式 CAD/黄金集证据仍未完成。完整边界见 `docs/space/reports/e13-rule-only-build-scene-production.md`。
- 合并后清理当前隔离工作区内 36 个可重建 `bin/obj` 目录、6,108 个文件，共 1,209,344,722 bytes（约 1.13 GiB）；临时功能分支待远端集成祖先链确认后删除。

## E13-S14 离线质量评估开发切片（2026-08-08）

- 在集成基线 `6c99b0fe` 上以功能提交 `e69b3bca`、报告提交 `9261d59a` 和 no-ff 提交 `292a26ed` 交付离线质量评估器、Calibration-only 阈值校准、Validation+ReleaseHoldout 样本外验证、95% Wilson 下界和规范报告 SHA-256；最终融合提案按 SampleId+SourceKey 一对一匹配，重复猜测计 False Positive，类型、关键属性和关系必须正确。
- 正式清单硬门禁覆盖 20 资产、L1～L5、10/5/5、唯一 CAD hash、授权/脱敏、应用/Parser/Provider/Model/规则/映射/答案版本、独立标注仲裁、验收日期、不可变和完整性审计。DevelopmentSeed 即使指标全绿也不能成为发布证据。
- `evaluate-ai-offline` 命令支持开发测量与 `--require-release-eligible` 失败关闭；无 Provider 调用、Draft 写入、Migration、HTTP/OpenAPI 或 SDK 变化。门禁为核心 11/11、命令 1/1、CAD 工具 26/26、Space Unit 482/482、默认 Integration 275 passed / 94 skipped、CP6.Tests 2811 passed / 17 skipped、完整 solution Release 0 warning / 0 error。
- 本切片是 E13-S14 工程能力，不是正式 S14 签收；仍需获授权黄金 CAD、独立标注/QA、真实版本冻结、Provider 输出、人工操作实测和签字。完整边界见 `docs/space/reports/e13-s14-offline-evaluation-gate-development.md`。
- 功能和报告已进入并推送远端集成祖先链，临时本地/远端分支已删除；清理 41 个可重建 `bin/obj` 目录、6,982 个文件，释放 1,380,171,591 bytes（约 1.29 GiB）。`main` 未修改。

## E03-S05 Excel 设计元数据权威 Apply 扩展（2026-08-08）

- 在集成基线 `0cff2123` 上以功能提交 `b5aa87b2` 关闭标准 Excel 的最后三个已知失败关闭字段：`Bindings` 成为 ModelVersion 内的设计声明，Site 权威 WarehouseCode 必须逐字符一致，AdapterId 固定自当前运行源；每个有绑定行的 Location 恰有一个 WmsPrimary，并可带 WmsAlias。Excel 声明不冒充 WMS Adoption。
- 新增版本化 `Space_LocationExternalBinding`、`Space_DesignAttribute` 和 `Space_LocationRevision.LocationType`。Attributes 只归属 Rack、`RackCode/LevelNo` 标识的 RackLevel 或 Location，Namespace 限 Owner/Batch/Container/Manufacturing/Custom；LocationType 限 Storage/Staging/Picking/Buffer。权威遗漏软删除并追加确定性命令，Primary/Alias 互换使用事务内两阶段更新。
- 同一 Serializable CommandBatch 现在覆盖 Rack/RackLevel/Location/Binding/Attribute；Apply 处理器与层级计划升级 v2。普通 Draft 和 Planning Scenario 克隆统一 v2，内容哈希/Validation v2、发布预览、Design Scene、Planning Exchange、OpenAPI 与 C#/TypeScript SDK 均包含新增数据。
- EF Migration `20260808131619_SpaceE03S05ExcelDesignMetadata` 与增量 SQL 已验证：脚本在临时 SQL Server 库连续执行两次后为 1 条历史、2 张新表、1 个 LocationType 列；EF model drift clean。
- 门禁：元数据/Validation 15/15、Match/Apply 12/12、Space Unit 471/471、默认 Space Integration 275 passed / 94 SQL-gated skipped、场景与 Clone/Migration 真实 SQL 10/10、CP6.Tests 2811 passed / 17 environment-gated skipped；完整 solution Release 0 error / 7 个未改动测试文件既有 warning，SDK drift、TypeScript strict、C# whitespace 与 diff 检查通过。完整证据见 `docs/space/reports/e03-s05-binding-attribute-location-type-apply.md`。
- 功能提交 `b5aa87b2`、验证报告提交 `713ac99d` 已通过 no-ff 合并提交 `691c2d31` 进入 `integration/space-v1-20260730`；本收尾提交随集成分支推送远端，`main` 未修改。合并后清理 37 个可重建 `bin/obj` 目录、9,093 个生成文件、1,783,694,326 bytes（1,701.06 MiB，约 1.66 GiB）。正式 CAD/Excel 签收仍等待获授权 DWG/DXF Provider、组织黄金集、真实大文件/故障/性能证据，以及生产镜像部署、备份迁移和 WMS 发布/恢复演练。

## E03-S05 Excel 层级与 RackTemplate 权威 Apply 扩展（2026-08-08）

- 在集成基线 `677f8df5` 上以功能提交 `cb802cf6` 扩展 E03-S05：同一 Serializable CommandBatch 现在按 `Racks → RackLevels → Locations` 写入，子对象按稳定业务键更新或按冻结行身份生成确定性 LogicalId/CommandId；整批只提升一次 Floor Revision 与 ContentRevision，重放不重复创建。
- 标准工作簿本身没有 Zone/Aisle 工作表，因此不伪造格式；`Racks.ZoneCode` 唯一解析已有 Zone，Aisle 保持可空。RackLevel 单元尺寸由 Rack 尺寸和 Bin/DepthCount 以整数毫米推导，Location 继承对应层的 CellWidth/ClearHeight/CellDepth/MaxLoad。映射方案声明子表权威时，省略的活动子对象改为 Disabled；WMS 已绑定 Location 不允许被省略删除。
- 非空 `RackTemplateCode` 解析可见活动 Asset：Tenant 同码优先于 System，并固定到最新 Ready 不可变 SpaceAssetVersion。Excel 显式尺寸与层级行仍是导入几何权威，模板只固定版本血缘。
- `Bindings`、`Attributes` 和非空 `LocationType` 继续返回 `SPACE_EXCEL_CAD_APPLY_SCOPE_UNSUPPORTED`，因为现有模型分别缺少仓库到 Site/Adapter 的权威解析、Rack/RackLevel/Location 属性归属和 LocationType 持久字段；不静默丢失数据。无 Migration、HTTP、OpenAPI、SDK 或前端变化，`main` 未修改。
- 门禁：Location 领域 3/3、Match/Apply 9/9、Space Unit 467/467、默认 Space Integration 272 passed / 94 SQL-gated skipped、CP6.Tests 2811 passed / 17 environment-gated skipped；完整 solution Release（含 Desktop/Android AOT）0 warning / 0 error，任务文件格式和 diff 检查通过。完整证据见 `docs/space/reports/e03-s05-hierarchy-template-apply.md`。
- 功能提交 `cb802cf6`、验证记录提交 `35e77a05` 已通过 no-ff 合并提交 `99801da1` 进入并推送至 `integration/space-v1-20260730`；`main` 未修改。合并后清理本隔离工作区 37 个可重建 `bin/obj` 目录、6,649 个生成文件，共释放 1,262.83 MiB（约 1.23 GiB）。

## Space 生产处理 Job Worker 接线（2026-08-08）

- 在集成基线 `17bce8df` 上以功能提交 `d09e44dc`、验证报告提交 `67d8b417` 和 no-ff 提交 `51012a43` 进入 `integration/space-v1-20260730`；`main` 未修改。
- 保留发布专用 Worker 独立处理 HistoricalRepublish/Reconcile/Publish；新增 Processing Worker 按租户消费 ExcelPreview、CadParse、ExcelCadMatch、ExcelCadApply、Import、BuildScene、ApplyGeneration、Validate 和 AiRetentionCleanup。E03/E13 非发布 Job 不再停留在“API 可排队、生产 WebApi Host 不认领”的内部状态。
- Processing Worker 每个租户每轮每类型最多认领一个 Job，复用既有租约、心跳、checkpoint、超时、退避、取消和接管；热队列不能饿死其他类型。稳定非空系统 Actor 同时传播到 Core/Application 上下文，满足 AI Apply 等后台安全门禁。
- 未配置 CAD、Import/BuildScene 或外部 AI Provider 时仍按既有稳定错误失败关闭；本卡不启用外部网络、密钥、URL 或来源不明的转换器。无数据库、Migration、API、OpenAPI、SDK 或前端变化。
- 门禁：Worker 聚焦 3/3、默认处理器注册 1/1、Job Processor 17/17、Space Unit 464/464、默认 Space Integration 270 passed / 94 SQL-gated skipped、CP6.Tests 2811 passed / 17 environment-gated skipped；完整 solution Release（含 Desktop/Android 双架构 AOT）0 warning / 0 error，任务文件格式与 diff 检查通过。完整证据见 `docs/space/reports/space-processing-job-worker-production.md`。
- 合并后清理 36 个可重建目录、6,190 个文件、1,206,049,385 bytes（约 1.123 GiB）。生产部署仍需发布包含本提交的镜像；正式 CAD Provider、授权 DWG/DXF 黄金集和真实性能证据未因此解除。E03-S05 的 RackLevel/Location 与 RackTemplateCode 权威原子写入已由后续扩展完成；标准工作簿没有 Zone/Aisle 表，ZoneCode 只解析已有 Zone。

## E03-S05 Excel 导入确认与幂等写入开发切片（2026-08-08）

- 在集成基线 `cdc629ea` 上以功能提交 `4e92e435`、验证报告提交 `d048e01a` 和 no-ff 提交 `f735747a` 进入 `integration/space-v1-20260730`；`main` 未修改。
- E03-S04 权威匹配面板现在必须由用户显式确认，服务端才创建 `ExcelCadApply` Job。确认绑定 Artifact ID/SHA、ExpectedContentRevision 与幂等键；Worker 重新打开私有 Artifact 和 Excel、重算规范投影并核验 Tenant/Site/Version/映射/Floor/修订，浏览器不能提交匹配结果、目标或写命令。
- Apply 在 Serializable 事务中写入货架、关联 Excel Source、追加确定性 CommandBatch/Command、提升一次 Floor Revision 和一次 ContentRevision，并把来源置为 Imported。重复确认、不同幂等键重确认、Job 重放及“数据库已提交但 checkpoint 未保存”的恢复均复用同一批次；任何权威输入漂移整批失败关闭、零部分写入。无新表或 Migration。
- 本卡正式写入范围仅为已匹配 `Racks` 行；其他目标工作表或非空 `RackTemplateCode` 返回 `SPACE_EXCEL_CAD_APPLY_SCOPE_UNSUPPORTED`，不会静默忽略。OpenAPI 操作数为 115，C#/TypeScript SDK 与前端显式确认状态已同步。
- 门禁：API/权限/OpenAPI 62/62，Apply/原子性/注册 8/8，Match/Artifact/Processor 27/27，Space Unit 464/464，默认 Space Integration 270 passed / 94 SQL-gated skipped，CP6.Tests 2808 passed / 17 environment-gated skipped，前端 132 files / 705 tests；类型、生产构建、SDK drift、格式与 diff 检查通过；完整 solution Release（含 Desktop/Android 双架构 AOT）0 error / 10 条既有 warning。完整证据见 `docs/space/reports/e03-s05-excel-import-confirmation-idempotency.md`。
- E03-S01～S05 的应用内主链已闭环，但生产 WebApi Hosted Worker 尚未认领 `ExcelCadApply`；完整 Excel 层级/模板写入、正式 CAD Provider、组织授权 DWG/DXF 黄金集和真实大文件/故障/性能证据仍未完成，因此不记作生产 CAD/Excel 正式签收。合并后清理 38 个可重建目录、28,788 个文件、1,544,566,969 bytes（约 1.438 GiB）。

## E03-S04 服务端权威 Match Artifact 开发切片（2026-08-08）

- 在集成基线 `27d3989b` 上完成功能提交 `4db2d0d0`、验证报告提交 `93f65a33`，并以 no-ff 提交 `3ee23655` 进入 `integration/space-v1-20260730`；`main` 未修改。
- 新增服务端权威 `ExcelCadMatch` Job 与私有 `ExcelCadMatchPreview` Artifact。HTTP 只冻结 Excel Preflight、CAD Parse/PreviewSet、映射方案和 Draft ContentRevision 后排队；Worker 重新打开私有 Excel、校验 Job/Artifact/Schema/SHA/血缘并直接读取数据库 Floor/Zone/Rack 权威快照，客户端不能提交匹配结果或哈希。
- 新增创建/查询 API、`space:model:edit/read` 权限、写/读审计、外部主体拒绝、受保护游标筛选分页和编辑器权威审阅/画布定位。Draft 修订漂移时保留只读历史证据但关闭 `CanConfirm`；重试只复用唯一且完整校验通过的 Artifact。OpenAPI 操作数为 113，C#/TypeScript SDK 已同步，无 Migration。
- 门禁：Space Unit 464/464；默认 Space Integration 267 passed / 94 SQL-gated skipped；CP6.Tests 2806 passed / 17 environment-gated skipped；前端 132 files / 702 tests；完整 solution Release（含 Desktop/Android AOT）0 error / 10 条既有 warning；类型检查、生产构建、SDK drift、受影响文件格式和 diff 检查通过。合并后清理 39 个可重建目录、32,452 个文件、2,475,932,206 bytes（约 2.306 GiB）。完整证据见 `docs/space/reports/e03-s04-authoritative-match-artifact.md`。
- E03-S05 的人工确认与原子 Draft 写入现已解除应用内前置依赖，但仍必须只消费本卡权威 Artifact，精确校验 ContentRevision 并失败关闭。正式 CAD Provider、组织授权 DWG/DXF 黄金集、生产 Excel/CAD Worker 部署和真实大文件/故障/性能证据仍未完成，因此本卡不是生产 CAD 正式签收。

## E06-S06 版本发布管理 UI 开发切片（2026-08-08）

- 在集成基线 `088648b7e` 上以功能提交 `69a8b77a`、验证报告提交 `3fefe0ef` 和 no-ff 提交 `0d61f3dc` 进入 `integration/space-v1-20260730`；`main` 未修改。`/space/publish` 现提供验证、权威差异/WMS 影响预览、外部审批引用确认、发布进度/失败恢复/人工重试、审计时间线和历史版本重新发布入口；原库位发布工具保留在 `/space/location-publish`。
- 页面严格复用 E06-S01 至 E06-S05 的服务端权威链：按 `space:model:validate/publish/rollback` 分权，保留不确定失败期间的稳定幂等键，409/422 失败关闭，历史回退创建新版本和新尝试而不改写旧证据。桌面 1440 px 与手机 390 px 实际浏览器检查均无横向溢出，四阶段流程完整可见。
- 新增只读 `GET /api/space/design/v1/sites/{siteId}/publish-attempts`，聚合发布尝试、版本、Job、未解决对账问题和 HistoricalRepublish 血缘，带 Tenant/内部主体/Site 范围、受保护游标、标准 Problem Details 和读审计；OpenAPI 操作数为 111，C#/TypeScript SDK 已同步并通过漂移检查，无 Migration。
- 门禁：Space Unit 462/462；CP6.Tests 2804 passed / 17 environment-gated skipped；默认 Space Integration 263 passed / 94 SQL-gated skipped；前端 130 files / 698 tests；类型检查、生产构建、WebApi/C# SDK Release、SDK drift、桌面/手机视觉和 diff 检查通过。合并后清理 26 个可重建目录、24,325 个文件、1,409,759,827 bytes（约 1.313 GiB）。完整证据见 `docs/space/reports/e06-s06-publish-management-ui-development.md`。
- E06 本地开发主链现已有管理入口，但仍不是生产签收：生产等价 WMS 外部写入/恢复/告警/人工对账演练、正式 CAD Provider 与组织授权 DWG/DXF 黄金集、E03-S05 权威 Match Artifact 写入链，以及 Beta/GA 的跨职能、容量、SLO、灾备和安全证据仍未完成。

## E06-S05 历史版本重新发布回退开发切片（2026-08-08）

- 在集成基线 `bf9c07d8` 上以功能提交 `ea16ce67`、no-ff 提交 `85e039f8` 进入 `integration/space-v1-20260730`；`main` 未修改。新增 `space:model:rollback`、创建/查询历史重新发布 API、OpenAPI 及 C#/TypeScript SDK，OpenAPI 操作数为 110。
- 回退不是改写旧版本：系统冻结不可变 HistoricalRepublish 证据，把 `Production + Superseded` 历史快照克隆成新生产候选，保留 LogicalId 并建立 BasedOn/CloneOperation 血缘；再按当前规则与当前 WMS 能力校验，创建新的 PublishPlan、PublishAttempt、Publish Job 和追加式审计链。历史版本、旧计划、旧尝试和旧审计均不修改或删除。
- `HistoricalRepublish` 后台 Job 按克隆、校验、排队三步执行，复用 E06-S04 的租约、超时、退避、恢复和对账。旧生产指针、活动草稿/发布、校验阻断、能力变化和计划漂移都失败关闭；新尝试完成前当前 Published 保持有效。运行态激活再次核验持久化 Attempt/Plan 与原申请人，允许系统 Worker 执行但防止内部身份或计划替换。
- Migration `20260807170204` 使用强 Tenant 复合外键、唯一幂等约束、不可变证据保护和前向修复 Down；幂等 SQL 已生成。门禁：Space Unit 462/462、CP6.Tests 2803 passed / 17 skipped、默认 Space Integration 261 passed / 94 SQL-gated skipped、发布真实 SQL 3/3（其中历史重新发布 1/1）；完整 solution Release（含 Desktop/Android 双架构 AOT）0 warning / 0 error，EF/SDK/TypeScript/Web/diff 门禁通过。证据见 `docs/space/reports/e06-s05-historical-republish-development.md`。
- 本卡没有实现 E06-S06 发布管理 UI。生产等价 WMS 演练、正式 CAD Provider/授权黄金集、E03-S05 权威 Match Artifact 写入链及 Beta/GA 证据仍未完成；下一张为 E06-S06。

## E06-S04 发布队列与恢复开发切片（2026-08-07）

- 在集成基线 `0c416d44` 上以功能提交 `0f1ee6a9`、no-ff 提交 `0c1e75ff` 进入 `integration/space-v1-20260730`；`main` 未修改。E06-S03 请求内 Saga 已改为持久化 Publish/Reconcile 作业，WebApi Hosted Worker 按 Tenant 执行，租约心跳使用独立 SpaceContext。
- PublishAttempt/Batch 冻结 Job、请求、步骤、退避和批次恢复信息。30 分钟步骤超时、Job Ledger 指数退避、显式人工 retry、未解决问题优先 Reconcile，共同支持进程退出、WMS 超时、部分结果和运行态结果不确定后的安全恢复；恢复完成前旧 Published 指针保持不变。
- 新增哈希链式追加审计，区分失败观察、重试调度、人工介入、对账请求/解决和最终完成；审计事件数据库禁止更新/删除。Migration `20260807144532` 对旧活动发布失败关闭，幂等脚本双执行通过。人工 retry API、OpenAPI 和 C#/TypeScript SDK 已同步，OpenAPI path 数为 108。
- 门禁：Space Unit 458/458、API/权限/OpenAPI 聚焦 59/59、CP6.Tests 2799 passed / 17 skipped、默认 Space Integration 259 passed / 93 SQL-gated skipped、E06-S04 真实 SQL 4/4；完整 solution Release（含 Desktop/Android AOT）0 error / 7 条既有 warning，EF/SDK/diff 门禁通过。证据见 `docs/space/reports/e06-s04-publish-recovery-development.md`。
- 本卡没有实现 E06-S05 历史再发布回退或 E06-S06 管理 UI。生产等价 WMS 演练、正式 CAD Provider/授权黄金集、E03-S05 权威 Match Artifact 写入链及 Beta/GA 证据仍未完成；下一张为 E06-S05。

## E06-S03 仓库级发布编排开发切片（2026-08-07）

- 在集成基线 `0bde7bc9` 上以功能提交 `48082680`、no-ff 提交 `5b9b95ab` 进入 `integration/space-v1-20260730`；`main` 未修改。新增创建/读取发布尝试 API、`space:model:publish` 权限、写/读审计、OpenAPI 及 C#/TypeScript SDK。
- 发布入口重新构建并验证 E06-S02 权威计划，绑定 Published 指针、ValidationRun、PlanHash、ContentRevision 和实时 WMS 能力。Serializable 仓库槽位事务持久化不可变 PublishPlan，同租户/Site 只允许一个活动发布；同幂等键同请求稳定重放，同键异请求冲突。
- 请求内 Saga 依次执行 WMS 预检、稳定分批 apply、异常状态查询、回执保存和逐项回读。外部写入可能开始后不随 HTTP 取消丢失证据。只有全部 WMS 回读一致，才在 SpaceContext/CP6Context 同一 SQL 事务中物化运行态、校验投影哈希并原子切换 Published 指针。
- 零影响预检失败退回 Ready 并释放槽位；部分/未知/矛盾回执、回读不一致或运行态失败保留旧 Published，将目标置为 ReconciliationRequired 并保存问题与证据。新增 6 张发布/回执/对账/运行态表，Migration `20260807135544` 的 Down 失败关闭。
- 门禁：领域聚焦 4/4、API/权限/审计/OpenAPI 58/58、真实 SQL 1/1 fact（覆盖成功激活和部分 WMS 对账）、Space Unit 452/452、CP6.Tests 2798 passed / 17 skipped、默认 Space Integration 259 passed / 90 SQL-gated skipped；Infrastructure/WebApi Release 0 warning/0 error，完整 solution 双架构 AOT 0 error / 10 条既有 warning，EF/SDK/幂等 SQL/diff 门禁通过。合并后清理 36 个可重建目录、6,566 个文件、1,625,959,672 bytes（约 1.514 GiB）。证据见 `docs/space/reports/e06-s03-publish-orchestration-development.md`。
- 本卡没有实现 E06-S04 队列/超时/重试/人工对账审计、E06-S05 历史再发布回退或 E06-S06 UI。生产 Hosted Worker、正式 CAD Provider/授权黄金集、E03-S04/S05 权威匹配写入链及 Beta/GA 证据仍未完成；下一张为 E06-S04。

## E06-S02 版本差异与影响预览开发切片（2026-08-07）

- 在集成基线 `05c1df86` 上以功能提交 `a174f7cc`、no-ff 提交 `5bd2c616` 进入 `integration/space-v1-20260730`；`main` 未修改。新增 `GET /api/space/design/v1/versions/{versionId}/publish-preview`、`space:model:read`、读审计、标准 Problem Details、OpenAPI 及 C#/TypeScript SDK。
- 预览只读取服务端权威目标版本、当前 Published 指针、匹配的终态 ValidationRun 和实时 WMS 能力；按 LogicalId 稳定生成 Create/UpdateMaster/UpdateGeometryOnly/Disable/Restore/NoOp、WMS 影响与阻断项。稳定 PlanHash 绑定版本、ContentHash、ValidationRun、规则/适配器/能力和全部有序计划项；Location 改码失败关闭，已采纳库位不重复创建。
- 支持楼层/对象/动作/影响/NoOp 筛选及绑定计划和筛选条件的受保护游标。`Publishable` 只在 Passed、Ready、零校验阻断且零计划阻断时为 true。本卡无 WMS 调用、无发布指针切换、无 PublishPlan 持久化、无 Migration；这些属于 E06-S03 及后续卡。
- E06-S01 来源规则同时前向修正：Editor/Template 内建来源的合法终态 Ready 可参与发布；DWG/DXF 等文件来源继续要求 PreviewReady/Imported。门禁：引擎聚焦 17/17、API/权限/OpenAPI 55/55、真实 SQL 3/3、Space Unit 448/448、CP6.Tests 2794 passed / 17 skipped、默认 Space Integration 259 passed / 89 SQL-gated skipped、完整 solution 双架构 AOT Release 0 warning/0 error，EF/SDK/diff 门禁通过。合并后清理 36 个可重建目录、6,578 个文件、1,621,278,419 bytes（约 1.51 GiB）。证据见 `docs/space/reports/e06-s02-publish-preview-development.md`。
- 这不是完整 E06/Beta/GA 签收。下一张为 E06-S03 仓库级发布编排：必须持久化不可变计划并以可恢复 Saga 执行，只有 WMS 成功并回读验证后才能激活运行态；生产 Hosted Worker、正式 CAD Provider/授权黄金集、E03-S04/S05 权威匹配写入链等缺口仍保留。

## E06-S01 版本权威校验引擎开发切片（2026-08-07）

- 在集成基线 `022cb937` 上以功能提交 `c17242c3`、no-ff 提交 `76c70230` 进入 `integration/space-v1-20260730`；`main` 未修改。校验只读取服务端权威 ModelVersion 快照，冻结 ContentRevision/Hash、规则集、WMS Adapter/CapabilityHash、Job 和 Correlation，不接受客户端自报校验结果。
- 新增 ValidationRun、统一 Issue 血缘和 Validate Job Processor；覆盖编码、几何、层级、绑定、来源、AI provenance、已发布身份冻结及既有问题。无 Blocking 时 Passed/Ready，有 Blocking 时 Blocked/Draft，权威输入或能力漂移时 Failed/Draft。相同输入在 SQL transaction app lock 与唯一索引下只生成一个 Run/Job；发布态在复用前失败关闭。
- 新增 POST 创建/复用校验和 GET 查询接口、`space:model:validate` 权限、写/读审计、OpenAPI 及 C#/TypeScript SDK。Migration `20260807105256` 与幂等 SQL 双执行通过，Down 失败关闭以保留审计证据。
- 门禁：校验引擎 9/9、API/权限/审计/OpenAPI 74/74、Space Unit 440/440、CP6.Tests 2793 passed / 17 skipped、默认 Space Integration 259 passed / 86 SQL-gated skipped、E06-S01 真实 SQL 3/3；完整 solution Release 0 warning/0 error，EF/SDK/幂等 SQL/diff 门禁通过。合并后清理 36 个可重建目录、7,159 个文件、1,974,302,339 bytes（约 1.839 GiB）。证据见 `docs/space/reports/e06-s01-validation-engine-development.md`。
- 这不是完整 E06/Beta/GA 签收。E06-S02～S06、生产 Hosted Worker、E03-S04 权威 Match Artifact/E03-S05 写入链，以及正式 CAD Provider/授权黄金集仍未完成；下一张可独立推进 E06-S02 版本差异与影响预览 API。

## E02-S08 CAD 解析作业安全开发切片（2026-08-06）

- E02-S08 已以功能提交 `20ade7e7`、证据提交 `29667831` 和 no-ff 提交 `feaf29fb` 进入 `integration/space-v1-20260730`；`main` 未修改。新增 CAD Source 上传、CadParse 排队/查询/取消/显式重试、Artifact 持久化和 PreviewReady 收口，全部复用现有 Job Ledger、Attempt、Step、租约与 checkpoint。
- 排队/运行期间 Source 保持 Ready；取消、Provider 失败或进程中断不会留下 Parsing 污染。只有 CadIr、LayerInventory、PreviewSet 三类 Artifact 的引用、大小和 SHA 全部复验通过，最终事务才执行 Ready → Parsing → PreviewReady；全路径不写 Draft。
- 同 Job 重放复用自身 Artifact；显式 Retry 只在输入哈希和 `space-cad-parse-v1` processor version 相同时复用直接父 Job checkpoint。SQL Server 同一 Tenant/Source 以 transaction-owned application lock 串行化启动和重试，并验证双连接同键只产生一个 Retry Job。生产 Provider 默认失败关闭，未配置文件存储时只在实际执行步骤时失败，不阻止 WebApi composition。
- 门禁：Space Unit 431/431；默认 Space Integration 259 passed / 83 SQL-gated skipped；本卡内存 4/4、processor 5/5、controller 3/3、权限 27/27；KOUSQLSERVER 血缘与跨 Retry checkpoint 各 1/1；CP6.Tests 2788 passed / 17 environment-gated skipped；Release build、EF 无漂移通过。完整真实 SQL 全量曾因长时间无输出人工终止，不记为通过。
- 这是开发闭环，不是正式 CAD 签收。仍缺生产原生 DWG/DXF Provider、组织有权使用的黄金集、production Worker host 自动接线和真实大文件/故障/性能证据；合成 DXF 不能替代发布门禁。合并后清理 21 个可重建目录、996 个文件、623,921,427 bytes（约 0.581 GiB）。证据见 `docs/space/reports/e02-s08-cad-parse-job-safety-development.md`。

## Version Clone 必填字段前向修复（2026-08-06）

- 在集成检查点 `90e51a2e` 上以功能提交 `0564afad`、no-ff 提交 `01eba1b7` 修复 Published → Draft Clone；`main` 未修改。
- 根因为手写快照 SQL 漏复制 Zone/Aisle/Rack 非空 `Name`，导致三类父记录插入失败并连锁触发 RackLevel/Location 外键错误；Rack 的可空 `RackType` 也存在静默丢失。该问题已在 `ac9c977c` 独立基线复现，确认不由 E13-S17 retention Migration 引入。
- 修复仅补齐 `Name` 与 `RackType` 列映射，无 Schema/Migration 变化；回归覆盖不同于编码的名称、非空 RackType、RowId 重映射、LogicalId 和层级关系保真。
- 门禁：新增回归修复前 1/1 失败、修复后 1/1 通过；Version Clone 7/7、Space Unit 430/430、Space Integration + KOUSQLSERVER 336/336 且 0 skipped；Unit/Integration Release build 均 0 warning / 0 error，diff check 通过。证据见 `docs/space/reports/version-clone-required-fields-forward-fix.md`。
- 远端集成已推进到 `08e3fe40` 并确认祖先链；临时功能分支已在本地/远端删除。清理 16 个可重建 `bin/obj` 目录，回收 513,840,161 bytes（约 0.479 GiB）。

## E13-S17 迁移、前向修复与保留清理（2026-08-06）

- 在 `ac9c977c` 基线上以功能提交 `12db5531`、no-ff 提交 `e7720df4` 进入 `integration/space-v1-20260730`；`main` 未修改。
- 新增 Tenant 级 `AiRetentionCleanup` Job、严格每日冻结 payload、5 次安全重试、Step checkpoint 与 SQL Server transaction-owned `sp_getapplock`。外部主体和非专用 service principal 在排队前失败关闭，公共 HTTP 未暴露清理入口。
- Draft/Failed/Abandoned 的非 current 终态 Run 默认 90 天后净化大载荷；Usage 至少 365 天后逻辑归档；Staging 清空临时 JSON 后软删除。Published/Superseded/Publishing/Reconciliation、有效保留锁、Decision、Locked Fact、CommandBatch、预算账本和审计历史不清理。
- Migration `20260806160931` 只新增 5 个 nullable 列与 4 个索引；幂等 SQL 连续执行两次通过。`Down` 以 `THROW 51017` 禁止破坏性回滚，失败必须通过更高版本 forward-fix Migration 修复。
- 门禁：E13-S17 unit 6/6、内存/迁移 4/4、KOUSQLSERVER 3/3、Space Unit 430/430、默认 Integration 255 passed / 81 SQL-gated skipped、Release build 0 error、EF 无漂移、diff check 通过。完整证据见 `docs/space/reports/e13-s17-ai-retention-forward-fix.md`。
- 全量真实 SQL 的两个 Version Clone 失败已在起始提交独立复现，属于既有 clone SQL 缺少 Zone/Aisle/Rack `Name` 的基线问题；随后已由 `0564afad` / `01eba1b7` 修复并复验 336/336。E13-S14/S15/S19 仍等待外部证据；E13-S18 依赖 S15，不能提前签收。
- 远端集成已推进到 `1659b333` 并确认包含功能提交；临时功能分支已在本地/远端删除。清理 16 个可重建 `bin/obj` 目录，回收 523,868,809 bytes（约 0.488 GiB）。

## E13-S13 外部用户拒绝与数据外发门禁（2026-08-06）

- 在 E13-S11 后续集成基线 `d2a96be4` 上完成功能提交 `37bf5c37`，并以 no-ff 提交 `e1682efc` 集成到 `integration/space-v1-20260730`；`main` 未修改。
- Provider Gateway 在策略、配额和 Provider 前再次拒绝外部主体并要求有效内部 Tenant/Actor。External Provider 发送前执行冻结 JSON 字段白名单和最小化 HMAC Token 语法门禁；原始名称、任意 Prompt、路径、URL、额外字段或非白名单锁定事实以 `SPACE_AI_OUTBOUND_PAYLOAD_DENIED/403` 失败关闭。
- 4 个 AI 控制器的 16 个端点均有显式审计元数据，7 个 GET 均启用读审计。Customer、Supplier、3PL × 16 操作形成 48 条稳定 `SPACE_EXTERNAL_SUBJECT_DENIED/403` 断言，并证明拒绝发生在数据访问和写入前。
- 验证：Space Unit 424/424，Provider/最小化 34/34，外部主体与 AI 管理 8/8，审计/OpenAPI/权限 87/87，非 SQL 管理/注册/矩阵 10/10，KOUSQLSERVER Apply/恢复/配额/外部矩阵 21/21；Application Debug/Release 0 warning/0 error，WebApi Release 仅有 3 条既存 Core nullable warning。证据见 `docs/space/reports/e13-s13-external-ai-security.md`。
- CSO 范围审计没有确认当前可利用漏洞：生产仍无 Gateway 调用方、External Provider 注册为空且配额失败关闭。本卡是未来接入真实 Provider 前的安全封口，不是网络端到端签收。验证后清理 28 个可重建 `bin/obj` 目录，回收 1,062,306,204 bytes（约 0.989 GiB）；下一张建议卡为不依赖外部 CAD/Provider 的 E13-S17 迁移、前向修复与保留清理。

## E13-S11 Generation Run 恢复产品化开发切片（2026-08-06）

- 在 E13-S10 集成状态基线 `c1efea2b` 上完成功能提交 `dcbbfca8`、证据提交 `c695850f`，并以 no-ff 提交 `d3c2da75` 集成到 `integration/space-v1-20260730`。
- 新增取消、同输入安全重试、废弃、权威 Apply 结果对账、Failed/Stale replacement Run 与 RuleOnly 降级 API；全部 mutation 要求内部主体、租户/Site、精确 rowversion、幂等键和审计。OpenAPI、C#/TypeScript SDK 与前端审核面板已同步。
- 取消在 Worker 安全点确认，不拆分 S10 原子 Apply；同输入重试只允许 Transient/Resource/Bug 且沿用同一 Job/Run/检查点/ApplyPlan。对账只信任匹配当前 revision、RunId 和 ApplyPlanHash 的已提交 CommandBatch，不根据 Job 文本猜测成功。
- Failed/Stale 恢复创建新 Run/BuildScene Job 并保留 basedOnRunId、冻结输入、Decision 与审计；旧 Run 不原地 rebase。Failed current 源先在同一事务中退役再插入 replacement；同键并发锁内优先重放，避免旧 rowversion 误报 409。只有 BuildScene Provider 不可用才提示 RuleOnly。
- 门禁：WebApi build 0 warning/0 error、状态机与分类 42/42、OpenAPI/权限 52/52、AI Apply/Recovery 真实 SQL 14/14、前端聚焦 2 files/6 tests、前端全量 129 files/695 tests、type-check、production build、SDK 生成与 `git diff --check` 全部通过。完整证据见 `docs/space/reports/e13-s11-generation-run-recovery.md`。
- 生产默认 BuildScene executor 仍失败关闭；真实全阶段 `LoadLockedFacts` 自动接线、异源几何建议继承与人工确认、外部 Provider、授权真实 CAD、正式黄金集、性能/试点和发布证据仍未完成。已清理 34 个可重建目录并回收 1,008,090,267 bytes（约 0.939 GiB）；`main` 未修改。下一张建议卡为 E13-S13 外部用户拒绝与数据外发门禁，E13-S17 迁移/保留清理也已独立解除依赖。

## E13-S10 Staging 与原子 AI Apply 开发切片（2026-08-06）

- 在 E13-S09 集成状态基线 `b663c4ae` 上完成功能提交 `43dc5534`、既有审核基线更新纠偏 `fbc59fb3`、证据提交 `5be724cf`，并以 no-ff 提交 `0c587d4c` 集成到 `integration/space-v1-20260730`。
- 新增 `Space_GenerationStagingElement`、冻结 ApplyPlanHash、ApplyGeneration Job/Worker、Run 状态查询和原子 Apply API。POST 同步重验 ContentRevision、Run rowversion、ReviewEtag、权限、租户/Site 与幂等键；Worker 在短 SQL 事务内按 Run→Version→Model 加锁并再次验证 Proposal/Issue/Revision/唯一/引用/边界/碰撞。
- Added 提案创建 Zone/Aisle/Rack/RackLevel/Location/Element；Modified/Unchanged 按同类型、同 Floor LogicalId 原位更新。Rack 派生项按确定性 ID 复用/补齐/停用，WMS 绑定 Location 不可被派生缩减移除；跨类型、跨楼层和资产库 Element 冲突失败关闭。CommandRecord 保存权威 before/after。
- Queue 使用 Serializable 与租户 + Run 的 transaction-scoped `sp_getapplock`；同键双连接只产生一个 Job/幂等记录。成功只增加一个 Floor Revision 与 ContentRevision；故障注入、Stale、409 和校验失败均保持 Draft 零部分写入，Published/WMS/设备状态不变。
- 门禁：E13-S10 真实 SQL 7/7、Space Unit 413/413、默认 Space Integration 248 passed / 71 SQL-gated skipped、CP6.Client 71/71、CP6.Tests 2783 passed / 17 environment-gated skipped、前端 129 files / 694 tests；完整 solution、type-check、production build、OpenAPI/C#/TypeScript SDK drift、EF/Migration/幂等 SQL drift 与 `git diff --check` 通过。完整证据见 `docs/space/reports/e13-s10-atomic-ai-apply.md`。
- 下一张独立卡是 E13-S11 取消、重试、降级和 Stale 恢复产品化。真实 Worker `LoadLockedFacts` 自动接线、异源几何建议继承、外部 Provider、授权真实 CAD、正式黄金集和发布晋级证据仍是独立缺口。功能分支已远端备份，前端依赖清理回收约 0.31 GB；`main` 未修改。

## E13-S09 决策与人工锁定修正开发切片（2026-08-05）

- 在 E13-S08 集成状态基线 `e469c6ca` 上完成功能提交 `c87289f2`、证据提交 `382d5722`，并以 no-ff 提交 `396ee38b` 集成到 `integration/space-v1-20260730`：交付追加式 Accept/Reject/Modify 决策、单条/批量 API、权威审核读取、问题解决血缘、人工锁定事实、Migration、OpenAPI/C#/TypeScript SDK 和实时前端决策面板。
- 单条写入同时复核 Proposal rowversion 与 ReviewEtag；受保护游标绑定租户、Run、审核状态和筛选，Serializable 事务原子提交 Proposal、Decision、Issue、审核完成时间和 24 小时幂等记录。批量上限 1,000，批量 Accept 默认关闭；服务端始终重验资格。
- Modify 仅允许 RFC 6902 `replace` 精确白名单，单次可修改并锁定 1～32 个唯一字段；关系、业务枚举、理由码和评论均失败关闭校验。Decision、锁定事实和审计均追加写；`ReviewCompletedAtUtc` 只由服务端在全部有效提案已决策且无 Open Blocking Issue 时写入。
- 新增不可变 `Space_GenerationLockedFact`：相同 SourceHash 按 `SourceKey + ProposalType + FieldPath` 自动继承人工终值；不同 SourceHash 不猜测、不自动继承。内部审核 API 受 `space:model:review-ai` / `space:model:edit`、租户/Site 和 external-principal 闸保护；本切片不写 Draft、Published、WMS 或设备控制数据。
- 门禁：E13-S09 真实 SQL 3/3、Space Unit 413/413、Space Integration + KOUSQLSERVER 312/312、CP6.Tests 2779 passed / 17 environment-gated skipped、CAD 工具 25/25、前端聚焦 2 files / 7 tests、前端全量 128 files / 692 tests；type-check、production build、OpenAPI/C#/TypeScript SDK drift、EF/Migration/幂等 SQL drift 均通过。完整 solution Release 非增量单线程构建 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变。完整证据见 `docs/space/reports/e13-s09-proposal-decisions-human-locks.md`。
- 下一张独立卡是 E13-S10：Staging + 原子 Apply；任何冲突或校验失败都不得产生部分 Draft。真实 Worker 的 `LoadLockedFacts` 自动接线、异源几何“建议继承 + 人工确认”、外部 Provider、授权真实 CAD 和正式黄金集仍是独立缺口，不因本切片解除。`main` 未由本任务修改。
- 功能 tip `382d5722` 已确认进入远端集成祖先链，本地/远端临时功能分支均已删除；本工作树已清理 `node_modules`、前端 `dist` 及本轮 Release `bin/obj` 共 38 个目录，回收 1,522,981,317 bytes（约 1.42 GiB）。Debug 缓存、源码、锁文件、报告和远端 Git 历史保留。

## E13-S08 AI 提案审核工作台开发切片（2026-08-05）

- 在 E13-S07 集成基线 `d100a956` 上完成功能提交 `b1ab93f6`、证据提交 `5c2e0605`，并以 no-ff 提交 `fbf4596e` 集成到 `integration/space-v1-20260730`：新增只读完整 Draft 基线、审核工作区合同、确定性差异投影、受保护游标分页、批量选择资格预检、开发 CLI 和 Design V1 本地审核面板。
- 工作区绑定 Tenant、ModelVersion、Floor、ProposalSet SHA、Baseline SHA、ContentRevision/Hash、ReviewEtag 和 Workspace SHA；反序列化会深验只读/未写入标记、规范排序、唯一身份、摘要、差异和哈希。默认页长 50、最大 200，工作区最多 100,000 项，批量选择最多 1,000 项；游标同时绑定工作区与筛选，陈旧 ETag、未知/重复 ID、空或超量筛选失败关闭。
- 每项保留字段胜者/evidence、关系、问题、整数毫米几何和画布位置，并给出 Added/Modified/Unchanged、字段 Added/Removed/Changed、RackLevel/Location 容量 before/after。前端支持筛选、分页、详情、选择和画布定位；Model/Floor/Revision/Hash 过期时禁用定位与选择；没有 API mutation、Accept/Reject 写按钮或 Draft 写入口。
- Accept 资格只允许 Ready + High + 提案允许批量接受且无 Blocking；Reject 也只生成资格预检。所有预检固定 `RequiresServerRevalidation=true`、`DecisionWritten=false`、`DraftWritten=false`。开发 CLI 使用 32～128 byte HMAC 游标 key 并清零；生产必须复用既有 Data Protection 租户/主体/授权/时效绑定。
- Sample13 本轮生成 21 项（High 13 / Low 8、Ready 0 / NeedsReview 21 / Blocked 0），空楼层开发基线使 21 项诚实标为 Added；Workspace `2fc473e4...c5530efd`、58,645 bytes，重复生成字节一致。两页各 5 项且无交集；High+Rule+locatable 返回 13；Accept 预检 13 项全部因需要单项审核而不可用，Reject 预检 21/21 可选，两者均没有写入。空基线不冒充真实 Draft 对比，Modified/Unchanged 由单元测试覆盖。
- 门禁：E13-S08 后端 4/4、Space Unit 401/401、CAD 工具 25/25、前端聚焦 4/4、前端全量 127 files / 689 tests、type-check 和 production build 通过；完整 solution Release 非增量单线程、禁用节点复用/共享编译构建 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变；合并态后端 4/4、CAD 25/25、前端 4/4 和 type-check 复验通过。完整证据见 `docs/space/reports/e13-s08-proposal-review-workbench-development.md`。
- 这是开发切片，不是正式生产签收：仍缺 Proposal/Review 持久化、Migration、租户授权/审计、公共 API/OpenAPI/SDK、权威 Draft 基线服务、真实项目性能/差异证据及 Worker/Run/Artifact 接线；E13-S05～S07 正式缺口不解除。下一张独立卡是 E13-S09：追加式单条/批量 Decision、rowversion/ReviewEtag 并发控制、服务端资格复验、补丁白名单和审核完成状态；仍不得提前 Apply，High 自动批量接受继续关闭。`main` 未由本任务修改。
- 功能 tip `5c2e0605` 已确认进入远端集成祖先链，本地/远端临时功能分支均已删除；本工作树的 `node_modules`、前端 `dist`、`tmp/e13-s08`（含开发 HMAC key）及本轮 Release/AOT `bin/obj` 已清理，共回收 1,653,860,578 bytes（约 1.54 GiB）。Debug 缓存、源码、锁文件、Schema、报告和远端 Git 历史保留。

## E13-S07 规则/AI 融合与确定性生成开发切片（2026-08-05）

- 在 E13-S06 集成基线 `44c87a26` 上完成功能提交 `8be8b20f`、证据提交 `d81119f5`，并以 no-ff 提交 `7d5c8aa0` 集成到 `integration/space-v1-20260730`：新增 `IWarehouseDraftSynthesizer`、只读提案合同、确定性合成器和 RFC 4122 UUIDv5 身份生成器。
- 合成器重新核验 Provider Output Canonical SHA，并绑定 ModelVersion/RuleVersion、Tenant/Floor、Source/Transform、Semantic Preview、Provider Input、local Source Map 和 locked-fact 快照；任一身份或快照错配整体失败，不返回部分提案。反序列化提案还会深验规范顺序、证据胜者、关系、LogicalId、货架派生、摘要和自身 SHA。
- 字段固定 `HumanLocked > DeterministicRule > AI > TemplateDefault`：锁定/规则/AI 冲突保留双方证据与稳定问题码；强规则保留 High，软规则冲突降为 Medium/Low。AI 只补 allowlisted 语义属性；无确定性规则几何的 AI 建议、类型不兼容锁定属性、未解析父目标和父关系环均 Blocking，不能创建隐式对象。
- 提案几何只能来自 E02-S06 的整数毫米 `CadIrDeterministicRule`；对象、RackLevel、Location 分别用 ModelVersion+SourceHash+SourceKey、Rack+层号、Rack+层/列/深位生成 UUIDv5。货架方案固定 HumanLocked > ExcelMapping > ExplicitSelected；缺方案报 `SPACE_RACK_PROFILE_REQUIRED`，不使用隐式尺寸。
- 样例 13：22 个特征/21 条 Local 建议生成 21 个唯一只读提案，High 13 / Medium 0 / Low 8；8 Rack 按显式开发方案派生 24 层、192 库位，0 Blocking；ProposalSet `fba6c44c...cf31a288`、40,424 bytes，重复运行字节一致；全部 geometrySource=Rule，external=false、draft=false、readyForApply=false。
- 门禁：E13-S07 聚焦 10/10、Space Unit 397/397、CAD 工具 25/25、默认 DI 1/1；完整 solution Release 非增量单线程、禁用节点复用/共享编译构建 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变；格式与差异检查通过。无数据库、Migration、公开 API、前端、OpenAPI 或 SDK 变化。
- 这是开发切片，不是正式 E13-S07 生产签收：仍缺持久化/授权/版本冻结的 RackGenerationProfile 与 Excel/人工方案选择链、现有编码服务的 Application 层只读纯预检端口、完整 Floor/Draft 边界/碰撞/父归属/现有码冲突证据，以及 Worker/Run Artifact/审计接线。E13-S05/S06 外部 Provider 正式门禁也不因此解除。完整证据见 `docs/space/reports/e13-s07-rule-ai-fusion-development.md`。
- 当前提案合同可作为 E13-S08 分页、差异预览和审查工作台的只读输入；E13-S08 不得写 Draft 或绕过上述正式缺口。`main` 未修改。

## E13-S06 Provider 输出 Schema 与不可信输入校验开发切片（2026-08-05）

- 在 E13-S05 集成基线 `0c59cc34` 上完成功能提交 `7b95c29e`、证据提交 `1b297a91`，并以 no-ff 提交 `551ad8d4` 集成到 `integration/space-v1-20260730`：新增 `IWarehouseGenerationOutputValidator`、确定性验证器和带 Canonical SHA-256 的 `ValidatedSemanticResult`。
- 原始 Canonical JSON 在反序列化前执行 64 MiB/深度/严格 JSON、必填/未知/重复字段、字符串枚举、非负 Usage、0～1 decimal、数组上限和 C0/C1 控制字符门禁；权威输出 Schema 同步收紧安全字符串并表达五类专属属性组合。
- typed 语义门禁拒绝未知/重复 Suggestion SourceKey、自引用/未知/重复/超量关系、非法枚举/范围、空或重复 Evidence、未知 Diagnostic SourceKey 和 Zone/Rack/Door/Dock/StaticEquipment 属性错配。成功输出生成稳定 Canonical SHA；失败统一为非重试 `SPACE_AI_OUTPUT_INVALID`/502，只暴露稳定违规码。
- `SpaceAiGenerationGateway` 在 Provider 返回后、配额租约释放前强制验证，失败不返回部分对象且租约正常释放；默认 DI 注册验证器，但 Provider Registry 继续为空。开发 CLI 可验证原始 Canonical 文件，成功只打印 Schema/模型/计数/SHA，读取缓冲区清零；无网络、持久化或 Draft 写入。
- 样例 13 的 22 个输入特征产生 21 条 Local 建议和 1 条诊断；原始文件 SHA `5e57cce3...6f7e23`，Canonical SHA `913e99b4...84767c`，独立重复运行字节一致。未知建议/关系引用为 0，39 个身份/哈希/SourceRef/属性值敏感候选命中 0，全部命令报告 external=false、draft=false。
- 恶意矩阵覆盖 9 类原始 JSON、16 类 typed 语义以及字节上限、稳定 SHA、Gateway 租约和 CLI 篡改。门禁：Provider/验证器/Gateway 55/55、Space Unit 387/387、CAD 工具 25/25、默认 DI 1/1；完整 solution Release 非增量单线程、禁用节点复用构建 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变；格式、Schema JSON 和差异检查通过。
- 这是开发切片，不是正式 E13-S06 端到端签收：仍依赖 E13-S05 首个外部适配器在传输层限流/限长、映射厂商原生响应并调用同一验证器，还需供应商/模型/区域/SecretReference/租户策略、真实非法响应故障注入和 Run/Artifact 审计。完整证据见 `docs/space/reports/e13-s06-provider-output-validation-development.md`。
- 功能 tip `1b297a91` 已确认进入远端集成祖先链，本地/远端临时功能分支均已删除；可重建的 `tmp/e13-s06` 共 7 个文件、71,099 bytes（包含开发 HMAC key）已删除，源码、Schema、报告和远端 Git 历史保留，`main` 未修改。

## E13-S05 Mock/本地 Provider 与故障降级开发切片（2026-08-05）

- 在 E13-S04 集成基线 `454c521c` 上完成功能提交 `e519942b`、证据提交 `e55b49aa`，并以 no-ff 提交 `6bb43fc5` 集成到 `integration/space-v1-20260730`：新增确定性、无网络的 Mock、本地启发式和可重试故障降级实现，三者统一通过既有 `IWarehouseGenerationProvider` SPI。
- Mock 按稳定 SourceKey 和上限生成固定类型/0.5 置信度建议；Local 只读取 E13-S04 允许的脱敏 Layer/Block 分类令牌，确定性识别 Rack/Aisle/Wall/Column/Door/Dock/StaticEquipment/Zone/Floor，未命中显式诊断，不读取原始 CAD、属性值、SourceRef、路径或租户身份。
- 只有 Unavailable、Timeout、RateLimited 可降级到 Local，并追加稳定 Warning；ContractViolation 和用户取消不降级。异常不携带端点、凭据、响应体或内部异常；建议、关系和诊断继续受合同上限约束。
- 开发 CLI 支持 `mock|local|fallback-local`，在反序列化前拒绝未知 Schema/WarehouseKind，明确拒绝 `external`；生产 Registry 未注册开发 Provider，也没有凭据、网络、Run/Usage 持久化或 Draft 写入。
- 样例 13 的 22 个最小化特征产生 Mock 22 条、Local 21 条以及三类降级各 21 条建议；5 份结果共 106 条建议，未知输入/关系引用与范围违规均为 0，38 个敏感候选命中 0。Mock、Local、Timeout 降级重复运行均字节一致，全部运行报告 external=false、draft=false。
- 门禁：Provider 实现 + SPI 27/27、Space Unit 359/359、CAD 工具 25/25；完整 solution Release 非增量单线程、禁用节点复用构建 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变。首轮恰逢 Visual Studio 更新导致旧 iOS 26.2 SDK 目录缺失；工作负载自动恢复到 iOS 26.5 后同一命令无代码修改通过。格式和差异检查通过，本切片无数据库、Migration、WebApi、前端、OpenAPI 或 SDK 变化。
- 这是开发切片，不是正式 E13-S05 完成：首个外部适配器仍等待供应商/合同、区域与数据驻留、端点别名、SecretReference、租户外发授权、生产输入输出校验和计费证据。下一独立切片可先做 E13-S06 本地不可信输出校验，但不能借此启用外部 Provider 或应用建议。完整证据见 `docs/space/reports/e13-s05-provider-development.md`。
- 功能 tip `e55b49aa` 已确认进入远端集成祖先链，本地/远端临时功能分支均已删除；可重建的 `tmp/e13-s05` 共 13 个文件、125,056 bytes（包含开发 HMAC key）已删除，源码、报告和远端 Git 历史保留，`main` 未修改。

## E13-S04 CAD IR 特征最小化与脱敏开发切片（2026-08-05）

- 在 E04-S05 集成基线 `f5f0c9e8` 上完成功能提交 `8fffdf07`、证据提交 `d635796e`，并以 no-ff 提交 `8bc1114d` 集成到 `integration/space-v1-20260730`：parsing-ready CAD Coordinate Preparation 现在可确定性投影为 `MetadataOnly` 或 `StructuredFeatures` Provider 输入，并生成独立 local-only SourceRef 映射。
- MetadataOnly 只发送 HMAC SourceKey/图层/块/属性/重复/映射提示令牌、实体枚举/计数、10 度角度桶和无量纲长宽比桶；坐标、关系和 object-level locked facts 均禁止。StructuredFeatures 才允许四位小数的 0～1 相对包围盒、有界重复关系和声明枚举锁定事实；绝对坐标、属性值、CAD Text、身份/文件/存储信息始终不发送。
- Run correlation 与全部可识别令牌按 Run、domain 和 32～128 byte HMAC key 隔离；Layer/Block 只暴露仓库领域白名单分类，其余原文只进入 HMAC。Provider 输入不含 Tenant/Site/ModelVersion/Run/File/Source/Floor/SourceRef/源哈希；local map 明确 `isLocalOnly=true` 并以 Provider 文件 SHA 和自身规范 SHA 防篡改。开发 CLI 从二进制 key 文件读取，使用后清零，不调用 Provider、不写 Draft。
- 合成样例 13 的 22 个 SourceRef 在 MetadataOnly 下形成 8 个统计特征，在 StructuredFeatures 下形成 22 个特征、12 个相对包围盒和 14 条关系；38 个非空敏感候选外发命中 0，越界包围盒 0。Provider SHA 分别为 `c5fbdcf2...7d697efa` 与 `164020fa...8bc2b65`，Structured Provider/local map 重复运行均字节一致。
- 门禁：minimizer + Provider SPI 聚焦 27/27，功能树与 no-ff 合并树的 Space Unit 350/350、CAD 工具 24/24；两棵树完整 solution Release 非增量单线程构建最终均为 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变。合并态首轮在第三方 Kotlin 协程程序集的 Android x64 AOT 汇编器处瞬时失败，关闭 build server、禁用节点复用后原强度重跑通过，未修改代码或关闭 AOT；格式、两份 AI Schema JSON、工件哈希/反序列化/应用验证和差异检查通过。本切片无前端、数据库、API 或 SDK 变化。
- 这是开发切片，不是正式 E13-S04 生产验收：仍需生产 E02-S03 CAD Artifact、Tenant/Run/Policy/SecretReference 绑定、Artifact 保留、权限/审计和授权真实 CAD 覆盖。E13-S05 才调用 Provider/实现降级，E13-S06 才校验不可信输出；本切片不应用建议。完整证据见 `docs/space/reports/e13-s04-cad-feature-minimization-development.md`。
- 功能 tip `d635796e` 已确认进入远端集成祖先链，本地/远端临时功能分支均已删除；可重建的 `tmp/e13-s04` 共 9 个文件、68,361 bytes（包含开发 HMAC key）已删除，源码、Schema、报告和远端历史保留，`main` 未修改。

## E04-S05 CAD 问题列表与画布定位开发切片（2026-08-05）

- 在 E03-S04 集成基线 `3300d01b` 上完成功能提交 `2ac9472f`、证据提交 `5114307e`，并以 no-ff 提交 `bd4ab90a` 集成到 `integration/space-v1-20260730`：E02-S07 的 CAD diagnostics、Low/Rejected proposals 和可选 E03-S04 Excel Unmatched/Conflict/Error 行现在形成确定性、只读的 CAD Review Workspace。
- Workspace 绑定 Tenant、ModelVersion、Floor、Diagnostic/可选 Match、编辑器修订/内容/快照、Previous Workspace 与自身 SHA。跨身份、旧修订、同修订不同内容哈希、篡改、重复身份和非法位置失败关闭；消失的稳定 TrackingKey 标为 Resolved，返回时重新 Open，不改写上游事实。
- Design V1 可本地加载开发工件，按状态、严重度、类型、关键字和可定位性筛选；点击后优先按 LogicalId、其次按精确 SourceRef 选中对象，并让底图、设计对象层与问题覆盖层共享 pan/zoom 自动居中。零面积 CAD 实体仍有 18px 可见锚点；过期工件禁用定位。
- 样例 13 产生 29 项 Open：Info 12 / Warning 17 / Blocking 0，25 项可定位、4 项不可定位，Workspace `3a296228...17288eb`，JSON `29ff0014...3f6eeb3`（34,843 bytes），两次运行字节完全一致且无 `null` 字段。Low+locatable 返回 8 项，Open+Warning+locatable 返回 17 项。
- 门禁：E04-S05 应用 5/5、前端聚焦 15/15、Space Unit 341/341、CAD 工具 23/23、前端全量 126 files / 685 tests、类型检查和生产构建通过；功能树与 no-ff 合并树的完整 solution Release 非增量单线程构建均为 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变。i18n 仍为既有 908 项缺失快照 key，本切片净新增 0。
- 这是开发切片，不是正式 E04-S05 验收：本地 JSON 导入不替代生产 CAD Artifact/Issue API、权限/审计、服务端权威签发、授权真实图纸或真实编辑器验收，也不写 Draft。E03-S05 的用户确认与幂等 Draft 写入仍受生产 CAD 链和并发修订门禁约束。证据见 `docs/space/reports/e04-s05-cad-review-workspace-development.md`。
- 功能 tip 已确认进入远端集成祖先链，本地/远端临时功能分支已删除；可重复生成的 `tmp/e04-s05` 与本工作树 `cp6.web/node_modules` 共清理 22,369 个文件、332,663,196 bytes（约 317.3 MiB），锁文件和源码保留，`main` 未修改。

## E03-S04 Excel/CAD 元素匹配开发切片（2026-08-04）

- 在 E02-S07 集成基线 `4d945b5d` 上完成功能提交 `2da39667`、证据提交 `02cdbcff`，并以 no-ff 提交 `b2a2320c` 集成到 `integration/space-v1-20260730`：E03-S03 规范化 Excel 货架行现在可与 CAD 语义提案和只读编辑器快照进行确定性匹配，显式给出 New/Update/Unchanged/Unmatched/Conflict/Error 及独立未匹配查询。
- 匹配只接受 CAD/编辑器 SourceRef 或受控货架码属性；多候选、来源不一致、跨楼层和多 Excel 行争用同一目标均失败为 Conflict。每行保留采用键、候选、差异、错误码、CAD 置信度、画布位置和独立证据 SHA；Tenant/ModelVersion/Floor、Excel 映射与工作簿投影、Semantic、Diagnostic、编辑器修订和顶层预览形成完整哈希链。
- 样例 13 的 10 行结果为 New 8 / Unmatched 1 / Error 1，8 条 New 全部可聚焦；Match Preview `c6ca3640...72a4c107`，JSON 文件 `369372e1...67acd951`（15,732 bytes），两次运行字节完全一致。未匹配、新建可定位和错误查询分别返回 1、8、1 条；因未匹配、错误和 Low CAD 候选，`CanConfirm=false`。
- 门禁：E03-S04 聚焦 8/8、E03-S03 预检回归 6/6、Space Unit 336/336、CAD 工具 23/23；功能树和 no-ff 合并树的完整 solution Release 非增量单线程构建均为 0 error / 10 条既有 warning，格式、Schema、类型验证、空字段省略和差异检查通过。
- 这是只读开发切片，不创建永久 LogicalId，不写 Draft/数据库/编辑器，不替代正式 CAD 适配器、生产 Artifact/持久化、权威编辑器快照服务、API/权限/审计、授权真实图纸或 UI 验收。E03-S05 继续负责用户确认和幂等 Draft 写入；生产链等待期间下一独立开发切片优先 E04-S05 问题/未匹配列表与画布聚焦。证据见 `docs/space/reports/e03-s04-excel-cad-matching-development.md`。

## E02-S07 CAD 语义证据与问题定位开发切片（2026-08-04）

- 在 E02-S06 集成基线 `68d59562` 上完成功能提交 `19b6c443`、证据提交 `2eee2081`，并以 no-ff 提交 `c792ea8c` 集成到 `integration/space-v1-20260730`：每个只读语义提案现在都带 SourceRef、采用规则、置信度分段、独立证据哈希和整数毫米画布位置；Mapping/Semantic 问题形成稳定、可筛选的空间索引。
- 诊断工件绑定 Tenant/Floor 及 Source、Transform、Inventory、Profile、Mapping、Semantic 全链 SHA-256；构建时重算语义链，错配与篡改失败关闭。Document/Layer/Block/Entity 四级定位显式区分可聚焦与不可聚焦，空图层保留 ID 但不伪造范围。
- 样例 13：Diagnostic Index `f0d18f95...17209448b`，JSON 文件 `aa04fc74...70eacdc0c`（46,892 bytes）；21 条提案证据为 High 13 / Review 0 / Low 8 / Rejected 0，21 条问题为 12 Info / 9 Warning / 0 Blocking，其中 17 条可聚焦、4 条真实空图层不可聚焦，重复运行字节完全相同。
- 门禁：E02-S07 聚焦 6/6、Space Unit 328/328、CAD 工具 23/23、合并后完整 solution Release 非增量单线程构建 0 error / 10 条既有 warning，受影响文件格式、Schema JSON 与差异检查通过。并行 Android AOT 曾瞬时崩溃，关闭残留构建节点后在不降低 AOT 强度的条件下通过。
- 这是开发切片而非正式 E02-S07 验收；尚无问题列表 UI/画布点击高亮、人工删除/合并/拆分、字段锁定或修正重放，也未写 Draft/数据库。正式验收仍等待授权原生 CAD 适配器、冻结 Worker、独立真实黄金集、生产持久化/API/权限/审计与精度/覆盖率证据。下一开发主线优先 E03-S04 Excel 行与 CAD/编辑器元素候选匹配，随后 E04-S05 消费本索引实现问题列表和画布定位。证据见 `docs/space/reports/e02-s07-cad-semantic-diagnostics-development.md`。

## E02-S06 CAD 基础语义解析器开发切片（2026-08-03）

- 在 E02-S05 集成基线 `b3c45a8f` 上完成功能提交 `c8e2ae87`、证据提交 `be32c9a7`，并以 no-ff 提交 `fdb210b4` 集成到 `integration/space-v1-20260730`：Prepared IR、Inventory、封存 Profile 与 Mapping Preview 现在形成同租户、全哈希绑定的失败关闭链，输出确定性只读语义提案，不创建永久 LogicalId，不写 Draft。
- 每个对象保留临时 `previewObjectId`、SourceRef/图层/块/属性、目标类型、采用规则、默认高度/厚度、整数毫米规范几何、置信度与选择状态；统一区分 Element/Zone/Aisle/Rack，覆盖 Wall、Column、Door、Dock、Zone、Aisle、Rack。零长度、零面积和不支持图元显式 Rejected，不静默丢弃。
- Block 规则逐引用检查属性，命中时优先于 Layer 且不重复；无真实块轮廓时保留 BlockInstance 仿射变换、置信度封顶 0.69 并告警，不伪造货架尺寸。阈值固定为 `>=0.90` 自动选中、`0.70–0.89` Warning 待确认、`<0.70` 候选展示；必需来源只有拒绝几何时 Blocking。
- 样例 13：Semantic Preview `e398d192...befc866`，JSON 文件 `75845d12...7202ea`；22 源对象中 21 提案、13 AutoAccepted / 8 Candidate / 0 Rejected、13 Confirmable / 13 Selected、8 Info / 8 Warning / 0 Blocking，重复运行字节完全相同。
- 门禁：E02-S06 聚焦 6/6、20/20 合成 CAD 完成语义链、CAD 工具 23/23、Space Unit 322/322、完整 solution Release 非增量构建 0 error / 10 条既有 warning、格式/Schema/差异检查通过。证据见 `docs/space/reports/e02-s06-cad-semantic-development.md`。
- 这是开发切片而非正式 E02-S06 验收；仍等待授权原生适配器、冻结 Worker、正式黄金集、生产 Artifact/持久化、复杂块/曲线证据和受权 Draft Apply。等待期间可继续 E02-S07 开发侧问题定位与锁定修正预览，不得提前声称正式 CAD 验收。

## E02-S05 CAD 图层映射方案开发切片（2026-08-03）

- 在 E02-S04 集成基线 `f4b596f0` 上完成功能提交 `2736427c`、证据提交 `29118c19`，并以 no-ff 提交 `b6d58a1e` 集成到 `integration/space-v1-20260730`：新增 Definition SHA-256 封装的不可变 CAD Mapping Profile；System 方案无租户、租户侧只读，租户复制记录 System 基线，后续修改创建新版本。Tenant Profile 跨租户失败关闭。
- Layer/Block 规则支持 Exact、Glob、受限 NonBacktracking Regex 和 Block 属性条件，冻结目标语义、几何规则、默认高度/厚度、置信度、优先级和必需标记。逐层 Override 优先；同优先级/特异性多命中 Blocking；必需来源缺失或为空 Blocking；空/未知来源仍完整列出。
- Preview 绑定 Tenant、Profile/Version/Definition、Source、Inventory、源结构、Override、Reuse Key 和 Preview SHA-256；复用键排除 Floor/坐标 Transform，因此同一 CAD 换楼层仍复用，但不同租户/方案/来源/覆盖不会串用。新命令 `seal-dev-mapping-profile`、`preview-dev-mapping` 不写 Draft，无 Migration/API/外部 AI。
- 样例 13：Profile `732eef8a...de59d1`，Structure `9636bd72...0911ab`，Reuse `014cdc75...1c879b`，Preview `98a0a315...8009ca`；15 图层中 10 mapped / 5 unmapped，1/1 块 mapped，覆盖 21 个图层对象和 8 个块引用，4 Info / 1 Warning / 0 Blocking，可进入开发侧语义解析。
- 门禁：E02-S05 聚焦 12/12、20/20 合成 CAD 标准方案预览、CAD 工具 23/23、Space Unit 316/316、完整 solution Release 非增量构建 0 error / 10 条既有 warning、JSON/CLI/差异检查通过。证据见 `docs/space/reports/e02-s05-cad-mapping-development.md`。
- 这是开发切片而非正式 E02-S05 验收；仍等待授权原生适配器、正式持久化清单/方案、并发、API/权限/审计/UI 和真实图纸证据。E05-S01 已完成，等待期间可继续 E02-S06 开发侧只读语义提案，不得直接写 Draft。

## E02-S04 CAD 图层与块清单开发切片（2026-08-03）

- 在 E02-S03 集成基线 `01a59696` 上完成功能提交 `b77faf96`、证据提交 `324c8755`，并以 no-ff 提交 `be639d07` 集成到 `integration/space-v1-20260730`：CAD IR v1 向后兼容增加图层颜色、线型和可见性；开发 DXF 转换器保留完整 `TABLES/LAYER` 和空图层，未声明图层显式合成并产生 Warning，不再只列出有对象图层。
- 新增来源/坐标 Transform/Floor/Inventory SHA-256 绑定的确定性清单：图层对象/支持/不支持/类型/块引用/属性数与范围，块定义/XRef/引用/属性摘要，以及每个块引用的稳定 SourceRef、受控属性值和范围。非 Ready、Blocking、来源/楼层/范围或坐标元数据不一致均失败关闭，无 Migration/WebApi/Draft 写入。
- 图层、块和引用支持受限分页查询，覆盖名称/ID、显隐、图元类型、XRef、图层、块名和属性键值；单页最多 200。开发工具新增 `build-dev-inventory` 与 `query-dev-inventory`，合同见 `docs/space/contracts/cad/v1/inventory.schema.json`。
- 样例 13：Source `aa573f04...1fb106`，新 CAD IR `b6aa6501...614310`，Transform `b1223a8f...353cfba`，Inventory `63432958...9697a9`；F01 范围 `(0,-1200)～(36000,24000)` mm，15 图层/7 空层、1 块、8 个带属性块引用、22 supported 对象；`RACK_ID=R-01-01` 精确查询返回 `H:110`。
- 门禁：E02-S04 聚焦 10/10、20/20 合成 DXF 清单链、CAD 工具 22/22、Space Unit 304/304、完整 solution Release 非增量构建 0 error / 10 条既有 warning、JSON/CLI/差异检查通过。证据见 `docs/space/reports/e02-s04-cad-inventory-development.md`。
- 这是开发切片而非正式 E02-S04 验收；仍等待授权原生适配器、冻结 Worker、正式黄金集、生产 streaming/持久化/API/权限/UI 与真实复杂图纸证据。等待期间可继续 E02-S05 开发侧图层映射方案。

## E02-S03 CAD 坐标确认开发切片（2026-08-03）

- 在 E02-S02 开发 CAD IR 集成基线 `97d6871f` 上完成功能提交 `09b26b87`、证据提交 `d78b3b09`，并以 no-ff 提交 `7741da61` 集成到 `integration/space-v1-20260730`：分析阶段分别给出源单位范围和建议毫米范围；已识别单位仍必须明确确认，未知单位不猜测，确认记录绑定来源 SHA-256。
- 确认合同冻结源原点、目标 Floor 原点、逆时针 Z 旋转、Floor LogicalId/Code/Level/Elevation、边界与 `LOCAL_MM_Z_UP`。变换可纠正错误检测比例，点、半径、偏移和边界按 AwayFromZero 量化为整数毫米；普通图元保持 Identity，块引用复合实例矩阵；同一输入产生稳定 Transform SHA-256。
- 默认图纸单边 1 m～5 km；边界缺失、范围异常、未确认单位、错误来源哈希、非法楼层坐标系或超出楼层边界 50 mm 均失败关闭。DWG/DXF `SpaceModelSource` 缺少规范坐标元数据时不能进入 Parsing；既有 Excel/底图/编辑器路径不受影响，无 Migration。
- 20/20 合成 DXF 完成转换、分析、确认和楼层准备。样例 13 归属 F01，22 图元、0 问题，范围 `(0,-1200)～(36000,24000)` mm，Transform SHA-256 为 `b1223a8f...353cfba`。
- 门禁：E02-S03 聚焦 13/13、CAD 工具 20/20、Space Unit 294/294、完整 solution Release 0 error / 10 条既有 warning，最终 SDK 可访问增量构建 0 warning / 0 error，JSON/CLI/差异检查通过。证据见 `docs/space/reports/e02-s03-cad-coordinate-development.md`。
- 这是开发切片而非正式 E02-S03 验收；仍等待授权原生 DWG/DXF 适配器、冻结 Worker、正式黄金集和同租户/同版本持久化服务链。等待期间可继续 E02-S04 开发侧图层/块清单。

## E02-S02 CAD IR 开发契约（2026-08-02）

- 在合成 CAD 图纸集成基线 `08fe896a` 上完成第一段可执行 CAD IR 链路：功能提交 `89759cec`、验证文档 `8f3e9252`、no-ff 受控集成 `9e8cf4af`。Contracts 定义供应商中立的 CAD IR v1，Application 定义 `ICadConverter`、只写 streaming sink 接口和失败关闭契约验证器；WebApi、Draft 仓储和供应商 SDK 类型不跨越该边界。
- 新增开发命令 `convert-dev-ir`，可把 UTF-8/ASCII DXF 转换为确定性 JSON IR；验证精确来源 SHA-256、稳定 sourceRef、图层/块/图元计数、坐标边界和转换器身份，支持毫米/厘米/米/英寸/英尺归一化，未知单位 Blocking，不支持图元显式保留并报问题，XRef 原始路径不出边界。
- 20/20 合成图纸转换通过：130 个图层记录、23 个块、292 个图元，其中 278 个受支持、14 个不支持且全部有显式问题、缺失 sourceRef 为 0。样例 13 产生 8 层、1 块、22 图元，IR SHA-256 为 `f080ac0c...20a9ba`。
- 门禁：CAD 实验工具 19/19、CAD IR 契约聚焦 9/9、Space Unit 281/281、完整 solution Release 0 error / 10 条既有 warning、`git diff --check` 通过。证据见 `docs/space/reports/e02-s02-cad-ir-development-contract.md`。
- 这是 E02-S02 开发切片，不是正式验收：仍等待 E02-S01 的原生 DWG 适配器/商业授权、冻结隔离 Worker、独立正式黄金集和生产规模 streaming/压力证据；完成供应商选择后再接同一契约并进入 E02-S03。

## E02 合成开发 CAD 图纸包（2026-08-02）

- 新增 `docs/space/acceptance/development-v2.0.0`：20 份仓库内可重复生成的合成 DXF，L1～L5 各 4 份，覆盖 AC1009/AC1015/AC1021/AC1027/AC1032 文件头以及规则、多楼层、非正交、综合和噪声场景。
- 新命令 `generate-dev-corpus` 同步生成 SHA-256 清单、场景索引、最小期望答案、期望问题、Provider IR、图层映射和开发使用声明。全部资产不含真实客户、供应商、地址、人员、标题栏或设备序列号数据。
- CAD 实验工具测试 12/12；20/20 文件完整性、哈希、成对 DXF 行、EOF、唯一 Handle、五类布局与 DXF 文件头矩阵通过。
- 该数据包明确为 `DevelopmentSeed` 且 `countsTowardReleaseGate=false`：可推进解析、映射、问题、IR、UI 和回归开发，但不替代原生 DWG、ODA/APS 授权、冻结 Worker 和独立正式黄金集。证据见 `docs/space/reports/e02-synthetic-development-cad-corpus.md`。

## E12-S05 完成状态（2026-08-02）

- E12-S05 已完成实现、全量门禁、远端备份、no-ff 受控集成和临时资源清理：起始基线 `ad77540d`，功能提交 `dd505f6f`，集成提交 `c4b139ab`。内部规划人员可从 Ready/Succeeded/Production Isolated 场景下载 glTF 2.0 单文件 `.glb`。
- 导出在 Serializable 一致性快照中稳定排序楼层、区域、巷道、货架、货架层、库位和通用元素，总数据节点上限 50,000；同一快照字节与 SHA-256 确定。CP6 `LOCAL_MM_Z_UP` 毫米坐标固定转换为 glTF `+Y` 向上、米制坐标。
- 货架、可定位库位和盒体元素提供共享网格、面法线与材质；边界、多边形、中心线、货架层规格和稳定 LogicalId 进入 `extras.cp6`。这是一份低成本可视化交换包，不是 CAD authoring、DWG 回写或生产发布入口。
- 新增 1 个 GET API、1 个只读权限和四个五语页面词条；Design V1 从 83 增至 84 operations，C#/TypeScript SDK 已同步。响应带 no-store、nosniff、ETag、schema 与 SHA-256 头，且不含库存、人员、设备事件或历史任务运行态。
- 全量门禁：Space Unit 272、默认 Space Integration 247 passed / 63 SQL-gated skipped、CP6.Tests 最终复验 2777 passed / 17 environment-gated skipped、前端 123 files / 676 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning；生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。证据见 `docs/space/reports/e12-s05-standard-gltf-exchange.md`。
- 合并态 GLB 2/2、权限/API/OpenAPI 65/65、前端同树 9/9、双 EF 与 SDK 门禁通过。功能 tip 已进入远端集成祖先链，远端/本地功能分支和功能工作树已删除，释放 2,869,523,790 字节（约 2.672 GiB）。E12-S06 仍等待正式黄金样本、DWG SDK/供应商授权与可审计试验环境；`main` 未修改。

## E12-S04 完成状态（2026-08-02）

- E12-S04 已完成实现、全量门禁、远端备份和 no-ff 受控集成：起始基线 `6d13d7da`，功能提交 `7b919b4b`，文档 tip `a9298bad`，集成提交 `577168e3`。内部规划人员可固定 2～10 个不同生产隔离场景的同源仿真证据，人工指定基线，并查看距离、拥堵、容量、吞吐和成本的原值、差值及显式阈值风险。
- 比较强制同 Site/Model/基础 Published 版本、来源数据哈希、历史窗口、任务口径、仿真定义、几何、币种、费率和吞吐窗口；容量假设差异显式标记。系统不计算总分、不排名、不推荐，也不预选决策方案。
- 人工决策只允许 Selected/Deferred/RejectedAll 和必填理由；后续记录必须替代唯一当前链头。比较、风险和决策全部追加式/不可变，永不合并、写入或发布到生产。
- 新增 6 个 planning API、4 个权限、四张租户隔离证据表、EF Migration/增量幂等 SQL；Design V1 从 77 增至 83 operations，C#/TypeScript SDK 已同步。规划页新增跨分支比较矩阵、风险与决策历史。
- 47 个比较词条均有五语运行时种子，静态 i18n 欠账仍为既有 908 项，本卡净新增 0。
- 全量门禁：Space Unit 272、默认 Space Integration 245 passed / 63 SQL-gated skipped、CP6.Tests 2775 passed / 17 environment-gated skipped、前端 123 files / 674 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning；生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。证据见 `docs/space/reports/e12-s04-scenario-comparison-decision.md`。
- 合并树与功能 tip 一致，合并态引擎 4/4、服务 3/3、权限/合同/OpenAPI/五语 66/66、前端 10/10、类型检查、双 EF、SDK 和 TypeScript 门禁通过。功能 tip 已确认进入远端集成祖先链，功能工作树、临时依赖链接及本地/远端功能分支已删除，释放 2,520,628,564 字节（约 2.348 GiB）。下一张可独立实施 E12-S05“标准交换格式导出”，`main` 未修改。

## E12-S03 完成状态（2026-08-02）

- E12-S03 已完成实现、全量门禁、远端备份和受控集成：起始基线 `1650e8ba`、功能提交 `ab21aed4`、文档 tip `2cd1faed`、no-ff 集成 `f2d68897`。内部规划人员可在生产隔离场景中基于不可变脱敏历史数据集运行确定性规划仿真。
- 距离使用同层货架格口锚点直线距离并显式报告未知覆盖；拥堵按目的位置历史执行区间重叠；容量使用调用方声明任务数量单位；吞吐使用精确历史时长和固定时间桶；人工按 worker token 区间并集；成本只使用显式距离/人工/拥堵单价。
- 新增 3 个 planning API、2 个权限、两张不可变租户隔离证据表、EF Migration/增量幂等 SQL；Design V1 从 74 增至 77 operations，C#/TypeScript SDK 已同步。规划页可配置容量、时间桶、币种和单价，并展示五类 KPI、热点、结果哈希与无生产回写护栏。
- 41 个仿真词条均有五语运行时种子。静态 i18n 欠账仍为既有 908 项，本卡净新增 0。
- 全量门禁：Space Unit 268、默认 Space Integration 242 passed / 63 SQL-gated skipped、CP6.Tests 2771 passed / 17 environment-gated skipped、前端 122 files / 670 tests、完整 solution 非增量 Release 0 error / 3 条既有 warning；生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。证据见 `docs/space/reports/e12-s03-planning-simulation.md`。
- 本卡不做巷道路由、实时交通、高精度物理求解、财务实际、方案排名或生产回写。合并态引擎 4/4、服务 3/3、权限/合同/OpenAPI/五语 65/65、前端 7/7 及剩余一致性门禁通过。功能 tip 已确认进入远端集成祖先链，功能工作树及本地/远端临时分支已删除，释放 2,182,809,248 字节（约 2.03 GiB）。下一张可独立实施 E12-S04“多场景比较与决策记录”，`main` 未修改。

## E12-S02 完成状态（2026-08-02）

- E12-S02 已进入远端受控集成基线：数据/时钟/迁移 `4fb6941d`、API/UI/权限/SDK `d89919b8`、no-ff 集成 `c8ccbf56`。内部规划人员可向 E12-S01 克隆成功且生产隔离的场景导入最多 10,000 条不可变历史任务，并以确定性回放时钟映射历史 UTC 时间。
- 合同只接受 64 位 SHA-256 task/worker token 和调用方不可逆脱敏确认，不含订单、人员、物料或 SKU 原始标识字段；所有任务位置必须存在于场景固定快照，数据集与任务不可修改/删除且永不回写生产。
- 新增 3 个 planning API、2 个权限、两张租户隔离证据表、EF Migration/增量幂等 SQL；Design V1 从 71 增至 74 operations，C#/TypeScript SDK 已同步。规划页仅对 Ready/Succeeded/Isolated 场景开放 JSON 导入、列表和回放证据读取。
- 25 个数据集词条及 3 个场景入口词条均有五语运行时种子。静态 i18n 欠账仍为既有 908 项，本卡净新增 0。
- 全量门禁：Space Unit 264、默认 Space Integration 239 passed / 63 SQL-gated skipped、CP6.Tests 2767 passed / 17 environment-gated skipped、前端 121 files / 667 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning、生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。合并态领域 3/3、服务 4/4、权限/契约/OpenAPI/种子 64/64、前端 5/5 及剩余门禁通过。证据见 `docs/space/reports/e12-s02-deidentified-history-replay-clock.md`。
- 功能 tip 已先远端备份并确认进入远端集成祖先链；随后删除功能工作树及本地/远端临时分支，释放 2,177,363,070 字节（约 2.03 GiB）。历史由远端受控集成分支完整保留，`main` 未修改。

## E12-S01 完成状态（2026-08-02）

- E12-S01 已进入远端受控集成基线：隔离模型/迁移 `c673b7ec`、功能 `8d75e79e`、no-ff 集成 `0ac603d4`、五语收口 `3d41c8d9`。新增内部生产隔离规划分支，固定当前生产 Published 快照，可并存且不占生产 Draft/Published 指针。
- 场景版本具有独立 `PlanningScenario` purpose，领域与数据库双重拒绝其进入生产发布生命周期；固定基础版本后即使生产版本变为 Superseded，异步 Worker 仍克隆原快照，不自动追随或合并生产变化。
- 新增 PUT/GET/list 场景端点、调用方 UUID + payload hash 幂等、不可变分支证据、租户复合外键、迁移与增量幂等 SQL；Design V1 从 68 增至 71 operations，C#/TypeScript SDK 已同步。
- `/space/planning` 提供站点选择、创建、固定血缘、版本、克隆任务、隔离状态和自动轮询；20 个页面词条已补齐五语运行时种子。i18n 静态欠账仍为既有 908 项，本卡净新增 0。
- 最终门禁：Space Unit 261/261、默认 Space Integration 235 passed / 63 SQL-environment skipped、CP6.Tests 2763 passed / 17 environment-gated skipped、前端 120 files / 664 tests、完整 solution Release 0 error / 10 条既有 warning、生产构建、两个 EF Context、SDK drift 与 TypeScript strict no-emit 全部通过。合并态聚焦复验与五语聚焦测试通过。交付证据见 `docs/space/reports/e12-s01-production-isolated-scenario-branch.md`。
- 功能 tip 先远端备份并确认进入远端集成祖先链，随后已删除功能工作树及本地/远端临时分支，释放 2,877,403,216 字节（约 2.68 GiB）。历史由远端受控集成分支完整保留，`main` 未修改。

## E11-S06 完成状态（2026-08-02）

- E11-S06 已进入本地受控集成基线：合同 `46884878`、功能 `f10b4b54`、文档 `f50ce454`、no-ff 集成 `d49fe1d0`。新增只读批次效果评估，组合 E11-S03～S05 的建议、审批、分派回执、当前执行事实和指定 Published 版本几何，不新增评估表或写操作。
- 看板提供推荐→选择→回执→开始→完成/关注/补偿漏斗、显式比率与带样本数的时长。任务按 `TaskId`、人员按 `SourceId + ExternalId` 稳定排序形成同一获批队列的计划几何反事实；样本不足、锚点不完整、跨层或原始距离约束不满足时整项不可用。
- 实际路线节省、吞吐提升和货币收益因缺少任务轨迹、历史控制与成本归因基线而固定不可用；回退结果如实显示，时间证据不完整或无效时排除样本并明示 limitation。响应不含姓名、邮箱、内部 `UserId`、`AssignedTo` 或逐任务收益明细。
- Viewer 新增效果看板、手动刷新、来源时点、样本量、计划改善/持平/回退和收益边界；新建议、新审批、关闭与卸载会使旧响应失效。28 个五语键使快照从 4,587 增至 4,615；i18n 仍有 908 项既有欠账，本卡净新增缺失为 0。
- 功能分支门禁：Space Unit 258/258、默认 Space Integration 232 passed / 62 SQL-environment skipped、CP6.Tests 2759 passed / 17 environment-gated skipped、前端 118 files / 660 tests、完整 solution Release、生产构建、两个 EF Context、SDK drift、TypeScript SDK strict no-emit、OpenAPI surface 与差异检查通过。合并态复验：引擎 9/9、服务 2/2、权限/合同/种子 7/7、前端 23/23及类型、EF、SDK/OpenAPI 与差异门禁通过。交付证据见 `docs/space/reports/e11-s06-outcome-evaluation.md`。
- 功能 tip `f50ce454` 先完成远端备份，再确认其为本地/远端一致的清理前集成状态 `fc123f5d` 的祖先；随后已删除功能工作树及本地/远端临时分支，共释放 2,707,376,655 字节（约 2.52 GiB），历史由远端受控集成分支完整保留。`main` 未被本轮操作修改。

## E11-S05 完成状态（2026-08-02）

- E11-S05 已进入受控集成基线：合同 `139c76b5`、功能 `e8df8288`、文档 `a0b247ab`、no-ff 集成 `cf35849c`。新增审批批次实时执行状态、持久化动作账本、调用方 UUID + payload hash 幂等、最多 3 次人工重试与整批未开始任务的安全补偿。
- 三层重放保护覆盖 OA 回调、任务适配器回执和执行动作；精确重放返回原结果，部分/冲突回执失败关闭。重试每次重新验证 Published、不可变建议、人员实时性与空闲、内部映射、WMS 范围及任务并发事实。
- 补偿仅在整批任务仍 Pending、仍为原分派人、执行版本未变、从未开始/完成且原始回执完整一致时撤销 `AssignedTo`；不修改执行版本或结果，不认领/启动/完成任务，不修改库存/订单，不发出 WCS/PDA 命令。Migration 为 `20260802192420_SpaceE11S05ExecutionReceiptsCompensation`。
- Viewer 新增执行聚合状态、逐任务事实、动作历史、重试余额、补偿阻断码及显式原因输入，并阻止旧异步响应覆盖。28 行五语种子中 26 个新键使快照从 4,561 增至 4,587；i18n 仍有 908 项既有欠账，本卡净新增缺失为 0。
- 功能分支门禁：Space Unit 249/249、默认 Space Integration 230 passed / 62 SQL-environment skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 658 tests、完整 solution Release、生产构建、EF/SDK drift、TypeScript SDK strict no-emit 与差异检查通过。合并态复验：服务/适配器 14/14、权限/合同/种子 35/35、前端 21/21、类型、SDK drift、EF pending model 与差异检查通过。交付证据见 `docs/space/reports/e11-s05-execution-receipts-compensation.md`。
- 功能分支先推送远端备份；确认功能 tip `a0b247ab` 是本地/远端一致的集成状态 tip `17d6a3e0` 的祖先后，已删除功能工作树及本地/远端临时分支。共享依赖目标保留，本轮释放 D 盘 2,170,007,351 字节（约 2.02 GiB），历史由远端受控集成分支完整保存。

## E11-S04 完成状态（2026-08-02）

- E11-S04 已进入受控集成基线：合同 `098fb54b`、功能 `a7298e28`、文档 `a552d05d`、no-ff 集成 `c19231db`。新增内部 PUT/GET/cancel 调度审批资源、调用方 UUID 幂等、OA BizType `SPACE_DISPATCH_ASSIGNMENT`、提交/读取/取消权限与审计；提交人与最终审批人严格分离。
- 审批请求冻结 E11-S03 建议哈希、Published/仓库、选中 rank、任务并发、人员真实身份与双时点以及内部用户映射；对外不返回人员姓名、邮箱或内部人员 `UserId`。最终通过前重新验证全部事实，任一漂移整批进入 `Stale` 或 `FailedNoEffect`，不产生部分写入。
- `cp6-mobile-task-assignment-v1` 只分配现有 Pending 且未分派的真实 `MobileTask`，完整预检、任务分配、事件和回执在同一工作单元中提交；不认领、不启动、不修改库存/订单、不伪造 WCS，也不另建 PDA 事实源。Migration 为 `20260802184419_SpaceE11S04DispatchApproval`。
- Viewer 新增显式选择、理由、提交、刷新、取消、状态与回执，并以请求版本阻止旧响应覆盖。21 行五语种子使唯一键快照从 4,542 增至 4,561；i18n 历史缺失由 909 降至 908，本卡没有新增缺失键。
- 功能分支门禁：Space Unit 249/249、默认 Space Integration 224 passed / 62 SQL-environment skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 656 tests、完整 solution Release 与前端生产构建、EF/SDK drift、两个 TypeScript strict no-emit 和差异检查通过。合并态冒烟：审批服务/适配器 8/8、权限/合同/种子/基础设施 44/44、前端 19/19、类型、SDK drift 与 EF pending model 通过。交付证据见 `docs/space/reports/e11-s04-dispatch-approval-adapter.md`。
- 功能分支先推送远端备份；确认功能 tip `a552d05d` 是本地/远端一致的集成状态 tip `b317dfa5` 的祖先后，已删除功能工作树及本地/远端临时分支。共享依赖目标保留，本轮释放 D 盘 2,190,180,352 字节（约 2.04 GiB），历史由远端受控集成分支完整保存。

## E11-S03 完成状态（2026-08-02）

- E11-S03 已进入受控集成基线：合同 `3cf42534`、功能 `419d3f6c`、文档 `eea62de0`、no-ff 集成 `cf7bf778`。新增内部 PUT/GET 人员调度建议资源、调用方 UUID 幂等、不可变推荐证据和 `space-dispatch-v1` 定义；不审批、不分配、不认领、不启动、不修改任务或人员，也不向 WMS/WCS/PDA 写入。
- 任务只来自当前 CP6 `MobileTask` 的 Pending 且未分配事实，首个可行动位置固定优先 From、缺失时 To，并携带 ContractVersion/ExecutionVersion/RowVersion。人员必须同时具备新鲜位置与工作状态、严格 Idle，默认排除 Simulated；所有任务、人员和 Published 身份越界均失败关闭。
- 匹配先用 Hopcroft–Karp 保证最大基数，再做确定性最小成本；配对乘积上限 100,000，返回上限 100。证据分别保存任务/人员/配对首因排除、最多 100 个样例、匹配容量、截断和限制说明；几何距离不冒充通道路线、时间或 SLA。
- Migration `20260802180049_SpaceE11S03DispatchRecommendations` 新增租户隔离的不可变证据表、Published 复合外键、计数/JSON/哈希约束和索引。Viewer 新增手动 `DSP` 面板，与 KPI/DIAG/PUT 互斥，展示来源、任务并发、人员双时点、建议与排除证据，并支持任务首端定位。
- 功能分支门禁：Space Unit 249/249、默认 Space Integration 216 passed / 62 SQL-environment skipped、CP6.Tests 2752 passed / 17 environment-gated skipped、前端 118 files / 653 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning、EF/SDK drift、两套 TypeScript strict no-emit、生产构建和差异检查通过。合并态冒烟：引擎/运行时合同 6/6、服务/适配器 6/6、权限/审计/API/种子 23/23、前端 16/16、类型与 SDK drift 通过。42 个五语键使快照到 4,542；i18n 历史缺失仍为 909，本卡净新增 0。交付证据见 `docs/space/reports/e11-s03-dispatch-recommendations.md`。
- 功能分支先完成远端备份，再确认其 tip `eea62de0` 是本地/远端一致的集成状态 tip `7e627624` 的祖先；随后已删除功能工作树及本地/远端临时分支，共释放 D 盘 2,528,428,032 字节（约 2.35 GiB），共享依赖目录保留，历史由受控集成分支完整保存。

## E11-S02 完成状态（2026-08-02）

- E11-S02 已进入受控集成基线：合同 `3ccd2936`、功能 `644293f1`、文档 `034a1b1b`、no-ff 集成 `a2b47826`。新增内部 PUT/GET 上架推荐资源、调用方 UUID 幂等、不可变推荐证据和 `space-putaway-v1` 定义；不预留、不移动库存、不创建任务，也不向 WMS/WCS/PDA 写入。
- 候选只使用当前 Published/Active 空间模型和一致的当前 WMS 库存/活动任务来源。精确合并要求显式货主与批次及全部正库存逐行完全匹配，否则只推荐空库位；返回稳定 rank、规则命中、九类首因排除计数和最多 100 个样例，几何距离不冒充路线距离，入库数量不冒充容量。
- Migration `20260802172258_SpaceE11S02PutawayRecommendations` 新增租户隔离的不可变证据表、复合 Published 外键、计数/JSON/哈希检查约束和查询索引。Viewer 新增手动 `PUT` 面板，与 KPI/DIAG 互斥，支持当前楼层、候选/排除定位、旧响应失效和失败保留上次成功结果。
- 功能分支门禁：Space Unit 245/245、默认 Space Integration 211 passed / 62 SQL-environment skipped、CP6.Tests 2748 passed / 17 environment-gated skipped、前端 117 files / 648 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning、EF/SDK drift、TypeScript SDK strict no-emit、生产构建和差异检查通过。合并态冒烟：引擎 5/5、服务 6/6、权限/审计/契约/种子 34/34、前端 14/14、类型与 SDK drift 通过。42 个五语键使快照到 4,500，i18n 历史缺失由 911 降至 909，本卡净新增 0。交付证据见 `docs/space/reports/e11-s02-putaway-recommendation-candidates.md`。
- 远端备份、祖先关系与集成本地/远端一致性验证后，已删除 E11-S02 功能工作树及本地/远端临时分支；共享依赖目录保留。本轮释放 D 盘 2,187,710,464 字节（约 2.04 GiB），功能历史由远端受控集成分支完整保留。

## E10-S06 完成状态（2026-08-02）

- E10-S06 已进入受控集成基线：合同 `bffe1877`、实现 `0676ba4a`、文档 `969e7c38`、no-ff 集成 `5f86edcb`。新增只读 `GET /api/space/design/v1/sites/{siteId}/runtime/overview`，只汇总当前 Published/Active 模型，ABC 窗口限定为 1～365 个完整自然日；库存、作业和 ABC 保持独立来源、观察时间和部分可用语义。
- 楼层面积使用毫米边界鞋带公式，缺失任一活动楼层面积时不伪造站点总面积；货架占地率只表达建模足迹。占用率按正库存物理库位计算；因没有容量主数据，容量利用率固定为空并给出 `WMS_LOCATION_CAPACITY_NOT_AVAILABLE`。库存不跨单位合计，活动任务数/Stop 数不冒充吞吐量。
- ABC 只使用正数 OUT 事实，按出库量和物料稳定排序，以排名前累计占比 `<80%`/`<95%` 划分 A/B/C；有当前库存但无正出库事实的 SKU 明确为 Unclassified。Viewer 新增 KPI/异常/逐层总览和固定 ABC 颜色，ABC、库存空间筛选、作业热图三个颜色权威互斥，请求版本阻止旧响应覆盖。
- Design V1 从 67 增至 68 operations，C#/TypeScript SDK 已同步，无数据库 Migration。功能分支全量门禁：Space Unit 236/236、默认 Space Integration 198 passed / 62 SQL-environment skipped、本卡真实 SQL 3/3、CP6.Tests 2739 passed / 17 environment-gated skipped、前端 115 files / 639 tests、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 和生产构建通过。合并态冒烟：合同 23/23、Runtime/适配器 81/81、权限/OpenAPI 46/46、前端 25/25、类型检查和 SDK drift 通过。
- i18n 快照仍未绿色：集成基线已有 881 项，本卡新增 30 项，共 911 项；没有篡改生成快照掩盖技术债。E10 P2 S01～S06 至此均有完成证据。CAD/E06 主链继续等待正式黄金集、授权供应商证据和冻结 Worker；下一张独立实施卡须按依赖重新选择，不能把快照口径直接扩写成趋势、推荐或执行控制。

## E10-S05 完成状态（2026-08-02）

- E10-S05 已进入受控集成基线：实现 `65c59555`、文档 `53bea9b9`、no-ff 集成 `e270c2cc`。复用 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory/locate`，新增可选货主条件；货主、SKU、批次和容器至少一个，多个条件固定精确 AND，货主在服务边界规范为大写。
- WMS 继续是库存业务事实源；服务端重新验证适配器返回的正库存及全部筛选条件，越界结果以 502 失败关闭。CP6 适配器通过仓库、库位、SKU、批次的唯一库存业务键为容器取得货主，不向 Design Revision 复制或在浏览器猜测业务事实。
- 3D Viewer 新增可持续库存空间筛选：命中库位琥珀色、当前层未命中库位压暗，展示本层/全站/分层数量和来源证据；筛选跨库存轮询与楼层切换保持，清除后恢复库存模式，并以请求版本阻止过期响应覆盖。库存筛选与作业热图互斥，原有一次性定位仍保留。
- Design V1 保持 67 operations，C#/TypeScript SDK 已同步，无数据库 Migration。门禁：运行合同 2/2、Runtime/适配器 68/68、权限/OpenAPI 45/45、前端聚焦 22/22、前端全量 114 files / 632 tests、Space Unit 236/236、默认 Space Integration 190 passed / 61 SQL-environment skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过；本卡真实 SQL 1/1。完整真实 SQL 矩阵 250 passed / 1 个已独立复现的 Excel 预检种子循环依赖基线失败。
- E10-S06“仓库 KPI 快照、利用率与 ABC 口径”是下一张具备前置条件的 P2 卡。CAD/E06 主链仍等待正式黄金集、授权供应商证据和冻结 Worker 等外部输入；本卡不改变该优先级或失败关闭边界。

## E10-S04 完成状态（2026-08-02）

- E10-S04 已进入受控集成基线：实现 `9a9802a8`、文档 `f961d7e5`、no-ff 集成 `b4d5b81e`。新增当前设备读取 `GET /api/space/design/v1/sites/{siteId}/devices`，沿用 `space:model:read`，支持来源/设备/状态/楼层/活动告警过滤与受保护游标；外部主体在读库前拒绝。
- `Space_DeviceState` 以独立位置/运行状态游标维护设备当前投影，`Space_DeviceAlarmState` 以设备+外部告警身份维护显式 Raise/Clear 生命周期。迟到事件继续追加台账并返回 `AcceptedStale`、`ProjectionApplied=false`，但不回退投影或重新激活已被较新 Clear 关闭的告警；台账和投影在同一 Serializable 事务中提交。
- 当前读取包含无事件的 Unknown 映射、当前 Published 映射有效性和锚点、来源位置/状态证据、5 分钟独立新鲜度、Real/Simulated 以及活动告警严重度与事件证据。Migration `20260802144027_SpaceE10S04DeviceRuntime` 新增两个投影表、rowversion、租户复合外键、唯一索引、检查约束和身份写保护。
- 3D Viewer 已移除旧设备演示接口调用：只绘制活动楼层，来源 XYZ 优先，缺失时仅回退当前 Published 映射元素锚点；状态色、模拟线框、过期透明度和活动告警环均显式呈现，Three.js userData 保留映射/来源/位置/状态/告警证据，切层、关闭和卸载会释放 GPU 资源。
- Design V1 从 66 增至 67 operations，C#/TypeScript SDK 已同步。门禁：领域 2/2、设备服务 9/9、真实 SQL 本卡 2/2、权限/审计/OpenAPI 70/70、前端聚焦 14/14、前端全量 113 files / 629 tests、Space Unit 236/236、默认 Space Integration 189 passed / 60 SQL-environment skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 248 passed / 1 已独立基线复现的 Excel 预检种子循环依赖失败。
- E10-S05“货主、SKU、批次和容器空间筛选”是下一张具备前置条件的 P2 卡；MQTT/OPC UA/厂商连接器、凭据、告警确认、设备控制、历史轨迹和预测分析仍未实现，也不得混入 S05。CAD/E06 主链继续等待正式黄金集、授权供应商证据和冻结 Worker 等外部输入。

## E10-S03 完成状态（2026-08-02）

- E10-S03 已进入受控集成基线：实现 `10b16c51`、文档 `8ce91d41`、no-ff 集成 `88efd23d`。新增版本化 `space-device-event-v1` 合同、设备主数据映射 GET/POST/PUT 与设备事件写入；读取沿用 `space:model:read`，变更要求 `space:integration:manage` 并使用稳定审计动作。
- 映射以 `TenantId + SiteId + SourceId + DeviceExternalId` 为权威身份，绑定当前 Published/Active 的稳定设备元素 LogicalId；设备类型与 Device/Conveyor/Workstation/Elevator/StaticEquipment 兼容子集失败关闭，同一来源的设备和元素保持一对一，更新使用 rowversion。
- 设备事件支持 PositionObserved、OperatingStateChanged、AlarmRaised、AlarmCleared 四类互斥形状，严格冻结 Real/Simulated、设备/状态/告警枚举、UTC 时间、五分钟未来偏差、非负序列、毫米 XYZ、Published 楼层/库位引用和来源事件幂等；相同载荷安全重放，不同载荷稳定冲突。
- Migration `20260802141148_SpaceE10S03DeviceEvents` 新增 `Space_DeviceMapping` 与追加式 `Space_DeviceEvent`，含复合租户外键、唯一索引、检查约束和事件历史写保护。旧 `WmsDeviceQuery` 仍明确保持 Unavailable 空占位，不冒充真实 WCS/IoT 来源。
- Design V1 从 62 增至 66 operations，C#/TypeScript SDK 已同步。门禁：E10-S03 服务/真实 SQL 7/7、权限/审计/OpenAPI 70/70、Space Unit 234/234、默认 Space Integration 186 passed / 60 SQL-environment skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 245 passed / 1 已独立基线复现的 Excel 预检种子循环依赖失败。
- 该节记录 E10-S03 完成时的后续边界；E10-S04 现已由上方最新状态接续完成。MQTT/OPC UA/厂商连接器、凭据、告警确认或控制写回仍未实现。

## E10-S02 完成状态（2026-08-02）

- E10-S02 已进入受控集成基线：实现 `e70c2715`、文档 `86ad63bb`、no-ff 集成 `29a69a2b`。新增内部当前位置读取与受审计的授权轨迹查询；当前位置沿用 `space:model:read`，轨迹要求 `space-audit:read` 并以 `space.personnel.trajectory.read` 失败关闭审计。
- 查询只返回稳定来源/人员外部 ID、空间运行字段、来源事件和时间证据，不返回姓名、邮箱或内部 `UserId`；外部主体在读库前拒绝，站点访问判断先于存在性查询。位置只来自 E10-S01 `PositionObserved`，不从 WMS、任务或几何推测。
- 当前新鲜度阈值为 5 分钟，过期数据仍返回并显式标记；轨迹单次最长 24 小时、可见查询期 30 天。30 天不是物理清除，追加式事件账本继续保留，物理归档/删除须在后续独立生命周期卡完成。
- 3D Viewer 已加入当前人员和授权轨迹图层，只绘制活动楼层的来源 XYZ，区分过期/模拟/工作状态，缺少 XYZ 时明确显示未定位并不推断；切层、旧请求和卸载均清理图层/GPU 资源。
- Design V1 从 60 增至 62 operations，C#/TypeScript SDK 已同步，无新 Migration。门禁：E10 服务 12/12、权限/审计/OpenAPI 68/68、前端聚焦 8/8、前端全量 113 files / 626 tests、Space Unit 234/234、默认 Space Integration 180 passed / 59 SQL-environment skipped、CP6.Tests 2736 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 238 passed / 1 已独立基线复现的 Excel 预检种子循环依赖失败。
- E10-S03 的设备实时状态、AGV/输送设备和告警尚未实现；E10 仍属 P2，不改变 CAD 阻塞主链的优先级和依赖。

## E10-S01 完成状态（2026-08-02）

- E10-S01 已进入受控集成基线：实现 `1c7aa0e2`、文档 `1da17591`、no-ff 集成 `ec29d41f`。新增版本化 `space-personnel-event-v1` 合同与 `POST /api/space/design/v1/sites/{siteId}/personnel-events`，只允许具有 `space:integration:manage` 的内部集成主体写入。
- 人员事件明确区分 `Real`/`Simulated`，同一站点和来源不能切换类型；来源事件 ID 提供业务幂等，相同载荷安全重放，不同载荷稳定冲突。Space 不猜测位置，不从 WMS/任务/几何推导忙闲状态。
- `Space_PersonnelEvent` 保存追加式事件事实，`Space_PersonnelState` 以独立的位置/工作状态游标维护当前投影；历史乱序事件进入账本但不回退投影，已绑定用户不能重分配。
- Migration `20260802125928_SpaceE10S01PersonnelEvents`、数据库检查/唯一约束、租户过滤、rowversion、OpenAPI 60 operations 及 C#/TypeScript SDK 已闭环。
- 门禁：E10 领域 3/3、服务/EF 7/7、权限/OpenAPI 43/43、Space Unit 234/234、默认 Space Integration 175 passed / 58 SQL-environment skipped、真实 SQL 本卡 2/2、CP6.Tests 2734 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 231 passed / 1 既有 Excel 预检种子循环依赖失败；该失败已在不含本卡的 `e8d4e1c2` 基线独立复现。
- E10-S02 的实时读取、授权轨迹和保留策略尚未实现；E10 仍属 P2，不改变 CAD 阻塞主链的优先级和依赖。

## E03-S01～S03 与 E13-S16 完成状态（2026-08-02）

- E03-S01～S03 已连续进入受控集成基线：标准建模 Excel 模板 `033e8872` / `8521a701`，版本化字段映射 `f1310b40` / `e0cc4964`，Excel 数据预检 `9d0a59e7` / `3571f677`。当前链路已具备标准模板下载、租户私有映射、不可变版本、50 MB 隔离上传、异步预检、结构化问题清单和受保护错误报告。
- E13-S16 已进入受控集成基线：实现 `0549a1f2`、文档 `6ec0c02a`、no-ff 集成 `ad4de0b0`。租户管理员可在 `/space/ai-admin` 管理版本化数据策略、站点、获批 Provider 别名、1～3 并发与日/月预算，并查询实际/估算/未定价用量；合同不接受或回显密钥、URL、Endpoint。
- E13-S16 新增 `Space_AiTenantPolicy` 追加式版本表、Design V1 策略/用量 API、`space-ai-admin:read/manage` 权限、五语 seed、OpenAPI 及 C#/TypeScript SDK。外部主体显式拒绝，无策略时继续 `Disabled` 失败关闭。
- 最新门禁：服务 5/5、权限 18/18、Space Unit 231/231、Space Integration 168 passed / 57 SQL-environment skipped、CP6.Tests 2733 passed / 17 environment-gated skipped、前端 112 files / 622 tests、type-check、production build、完整 solution 0 warning / 0 error、EF/SDK drift 与差异检查全部通过。
- `npm run i18n:check` 保持既有 843 项存量缺口，本卡未增加缺口。E03-S04 及 E04-S05 仍等待 E02-S07/CAD 语义预览链；E13-S04 仍等待 E02-S03，不能提前启用外部 Provider、CAD IR、输出校验或 Apply。

## E04-S06 完成状态（2026-08-02）

- E04-S06 2D/3D 同源预览已完成并进入受控集成基线：功能提交 `20f248bd`，no-ff 集成提交 `2b6ef127`，起始基线 `99e367f1`。
- Design V1 编辑器默认提供 2D+3D 分屏以及纯 2D、纯 3D 切换；两种投影消费同一个 `ISpaceDesignSceneDto`，保存成功后使用服务端回读场景同步重建，不维护第二套模型。
- 3D 只读预览复用参数化 SceneBuilder、InstancedMesh 与资产链，支持俯视/等轴/正视和自动适配；界面明确显示版本状态及“不含生产库存/任务”，不开放 3D 写操作。
- 机器一致性清单分别来自编辑器实际 Konva 投影和实际 Three.js 对象树/实例矩阵/几何，统一比较数量、LogicalId、父级、业务编码、毫米位置/尺寸、旋转、逐层规格与规范 primitive，并用 SHA-256 固化；篡改实际实例矩阵会被自动化识别。
- 生命周期边界保持失败关闭：Removed/Disabled 不渲染、移除货架不泄露遗留 Active 子层、真正孤立层仍报错；2D 路径多线段在套索选择时按 LogicalId 去重。
- 门禁：前端聚焦 4 files / 13 tests、全量 108 files / 612 tests、type-check、production build、Space Unit 231/231、默认 Space Integration 140 passed / 55 SQL-gated skipped、Design Scene 真 SQL 3/3、Space Integration + KOUSQLSERVER 195/195、CP6.Tests 2720 passed / 17 既有环境 Skip、完整 solution 非增量构建 0 error / 10 条既有 warning、SDK drift、TypeScript SDK strict no-emit 与 `git diff --check` 全部通过。
- 本卡无新端点、DTO、数据库模型、Migration、OpenAPI 或 SDK 表面变化。机器证据见 `docs/space/reports/e04-s06-shared-2d-3d-preview.md`。
- 下一张建议独立卡为 E03-S01“标准建模 Excel 模板”，其 E05-S02 依赖已满足；E04-S05、E06 与 E13 后续链路继续等待各自前置条件。

## E09-S05 完成状态（2026-08-02）

- E09-S05 外部访问审计与有效期已完成并进入受控集成基线：功能提交 `83798dcf`，no-ff 集成提交 `c658871c`，起始基线 `a5b53a2b`。
- 登录继续复用统一 `Sys_SecurityLog`；Space 追加写账本新增稳定 Portal 会话入口、组织选择、PublishedScene/Stock/Task 查看事件，记录 Organization Context、Site、结果、条目数、授权版本、Correlation/Trace 与受控客户端元数据，404 范围拒绝记为 `Denied` 且不保存异常明文。
- Organization、Membership、Grant、FieldPolicy 变更端点使用稳定业务动作码和 `space:external:manage` 证据；写入前审计失败关闭，成功写入后最终审计不可用返回 outcome unknown。暂停、撤销和退休通过同一 update 资源链可追踪。
- 外部 `Export` 求值现在强制审计允许/拒绝，并记录组织/成员安全戳、AuthorizationVersion、GrantIds 与 FieldPolicyIds；审计不可用时清空命中授权并返回 `SPACE_AUDIT_UNAVAILABLE`。本卡未新增独立导出端点。
- 同一现有会话已自动化证明 Membership/Grant 到期、暂停、撤销以及 FieldPolicy 退休在下一请求立即失效；Active Policy 版本变化在下一响应产生新 AuthorizationVersion。真 SQL 连续证明成员到期→Grant 到期→续期恢复→Policy 退休的逐请求重验证链。
- 本卡无新端点、DTO、迁移或 SDK 表面变化，OpenAPI 保持 36 paths / 47 operations。门禁：审计聚焦 48/48、Portal/真 SQL 合并态 16/16、Space Unit 231/231、Space Integration + KOUSQLSERVER 195/195、CP6.Tests 2720 passed / 17 既有环境 Skip、非增量 solution 0 error / 10 条既有 warning、EF/SDK drift、TypeScript SDK strict no-emit、前端 type-check/106 files/607 tests/production build 全部通过。
- E09-S01～S05 工程范围完成；产品、QA、WMS 与安全负责人的正式 GA 签字仍是发布治理动作，机器证据见 `docs/space/reports/e09-s05-external-access-audit-validity.md`。
- 下一张建议独立卡为 E04-S06“2D/3D 同源预览”；E04-S03、E05-S03 依赖已满足。E04-S05 继续等待 E02-S07，E10 仍为 P2。

## E09-S04 完成状态（2026-08-02）

- E09-S04 跨租户越权自动化已完成并进入受控集成基线：功能提交 `f045bd6f`，no-ff 集成提交 `c82d4fae`，起始基线 `dfacbb48`。
- 新增发布阻断矩阵，覆盖猜测 Organization/Site/Location ID、同码租户协作图、Published 场景身份、Stock/Task 运行态身份、分页游标、授权版本和字段裁剪；所有越权路径统一失败关闭，不泄露目标存在性。
- 内存与真实 SQL 使用两个租户创建相同 User、Site/Floor/Zone LogicalId、组织码、策略名、Owner 和 Task ID，九类外部协作表的正常查询各只见本租户，审计视图可确认两套数据确实同时存在。
- Portal 场景投影前新增 Schema/Authority/Site/PublishedVersion/Published 状态/Floor-Site 身份校验；Stock/Task 在处理条目前新增 SiteId + PublishedVersionId 校验；同码但非候选 LocationLogicalId 的运行态条目固定 404。
- `AuthorizationVersion` 现在显式绑定 Tenant/User/Organization/Resource，并继续包含组织/成员安全戳、Grant/Policy 版本；Data Protection 游标已自动化验证 Tenant、Actor、Organization、grant version、资源和过滤哈希任一变化均不能复用。
- 本卡无新端点、DTO、迁移或 SDK 表面变化；OpenAPI 保持 36 paths / 47 operations。功能门禁：Space Unit 231/231、Space Integration + KOUSQLSERVER 187/187、CP6.Tests 2713 passed / 17 environment-gated skipped、完整 solution 0 warning / 0 error、EF/SDK drift 与前端 106 files / 607 tests 全部通过。
- 合并态聚焦门禁：隔离矩阵含真实 SQL 16/16、游标/执行上下文中间件 42/42、完整 solution 非增量构建 0 error / 10 条既有 warning、EF/SDK drift、TypeScript SDK strict no-emit、前端 type-check/607 tests/production build 全部通过。交付报告见 `docs/space/reports/e09-s04-cross-tenant-isolation.md`。
- 下一张建议卡为 E09-S05：外部登录/组织选择/查看/导出/授权变化审计，以及 Membership/Grant/Policy 到期、暂停或撤销后现有会话下一次请求立即失效的证据。

## E09-S03 完成状态（2026-08-01）

- E09-S03 外部只读 Portal 与字段策略已完成并进入受控集成基线：功能提交 `88bc42d1`，no-ff 集成提交 `1850b2d8`，起始基线 `13c7b9da`。
- 新增租户权威 `Space_FieldPolicy` 与 `Space_FieldPolicyField`，覆盖 PublishedScene/Stock/Task、显式字段 allowlist、None/Partial/Hash/Redact 掩码、Audience、`CanExport`、版本和终态退休；数据库以复合租户外键、检查约束和过滤唯一索引失败关闭。
- 新增 `/api/space/field-policy` 管理 API，读取/变更沿用 `space:external:read/manage`；Grant 控制行与空间范围，字段策略控制字段、脱敏和导出能力，未知字段默认不可见，导出必须同时得到 Grant 与策略许可。
- 新增 `/api/space/portal/v1` 只读 Organizations/Sites/PublishedScene/Stock/Tasks。外部主体只允许 Portal 的 GET/HEAD；除组织选择外必须提供单一非空 Organization Context，内部主体、未知主体类型、缺失/歧义上下文和外部主体访问其他 Space 路径均拒绝。
- Portal 只读取当前 Published/Active；结构 ID 来自数据库权威候选，业务值字段按策略裁剪。多个合法 Grant 按完整子句 OR，字段采用命中范围内最少限制掩码；其他资源 Grant、运行源身份和 Zone-only 父层字段均不能扩大可见范围。
- OpenAPI 从 29 paths / 38 operations 增至 36 paths / 47 operations，并更新 C#/TypeScript SDK；OpenAPI、C#、TypeScript 工件 SHA-256 分别为 `BCFFEF09...DAF2`、`C7BCC222...6C4B`、`5F5132E7...9ABF`。
- 功能门禁：Space Unit 231/231；Space Integration + KOUSQLSERVER 181/181、0 skipped；CP6.Tests 2711 passed / 17 environment-gated skipped；完整 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift 与 C#/TypeScript SDK 编译通过。S02→S03 增量 SQL 在临时库连续执行两次通过。
- 合并态聚焦门禁：完整 solution build 0 error、字段策略领域 3/3、Portal/策略/Grant/求值器含真实 SQL 22/22、权限/OpenAPI/中间件/ProblemDetails 84/84、EF/SDK drift、前端 106 files / 607 tests、type-check 和 production build 全部通过。交付报告见 `docs/space/reports/e09-s03-external-portal-field-policy.md`。
- 下一张建议卡为 E09-S04：跨租户越权自动化测试，覆盖猜测 ID、同码组织/仓库/库位、分页/游标、缓存和 Portal DTO；E09-S05 随后补齐外部访问审计与有效期证据。

## E09-S02 完成状态（2026-08-01）

- E09-S02 外部组合授权与访问求值器已完成并进入受控集成基线：功能提交 `cae12c7e`，no-ff 集成提交 `feefa9cd`，起始基线 `8869ac58`。
- 新增 `Space_ExternalGrant` 及 Floor/Zone/Owner/Object 规范化子表；Site 必填，Floor/Zone 固定使用当前 Published Revision 的稳定 LogicalId，数据库以复合租户外键、状态/有效期/版本检查和过滤唯一索引失败关闭。
- 多个 Grant 保持完整子句 OR，单个 Grant 内 Site/Floor/Zone/Owner/BusinessObject 按 AND 匹配；禁止跨 Grant 展平维度造成笛卡尔权限升级。Export 还要求命中子句显式 `CanExport`。
- `ISpaceAccessEvaluator` 验证可信 Tenant/User、单一 Organization Context、Active Organization、有效 Active Membership 与有效 Active Grant；任一缺失、过期、暂停、撤销、歧义或跨组织拼接均拒绝。安全范围携带组织/成员安全戳、GrantVersion 和确定性 AuthorizationVersion。
- 新增 `/api/space/external-organization/{organizationId}/grant` 管理 API，读取/变更沿用 `space:external:read/manage`。`FieldPolicyId` 在 E09-S03 前只作保留字段，非空请求固定 422；外部主体仍被全局拒绝直接进入 `/api/space`，未提前开放 Portal。
- OpenAPI 增加 2 个 route family / 4 个操作并更新 C#/TypeScript SDK；运行时客户端表面哈希为 `FFCE63E749C7653E553A57D32EA85A7FF846F17199AF3436FE787CD26F509259`。
- 功能门禁：Space Unit 228/228；Space Integration + KOUSQLSERVER 169/169、0 skipped；CP6.Tests 2703 passed / 17 environment-gated skipped；完整 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift 与 C#/TypeScript SDK 编译通过。S01→S02 增量 SQL 在临时库连续执行两次通过。
- 合并态聚焦门禁：完整 solution build 0 error、领域 4/4、访问求值/管理/真实 SQL 10/10、权限/OpenAPI 35/35、EF/SDK drift 通过。交付报告见 `docs/space/reports/e09-s02-external-grant-scope-evaluator.md`。
- 下一张建议卡为 E09-S03：接入 Published-only 外部只读 Portal、资源 DTO allowlist、字段策略/脱敏和导出裁剪；完成前继续保留外部主体的全局拒绝。

## E09-S01 完成状态（2026-08-01）

- E09-S01 外部组织与成员模型已完成并进入受控集成基线：功能提交 `a599cfd7`，no-ff 集成提交 `09538ca3`，起始基线 `0c02fc80`。
- 新增租户权威 `Space_ExternalOrganization` 与 `Space_ExternalMembership`；支持 Customer/Supplier/ThirdPartyLogistics、ERP BusinessPartner 可选关联、用户多组织成员关系、有效期、终态生命周期、SecurityStamp、rowversion 和审计字段。
- 数据库以复合租户外键、过滤唯一索引、枚举/有效期/关联成对检查约束失败关闭；同类型组织编码唯一，客户/供应商/3PL 可以使用相同业务码而保持组织隔离。
- 新增 `/api/space/external-organization` 组织/成员管理 API，读取需要 `space:external:read`，变更需要 `space:external:manage`；跨租户用户、客商与组织引用不泄露存在性。
- OpenAPI、C# 与 TypeScript SDK 已更新；运行时客户端表面哈希为 `6011AA0FC2B4B2A81C5D915B1DEE1D0ADC84BE01BB8D2962A3D087B896E1EF76`。
- 功能门禁：Space Unit 224/224；Space Integration + KOUSQLSERVER 159/159、0 skipped；CP6.Tests 2703 passed / 17 environment-gated skipped；完整 18 项目 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift、TypeScript SDK 与运行时客户端表面均通过。
- 合并态聚焦门禁：领域 4/4、组织/成员内存与真实 SQL 6/6、权限/种子/ProblemDetails/OpenAPI 49/49、EF/SDK drift 与 TypeScript SDK 编译通过。交付报告见 `docs/space/reports/e09-s01-external-organization-membership.md`。
- 下一张建议卡为 E09-S02：实现 Organization Context、有效 Membership 与 Site/Floor/Zone/Owner/BusinessObject 组合 Grant，并在缺失、歧义或跨组织拼接时失败关闭。

## E08-S05 完成状态（2026-08-01）

- E08-S05 10,000 库位性能基线已完成并进入受控集成基线：功能提交 `cc1d8baf` + `24464fab`，no-ff 集成提交 `7a05c05f` + `675e485c`，起始基线 `5d37865a`。
- 锁定门槛：完整场景 ≤100 draw calls、Medium tier ≥50fps、≤3 秒可交互、标签 P95 ≤16ms/对象池 ≤200、拾取 P95 ≤150ms、10,000 条着色与运行态查询 ≤3 秒。
- 500 个货架框已从逐对象 `LineSegments` 合并为单个 wireframe `InstancedMesh`；完整标准仓 draw calls 从 535 降到 36。库位颜色缓冲在建桶时预分配，库存覆盖层走分桶批量着色并保留旧 ViewerHandle 回退。
- 硬件 WebGL 验收使用 Intel Iris Xe / D3D11：10,000 库位、36 draw calls、275ms 可交互、P95 帧间隔折算 83.3fps、标签 3.5ms、拾取 0.4ms、着色 3.0ms、35 个同屏标签、0 console errors。执行器检测到 SwiftShader 时拒绝形成 GPU PASS。
- 运行态服务现以精确 10,000 个 Published/Active 库位验证库存与任务查询各 20×500 分块且各自 ≤3 秒；既有 10,001 个不同库位在 WMS 调用前 400 拒绝。
- 功能分支门禁：Space Unit 220/220；Space Integration 105 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 106 files / 607 tests、CPU/硬件性能门禁、type-check、production build；WebApi/C#/TypeScript SDK build 与 SDK drift 通过。
- 合并态聚焦门禁：Viewer 19/19、CPU 性能 1/1、运行态 10,000/10,001 边界 2/2、follow-up 2/2、type-check 与 range whitespace 通过。交付报告见 `docs/space/reports/e08-s05-10000-location-performance.md`。
- 下一张建议卡为 E09-S01：外部组织与成员模型，使客户、供应商、3PL 可关联用户和租户。

## E08-S04 完成状态（2026-08-01）

- E08-S04 拣货任务与路径验收已完成并进入受控集成基线：功能提交 `9f7e38f8`，no-ff 集成提交 `994339a6`，起始基线 `944e465f`。
- 新增 `GET /api/space/design/v1/sites/{siteId}/runtime/tasks/path?taskId=...`；任务号必填且在 WMS 边界筛选，继续沿用 Published/Active、采纳身份、500 分块、10,000 上限及来源新鲜度。
- 响应提供 WMS 实际顺序、楼层/库区/坐标、跨层/跨区切换、总量与分区工作量，以及当前 Published 巷道拓扑；可用空结果与 `Unavailable` 严格区分，重复实际序号失败关闭为 502。
- Viewer 同时展示实际顺序、仅演示且不回写 WMS 的优化顺序、跨层/跨区和工作量；实际停靠点支持 Locator 跨层定位，残缺坐标不生成不完整优化路径。
- 当前 Design Revision 没有连接体拓扑，跨层段明确降级为近似直连并提示，不把近似路线伪装为精确结果。
- 功能分支门禁：Space Unit 220/220；Space Integration 105 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 105 files / 603 tests、type-check、production build；WebApi/C#/TypeScript SDK build 与 SDK drift 通过。
- 合并态聚焦门禁：runtime 47/47、OpenAPI/SDK 18/18、前端 9/9、type-check 与 SDK drift 通过。交付报告见 `docs/space/reports/e08-s04-task-path-acceptance.md`。
- 下一张建议卡为 E08-S05：10,000 库位性能基线，锁定场景交互、标签和批量查询门槛。

## E08-S03 完成状态（2026-08-01）

- E08-S03 物料/批次/容器定位验收已完成并进入受控集成基线：功能提交 `8d8f7e01`，no-ff 集成提交 `dfb6e93b`，起始基线 `faeacd4b`。
- 新增统一运行源端点 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory/locate`；物料、批次、容器至少一个，多个条件固定按精确 AND 匹配。
- 查询当前 Published/Active Space 库位并沿用采纳后的 WMS 身份、500 分块和 10,000 上限；响应按 Space 逻辑库位聚合，显式提供双身份/双编码、楼层、数量、匹配事实、命中库位数、楼层数与 E08-S02 来源新鲜度。
- 可用来源的零命中与 `Unavailable` 来源严格区分；不满足条件、非正库存或同一 WMS 身份多编码的适配器响应以 502 合同违例失败关闭。
- Viewer 搜索支持编码、物料、批次、容器；多结果按楼层分组，由用户选择候选后复用现有跨层 Locator，不再擅自跳第一条；旧并发响应不能覆盖新搜索。
- 功能分支门禁：Space Unit 220/220；Space Integration 101 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 103 files / 597 tests、type-check、production build；WebApi/C# SDK 0 warning / 0 error；TypeScript SDK 与 SDK drift 通过。
- 合并态聚焦门禁：runtime 44/44、OpenAPI/SDK 18/18、前端 5/5、type-check 通过。交付报告见 `docs/space/reports/e08-s03-inventory-locate.md`。
- 下一张建议卡为 E08-S04：拣货任务与路径验收，覆盖实际/优化顺序、跨区/跨层和工作量。

## E08-S02 完成状态（2026-08-01）

- E08-S02 库存来源、时间和延迟展示已完成并进入受控集成基线：功能提交 `9a478c7a`，no-ff 集成提交 `d4cd8a82`，起始基线 `bbe77f3e`。
- 3D Viewer 库存覆盖层已从旧楼层库存接口迁移到 E08-S01 统一运行源；按当前楼层 Space 逻辑库位 ID 查询和聚合，不以 WMS 编码漂移替换稳定身份。
- 运行源公开并显示来源类型、来源系统、运行连接、数据观察时间、CP6 接收时间、延迟、时钟超前，以及 Viewer 会话最近成功/最近失败/恢复状态。
- 统一源暂无容量、锁定和拣货流程事实，Viewer 只展示空/有库存，并把利用率明确标为占用估算；不伪造满、锁定或在拣状态。
- 功能分支门禁：Space Unit Release 220/220；Space Integration Release 96 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 102 files / 593 tests、type-check、production build；C# SDK 0 warning / 0 error；SDK 无 drift。
- 合并态聚焦门禁：runtime 40/40、OpenAPI/SDK 18/18、前端 20/20、type-check 通过。交付报告见 `docs/space/reports/e08-s02-runtime-freshness.md`。
- 下一张建议卡为 E08-S03：物料/批次/容器定位验收，覆盖多结果、空结果和跨层结果。

## E08-S01 完成状态（2026-07-31）

- E08-S01 统一运行态数据源已完成并进入受控集成基线：最终功能提交 `3df6b1d2`、no-ff 集成提交 `b2bb7a35`、设计提交 `636eb6d5`。
- 功能分支全量验证：Space Unit 220 passed / 0 failed / 0 skipped；默认 Space Integration 94 passed / 0 failed / 48 SQL 环境门禁 skipped；OpenAPI/权限/数据源合同聚焦 45 passed；Release 完整 solution build 0 error / 10 个既有 warning；SDK 无 drift；EF 无待迁移模型变化；feature range `git diff --check` 静默通过。
- 最终复审修复了生成 C#/TypeScript SDK 丢失合法 nullable 响应类型的问题；修复后 OpenAPI/权限 34/34、Client build 0 warning / 0 error、SDK 无 drift，并由原 API 复审者确认关闭。合并态门禁为 runtime/adapter unit 23/23、runtime/adapter/simulator integration 56/56、OpenAPI/权限 34/34、SDK 无 drift。
- 运行权威规则：当前 Published/Active Space 模型是空间与身份权威；生产 `Cp6SpaceWmsAdapter` 是库存/任务运行态权威；模拟器只允许显式选择/测试；Design Revision 不持久化库存、任务等运行事实。
- 已交付 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory` 与 `GET /api/space/design/v1/sites/{siteId}/runtime/tasks`，均要求 `space:model:read`，支持重复 `locationLogicalId` 筛选、Space/WMS 双 LogicalId 与双编码。
- 查询按 500 个位置分块、最多 10,000 个位置；来源/输出合同违例失败关闭为 502，适配器异常为可重试 503；明确 `Unavailable` 返回空 `Items` 并携带 `IsAvailable=false`，不与真实空结果混同。
- 该节记录 E08-S01 完成时的后续建议；E08-S02 现已由上方最新状态接续完成。E08-S01 交付报告见 `docs/space/reports/e08-s01-unified-runtime-source.md`。

## E07-S05 完成状态（2026-07-31）

- E07-S05 存量 WMS 采纳与绑定已完成：独立采纳账本、刷新、分页、单项/批量绑定、空位放置、差异 Issue 同步、rowversion 并发、权限/OpenAPI/SDK 和 Design V1 编辑器侧栏均已闭环。
- 功能提交 `15ccf992`，no-ff 集成提交 `389bf4ec`；交付报告见 `docs/space/reports/e07-s05-wms-adoption.md`。
- 验证：Space Unit 218；默认 Space Integration 56 passed / 48 SQL-gated skipped；WMS 聚焦 11/11，其中 KOUSQLSERVER 3/3；OpenAPI/权限 35/35；前端 98 files / 579 tests；production build、完整 solution build、EF model drift 和 SDK drift 均通过。
- E07-S01 至 E07-S05 已全部进入受控集成基线。该条记录的是 E07-S05 完成时的后续建议；E08-S01 现已由上方最新状态接续完成。

## Git

- 交付分支：`main`
- T6 通过 merge commit `d79a39c` 合入并推送；T7 冒烟修复为 `ffca422`
- Space E04 S06 功能/集成提交：`20f248bd` / `2b6ef127`
- Space E09 S05 功能/集成提交：`83798dcf` / `c658871c`
- Space E07 S05 功能/集成提交：`15ccf992` / `389bf4ec`
- Space E08 S01 功能/集成提交：`3df6b1d2` / `b2bb7a35`
- Space E08 S02 功能/集成提交：`9a478c7a` / `d4cd8a82`
- Space E08 S03 功能/集成提交：`8d8f7e01` / `dfb6e93b`
- Space E08 S04 功能/集成提交：`9f7e38f8` / `994339a6`
- Space E08 S05 功能/集成提交：`cc1d8baf` + `24464fab` / `7a05c05f` + `675e485c`
- Space E09 S01 功能/集成提交：`a599cfd7` / `09538ca3`
- Space E09 S02 功能/集成提交：`cae12c7e` / `feefa9cd`
- Space E09 S03 功能/集成提交：`88bc42d1` / `1850b2d8`
- Space E09 S04 功能/集成提交：`f045bd6f` / `c82d4fae`
- Space E10 S01 功能/文档/集成提交：`1c7aa0e2` / `1da17591` / `ec29d41f`
- Space E10 S02 功能/文档/集成提交：`e70c2715` / `86ad63bb` / `29a69a2b`
- Space E10 S03 功能/文档/集成提交：`10b16c51` / `8ce91d41` / `88efd23d`
- Space E10 S04 功能/文档/集成提交：`9a9802a8` / `f961d7e5` / `b4d5b81e`
- Space E10 S05 功能/文档/集成提交：`65c59555` / `53bea9b9` / `e270c2cc`
- Space E10 S06 功能/文档/集成提交：`0676ba4a` / `969e7c38` / `5f86edcb`
- Space E11 S01 合同/功能/文档/集成提交：`66b6c17f` / `53a07d46` / `a6d7a55c` / `8d4732e2`
- Space E11 S02 合同/功能/文档/集成提交：`3ccd2936` / `644293f1` / `034a1b1b` / `a2b47826`
- Space E11 S03 合同/功能/文档/集成提交：`3cf42534` / `419d3f6c` / `eea62de0` / `cf7bf778`
- Space E11 S04 合同/功能/文档/集成提交：`098fb54b` / `a7298e28` / `a552d05d` / `c19231db`
- Space E11 S05 合同/功能/文档/集成提交：`139c76b5` / `e8df8288` / `a0b247ab` / `cf35849c`

- 交付分支：`main`
- T6 通过 merge commit `d79a39c` 合入并推送；T7 冒烟修复为 `ffca422`
- Space 受控集成分支：`integration/space-v1-20260730`
- Space E00 + E01 S01–S03 集成提交：`539d56de`
- Space E01 S04 功能/集成提交：`bac76444` / `85792161`
- Space E01 S05 功能/集成提交：`3258d47f` / `36f534d9`
- Space E01 S06 功能/集成提交：`6daf1aeb` / `2ccdff7a`
- Space E02 S01 实验门禁功能/集成提交：`fe959066` / `3742fbff`
- Space E04 S01 功能/集成提交：`1d57a3b5` / `e8e84853`
- Space E04 S02 功能/集成提交：`20ee0af0` / `c1043d15`
- Space E04 S03 功能/集成提交：`b322e84a` / `39146c38`
- Space E04 S04 功能/集成提交：`9a87dc30` / `f9c7fd21`
- Space E07 S01–S03 功能/集成提交：`d06a8bd1` / `6e67a9d1`
- Space E07 S04 功能/集成提交：`74577015` / `6d751e0c`
- Space E13 S01 功能/集成提交：`8f7fc25e` / `ea161975`
- Space E13 S02 功能/集成提交：`cff25a25` / `94822669`
- Space E13 S03 功能/集成提交：`cebd401a` / `dca6e19c`
- Space E13 S12 功能/集成提交：`54456946` / `b33929fb`
- Space E13 S16 功能/文档/集成提交：`0549a1f2` / `6ec0c02a` / `ad4de0b0`
- Space E03 S01 功能/集成提交：`033e8872` / `8521a701`
- Space E03 S02 功能/集成提交：`f1310b40` / `e0cc4964`
- Space E03 S03 功能/集成提交：`9d0a59e7` / `3571f677`
- Space E05 S01 功能/集成提交：`5bb0cdfb` / `49dbabe3`
- Space E05 S02 功能/集成提交：`2fc03681` / `3d554852`
- Space E05 S03 功能/集成提交：`00021f0a` / `a1edecef`
- Space E05 S04 功能/集成提交：`85b57960` / `888de795`
- Space E05 S05 功能/集成提交：`856f138c` / `a3864d9c`
- Space 历史基线文档提交：`407dcbea`
- Space 后续候选安全检查点：`checkpoint/space-candidate-20260730`（`0d25da4d`，不得整包合入）
- 远端：`origin`（GitHub 私有仓库）
- 换机标签：`migration-2026-07-18-ready`
- 数据备份：Git LFS 三对象，已推送并校验

## 当前波：Space V1 受控集成

| 范围 | 状态 | 证据 |
|---|---|---|
| E00 S01–S04 | 已进入集成基线 | `539d56de`；事实清单、兼容护栏、数据源契约、审计/可观测性 |
| E01 S01–S06 | 已进入集成基线 | `539d56de` + `85792161` + `36f534d9` + `2ccdff7a`；版本/来源文件/Job Ledger、Published→Draft Clone、Design API v1、生成 SDK、文件安全扫描与保留清理 |
| E02 S01 | 部分进入集成基线，最终签收受阻 | `fe959066` + `3742fbff`；中立审计/压力/运行证据/preflight 已集成；另有 20 份可重复生成的合成开发 DXF（L1～L5 各 4 份，五种 DXF 文件头），但正式 DWG 黄金集、授权、供应商包/凭据和冻结 Worker 尚缺 |
| E03 S01–S03 | 已进入集成基线 | `033e8872` + `8521a701` + `f1310b40` + `e0cc4964` + `9d0a59e7` + `3571f677`；标准 Excel 模板、版本化字段映射、隔离上传、异步预检、结构化问题与受保护错误报告 |
| E02 S02–S08、E03 S04、E04 S05、E13 S04 | 开发切片已进入集成，正式签收受阻 | 合成 DXF 已贯通中立 CAD IR、坐标、Inventory、Mapping、语义、问题定位、Excel/CAD 匹配、Review Workspace、CAD Parse 作业安全和 AI 外发最小化；均不替代授权适配器/生产 Artifact/权限审计/真实黄金集验收。E13-S04 no-ff 为 `8bc1114d` |
| E04 S01–S04、S06 | 已进入集成基线 | `1d57a3b5` + `e8e84853` + `20ee0af0` + `c1043d15` + `b322e84a` + `39146c38` + `9a87dc30` + `f9c7fd21` + `20f248bd` + `2b6ef127`；安全底图、坐标标定、通用元素属性、统一批量编辑与补偿命令，以及同一 Design Scene 的 2D/3D 只读预览和实际渲染结构一致性证明 |
| E07 S01–S05 | 已进入集成基线 | `d06a8bd1` + `6e67a9d1` + `74577015` + `6d751e0c` + `15ccf992` + `389bf4ec`；版本化能力合同、CP6 真实适配器、持久化幂等账本、标准模拟器、确定性标准仓与存量 WMS 采纳/绑定 |
| E08 S01–S05 | 已进入集成基线 | `3df6b1d2` + `b2bb7a35` + `9a478c7a` + `d4cd8a82` + `8d8f7e01` + `dfb6e93b` + `9f7e38f8` + `994339a6` + `cc1d8baf` + `24464fab` + `7a05c05f` + `675e485c`；统一 Published 运行源、双身份、来源新鲜度、库存定位、任务路径与 10,000 库位性能基线 |
| E09 S01–S05 | 已进入集成基线 | `a599cfd7` + `09538ca3` + `cae12c7e` + `feefa9cd` + `88bc42d1` + `1850b2d8` + `f045bd6f` + `c82d4fae` + `83798dcf` + `c658871c`；外部组织/成员、组合 Grant、字段策略/脱敏、Published-only Portal、跨租户阻断矩阵，以及访问审计和授权有效期即时重验证 |
| E10 S01–S06 | 已进入集成基线 | `1c7aa0e2` + `1da17591` + `ec29d41f` + `e70c2715` + `86ad63bb` + `29a69a2b` + `10b16c51` + `8ce91d41` + `88efd23d` + `9a9802a8` + `f961d7e5` + `b4d5b81e` + `65c59555` + `53bea9b9` + `e270c2cc` + `0676ba4a` + `969e7c38` + `5f86edcb`；人员/设备事件与运行投影、3D 叠加、库存空间筛选，以及仓库 KPI、面积/占用、ABC 与异常快照 |
| E11 S01 | 已进入集成基线 | `66b6c17f` + `53a07d46` + `a6d7a55c` + `8d4732e2`；内部只读运营诊断、路径覆盖/折返/停留/观测重叠、诚实库位占用压力、隐私边界、审计权限和 Viewer DIAG 面板 |
| E11 S02 | 已进入集成基线 | `3ccd2936` + `644293f1` + `034a1b1b` + `a2b47826`；内部上架推荐、不可变证据、首因排除解释、精确合并与空库位候选、权限审计和 Viewer PUT 面板 |
| E11 S03 | 已进入集成基线 | `3cf42534` + `419d3f6c` + `eea62de0` + `cf7bf778`；内部人员调度建议、真实待分配任务与人员双时点、确定性最大基数匹配、不可变证据、首因排除和 Viewer DSP 面板 |
| E11 S04 | 已进入集成基线 | `098fb54b` + `a7298e28` + `a552d05d` + `c19231db`；OA 审批、提交/终审人分离、最终事实重验证、真实 `MobileTask` 整批分派、幂等回执与失败关闭 |
| E11 S05 | 已进入集成基线 | `139c76b5` + `e8df8288` + `a0b247ab` + `cf35849c`；实时执行状态、三层幂等回执、受限人工重试、安全整批补偿、权限审计和 Viewer 执行治理 |
| E13 S01–S13、S16 | 已进入集成基线；真实外部 Provider 仍关闭 | Provider/确定性端口、Run/Proposal/Decision/Usage、可恢复 Worker、最小化/本地生成/输出校验/融合/审核/决策/原子 Apply/恢复、同源人工锁接线、RuleOnly PreviewSet→AwaitingReview 生产执行、外部主体与外发门禁，以及数据库配额和策略/用量 UI |
| E05 S01–S05 | 已进入集成基线 | 通用元素、逐层货架、统一场景 DTO、版本化资产库及确定性参数化 3D 渲染 |
| E06 S01–S02 | 已进入集成基线 | `c17242c3` + `76c70230` + `a174f7cc` + `5bd2c616`；权威 ValidationRun 与来源/规则/WMS 能力冻结、确定性版本差异/WMS 影响预览、稳定 PlanHash、权限审计和 SDK；两卡真实 SQL 各 3/3、完整 Release 与漂移门禁通过 |
| E02 S02–S08 正式签收、E03 S05、E06 S03–S06、E13 S05～S11 正式外部链验收、S14～S15/S17～S19 等剩余范围 | 候选证据或尚未实现 | 生产 CAD 链、正式输入、Hosted Worker、权威 Match Artifact、持久化发布编排/重试/回退/UI 和真实外部 Provider 证据仍需逐卡解除；`0d25da4d` 只作提取来源，不得以候选报告或开发切片替代正式集成验收 |

## 上一完成波：GR-VP

| 任务 | 状态 | 证据 |
|---|---|---|
| T1 标准一般用户角色种子 | 完成 | `ddcfa1ac`，7 测试 |
| T2 OA/WF v-permission | 完成 | `15823c38`，40 按钮/17 视图 |
| T3 ERP v-permission | 完成 | `4a48525e`，39 按钮/16 视图 |
| T4 MES v-permission | 完成 | `6e4ade1`，31 指令/12 视图/24 键 |
| T5 FIN v-permission | 完成 | `5732057`，66 指令/16 视图/51 键 |
| T6 PUR/PLAN/PUB v-permission | 完成 | `4bb7512` + `cf20d42`，37 页面级声明/12 视图/33 键；异步加载守权 |
| T7 部署与冒烟 | 完成 | API/Web 双镜像；A1 角色 SQL；`qa_general` 自审批/越权/403 冒烟 |

## 最近验证基线

- E06-S02 已推进至受控集成提交 `5bd2c616`：功能提交 `a174f7cc`；引擎聚焦 17/17、API/权限/OpenAPI 55/55、Space Unit 448/448、CP6.Tests 2794 passed / 17 environment-gated skipped、默认 Space Integration 259 passed / 89 SQL-gated skipped、本卡真实 SQL 3/3、完整 solution 双架构 AOT Release 0 warning / 0 error、EF/SDK drift 和差异检查均通过。
- E06-S01 已推进至受控集成提交 `76c70230`：功能提交 `c17242c3`；Space Unit 440/440、CP6.Tests 2793 passed / 17 environment-gated skipped、默认 Space Integration 259 passed / 86 SQL-gated skipped、本卡真实 SQL 3/3、完整 solution Release 0 warning / 0 error、EF/SDK drift、幂等增量 SQL 双执行和差异检查均通过。
- E11-S05 已推进至受控集成提交 `cf35849c`：合同 `139c76b5`、功能 `e8df8288`、文档 `a0b247ab`。功能分支全量门禁为 Space Unit 249/249、默认 Space Integration 230 passed / 62 SQL-gated skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 658 tests、完整 solution Release、生产构建、EF/SDK drift、TypeScript SDK strict no-emit 和差异检查通过；合并态服务/适配器 14/14、权限/合同/种子 35/35、前端 21/21、类型、SDK drift 与 EF pending model 通过。i18n 仍为 908 项既有欠账，本卡净新增 0。
- E11-S04 已推进至受控集成提交 `c19231db`：合同 `098fb54b`、功能 `a7298e28`、文档 `a552d05d`。功能分支全量门禁为 Space Unit 249/249、默认 Space Integration 224 passed / 62 SQL-gated skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 656 tests、完整 solution Release、生产构建、EF/SDK drift 与两个 TypeScript strict no-emit 通过；合并态审批服务/适配器 8/8、权限/合同/种子/基础设施 44/44、前端 19/19、类型、SDK drift 与 EF pending model 通过。i18n 历史欠账由 909 降至 908。
- E11-S03 已推进至受控集成提交 `cf7bf778`：合同 `3cf42534`、功能 `419d3f6c`、文档 `eea62de0`。Space Unit 249/249、默认 Space Integration 216 passed / 62 SQL-gated skipped、CP6.Tests 2752 passed / 17 environment-gated skipped、前端 118 files / 653 tests、完整 solution Release、EF/SDK drift 与生产构建通过；合并态引擎/运行时 6/6、服务/适配器 6/6、权限/审计/API/种子 23/23、前端 16/16、类型与 SDK drift 通过。
- E11-S02 已推进至受控集成提交 `a2b47826`：合同 `3ccd2936`、功能 `644293f1`、文档 `034a1b1b`。Space Unit 245/245、默认 Space Integration 211 passed / 62 SQL-gated skipped、CP6.Tests 2748 passed / 17 environment-gated skipped、前端 117 files / 648 tests、完整 solution 非增量构建 0 error / 10 条既有 warning、EF/SDK drift、两个 TypeScript strict no-emit、production build 与差异检查通过；合并态引擎 5/5、服务 6/6、权限/审计/契约/种子 34/34、前端 14/14 和 SDK drift 通过；i18n 欠账由 911 降至 909。
- E11-S01 已推进至受控集成提交 `8d4732e2`：只读运营诊断、Real-only 人员证据、路径/折返/停留/观测重叠、当前库位占用压力与真实容量不可用边界完成；Space Unit 240/240、默认 Space Integration 205 passed / 62 SQL-gated skipped、CP6.Tests 2744 passed / 17 environment-gated skipped、前端 116 files / 643 tests、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 通过。合并态引擎 4/4、服务 7/7、权限/审计/契约/种子 59/59、前端 12/12 和严格类型检查通过。35 个新界面键均有五语种子，i18n 欠账保持基线 911 项。交付证据见 `docs/space/reports/e11-s01-operations-diagnostics.md`。
- E10-S06 已推进至受控集成提交 `5f86edcb`：仓库 KPI、面积/占用口径、独立来源部分快照、ABC 分类和 Viewer 互斥覆盖完成；Space Unit 236/236、默认 Space Integration 198 passed / 62 SQL-gated skipped、本卡真实 SQL 3/3、CP6.Tests 2739 passed / 17 environment-gated skipped、前端 115 files / 639 tests、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 通过。合并态合同 23/23、Runtime/适配器 81/81、权限/OpenAPI 46/46、前端 25/25、类型检查和 SDK drift 通过。i18n 保留 881 项基线债务和本卡新增 30 项。交付证据见 `docs/space/reports/e10-s06-warehouse-overview.md`。
- E10-S05 已推进至受控集成提交 `e270c2cc`：货主、SKU、批次和容器精确 AND 空间筛选完成；运行合同 2/2、Runtime/适配器 68/68、权限/OpenAPI 45/45、前端 114 files / 632 tests、Space Unit 236/236、默认 Space Integration 190 passed / 61 SQL-gated skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK/TypeScript drift 通过，本卡真实 SQL 1/1。完整真实 SQL 矩阵 250 passed / 1 个已知基线失败。交付证据见 `docs/space/reports/e10-s05-inventory-spatial-filters.md`。
- E10-S04 已推进至受控集成提交 `b4d5b81e`：设备当前/告警投影、读取 API 和 3D 叠加完成；领域 2/2、设备服务 9/9、本卡真实 SQL 2/2、权限/审计/OpenAPI 70/70、前端 113 files / 629 tests、Space Unit 236/236、默认 Space Integration 189 passed / 60 SQL-gated skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 与两个 TypeScript strict no-emit 通过。完整真实 SQL 矩阵 248 passed / 1 已知基线失败；合并态设备 9/9、权限/审计/OpenAPI 70/70、前端聚焦 14/14、EF/SDK drift 通过。交付证据见 `docs/space/reports/e10-s04-device-runtime-overlay.md`。
- E04-S06 已推进至受控集成提交 `2b6ef127`：功能分支与合并态前端全量均为 108 files / 612 tests，聚焦 4 files / 13 tests、type-check 和 production build 通过；Space Unit 231/231、默认 Space Integration 140 passed / 55 SQL-gated skipped、Design Scene 真 SQL 3/3、Space Integration + KOUSQLSERVER 195/195 且 0 skipped、CP6.Tests 2720 passed / 17 environment-gated skipped、完整 solution 非增量构建 0 error / 10 条既有 warning，以及 SDK drift、TypeScript SDK strict no-emit 和差异门禁均通过。
- E09-S05 已推进至受控集成提交 `c658871c`：审计聚焦 48/48、Portal/真 SQL 合并态 16/16、Space Unit 231/231、Space Integration + KOUSQLSERVER 195/195、CP6.Tests 2720 passed / 17 environment-gated skipped、完整 solution 非增量构建 0 error / 10 条既有 warning、前端 106 files / 607 tests、EF/SDK drift 与 TypeScript SDK strict no-emit 均通过。
- E09-S04 已推进至受控集成提交 `c82d4fae`：功能分支门禁为 Space Unit 231/231、Space Integration + KOUSQLSERVER 187/187 且 0 skipped、CP6.Tests 2713 passed / 17 environment-gated skipped、完整 solution 0 warning / 0 error、前端 106 files / 607 tests、EF/SDK drift 与 TypeScript SDK strict no-emit；合并态聚焦门禁为 16/16、42/42、完整 solution 0 error / 10 条既有 warning、前端 607/607 及 EF/SDK drift。
- E09-S03 已推进至受控集成提交 `1850b2d8`：功能分支门禁为 Space Unit 231/231、Space Integration + KOUSQLSERVER 181/181 且 0 skipped、CP6.Tests 2711 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、C#/TypeScript SDK 编译与 S02→S03 幂等增量 SQL 双执行通过；合并态聚焦门禁为 3/3、22/22、84/84、前端 607/607 及 EF/SDK drift。
- E09-S02 已推进至受控集成提交 `feefa9cd`：功能分支门禁为 Space Unit 228/228、Space Integration + KOUSQLSERVER 169/169 且 0 skipped、CP6.Tests 2703 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、C#/TypeScript SDK 编译与 S01→S02 幂等增量 SQL 双执行通过；合并态聚焦门禁为 4/4、10/10、35/35 及 EF/SDK drift。
- E09-S01 已推进至受控集成提交 `09538ca3`：功能分支门禁为 Space Unit 224/224、Space Integration + KOUSQLSERVER 159/159 且 0 skipped、CP6.Tests 2703 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、TypeScript SDK 和运行时客户端表面通过；合并态聚焦门禁为 4/4、6/6、49/49 及 EF/SDK drift。
- E08-S04 已推进至受控集成提交 `994339a6`：功能分支门禁为 Space Unit 220/220、Space Integration 105 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 105 files / 603 tests、type-check、production build、WebApi/C#/TypeScript SDK build 与 SDK drift；合并态聚焦门禁为 runtime 47/47、OpenAPI/SDK 18/18、前端 9/9、type-check 和 SDK drift。
- E08-S03 已推进至受控集成提交 `dfb6e93b`：功能分支门禁为 Space Unit 220/220、Space Integration 101 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 103 files / 597 tests、type-check、production build、WebApi/C#/TypeScript SDK build 与 SDK drift；合并态聚焦门禁为 runtime 44/44、OpenAPI/SDK 18/18、前端 5/5 和 type-check。
- E08-S02 已推进至受控集成提交 `d4cd8a82`：功能分支门禁为 Space Unit Release 220/220、Space Integration Release 96 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 102 files / 593 tests、type-check、production build、C# SDK build 与 SDK drift；合并态聚焦门禁为 runtime 40/40、OpenAPI/SDK 18/18、前端 20/20 和 type-check。
- E08-S01 已推进至受控集成提交 `b2bb7a35`：功能分支全量门禁为 Space Unit 220、默认 Space Integration 94 passed / 48 SQL-gated skipped、OpenAPI/权限/数据源合同 45、完整 solution build 0 error / 10 个既有 warning、EF/SDK drift 均通过；合并态聚焦门禁为 23/23、56/56、34/34 和 SDK 无 drift。
- 历史 E04-S04 验证快照：当时集成代码提交为 `f9c7fd21`。合并态完整 solution 构建 0 error / 10 个既有 warning；Space Unit 213 passed，默认 Space Integration 48 passed / 45 SQL-gated skipped，Design Scene 真实 SQL 3/3 passed；OpenAPI/权限 25/25 passed；前端 96 files / 575 tests、type-check、production build 通过；EF 无待迁移模型变化，SDK drift 通过。
- E02 S01 实验门禁已推进至 `3742fbff`：中立工具 10/10 测试通过，Aspose 隔离实验适配器构建 0 warning / 0 error；5 个冻结 Seed 完整性通过，50MiB 与 100 万实体压力资产生成通过。严格 readiness 按预期退出 `3`，ODA/APS 模板 preflight 按预期退出 `4`，表明外部签收条件仍未满足。
- E02 合成开发语料新增 `development-v2.0.0`：仓库内生成器可重复生成 20 份 DXF，L1～L5 各 4 份，覆盖 AC1009/AC1015/AC1021/AC1027/AC1032 以及块/属性/填充/样条/椭圆/标注/XRef 等开发场景。工具测试 12/12、数据包完整性、哈希、句柄和 DXF 文件头矩阵通过；清单明确 `countsTowardReleaseGate=false`，不替代原生 DWG、供应商授权和正式黄金集。交付证据见 `docs/space/reports/e02-synthetic-development-cad-corpus.md`。
- E07 S01–S03 已推进至 `6e67a9d1`：Release 全解构建 0 error（7 个既有测试 warning），Space Unit 73 passed，Space Integration 35 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2674 passed / 17 environment-gated skipped，Client 71 passed，EF 模型与 Migration 一致；新增代码精确格式门禁通过。
- E07 S04 已推进至 `6d751e0c`：500 货架、10,000 库位、100 SKU、5,000 库存记录、100 拣货任务和 6 个固定故障样本由同一固定种子生成；两次独立生成的 17 个文件差异为 0，干净检出后的 Manifest 16 个受管文件哈希错误为 0。合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 79 passed，Space Integration 40 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2680 passed / 17 environment-gated skipped，Client 71 passed。
- E13 S01–S03、S12 已完成 Provider 安全端口、运行审计模型、可恢复 Worker 控制面、三并发槽和日/月预算原子账本；外部 Provider、CAD IR、输出校验与 Apply 仍未提前启用。
- E03 S01–S03 与 E13-S16 已推进至集成提交 `3571f677` 和 `ad4de0b0`；最新门禁为 CP6.Tests 2733 passed / 17 environment-gated skipped、前端 112 files / 622 tests、完整 solution 0 warning / 0 error、EF/SDK drift 通过。E13-S16 交付证据见 `docs/space/reports/e13-s16-ai-policy-budget-usage-ui.md`。
- E10-S01 已推进至集成提交 `ec29d41f`：人员事件合同、追加式账本和双时间游标投影完成；Space Unit 234/234、默认 Space Integration 175 passed / 58 SQL-gated skipped、本卡真实 SQL 2/2、CP6.Tests 2734 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK/TypeScript drift 通过。交付证据见 `docs/space/reports/e10-s01-personnel-event-contract.md`。
- E05 S01–S05 已完成通用元素、非均匀逐层货架、Design Revision 权威场景、System/Tenant 版本化资产库和 `space-parametric-v1` 确定性前端渲染链；资产不加载外部 URL 或脚本。
- E04 S01–S04 已完成 PDF/PNG/JPG 底图、安全扫描、挂接、标定、通用元素属性，以及货架/元素共享的框选、对齐、等距、旋转、删除、阵列和补偿式撤销/重做；命令继续保持 Draft/revision 失败关闭、协议幂等、整批原子性和逐命令 before/after 审计。默认扫描器继续失败关闭，多副本生产环境必须配置真实扫描引擎与共享耐久卷。
- Space 集成前端：type-check 通过，96 files / 575 tests passed，production build 通过；仅有既有大 chunk 提示。
- CP6.Tests 全量本轮为 2682 passed / 6 个既有 RFQ 固定日期失败 / 17 environment-gated skipped；同一 RFQ 失败已在 S03 前的 `f8dff096` 基线复现，不是 Space 回归。
- 后续候选检查点 `0d25da4d` 已独立通过更大范围候选回归，但它仍不是实现真相，也不授权整包合并。
- 后端在 GR-VP T1 报告中：2220 passed / 5 skipped。
- 前端在 T7 干净 `main` 重新验证：73 files / 488 tests passed，type-check 0，2649 modules production build 通过；在线 Web 与新 chunk 均为 200。
- T6 后端权限 oracle：11/11 passed。
- T7 真实权限链：4 菜单、8 动作；本人待办 200、他人待办 400、无权端点 403；测试流程数据已清理。
- 这些是最近任务报告基线，不代表生成本知识库时重新运行了全量测试。

## 数据状态

- `CP6DB`、`CP6DB_OA`、`CP6DB_SpaceQA` 已于 2026-07-18 备份。
- 三份均通过 SQL Server `RESTORE VERIFYONLY WITH CHECKSUM`。
- 新机恢复后需重新轮换 Secrets 并做登录、权限、i18n 与关键业务冒烟。
- 当前运行库 `CP6DB` 的租户注册表只有 `DEFAULT/A1`；RoleId=10 为 1 角色/4 菜单/8 动作，admin 为 148 菜单/323 动作。`qa_general` 保留为 A1 常驻测试用户。

## 下一动作

`main` 同步和 P2.5 受控整合已经完成，不再重复处理候选分支。下一张不依赖外部输入的 Space 卡优先实现无人工锁时的确定性父关系推导：只有唯一、可证明的几何包含关系才能生成 Zone/Aisle/Rack 父关系，无匹配或多候选继续 Blocking。不同 SourceHash 的几何建议继承与人工确认另开产品卡。GR-VP T1–T7 已完成，不要重做；其 PMS/Sys 权限 UX、角色显示名和 insert-only 基线语义仍按 `06-Todo.md` 的 P1 独立处理。正式 CAD/黄金集、外部 Provider、S14～S15/S18～S19、E12-S06、生产迁移与 WMS 发布/恢复演练继续作为独立失败关闭门禁；禁止把候选检查点 `0d25da4d` 整包合入。
