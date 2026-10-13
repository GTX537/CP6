# S01-03 执行计划

授权入口：Main `8569b669fed56ddc7b53b7bb24666c7fb93d544a` 的第三轮 S01-03 执行包；COORD Handoff `ad1260fd0de832817d52522c6111df6bacd788b3` 的 assigned-wave3。独立工作树与 CONFIRMED 窗口已登记；共享状态由 COORD 同步。

1. 核对最新主线、S01-02 已交付代码、三个输入 pin、原生 FIN-CREDIT Schema 和 4/33/5 个字段绑定；发布 START。
2. 按 TDD 在 FinCredit 域实现显式 profile、BigInteger 系数/scale、精确比较和任意精度中间运算、最终字段校验及字符串 JSON converter。25 个固定 ID/预期 pair 必须逐项执行，未知/缺失/新增 case 失败。
3. 补充词法/错误 token/profile 误用、不同 scale、越界中间量恢复、负中间量后正结果、最终越界、Wire 重复键及原字节保持测试。保存真实 RED/GREEN、环境、源码/输入/依赖及产物 hash。
4. 对完整任务集中做合同审查，再做代码质量审查；修复只复查受影响部分。完成范围/diff/敏感信息与发布工作流检查，提交候选和 draft PR。
5. 用固定提交发布 DELIVERY，交给 COORD 串行审查、共享状态同步和合并；候选不冒称已进入远端 main。

现有 CreditControlService/ICreditControlService 的 decimal 接口与舍入保持原状。Wire、共用输入、csproj、根配置、其他任务文件只读；不运行 DB、迁移、Actions、包发布或部署。本任务只提供合同子断言，不修改原包 NOT_RUN、完整业务场景、历史接受及缺失记录。
