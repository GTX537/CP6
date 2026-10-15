# 新验收依据采纳与一次正式发布

2026-10-15，用户在当前协调对话对[两项具体提案](https://github.com/GTX537/CP6.AI-Handoff/blob/d13a9a2ff2c213ce0c27132af621994aecfbaf0c/handoffs/CP6-DEV-20261012/waves/20261015-next-step.md)明确回复“同意两项”。
[授权记录](authorization.json)绑定准确 manifest、源码和执行范围；记录时间为协调者观察时间，没有虚构原消息时间、用户实名或独立签名。

## 已采纳的设计依据

用户采纳 `WP01-01-ACCEPTANCE-BASIS-1.2.0`，版本 `CP6-WP05-01-ACCEPTANCE-BASIS/1.2.0`。
原 [manifest](../WP01-01/adoption-v1.2/manifest.json) SHA-256 为
`ad811cc0af66c77b4953b0ec2b9fe6393f50e9823225e17bfc3f67ea914e3883`，原字节长度2570。
ITER401、REL178，共579条义务作为后续开发和验收依据；无需再次请求同一采纳决定。

台账已选择此准确 manifest。冻结候选及其 `ADOPTION_PENDING` 字段作为历史原样保留，
本次真实决定另行追加；旧30/48原件仍不可恢复，逐项等价仍无法证明。
另外123目标的原验收要求没有改变。

用户决定已经生效。自动台账验证所需的独立信任配置尚未安装，因此不能把本记录当作
独立签名注册表，或直接令 `basis_adoption_valid=true`。实际业务证据仍为 `NOT_RUN`。
WP01-01阶段收口仍需按现有规则完成这一技术登记；不能再把“等待用户采纳”列为阻塞。
真实成员、审批、留存、SLO及消费者运行资料在对应任务需要时补充。

## 一次 Platform 0.10.3 正式执行

准确源码：`GTX537/CP6.Platform@1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`。
工作流 `.github/workflows/p10-formal-packages.yml`，`main`，输入 `expected_commit` 为上述SHA、`version=0.10.3`。
[运行38709310767，attempt1](https://github.com/GTX537/CP6.Platform/actions/runs/38709310767)
于08:56:48Z启动，已通过原有 `p10-formal-release` required-review流程；保护规则未修改。

执行前重查准确main、七包完整分页未占用0.10.3、没有相同源码的既有运行；
复用之前对相同策略/证书/环境绑定的只读检查。实际私钥绑定、RFC3161、签名和feed结果由本次运行验证。
既有Release包public、其他六包private的可见性保持。

**发布与制品核对通过。** Windows作业08:57:34Z至09:01:17Z，Linux作业09:01:19Z至09:01:53Z，
合计257秒实际作业时间，只有1次run/attempt，没有重跑。
七包0.10.3均已发布，当前feed版本唯一且既有可见性不变；签名身份为内部固定证书
`PinnedSelfSigned`，不宣称公共CA背书。

[执行回执](release-receipt.json)记录实际API观察、作业时间、Artifact和feed版本ID。
原样保存五份正式记录，入口为[正式publication](publication/formal-package-publication.v1.json)，
其原字节SHA-256：`fae24b7cc239d79f12f7e2b0dc627789423f3aa6275ad38fb54a5f9ebc05bc36`。

| 原始归档 | Artifact ID | SHA-256 |
|---|---|---|
| Windows七包及查回证据 | [11959124489](https://github.com/GTX537/CP6.Platform/actions/runs/38709310767/artifacts/11959124489) | `9a0933c85e24a9d3a894d767d5392d2661ae61ac2243e055aa526b0a582491aa` |
| 最终正式记录 | [11959274479](https://github.com/GTX537/CP6.Platform/actions/runs/38709310767/artifacts/11959274479) | `e5cbbf44630419334cb9338aa9940c81adb4e432e3690a42d52199c85992d65c` |

协调实际下载两个归档并核GitHub摘要；核对七包原字节与provenance、author-signed、feed readback、
Windows及Linux记录相等，核准确source/workflow blob及仓库固定trust policy/公开证书。
密码学Current签名/RFC3161验证复用该次真实受保护Windows/Linux执行，没有另开独立重验证运行。
两个平台为同一时间戳选择的系统根链可以不同，叶证书、策略OID及包字节一致，符合原合同。

正式包缺失已解除；Main尚未采用0.10.3，正式consumer matrix/lock尚未生成，兼容性未验收。
这不是Main或PostgreSQL业务通过，也不把全部Platform源码能力自动宣称为正式能力采纳。
此次授权只覆盖一次Windows构建/签名/发布/查回及同一归档的Linux独立验证；
额外重跑、恢复、独立预检/重验证、Main接线、迁库和部署均不在本次范围。

## 验证与交付范围

本次协调修改为授权、台账与文档登记，只检查准确绑定、完整差异、必要链接及真实台账CLI。
复用[第八轮报告](../wave8-integration-20261015/README.md)及[Issue216修复证据](../bug-216-configuration/README.md)，
没有重跑不受影响的产品测试或新增文档测试。
实际CLI结构检查PASS；basis覆盖true，独立信任false（INDEPENDENT_VERIFIER_UNCONFIGURED），
ITER/REL运行false。集中自查本任务完整diff、链接及未改变的冻结输入，无阻断问题。

24/375任务DONE、业务0/125、247运行场景NOT_RUN、41历史REFERENCE_ONLY保持。
WP01-01仍PLANNED/NOT_CHECKED，WP02-02仍BLOCKED/NOT_CHECKED。
原17PGT、Main实际采用及真实Owner验收未执行。
本次仅执行上述用户明确授权的Actions；没有取消运行或修改触发器，没有迁库或部署。


## 下一步的准确边界

- A/WP01-01：不再等待用户采纳；补齐独立信任登记的实际维护身份/配置，按既有规则收口。
  本次没有私自建立签名密钥、指定独立审批者或伪造注册表。
- B/WP02-02：正式包发布前置已具备。下一窗口采用这里的准确publication和七包哈希，
  先完成实际消费依赖/兼容性与matrix/lock，再接入Main PostgreSQL多租户路径及原17PGT。
  旧宿主结果只复用未变化输入，不可替代新包采用；不得改回SQL Server或双库验收。
- 其他工作区与旧准备交付保留；没有新增实现派工或自动唤醒执行对话。
