# Space Studio V1 WP6/WP8 正式发布演练证据

日期：2026-08-28
DeliveryOwner：`BUBAO.GAO`
应用提交：`21a81767a0155a8cc92325acc3e3cdcc076ee930`
结论：`Pass`

## 结论

WP6 的 Publish/WMS、安全与恢复结果以及 WP8 的一次受控发布演练均已通过并由唯一 DeliveryOwner 接受。Core GA 的两类外部输入、WP0～WP8 九个阻断 Gate 和唯一签署均已闭环，派生状态为 `GaReady` / 100%。

该结论是本机受控、非生产的 V1 Core GA 结案，不代表生产数据验收、生产 WMS 时间窗口、客户现场 Pilot 或生产部署已经发生。Release/CD 的生产候选、环境审批和部署门禁继续独立执行。

## 本次实际执行

运行器在干净工作树上绑定完整应用提交，并使用 SQL Server Express LocalDB `17.0.4025.3`、完整 CP6 迁移、产品 `Cp6SpaceWmsAdapter`、`Real` 数据源分类及 Kestrel loopback HTTP 执行。WMS 超时和部分写入只在产品适配器外层使用受控故障注入；没有连接或声称生产 WMS。

| 门禁 | 结果 | 实测 |
| --- | --- | --- |
| Publish / WMS / Published 隔离 / 恢复 SQL | 8/8 通过，0 failure，0 skip | Release 测试耗时 119.321 秒 |
| 外部主体 HTTP/JWT 负向 | 1/1 通过，0 failure，0 skip | Kestrel + 验签 JWT；Customer、Supplier、3PL 控制面均为 403 |
| 自动恢复 | 通过 | 配置并观测到 5 秒，门槛不超过 900 秒 |
| 人工备份恢复 | 通过 | 3.6480777 秒，门槛不超过 14,400 秒 |
| SQL 备份完整性 | 通过 | `BACKUP WITH CHECKSUM`、`RESTORE WITH CHECKSUM`、`DBCC CHECKDB` |
| 恢复后业务状态 | 通过 | Published Hash 与 WMS 写入计数恢复正确 |
| 备份体积与清理 | 通过 | 7,524,352 bytes；临时数据库与 `.bak` 已删除 |
| 重试一致性 | 通过 | 旧 Published 持续可用、部分写可对账、幂等重放无重复写 |

JWT 使用本次测试进程内随机生成的临时签名密钥，仓库和证据中没有写入 Secret。外部角色只能读取 Published Portal；Portal 写入被拒绝，内部签名主体可以进入受保护控制面。

## 冻结基线复核

以下结果在本次演练中重新运行各自的正式 Manifest 校验器并核对 SHA-256，没有把它们描述成本次重新执行的完整业务套件：

- WP2 CAD Start：`03e38a47b84097b72067b30786078e3e6b6f8ab479b84a89e9007cff700b94b7`；
- WP4 三路径：`b3c65a8aa8b34c01b3d8e2816c52bd0a1fdc6edf39ed87714f2b1e3269b23069`；
- WP7 20 份黄金 CAD：`892ce026cb6a4d9924ddc492c07204d5e7c5e182290d812fc81c8263c75b9a46`；
- WP5 Published-only Viewer：`ae2a5ee962c0f15f170adcd3ea0c821631521bd4e853485c7700544fcbe86c12`。

冻结身份继续为 Source Set `7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955`、Golden Dataset `2b9438e09e2953b169770d0ee9292d8f9cc9ed697337111bcb61b913484b1f15` 和 Worker Environment `c9bbbe362a01e951379d60990f227fc4d5634ac9c86534f009f1d7e87d601717`。

## 不可变证据

仓库内正式 Manifest 为 `docs/space/acceptance/v1.3-ga/release-rehearsal-formal-evidence-v1.0.0.json`，SHA-256 为 `59bac2addb20a63eacde5779689b3acb6850a2ce4e390cba04c997c90cbe6501`。仓库外原始证据以 URN 引用并由正式 Manifest 固定：

| 证据 | SHA-256 |
| --- | --- |
| Execution | `fd7635f9f237e7af956ad4c8880c9bfba6d107818edde79942e4e75976c0bdb7` |
| Publish/WMS | `e8f0037ee0b7783d6dd1c1dcd4642efbd306f62a15f43a691a15822c8950f177` |
| Recovery | `b94535ed012ea2ae0762abd2426bee05553e1e6c49d74155f27fc55dca5604c1` |
| HTTP Security | `6cd0d646ca6d09d2e9371f43a94fe3388c187b8af500e11f4041bee0b7cc0a77` |
| Integration TRX | `66698ea21e5df76f94fe41cef37f81f42a900e852fd8e6aa1bd6987c7edf2748` |
| HTTP Security TRX | `b75c9bc753f06587ff61f685fdf82a8acd06655e3334e74048998479cc34b770` |

正式 Manifest 还绑定了 10 个产品、测试和运行器源文件的 Git Blob OID；校验器要求这些 OID 同时匹配被测提交和当前 HEAD，任何实现漂移都会失败关闭。

## 接受与边界

`BUBAO.GAO` 于 `2026-08-28T08:26:51.0339206Z` 完成可重复自审、WP6/WP8 接受及 DeliveryOwner 最终签署。开放 S1、S2 和 Blocking S3 均为 0。

本次明确为：

- `productionDataClaimed=false`；
- `productionWmsClaimed=false`；
- `productionDeploymentPerformed=false`；
- `pilotRequired=false`；
- `distinctPersonReviewRequired=false`。

因此，V1 Core GA 可以按单人交付规则结案；Backup Provider、现场 Pilot、生产 WMS 联调和生产部署均作为后续独立工作处理，不能反向改写本次证据。
