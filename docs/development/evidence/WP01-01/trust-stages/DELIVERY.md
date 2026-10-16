# WP01-01 第九轮：分离采纳与运行信任配置

2026-10-16；执行者 A；局部代码窗口交付，完整任务状态由 COORD 维护。
基线 `37d84b4b6eaeefe7e8b35c21df26a8b1f8c119d5`；候选分支 `codex/wp01-01-trust-stages-20261015`。
按[第九轮执行包](../../../dispatch/20261015-wave9/WP01-01.md)实施；START 已发布于 Handoff `e6a10d2`。候选固定提交和草稿 PR URL 随本轮 Handoff DELIVERY 登记，合并及共享状态同步交 COORD 串行完成。

## 改动与边界

`TrustedRegistryVerifier.from_environment` 只读取共同策略，角色配置在首次读取对应 registry 时校验。采纳只需 authority；未提交运行时不访问 execution。提交运行后，execution 缺失、配置错误或签名失败只拒绝相应运行，保留独立验证成功的采纳。没有自动运行 PASS 或角色替代路径。

缺少策略环境值保持 `INDEPENDENT_VERIFIER_UNCONFIGURED`；缺少角色是 `AUTHORITY_TRUST_UNCONFIGURED` / `EXECUTION_TRUST_UNCONFIGURED`，已提供但无效的角色是对应 `*_TRUST_INVALID`，保留底层原因。策略语法/schema/产品仓库映射仍是共同要求。未使用的 execution 不代表已经验证；需要执行证明时必须通过它。

OPERATOR 明确用户已采纳的真实授权与尚未配置的独立机器信任之区别，并说明部分配置和运行拒绝。冻结 manifest/basis/candidate 原字节未改；manifest SHA-256 仍为 `ad811cc0af66c77b4953b0ec2b9fe6393f50e9823225e17bfc3f67ea914e3883`。579 条义务（401 ITER、178 REL）、18 条结构断言、历史 30/48 缺件及其余 123 目标要求保留。

生产 `manage_roadmap.py` 无改动。相关 manager 测试调整了一个已过时的夹具假设：现在基线已选择 manifest，测试只应比较注入测试选择后的状态是否被验证器改写，不能删除选择后与原基线比较。共享台账、生成页和四份状态文件未编辑。

## 实际验证与失败记录

环境：本地 Windows，Python 3.13，OpenSSH；临时测试密钥在测试结束删除，GitHub 传输及业务资料为隔离夹具。它们证明算法行为，不证明真实采纳签名或业务运行。

在 `docs/development/tools` 执行（Python 使用 `-B -X utf8`）：

```text
python -B -X utf8 -m unittest test_acceptance_basis test_manage_roadmap.FormalAcceptanceRouteTests
python -B -X utf8 -m unittest test_manage_roadmap.FormalAcceptanceRouteTests.test_unadopted_candidate_leaves_all_original_states_unchanged
```

第一条实际执行 36 项：adapter 27 项和 manager 8 项通过，1 项 manager 旧夹具失败；修正后第二条单项通过（1.380 秒）。最终相关范围全部通过；复用第一条未改输入的 35 项成功结果，未将第一条失败运行改称全绿。未执行其他 manager 测试或全仓产品测试。

覆盖 authority-only 采纳、无运行仍 false、缺失/空值/错误 execution 仅运行拒绝、双角色完整路径、签名篡改按阶段拒绝，以及 execution 无法替代缺失/无效 authority。现有撤销、过期、范围、身份历史、远端字节和包含性测试在 adapter 范围内实际通过。

开发失败保留如下（原输出在本对话工具记录）：

- 修改生产实现前，三个新增回归产生 14 个失败子场景：缺 execution 导致采纳 false（`'execution'`），坏配置导致全局失败，角色缺失只有原始 KeyError。确认原耦合及诊断问题。
- 首次 adapter 回归 27 项中 1 项 ERROR：旧直构 verifier 的签名夹具缺 `identity_root`，新统一角色检查返回 `AUTHORITY_TRUST_INVALID: 'identity_root'`；补齐夹具原本就要求的字段，未放宽生产校验。
- 上述 36 项运行的唯一 FAIL 为 `test_unadopted_candidate_leaves_all_original_states_unchanged`：旧 `pop('acceptance_basis')` 删除了当前基线已存在的选择；已按前述修正并单项复测。

真实只读检查：设置 `CP6_ACCEPTANCE_ARCHIVE_ROOTS=D:/CP6`，从工作树运行 `python -B -X utf8 docs/development/tools/manage_roadmap.py --basis-report <外部临时路径>`，退出 0、结构检查 PASS、errors=[]。报告实际为：

```json
{"basis_adoption_valid":false,"inventory_coverage_valid":true,"target_runtime_valid":{"PLM-ITER-01":false,"PLM-REL-01":false},"errors":["INDEPENDENT_VERIFIER_UNCONFIGURED"]}
```

业务 0/125、247 个修订场景 NOT_RUN、41 历史引用不变。没有安装独立信任配置，不能把用户授权记录或正式包公钥冒充独立 registry 签名；不再次要求用户采纳同一依据。

## 集中审查与交付

执行者按一次任务级 SPEC/QUALITY 集中审查核对完整 diff：角色校验移至使用点，外部信任来源、签名 namespace、撤销/有效期、身份唯一创建、源字节及运行绑定逻辑保持；测试走真实策略加载与 OpenSSH 签名校验，缺 execution 不会使运行默认成功。未发现阻断问题。差异空白检查、授权与执行包链接检查通过，变更均在允许路径。

候选及远端 main 的 `.github/workflows` / `.github/actions` 与已分析基线 `99fc0ce383d2d02c1404a8adbb3abe971b41d332` 一致：普通工作流仅手动，唯一 push 入口限 `v*.*.*` tag。本次仅推任务分支并创建草稿 PR，不推 tag；未触发/取消 Actions，未更改触发配置。Handoff Actions 禁用，任务资料及默认分支无工作流。

剩余范围：独立维护者实际控制的 authority/execution 配置与有效签名、真实业务证据、完整 WP01-01 收口，以及 COORD 合并/共享状态同步。此次局部实现不宣称上述完成；无发包、数据库、迁库或部署操作。
