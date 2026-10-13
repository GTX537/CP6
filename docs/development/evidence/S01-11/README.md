# S01-11：ERP 贷项金额与冻结重放

本目录只交付第四轮分配窗口内的 ERP-CREDIT 合同编解码、字节保留与重放 guard。任务状态、共享项目状态和正常保护合并由 COORD 统一处理。

## 合同与实现

- current-v1 / frozen-R8 分别固定 profile、namespace、原生 Schema、TypeRegistry、root 和 SHA-256；完整对象通过准确三文件依赖闭包校验。[私有合同包](../../../../eng/contracts/s01-11/)保存可移植输入；[来源与 API 映射](../../../../eng/contracts/s01-11/surface.json)说明原件、组合修订和手工适配。
- 14 个原设计发布根返回完整、不可变的 native JSON 文档；Money / D8 提供两个具名标量辅助入口。WMS Money 保留 30 位整数、最多 8 位小数及合法 `-0` / `1.0`；FIN-AR 使用自己的 20 位整数 D8 字段域。不经 `decimal`、浮点或 FIN-CREDIT 规范化缩小输入。
- C# 复用 S01-02 的严格 JSON 与结构相等实现；TypeScript 保留精确数字词法至校验后。重复/转义等价键、缺必填项、未知字段、NullOnly、版本与合同身份均按完整根校验。
- 冻结原始命令保存实际 UTF-8 与摘要；C01/C02 保存完整首次 HTTP201 COMMITTED 正文，并原字节返回 HTTP200。guard 不实现 HTTP 服务或持久化 Store，也不把投影向量当作合法完整命令。
- 当前适配入口缺少可信 Owner / Store / issuer / 当前 AR/GL/member/finite 证明时，始终返回 `READ_ONLY_UNPROVEN` / `MANUAL_RECONCILIATION`，禁止据此确认 FOUND、释放或再次派发。

## 可追踪证据

最终本地结果为 C# 417/417、TypeScript 416/416（均零失败、零跳过）、前端 type-check exit 0、设计样例审计 5/5。每端含 10 条固定向量、362 条构造用例与 38 条选定设计投影，另有 6 个 guard；C# 多 1 个实际 .NET 8.0.29 断言。[独立回读](validation.json)按真实运行输入核验，并非重跑测试。[规格审查](review/20261013-spec-review.md)与[质量审查](review/20261013-quality-review.md)均通过。

- [执行计划](PLAN.md)、[开工来源核对](entry.json)、[START 固定提交回读](start-receipt.json)。
- [真实 Owner 边界](owner-boundary.json)、[原设计与修订索引](design-references.json)。
- `implementation/` 保留各次真实命令、运行环境、原始日志、TRX/Vitest 结果、执行前后输入及编译产物哈希，包括 RED 和首次类型检查失败。
- `check-evidence.py` 独立读取既有结果、逐 ID 核对固定向量与构造边界，并绑定实际输入字节；不会重跑或改写测试。提交后的源码绑定同时记录 Git blob 和工作树执行字节，避免混淆换行过滤。
- `check-publication.py` 在每次候选 push / PR 前刷新并检查候选和 protected main 的工作流事件、过滤及本地依赖；不触发 Actions。

## 验收边界

设计 fixture 的 Money、OpenAPI、rawRef / Store / receipt / fence 子断言与本地 codec 单元测试分别记录。它们不等于真实 Owner / Store / PostgreSQL / 权限 / 租户 / 事务 / HTTP 业务验收。原 247 完整运行场景仍为 NOT_RUN，41 历史静态条目仍为 REFERENCE_ONLY，ITER/REL 缺失原件及 WP05 PROPOSED 均不变。

三文件准确包不包含 F09-LOCAL-TYPES；没有开放 `EC_C06_200_StoredTerminal`、`EC_C06_202_StoredAdmission` 或 `WholeC06PublicRequest` 通用根。未来接线需要单独补齐可信生产者、完整依赖闭包与协调窗口。

既有 CreditNote Service / Entity / DTO / Controller 未修改，其历史数据库字段不在本任务中取得精确原生合同存储验收。未修改共享合同包、依赖配置或工作流；未操作数据库、运行 Actions、迁移或部署。

质量审查保留一项非阻断容量风险：区间重叠校验目前为逐对比较。小范围 TS 探针结果见质量报告，最大合法规模及 C# 容量未测；未来接线前须在保留合法域和精度的前提下改用排序扫描并验证容量。
