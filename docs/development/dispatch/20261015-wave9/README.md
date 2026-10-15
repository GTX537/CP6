# 第九轮：四对话并行实施

本轮将已确认能独立推进的代码工作交给 A/B/C/D；用户已授权分任务继续推进。四个独立工作树已从 Main `3a975dd39611f2911f02f2413fbb21419261af23` 创建，Platform 只读基线为 `1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`。派工 PR 合并后将干净工作树快进到该 main，实际 START 再记录准确源码 SHA。

| 对话 | 执行包 | 本轮结果 | 独占写入和运行资源 |
|---|---|---|---|
| A（原 WP01-01） | [WP01-01](WP01-01.md) | 采纳/运行分别加载信任配置 | acceptance_basis 工具与相关回归；无 DB |
| B（原 WP02-02） | [WP02-02](WP02-02.md) | Identity 四表隔离、ERP 同事务共存 | Core Tenancy、相关 EF 接入；PG 55436 |
| C（原 WP02-12） | [WP02-28](WP02-28.md) | 真实 Main 身份观测及证据驱动回退判断 | 宿主诊断与 Program 的注册/health 小范围；PG 55438 |
| D（原 WP02-26） | [WP02-16](WP02-16.md) | CodeGen 后端与页面的安全即时预览 | CodeGen 服务/Controller 的预览部分/UI；无 DB |

程序路径互不重叠；B 不编辑 Program，C 不编辑 Core 存储，D 不编辑 EF/元数据保存。共享项目配置、包引用/锁、公共合同/迁移历史没有分配并行写入权限。若确有新依赖或同一文件需要修改，向 COORD 提交具体差异再调窗口，不各自创建权威定义。端口占用时只换本任务的新空闲端口并记录，不停止他人服务；此类运行细节无需额外审批。

## 状态与依据

唯一台账是 [roadmap.json](../../roadmap.json)，机器可读派工见 [dispatch.json](dispatch.json)。COORD 保留共享台账、生成页、四份状态文件及串行合并窗口。A/B 旧窗口已归档；B 的 ERP 局部实现 PR223 已核对远端 main 后转交本轮唯一负责人 B。

- A：用户已采纳 v1.2，但独立机器信任尚未配置；只开放工具代码窗口。完整任务 PLANNED/NOT_CHECKED。
- B：直接前置已交付，存储整体 IN_PROGRESS/CONFIRMED；新负责人尚未 START。局部结果不等于真实 Owner/宿主及完整 17 PGT 完成。
- C：WP02-25/27 已 DONE，正式 0.10.3 已实际采用，完整任务 READY/CONFIRMED；缺少某项真实回退证据仍应保留未验证范围。
- D：设计明确将代码预览与采番分开，仅开放无持久副作用的预览实现；WP02-10 前置及完整任务 PLANNED/NOT_CHECKED 保留。

本轮没有新增 DONE：仍为 24/375；业务 0/125，247 修订场景 NOT_RUN，41 历史引用保持。四份执行包均为 ASSIGNED、START 未收到。Handoff 发布不自动唤醒对话；用户在对应对话回复“按第九轮执行包继续 <任务ID>”即可让其读取执行包并开始。不要把运输 ACK、预置分支或本次资料评估称为实现开工。

## 执行方式

按最新 AGENTS 的精简规则：实现只做受影响验证和一次集中审查，复用有效证据，一份交付报告；普通文档仅 diff/必要链接，不增加测试数字或证据包目标。本轮派工只自查差异、链接、台账/依赖与写入资源，不运行产品测试。只在代码交付涉及权限/租户隔离/并发等实际风险时保留相应定向检查。

工具可使用 `C:/Users/tt/.dotnet/dotnet.exe`（SDK8）、Python3.13、`C:/Program Files/PostgreSQL/18/bin`；现有私有 NuGet cache 为 `D:/CP6-local-test/WP02-02/adopt-a1/n`。源码/依赖不变的成功结果直接引用，包/项目配置仍由 COORD 串行安排。归档来源在 `D:/CP6`，Python manager 可用 `CP6_ACCEPTANCE_ARCHIVE_ROOTS=D:/CP6`；不复制原归档到每个工作树。

实现与资料分支分开。A/B 续用原任务 Handoff 分支；C/D 新建 `codex/dev-handoff-wp02-28` / `codex/dev-handoff-wp02-16`。协调入口是 `GTX537/CP6.AI-Handoff` 的 `codex/dev-coordination-20261012` 分支下 `handoffs/CP6-DEV-20261012/waves/20261015-wave9.md`。按固定提交读写一次真实 START 与交付，不重复等待相同范围准入。未经新的明确授权不运行 Actions/正式发包/迁移既有业务库/部署。

## 本次派工检查

四项实际源码入口、前置提交远端包含性和精确正式包采用记录已核对；四工作树创建后干净。保留原任务定义/依赖/独占资源，局部窗口写入范围单独登记；没有为并行删除原锁。台账生成/一致性、执行包必要链接和资源范围检查通过，执行者已集中自查本任务完整 diff。独立信任未配置导致的 adoption=false 是原有真实状态，不作为此次派工或工具修订的新增阻塞。无产品构建/测试、Actions 触发/取消/配置变更、数据库操作或部署。
