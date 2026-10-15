# WP02-02 第七轮执行计划

范围是 SOURCE_COMPATIBILITY_PREPARATION_ONLY。权威派工 Main `99fc0ce383d2d02c1404a8adbb3abe971b41d332`；产品输入 Main `1129953156b7775152a74ad2f51cb95ef430ee56` / Platform `b28db763f70ab6f164cb7c87d6ce49a6d97267f1`，Handoff `ebeae808b2aa35c8c9115519fed564edebbbde0c`。前一窗口 f395d92a 已合入；本轮独立分支 codex/wp02-02-closure-candidate-20261014，旧目录只读。

1. 核准入 37 个固定来源指纹与实际 main 引用、全部项目/props/锁/SDK；发布实际 START_PREPARATION。各角色只写新的两个私有目录。
2. 实现并负向验证 fresh owner32、包含性、固定 Git 原字节投影、精确四处 overlay、原生产文件不变、外部源/签名策略不变。记录完整 Git 清单和实际编译投影，所有排除透明可审查。
3. 使用 SDK8.0.424 实际 restore/build 两个复制的 Main 项目根及完整 Platform 依赖；显式 `-warnaserror:MSB3277`。保留真实错误和警告、assets/deps/私有锁、依赖图和源码/输出身份；不执行 DatabaseCompatibility.Probe 或 Main Program。
4. 在同一 owner 中编译并执行隔离 Kestrel，127.0.0.1:0；实际上下文与 WP02-25 trace/metric/log，verifier/authority 显式 synthetic。记录 HTTP、真实 SDK 输出、已加载程序集路径/hash/MVID/version、DiagnosticSource 绑定和清理。运行前独立固定 build 输出，运行后与观察值比对。
5. 负例覆盖 missing dependency、version/source/hash 错误、duplicate assembly、Unknown compatibility、cross-owner/cross-snapshot；不允许任意自报 JSON 转为正式采用。只有指定源码/依赖/路径实际通过才能给出对应技术范围结论。
6. 收尾生成 SOURCE-ADOPTION-CANDIDATE.md，区分 source/build/runtime/plan/formal trust/Main consumption，列具体后续改动、撤销/回退及失败修复点。一项任务级独立审查，必要时只复查修改及影响。检查候选/保护 main 工作流后推任务分支，Handoff 固定 SHA 交付 COORD 集成。

运行资源只在本任务忽略 artifacts/wp02-02/consumption-compatibility/<owner32>/；每次新 owner、无 DB、不用其他窗口固定端口或服务。临时副本的修改不回写生产。没有 Actions、签名/发布或正式源码采用授权；WP02-02 BLOCKED/NOT_CHECKED、17 PGT NOT_RUN、WP02-28 和业务完成门均不改变。

分工：生成器子代理负责 snapshot/overlay/CLI 及其局部测试；探针子代理负责私有 Kestrel/SDK 代码；主对话负责实际运行、来源与依赖证据、运行身份拒绝、候选报告及交付。共同文件由主对话单写；测试和审查节奏遵守 AGENTS。
