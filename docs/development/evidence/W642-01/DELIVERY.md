# W642-01 受限库存财务意图

关联 [#1156](https://github.com/GTX537/CP6/issues/1156) 与 [#1012](https://github.com/GTX537/CP6/issues/1012)。固定 Main 基线为 `e3548b6581e41f83440c858cbff5fd4a960c8606`，本地提交为 `e60ab2f5f9884f2f7e92b14e3e896a1cf240104e`。最终审查时已确认 `origin/main` 为 `2cae251e90fecc2cd6d49e0297c8c71a51624d25`，其新变更与本任务七个文件无重叠。

## 实现

原 `StockMovementWriteContextV1` 在库存、原流水和序列写入自己的事务后，以原 `StockFinBridge.Resolve` 的判断暂存固定 WMS → FIN 事件。固定函数以受信租户、原 native movement UUID、当前顶层 XID 和 PostgreSQL 存储投影绑定一笔流水。原请求的未舍入数量和单价进入 W636 事件，事件沿用已接受流水身份。函数从原流水读取分类和来源，原 W636 消费者继续按现行规则归类、核价和过账。库存原回执、库存通知、财务工作器和旧版调用契约沿用原有处理。函数按原流水绑定，无部门继承的合法路径无需生成额外映射。

财务函数由现有 NOLOGIN 私有角色拥有。获准 Stock LOGIN 只有对应的 `EXECUTE`，没有 `T_IntegrationEvent` 的表或列级权限。真实第二租户 LOGIN 对第一租户原流水的正常查询返回零行。新装通过原 `install-stock-movement-writer-v1 --stdin` 一并采用。现有安装在更新宿主前使用 `install-stock-movement-finance-v1 --stdin`，沿用同一物理目标和原 `ReadInstallationId`、已批准租户 LOGIN 的受保护输入。安装可用相同输入重复验证，OID 与原库存安装身份稳定。处理的是新业务写入，既有历史事件由 W636 消费逻辑按其原格式读取。

## PostgreSQL 18.6 实际验证

本地隔离数据库、真实 Main Kestrel 签名、真实受限 LOGIN 和模拟库存、部门、租户、科目账套。基线运行在任何产品改动之前，实际签名 V1 当时成功返回。原库存流水 `xmin=1023`，旧路径后来留下 `xmin=1027` 的失败财务事件，缺少 W636 版 `stockTransactionId`。闭账为明确模拟条件。断言失败保留在 `baseline-v1/results.trx`，实际写集保留在 `baseline-v1/permission-consumption-baseline-v1/w642-v1-baseline-intent.json`。

| 实际入口 | 暂停先提交 | 库存先提交 |
| --- | --- | --- |
| `POST /api/wms/stock/commands/v1/apply` | 原生等待，401，原键为 `UNKNOWN`，库存、事件、原回执和通知无新增 | 原库存、财务意图及回执顶层 XID 1039。重新启用后 GET 和同键 POST 均返回原 `TXN2026110001`。实际 FIN 以原 `2.000000004 × 1.00249` 过账 2.00 |
| `POST /api/wms/stock/commands/v2/apply` | 原生等待，401，同一原键无提交，无新建库存 | 经真实创建上下文新建库存。原流水、事件及回执顶层 XID 1095。保留原大小写与外围空白的 `InBoUnD`，原单价为 null，同键返回原 `TXN2026110002`，原事件由实际工作器结为 `SKIPPED` |
| `POST /api/wms/stock/apply` | 原生等待，401，库存与财务均无新增 | 原 `SCRAP` 调整及财务意图顶层 XID 1149，实际计价 7.50，保留原 `WM-MSG-071` 回执。旧入口仅返回原流水号，跟踪的是已提交事务与财务事件 |

两种顺序均观测到指定原生锁持有者与等待者。库存先行时，受限连接在固定暂存函数处等待，下游签名暂停等候 Main 根。暂停先行时，受限库存事务在原 `lock_graph` 等候持有根的暂停事务。V1/V2 的原库存通知也随原任务提交并仅有一份，现有 EF 保存点可使通知行显示独立子 XID。三条入口的运行证据在 `native-orders-2/results.trx` 及相邻 PostgreSQL 目录六份 `w642-*.json`。

- 真正 COMMIT 应答遗失。限定受限 LOGIN 的实际 PostgreSQL relay 在 COMMIT 完成后断开应答。HTTP 返回 503 `OPERATIONS_OUTCOME_UNKNOWN`。旧宿主与 relay 完全停止后，新直连宿主签名读回同一个原动作，同键 POST 返回同一流水和回执，原事件仍为 `PENDING`。真实 W636 工作器只过账一次，第二次空转数据库不变。库存、原流水、意图和原回执顶层 XID 均为 1021。见 `native-ack-1/results.trx` 和同目录 PostgreSQL 证据。
- 真实原安装升级。测试沿用原 movement 布局，更新宿主提前进入会在暂存处安全返回 503，当笔库存、原回执、事件和库存通知均回滚。采用同一安装两次保留固定 OID 39865，原库存和历史状态未改写。原前台键可再次实际提交并正确过账。已编译的 `CP6.TenantStorage` 对同一实际数据库重跑升级，返回 `INSTALLED`，原 `inspect-stock-read-v1` 返回 `VERIFIED` 且 OID 一致。见 `native-upgrade-1/results.trx`、相邻 PostgreSQL 快照与 `cli-verified.json`。
- 原 W636 相关八项测试实际通过。包括标准入库原金额、原事件恢复、原过账账套、库存盘点和报废、无单价、禁用处理后的历史保持。见 `w636-regression/results.trx`。
- 首次候选运行复用了入库 API 的成功消息断言，版本化库存接口实际返回 `OK`。只修正测试解码，保留失败 `native-orders-1/results.trx`。CLI 检查第一次将 psql 提取的 OID 以 JSON 字符串传入，工具正确返回 `NOT_APPLIED`。改用 JSON 数值后完成复验，保留 `cli-install-initial-input.err`。工具和测试构建存在基线已有的依赖告警。

## 本地发布与交接

`publish-verified-smoke.json` 绑定上述提交、七个源文件 SHA-256 与实际已测试的 Main、Core、Entity、PostgreSQL DLL 哈希。Debug PostgreSQL 发布逐文件清单 `publish-sha256.json` 含 310 个文件，清单 SHA-256 为 `3cdd6af31d0fa4d45c04f191b9d021d60e55fd6c5bb400b5ef65b5ad5e804f53`。实际发布宿主在独占 57169 运行，健康检查 200，V1、V2、legacy 三条匿名写请求分别为 401。隔离 PostgreSQL 使用 57160，曾验证的所有 57160–57169 监听和本任务 PostgreSQL、Kestrel、relay 都已停止。

`codex/w642-01` 在 DEV-1 固定路径保留本地提交，工作树干净，席位由协调方集成后释放。本次仅本地验证，无 GitHub Actions 运行、远程候选或生产安装。
