# W261-01 / CodeGen差异与覆盖范围纯预览

CodeGen新增固定模板差异与覆盖范围纯预览：精确建议路径、用户提供原文件、无损自定义区保留、线性差异及逐文件覆盖白名单，实际API和Vue页面已验证。没有文件应用或元数据持久化。

见[比较合同](../../codegen-comparison-v1.md)及[执行计划](../../planning/20261102-codegen-comparison.md)。Main从`457e4a6b7ef58108aa9e9db2cb9d75fefed193c8`独立分支codex/w261-01，COORD单人使用DEV-1；DEV-2为本轮Handoff，未新增工作树。

## 改动与实际验证

Core Compare采用原固定模板输入边界，五种产物映射固定建议路径；模板版本不可用/未知路径/重复项/空必需值/非法文本立即拒绝。原文件和覆盖清单是外部输入边界，下游信任已校验路径，不重复包装权限或Owner证明。提供空文件与未提供基线有不同状态，覆盖仅针对明确提供且选择的文件。自定义区无损保留，损坏/嵌套/无对应槽明确冲突，末尾无换行安全分隔。线性单区间差异可重建候选，不截断、不采用平方复杂度。

42 affected CodeGen checks PASS,0 skipped (including new comparison behavior and real MVC routing/auth/permission with identity and in-memory metadata fixtures);Vue type-check exit0;actual Chrome/Vue page behavior PASS with simulated API/identity. No PostgreSQL instance required or started for pure text comparison. Initial missing-route RED,two test compilation failures,and first page-selector timeout retained;fixed and relevant checks passed. No Actions or deployment.

后端证据D:/CP6-local-test/wave261/foundation/comparison-behavior-20261102T073158Z.json。覆盖旧preview兼容、模板输入验证及新比较完整行为；新MVC场景真实监听HTTP并执行Authorize/RequirePermission，身份/权限来自测试夹具，内存元数据通过GuardedContext证明零SaveChanges。不是实际PG/企业身份端到端验收。本轮没有持久数据库改动，PostgreSQL基线保持，无需为纯计算启动PG。

前端实际浏览器检查脚本同目录check-page.mjs，证据D:/CP6-local-test/wave261/foundation/page-02/result.json及comparison.png；API/身份响应明确模拟。已执行固定路径、未提供/空文本、UTF-8 BOM和CRLF原文读取且不自动上传、显式白名单请求、恶意HTML按文本显示、冲突禁止候选、晚到响应丢弃、输入清除、API错误和无view隐藏。类型检查日志type-check.log，退出0。

初始路由未实现的RED返回404；首次测试构建的params数组new类型推断错误以及修正时误改工厂返回类型的第二次构建错误均保留；修复后42项通过。页面首轮脚本尝试点击ElementPlus隐藏checkbox input超时，改用可见标签并验证checked后实际页面通过；没有强制点击、跳过或伪造结果。已有不相关分析器警告保留。

## 审查与剩余范围

一次作者集中SPEC/QUALITY审查覆盖完整产品、测试、页面、路径/版本/自定义区策略和准确台账；没有依赖、工作流、存储或编号Owner改动。前端截图人工检查完成，输入/差异均纯文本。正式应用、实际磁盘并发与链接安全、元数据CAS/unknown/真实租户持久化仍需独立实现；本轮不将建议路径白名单冒充真实文件应用权限。旧编号消费者/其他域、CRM新过期事件消费者以及整卡联合验收仍保留。

上期三天最终证明wave260-final-verification.json已核销，W259历史快照与第三期计划归档为完成，不重跑七天/W249/已完成三天。原375卡27DONE/2IN_PROGRESS/346PLANNED、原59局部余56、125目标0完整完成及247正式验收NOT_RUN保持。

## 交付生命周期

本提交为合并前快照。相关本地验证和集中审查完成；Main/Handoff待正常PR采用、准确任务分支删除及官方finish。本轮最终分支处置、固定工作区状态和席位释放由C:/Users/tt/CP6-planning-reports/20261017-foundation/wave261-final-verification.json核销。浏览器/Vite/测试宿主已退出；未启动PG；无Actions运行、取消或触发修改，无部署。
