# W98 实际库存MOVE页面与原操作恢复

COORD本聊天单人，Main基线 `c7324fab629a4d66057d4b370ba522e891f3b680`，`codex/w98-01`固定DEV-1；Handoff `codex/w98-handoff`固定DEV-2。W97 Main507/508、Handoff73与Issue503已完整收尾、分支清理和席位释放。本轮官方START2026-10-24T04:16:19.155726Z，原估25–35min＋独立前置约2min，最终据finish计且不重复Handoff。

## 实际行为

库存查询行增加当前move权限的棚移動按钮，打开现有操作组件的MOVE模式。显示冻结的来源仓库／库位／产品／批次，输入同仓目标库位、正数数量与备注。数量沿用decimal(21,8)文本规范，不经过Number或舍入；目标不trim/改写，非空、30字符及字面同库位先检查，数据库的完整相等语义由真实Owner决定。移动时仅发送原业务键、目标、数量、备注和原操作UUID，不发送浏览器货主、价格、QC或操作人。

MOVE与ADJ分别校验各自当前动作，query提供原键恢复。发送前冻结模式、目标和所有请求值，等待共享浏览器锁/当前scope时的编辑不能改换请求；重复点击不再发第二条命令。复用W96当前tenant/actor分区的发送前公共原键持久化、Web Locks即时独占、身份／权限／storage事件失效和无自动重发策略。仅公共UUID/kind持久化，不保存库存或价格草稿。

超时、拒绝、UNKNOWN继续保留原键；刷新/撤销move权限后当前query仍可查询或显式close，匹配COMMITTED必须是原MOVE及两条不同且非空的原流水号，否则不清键。CLOSED仅封闭未执行原键，不撤销已提交库存。存储或安全锁不可用不发送新写；存储被外部删除/丢失仍是既有恢复边界。后端W97维持来源属性／目标兼容和原子写，不由浏览器决定货主身份或价格。

## 验证和审查

1项原生PostgreSQL实际HTTP＋29项Vue/client定向检查通过，vue-tsc通过，无最终跳过；1项修复前MOVE入口红灯保留。

- 页面测试先复现MOVE未调用（期望1次，实际0次），仅选中该行为失败，未选用例不算交付通过。实现后完整选定三套29项无跳过：实际库存行按钮连通、请求冻结、最小正数/超范围/零/负数/科学计数拒绝、目标边界、动作分离、等待时撤权、重复点击、超时重挂载、双腿不匹配保护；原ADJ、原键、身份、盘点/看板相关回归继续通过。
- 新增实际Program/native PostgreSQL HTTP：仅move/query且价格禁止读写，发送`9007199254740.12345678`字符串，两端数量精确保留、来源九类属性维持；撤销move后同原键恢复/重放仅返回原结果，2流水、1原记录、1同事务Outbox。
- vue-tsc实际通过。一次完整COORD SPEC/QUALITY覆盖代码diff、权限／身份／持久键、精度和真实服务端采用；UI沿用现有组件，不另造业务写者。源自W96的API/恢复函数与W97后端未变，原W97的70项成功按准确范围复用，未称为本轮重跑。

本轮实际证据索引 `D:\CP6-local-test\wave98\foundation\final-validation.json`，所有TRX、JSON、日志及红灯保留；1个自有PG停止、无PID。未运行现场交互浏览器验收、全仓回归、Actions、既有库迁移或生产部署。

## 交付边界

实际同仓MOVE页面与原键恢复，不等于正式Stock/TRANSFER Root/Claim/Stage/SourceDemand/capacity或现场业务验收；旧错误数据、Finance业务接收和外部ACK仍另行实施。当前候选待Main/Handoff正常PR采用与准确分支/席位finish；最终证明 `C:\Users\tt\CP6-planning-reports\20261017-foundation\wave98-final-verification.json`。24/375整卡、125总目标0完整、247正式运行NOT_RUN不变，七天持续目标ACTIVE；后续继续Site/Form Owner直接前置。

## 实际采用与最后收尾

Main [产品PR](https://github.com/GTX537/CP6/pull/509) source `98a4d468c264bcc050e2272ec798c6b3b6ab4072`采用远端main `0b8a3ee014e7bcfb1fe58adf5b2792f4e61a7992`，15任务路径一致；Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/74)采用`84897920632c79f32c2ad95695c30c1b7b45b049`，准确分支删除、固定树干净detached、finish释放。4产品路径与已测提交逐blob一致，复用1原生HTTP＋29Vue/client和类型检查；最后文档diff和台账生成独立检查。

Main同任务最终状态PR/finish待完成，当前约11.47min，原估25–35min＋前置约2min，最终以`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave98-final-verification.json`为准，不重复Handoff。自有PG停止、所有证据/数据保留，无Actions／迁移／部署。实际MOVE与原键恢复采用，正式业务验收仍独立，七天ACTIVE。
