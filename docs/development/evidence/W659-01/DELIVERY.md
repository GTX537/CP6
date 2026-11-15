# W659-01 租户期限修改的持久封存

对照已登记 Main 基线 `382328801d062080426c97b5f9fdf1d87ed957c2`，覆盖已登记 CRM 的 `POST /api/platform/tenant/{id}/management-v1/commands` 原 `update` 及 `PUT /api/platform/tenant/{id}`。两套隔离 PostgreSQL 18.6、实际 Main/CRM Kestrel 和签名授权，租户、管理员及 CRM 业务数据均为模拟。 [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012) · [W649 持久暂停](https://github.com/GTX537/CP6/blob/382328801d062080426c97b5f9fdf1d87ed957c2/docs/development/evidence/W649-01/DELIVERY.md) · [W548 原期限两序](https://github.com/GTX537/CP6/blob/382328801d062080426c97b5f9fdf1d87ed957c2/docs/development/evidence/W548-01/DELIVERY.md)

## 业务提交

原 Main PostgreSQL 权限根先持久保存一份期限意图，固定原请求、actor 及 actor 租户、CAS 修订、CRM StoreId 和 C02 源版本。两类期限请求复用 W649 表、触发器和现有签发限制。恢复核对完全相同的原命令，复核物理前驱，在同一事务内清除意图并使用原 C02 采集推进 CRM 门槛。确认后一次提交期限、认证及管理修订、C02 快照、原普通 Outbox、审核和命令回执。失败时意图保留，已接纳原件按原回执恢复。产品改动集中在 TenantAdminService 的四个分部，持久意图复用已安装的 W649 迁移。

版本化保留命令查询、原输入指纹和 CAS。旧 PUT 以独立服务器键域绑定原 Store/前驱，原操作重试仍须同一 actor 和完整输入。已完成重放以仍当前的业务结果修订及 C02 后继识别，HTTP 保持 `code=0`。旧输入保留原请求值供匹配，实际期限按原 Npgsql 时间戳读取。只在已启用的真实期限 C02 变化提交意图。现有自然到期沿用时钟判断。

## 实际运行

| 场景 | 独立回读 |
| --- | --- |
| 原版两条入口 | 仅禁止隔离库专用 CRM 封存登录。两条实际 Main 请求原样返回 503，本库和 CRM 生效写集不变。两项基线都观测到新配置授权仍带当前写 fence。 [版本化 RED](D:/CP6-test-artifacts/W659-01/baseline-versioned/main.trx) · [旧 PUT RED](D:/CP6-test-artifacts/W659-01/baseline-legacy/main.trx) |
| 版本化与旧 PUT 冷恢复 | 首次拒接仍为 `AUTHORITY_FENCE_UNAVAILABLE` 503。Main 停止并换 PID 启动，原意图保持。新写 fence 停发，只读和原 CRM 结果仍可用。待确认换 actor、换输入、重启用与原生变更被拒。版本化按原键可查到 `TENANT_OUTCOME_UNKNOWN`。 |
| 两库原提交确认丢失 | 每条入口的 CRM 真 COMMIT 后断开确认，独立观察 C02 门槛 1→2、接收方技术 Revision 6→7，本库生效前值保持。恢复到 Main 真 COMMIT 后再断开确认，原请求读回唯一已完成结果。权限修订、原审核、命令及普通 Outbox 各一次，优先 Outbox 保持。 |
| 输入与无期限变更 | 旧 PUT 实际保存 200 UTF-16 单元的补充字符名称和原尾随空格，原期限 `.1234567` 回读为 PostgreSQL `.123456`。完全相同原输入恢复 `code=0`。另一个不同的七位原输入具有同一已存微秒，在 CRM 专用连接不通时仍能更新备注，C02 版本和期限门槛保持。 |
| CRM 业务先 | 原 W548 实际 CRM 导出持业务身份锁，Main 专用封存连接在 PostgreSQL 等待。原制品先提交并保留，Main 随后提交期限。 [Main TRX](D:/CP6-test-artifacts/W659-01/candidate-owner-first/main.trx) |
| Main 期限先 | 原许可已经签出，Main 已完成期限提交且后续 C02 消息未投递。CRM 拒绝迟到的原许可，制品、审核及业务 Outbox 保持。 [Main TRX](D:/CP6-test-artifacts/W659-01/candidate-authority-first/main.trx) |

原业务回归通过 34 项 Main，包含当前平台 actor、事务审计、版本化 CAS、原回执和 actor 自然到期。原 W649 旧暂停两库对照各通过 1 项。CRM 公共表单、attempt 及 COMMIT 的自然到期沿用同一产品和测试程序集已有的 3 项实际锁等待结果。 [Main 23 项](D:/CP6-test-artifacts/W659-01/tenant-management-regression/main.trx) · [版本化 11 项](D:/CP6-test-artifacts/W659-01/management-v1-regression/main.trx) · [W649 旧暂停](D:/CP6-test-artifacts/W659-01/w649-legacy-regression/main.trx) · [CRM 自然到期](D:/CP6-test-artifacts/W649-01/candidate-expiry/crm.trx)

最终版本化样本连同原非法时区 `400 E-WF-028`、已启用租户清空期限、已停用租户的期限更新通过。清空使 C02 门槛再推进一次。随后实际暂停将 CRM 置闭态，专用连接再次拒接时，Main 成功保存停用态租户的未来期限，CRM 的 `closed_through`、空 `reopened_at` 和接收方 Revision 保持。首次追加该控制时，夹具误把正常闭态的 `reopened_at=NULL` 当成数字。已保留首次 TRX，校正后复跑通过。 [最终版本化 Main](D:/CP6-test-artifacts/W659-01/final-versioned-closed/main.trx) · [最终版本化 CRM](D:/CP6-test-artifacts/W659-01/final-versioned-closed/crm.trx) · [写集事实](D:/CP6-test-artifacts/W659-01/final-versioned-closed/permission-consumption-main/coordination/deadline-versioned-facts.json) · [首次夹具失败](D:/CP6-test-artifacts/W659-01/final-versioned/main.trx)

旧 PUT 冷链及 W548 两序复用先前本轮同候选成功结果。末次产品调整只将版本化时区验证置于查找 C02 前驱之前。最终版本化冷链覆盖该先后顺序。期限联验 Main 4 项、CRM 4 项，另有 34 项 Main 及旧暂停两库各 1 项，零跳过。 [旧 PUT Main](D:/CP6-test-artifacts/W659-01/candidate-legacy/main.trx) · [旧 PUT CRM](D:/CP6-test-artifacts/W659-01/candidate-legacy/crm.trx) · [原期限写集](D:/CP6-test-artifacts/W659-01/candidate-legacy/permission-consumption-main/coordination/deadline-legacy-facts.json)

## 受测来源与分支

源码提交 `65851f0e58bdb82cf0116a314d0a5424f5f7203b`。六个本任务文件的 Git blob 均与受测记录逐个一致。CRM 沿 W649 实际宿主制品，产品源码 `8f5064db86bee2f0660fcb163a94d3496d7a39c2`。 [最终受测来源与程序集](D:/CP6-test-artifacts/W659-01/final-tested-source-and-assemblies.json)

本地发布逐一核对 WebApi、Core 和 PostgreSQL 迁移程序集的嵌入提交。发布清单 310 文件，SHA-256 `235AA9BC8BE27B266550CEC8E0A033779C28A533F4BA9392C2E9809EA84FB764`。隔离 PostgreSQL 57277 的 DatabaseInit 退出 0，原 W649 待确认表和触发器存在。发布宿主在 57278 实际返回健康 200，匿名版本化命令和旧 PUT 均为 401。 [实机结果](D:/CP6-test-artifacts/W659-01/main-publish-smoke/result.json) · [发布清单](D:/CP6-test-artifacts/W659-01/main-publish-smoke/publish-sha256.json)

分支 `codex/w659-01` 保留已验证的本地提交。DEV-2 工作树干净，所有任务进程已停止，57260–57279 无监听。席位仍由 W659-01 登记，协调方负责采用和释放。Actions 与部署均为零。 [停止与源码复核](D:/CP6-test-artifacts/W659-01/verification-stop.json)

## 采用 W658 后的组合验证

Main W658 #1178 为 `65b0cd7ccef2f23822f26bdecfec6df1348963e6`，受测组合为 `d559b5fb81af281a4c621953d315520c16e44290`。复用 W659 原编译缓存和 W649 的独立 CRM 测试宿主。隔离 PostgreSQL 18.6、真实 Main/CRM 宿主与签名，使用模拟询盘、导出与租户数据。 [组合来源](D:/CP6-test-artifacts/W659-01/combined-source.json) · [受测程序集 SHA-256](D:/CP6-test-artifacts/W659-01/combined-tested-assemblies.json)

| 实际组合场景 | Main/CRM | Main 运行时间 UTC | 观察 |
| --- | --- | --- | --- |
| 版本化冷恢复 | 1/1、1/1 | 21:41:26–21:42:54 | 首次拒接 503，Main 换 PID 后原请求继续。两库真 COMMIT 确认丢失后原回执唯一，CRM 门槛 1→2、Revision 6→7。 [Main](D:/CP6-test-artifacts/W659-01/combined-cold-versioned/main.trx) · [CRM](D:/CP6-test-artifacts/W659-01/combined-cold-versioned/crm.trx) |
| 旧 PUT 冷恢复 | 1/1、1/1 | 21:41:27–21:42:52 | 同一原 actor 和输入恢复 `code=0`。原 `.1234567` 按 PostgreSQL `.123456` 回读，两库确认丢失后写集无重复。 [Main](D:/CP6-test-artifacts/W659-01/combined-cold-legacy/main.trx) · [CRM](D:/CP6-test-artifacts/W659-01/combined-cold-legacy/crm.trx) |
| CRM 导出先 | 1/1、1/1 | 21:43:27–21:44:24 | Main 封存事务等待原 CRM 业务锁，合法制品先提交并保留。 [Main](D:/CP6-test-artifacts/W659-01/combined-owner-first/main.trx) · [CRM](D:/CP6-test-artifacts/W659-01/combined-owner-first/actual-main-worker-pause-20261115T2144021915783Z.trx) |
| Main 期限先 | 1/1、1/1 | 21:43:27–21:44:20 | Main 提交后，CRM 在后续 C02 事实投递前拒绝原迟到授权，无新增制品。 [Main](D:/CP6-test-artifacts/W659-01/combined-authority-first/main.trx) · [CRM](D:/CP6-test-artifacts/W659-01/combined-authority-first/actual-main-worker-pause-20261115T2143598509778Z.trx) |

2026-11-15 四组共 Main 4/4、CRM 4/4，零失败、零跳过。 [真实 TRX 时间、用例与端口](D:/CP6-test-artifacts/W659-01/combined-native-results.json)

从组合源码在独立新库实际初始化，迁移历史和原生对象均有 W649 `20261115190000_CrmTenantPendingSuspensionV1` 和 W658 `20261115210000_IamCrmRoleSealIntentV1`。发布的 WebApi、Core、迁移三个 DLL 与组合测试 DLL 逐字节一致。310 文件清单 SHA-256 `121FA9B2E8358750A9FEEB9B64474842F1C69780BE95C18CE0909B819CE8E4FE`。发布宿主返回健康 200、两条匿名写请求各 401。 [实机结果](D:/CP6-test-artifacts/W659-01/combined-publish-smoke/result.json) · [制品清单](D:/CP6-test-artifacts/W659-01/combined-publish-smoke/publish-sha256.json)

DEV-2 干净，组合提交保留在 `codex/w659-01`。所有任务进程已停止，57260–57279 无监听。席位仍登记为 W659-01，协调方采用后释放。Actions、远端修改及部署均为零。 [停止记录](D:/CP6-test-artifacts/W659-01/combined-verification-stop.json)
