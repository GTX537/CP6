# BUG-887：条码配置最终授权与整批失败清理

2026-11-10 UTC。[Issue #887](https://github.com/GTX537/CP6/issues/887)，原 WP02-03 / CTX02-A4/A7 / PGT-017 的三个现存配置写入口。COORD 单人复用 DEV-2，分支 `codex/bug-887-barcode-config-fence`，基线 `a8cae2da20f7e53c3f3342d4617741b61ae41183`。start 成功一次，官方租期始于 14:12:24.705212Z；原生开工前样本 119473（14:11:54Z），估计 40–70 分钟实际 Codex。48 小时目标为 172800 秒，下次原生校准 129600。

## 原问题与改动

profile upsert、alias upsert 和有效 XLSX import commit 只有请求早期的 barcode-manage 检查。实际暂停先提交后，三个受阻写请求均返回 200 并改变配置；原生角色动作撤销先提交后，配置请求仍返回 200。第二个导入行的原生插入故障还实际复现：原导入事务回滚、API 返回 500，但请求跟踪器仍保留失败实体，后续操作日志 SaveChanges 把一条失败别名插回数据库。完整快照确认 Alias 0→1，其他四类行保持。

两个服务现使用同一个内部 BarcodeWriteAuthority，复用现有 IamAdministrationV1.ExecuteCurrentAsync、真实签名会话与同一请求 Context，在共同认证根内按现有 `wms-mobile:barcode-manage` 决定当前 actor。两张条码配置表按同一顺序取得原生 SHARE ROW EXCLUSIVE 围栏，覆盖直接 SQL 配置竞争；最终复核当前会话和取消，沿用既有 IAM 回滚清理及提交未知协议。数据库失败返回 BARCODE_AUTHORITY_UNAVAILABLE 503；提交未知保持 BARCODE_OUTCOME_UNKNOWN 503，不盲目重试。

导入按原格式先解析和校验，只有有效批次真正提交时进入父事务。逐行采用已校验的内部 Upsert 原语，不嵌套独立 Owner，也不重复校验。一次失败回滚整个配置批次并清空请求跟踪对象，操作日志不能重刷失败记录。普通 alias/profile upsert 同样由当前 actor 写入审计用户名。两服务原单参数构造函数、公开接口、DTO、row-version 判断与既有业务错误保持；实际 Program 显式注入 IAM 和当前会话。三个 Controller 写方法复用 OrderOwnerResult，导入的原 InvalidOperationException 捕获不再吞掉 Owner 拒绝或不可用状态。

输入的既有规范化仅在拥有条件的边界执行：profile 的 Format 和 alias 的 BarcodeType 在原 Validate 中规范化，下游直接复用；导入逐行不再重复执行相同校验。原工作簿解析、preview、无效行汇总和 tenant-wide 配置语义保持，没有新增目标存在性、仓库/部门或字段管理合同。没有模型、迁移或共享认证 Owner 改动。

## 实际验证与复用边界

同一最终测试源码 SHA-256 `5aec08cf17b464a5a69dbdbb81b077d4d97353d5066c7169bbb92f5c42dcec07` 在原产品上完整复现五项预期失败，再由最终实现验证 **5 个实际 Main Kestrel / PostgreSQL 18.6 场景通过，0 跳过**。测试使用真实登录 Cookie/CSRF、原 HTTP 路由、实际 XLSX 和真实 PostgreSQL 锁/触发器；配置、原 Stock、流水、商业出库单以五类完整 JSONB 前后快照比较。

| 场景 | 原实现 | 修复后 |
| --- | --- | --- |
| 三个正常路径后的暂停先提交竞争 | 均 200，均写配置，未等共同认证根 | 均 401，原生观测均等待该根，五类行保持 |
| 原生撤销 barcode-manage 先提交 | 配置写入 200，数据改变 | 403，等待原认证根，五类行保持 |
| 第二行在第一行 SaveChanges 后原生故障 | 500；回滚后日志重刷出一条别名 | 503，五类行保持；删除测试故障后同一输入完整导入两条 |

三个完整业务场景还实际执行普通新增、profile/alias 更新、实际 actor、profile 解析结果、XLSX 预览零写入、有效整批提交、重新激活/登录后的原输入恢复、原 profile 语法错误/alias 非法转换率/导入无效类型汇总。转换率 1.23456789 原值保存；已有库存 9.12345678 和价格 12.3456 保持，没有库存或商业出库效果。业务错误保留原码；不以测试用例数或静态字段复述作为目标。

本机原件 `D:/CP6-local-test/CP6-72H-20261109/W443-01`：`red-build-03` / `red-hosted-02` 为有效原复现，`green-build-01` / `green-hosted-01` 为最终实现，包含 actual.json、TRX、原生等待、完整快照、native ERROR、编译输出及 stopped.json。故障用只触发一次的测试序列与原生触发器模拟暂时插入失败，以实际观察日志重刷；它们位于隔离测试库，finally 准确删除。测试后停止本次 API/测试 PostgreSQL，不处理用户数据库。

保留真实失败：red-build-01 把已有 Cookie 登录辅助函数误当字符串 bearer；red-hosted-01 重复插入共享菜单。分别修正测试类型和共享菜单复用后重新编译并执行完整五项原复现，不把夹具错误当产品失败或通过。Core/Web/Tests 正常 SDK 相关构建通过，Core 完整分析器沿既有资源限制关闭。未变 PostgreSQL 和项目依赖复用 W442 的正常 SDK 成功程序集，逐程序集核来源；没有手工删减产品类型构造假程序集。

## 集中审查与真实剩余范围

COORD 一次任务级完整 diff 审查实际 Context、当前签名 actor/动作、原生配置围栏顺序、父事务与内部逐行原语、跟踪清理、最终会话/取消、原 row-version/构造函数和业务协议、输入规范化、转换精度、原测试依赖及实际原生证据、状态同步和工作区生命周期。原操作日志仍按既有渠道保存；本窗证明其不能补写失败业务，不宣称它与配置写集、Outbox 或正式 receipt 全部同事务，PGT-013/016 等各保留真实范围。

工作流内容树仍为 `3355235636fd45116f5445fca32fc65e49f13003`，复用普通手动入口及生产受保护 Tag 过滤分析，推送/PR 前核候选与保护分支的当前引用和内容；无 Actions、工作流改动或部署。合并后核远端 main，再关闭 Issue、准确清理任务引用并调用 finish。报告生成时为待远端采用候选，最终分支处置、工作区状态和席位释放以 PR/远端证据及 finish 为准。

本窗只采用三个现存条码配置写者。read/parse 全面授权、完整字段注册和 V1 来源采用、tenant schema/source 采用、完整 TRACE、正式收发及盘点仍归原任务；没有把配置成功当作后续库存或新来源合同采用。原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED，余 337；正式 PostgreSQL 16 PASS、11 NOT_RUN，PGT-017 保持 NOT_RUN。模拟业务数据在本机隔离环境执行，不宣称生产推广。

同时核销 W442 的 #886 远端采用、准确任务分支删除、固定 DEV-2 干净释放后复用。W442 墙钟 2628.01116 秒，保守原生活跃区间约 43.77–44.15 分钟，短于 55–85 分钟估计；同类元数据窗口下调到 35–60 分钟。本窗另含真实 XLSX/整批跟踪恢复，因此按自己的实际前置估计 40–70 分钟。原生活跃时钟权威，租期和进程阶段不另加预算，W436 缺失活跃时间重建仍未解决。
