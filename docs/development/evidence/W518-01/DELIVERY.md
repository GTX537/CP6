# W518-01 · 同库新旧 RMA 宿主兼容与旧写者拒绝

INTEGRATION-1 以已采用 Main `7783e38adc59b8213e0c1257d06ae102a274cf81` detached 验证。两个实际 Kestrel 宿主使用固定二进制，旧读版本来自 W359 `cedf2d031b46a391cabf96af996569570e98bdf1`，新版来自 W508 `93b4e5eb6f8056a0c2714167abe3e7167e7c8b1d`。同一独占 PostgreSQL 18.6 `w518_rma`，实际 OID 16386、system identifier `7707440422438334404`。复用正式 DatabaseInit、RMA commissioning、原 journal 和 draft 安装工具。租户、业务及登录资料为模拟输入。 [原 RMA 读取](../W359-01/DELIVERY.md) · [原存储切换](../W515-01/DELIVERY.md)

- 两个真实请求开始于 00:05:58.937543 UTC。旧宿主的 B 读取于 00:05:59.550673 返回 200，新宿主的 A 创建于 00:05:59.772927 返回 201。原生日志记录 B 仅查询其物理租户 schema。A 普通后端 13508 实际取得草稿锁、执行原写入并提交 xid 1287。原键重放得到相同 201、结果和 ETag。A 恰新增一份 head、version 及 COMMITTED 原回执，原输入摘要与保存字节一致。
- 相同实际 A 权限用另一个有效新键请求旧写入口，返回 `503 RMA_TENANT_ENTRY_UNAVAILABLE`。原生时间窗口无普通租户 SQL、无共享旧写入、无回执变化。停止新版后，仍运行的旧版分别用实际普通 A、B 登录读取各自对象，返回 200 和原 ETag。B、旧共享图、三处共享写保护、A/B Ready 和 Core/Space/IdentityPriority/ERP 四份迁移历史保持，计数 224/1/1/1。
- 首轮实际 HTTP 行为通过，私有比较器对 HTTP camelCase 与原 journal PascalCase 使用了直接对象比较。修正比较逻辑并核原输入哈希后，在新独占库执行 `native-02`，退出 0。原失败、制品身份、实际结果、SQL 和运行入口在 `D:/CP6-test-artifacts/W518-01`。两轮专用数据库和四个宿主均已停止。集成验证保留 detached、无任务分支，已通过官方 finish 释放。

本窗验证同一实际 RMA v1 模型的新旧宿主共存、写者拒绝及兼容读取。PGT-024 仍需发布兼容矩阵、A/B 两个已支持模型的实际绑定和共享最低版本。W351 的真实前向迁移及 C02 应用回退、W369 的共享失败/旧路径维持、W372 的旧 CLI 拒绝按各自实测范围复用。 [前向迁移](../W351-01/DELIVERY.md) · [共享失败验收](../W421-01/DELIVERY.md) · [旧 CLI 拒绝](../W372-01/DELIVERY.md)

PGT-022 现有真实缺件拒绝、同键旧 Pending 对账、现行撤权和全局键保护，以及 Platform 0.10.3 的旧票据拒绝、实际 epoch 1→2，按各自范围保存。当前 Main/CRM 对应队列未采用可授权切换 profile，仍需受控恢复按推进后的当前纪元验证旧归档准入。Stock #844 的 0.10.4 正式包解决派送 guard 兼容。已核对正式 feed 尚无 0.10.4，发布独立按现有授权门。 [缺件与原件](../W479-01/DELIVERY.md) · [现行对账](../W481-01/DELIVERY.md) · [Stock #844](https://github.com/GTX537/CP6/issues/844)

正式 PostgreSQL 21 PASS / 6 NOT_RUN，原询盘业务 60/60。本次 Main 分支同步单一报告与协调记录。Actions 触发和取消为零，部署为零。
