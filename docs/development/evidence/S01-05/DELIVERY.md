# S01-05：TypeScript 精确金额与数量

- 执行方：B；业务分支 `codex/s01-05-ts-exact-20261012`。
- 工作区：`D:/CP6-worktrees/s01-05-20261012`；实现及本地验证基线 `4ab979e2f391c5a5645da0d1586781e824a9f8cd`。
- 准入：WP01-02 的 S01-05 确认编辑窗口，S01-01 已 DONE。START 已发布至 Handoff `5c7eb281e7ce232a0e67cd466a73b18e4d002644`，并以固定 SHA 回读字节核对。
- 输入：`CP6-S01-CONTRACT-INPUTS/1.0.0`，固定源提交 `dfc043b1473dcea51d34ff87a7808692349c278a`。具体输入、源文件与验证日志 SHA-256 见 [verification.json](verification.json)。

## 已实现

1. `CreditAmount` 分开验证 signed/nonnegative/positive，保留 38 总位和 8 位小数上限，拒绝负零、尾零、前导零、指数及非字符串 token。
2. `InventoryDecimal` 分开验证 Decimal18/Qty12；无新增整数位上限，原 wire 与归一化 BigInt 系数/scale 分离。负零和尾零可原样输出；90 位固定向量及 200 位补充测试无损。
3. 不可变 `ExactDecimal` 提供 BigInt 加减、比较和规范文本；中间值不套用最终字段精度。写回时显式选择目标 profile，不舍入、不截断。禁止对象隐式数值转换。
4. 固定包的 34 条输入与预期在同一测试中读取；ID 集、数量和 operation 都有断言，不能静默忽略未知项。其他测试覆盖 JS 安全整数两侧、错误 token、词法、原 wire、运算、符号及最终字段溢出。

## 本地验证

环境为 Windows、Node v20.19.0、npm 10.8.2；使用现有锁文件 `npm ci --prefix cp6.web --no-audit --no-fund` 恢复 426 个包，退出 0。没有升级依赖或修改锁文件。安装提示已有 glob 版本弃用；未为本任务扩大依赖变更。

| 阶段 | 命令/结果 | 证据 |
| --- | --- | --- |
| 首次收集 | 测试标题 `%j` 序列化 BigInt 失败，0 项执行，退出 1；改为 `%s` | [collection-failure.log](collection-failure.log) |
| RED | 指定 Vitest 文件实际执行 202 项；100 失败 / 102 通过，退出 1。实现为显式未实现桩；不把收集失败当 RED | [red.log](red.log) |
| GREEN | `npm --prefix cp6.web test -- src/contracts/exact/profiles.test.ts`：202 通过，0 跳过，退出 0 | [green.log](green.log) |
| 类型检查 | `npm --prefix cp6.web run type-check`：`vue-tsc --build`，退出 0 | [type-check.log](type-check.log) |

34 个固定 case 的实际采用结果与补充测试源码身份记录于 `verification.json`。GREEN 与 type-check 之后只补充说明和证据文件；没有改动被验证的 TypeScript、输入包、依赖或配置。提交/合并 SHA 变化不触发相同业务验证重跑。

## 审查与交付边界

完整任务差异审查记录于 [REVIEW.md](REVIEW.md)。共享总台账、生成视图及四状态页由 COORD 在交付窗口统一修改。此报告不是 main 合并证明；远端包含关系由 COORD 合并后核对。

本地通过仅证明 TypeScript 合同单测及现有前端类型检查。原输入包仍为 NOT_RUN，没有把其历史预期改成业务执行结果。C#/TS/PG 联合比对、真实业务 Owner、页面输入迁移、权限、持久化、库存/财务效果和完整验收均未执行。

Actions：本任务没有排队/执行中的编译测试运行需要取消，没有主动触发或重跑 Actions，没有修改触发配置。候选与已确认 origin/main 的 12 份普通/推广工作流为手动入口；R2 candidate 仅受 `v*.*.*` tag push 触发，普通分支 push 不匹配；Azure 桥 trigger/pr 均为 none。没有创建 tag、部署或迁库。
