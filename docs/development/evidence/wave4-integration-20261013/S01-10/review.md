# S01-10 协调准入审查

结论：**ACCEPT_WITH_FORMAL_MAPPING_ADOPTION**。对 `e20740e37e20bc0210e0b32c5670c2bce962002a..d90be911bd54dc14bf9f11cdc73bf237c778d2f9` 的59文件完整任务范围未发现 Critical / Important 阻断。适配器与真实 PostgreSQL fixture 可采用；**COORD 必须先新增正式映射、登记这20项有版本补充，再完成状态/集成/远端main核对，不能把 PROPOSED 留着直接 DONE**。

本次仅只读审查和 scratch 报告，未执行数据库测试、连接数据库、启动/停止服务、Git写入、网络发布或 Actions。原完整S01计划、wave4执行包、准入原件和 D:/CP6/AGENTS.md 全部义务作为准绳；按 requesting-code-review 做一次任务级审查。

- CREDIT：42项原生字段/profile逐节点匹配（4 signed /33 nonnegative /5 positive）。最终字段域校验后以 BigInteger/Npgsql Numeric 绑定并用 GetFieldValue<BigInteger> 读回，数值与scale规范/null耦合完整。测试明确区分原始输入守卫与 numeric(38,0) 的先舍入行为。
- INV：66项/13表、51非空/15可空，字典pointer、列名/旧类型/nullability核对齐全；任意长ASCII整数text、数值scale和原wire三列耦合，不通过有限numeric/decimal。真实131073整数位用例通过。20项原scale8补充有17个原生金额字段与3个字典派生字段，区别详见 scale-adoption-review；Money/政策scale保持0..8。
- MAVG：原00/-0分子、未约分正分母与余数字面量独立保存；ACTIVE的5个可空字段实际非空、afterQty>0；EMPTY五字段全null且数量/金额为零。其余完整Rational codec、除法余数/成本算法采用仍由Owner负责；S01-04不变合同证据可按原范围复用。
- ATP：9个选定定义与 composed-14 只改schema，5列UOM保留完整CHECK/FK/终态、表示链及扣减关系。5处PLAN-IF-005原补丁在准确组合中存在，执行子集验证C排序replay锁先于evaluation及双方digest绑定；不把子集称完整恢复/授权/publish采用。适配器在绑定前校验且无Size/CAST截断；text守卫覆盖原始非法串，另保留尾空格typmod裁剪反例。
- 事务与资源：真实约束失败产生25P02，回滚后新连接确认完整写集为空；提交后新物理PID恢复CREDIT/INV/原UTF8与ATP全部5列128字符。owned runner只用55410和任务目录，核marker/路径/system identifier/OID/Owner/会话，受限writer无法DDL/SET ROLE；无其他client会话才普通DROP与smart stop，凭据/lease删除有回执。失败前尚未证明归属的资源保持fail-closed，由单独收尾证据解释，未强杀不明会话。

独立解析实际TRX：完整运行 **210/210**，pin/reconnect回归 **57/57**，生命周期回归 **1/1**，均0失败/0跳过。210个唯一testId；长UOM有截断后相同显示名，实际case身份不同。57项是重复及定向覆盖，不累计为267独立用例。实测PG **18.6**、Npgsql **8.0.8.0**，项目/产物为net8.0；SDK10.0.302来自开工探测/交付记录，未新执行环境探测。

复用绑定：11/11原件hash、42/42 CREDIT节点、66/66字典项、9/9 SQL定义、5/5恢复补丁和13项已有依赖源码pin一致。最终4份DLL与57项运行hash一致；生产/Entity/Npgsql产物与210项也一致。完整运行后Boundaries仅新增3行ATP重连校验，删除这3行可精确还原完整运行hash；最终TestInputs由57项执行覆盖；其后prepare_sources只加42个原生节点核对，不影响运行代码。Git blob与工作文件的差异已核为CRLF/LF，不能把源HEAD=e20740e3误称已含当时尚未提交实现。完整身份/命令/计数见 S01-10-original-validation.json。

真实失败继续保留：RED1失败、first-pg编译失败未运行数据库、second-pg/third-pg各210个共享fixture初始化失败及对应收尾；没有改为PASS。默认diff --check报告18401条证据CRLF尾空白；`git -c core.whitespace=cr-at-eol diff --check` 为0，原证据字节保持。工作树两份无关历史WP6超长路径缺失不属于59文件候选，未处理。

采纳步骤：保留原输入与提案；在 `eng/contracts/s01/postgresql-mappings.json` 正式登记 `CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0`，记录COORD决定、20项覆盖和proposal hash，保持全部已测语义内容。TestInputs已优先读取正式文件；静态核对路径/version/adoption及本报告semantic hash即可复用结果，任何语义变化仅做受影响回归。生产Owner表/所有旧writer切换、迁移/回退、租户schema/池/权限、完整业务对象及247运行场景仍未验收；41 REFERENCE_ONLY、ITER/REL缺件与WP05 PROPOSED不改变。
