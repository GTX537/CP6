# Space Studio WP7 正式黄金 CAD 接受

日期：2026-08-28

Owner / Reviewer：`BUBAO.GAO`

结论：`WP7_GOLDEN_CAD_FORMAL_EVIDENCE = Complete / Accepted`

## 结果

- 正式集合为 20 份 `ApprovedOriginalWork` CAD，DWG/DXF 各 10 份，`Calibration/Validation/ReleaseHoldout=10/5/5`，L1～L5 各 4 份。
- Golden Dataset SHA-256 为 `2b9438e09e2953b169770d0ee9292d8f9cc9ed697337111bcb61b913484b1f15`；Source Set SHA-256 为 `7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955`。
- 唯一 Primary 为 `cp6-autocad-worker/1.0.0+worker.c794e9c0ebbb.autocad.25.0.58.0.0.dxf.1.1.0`，资格分 86/100；冻结 Worker Environment SHA-256 为 `c9bbbe362a01e951379d60990f227fc4d5634ac9c86534f009f1d7e87d601717`。
- 规则文件在 Calibration 后冻结；Validation 和 Release Holdout 只用于出样评估，没有反向修改规则。规则 SHA-256 为 `5ed57628e86b05943d271da1705ee0e6a704326e89578265d46945ee24538ee7`。

## 业务质量

| 指标 | Overall | Out-of-sample | 门槛 |
|---|---:|---:|---:|
| 目标覆盖率 | 99.0224% | 99.2352% | ≥80% |
| 整体准确率 | 98.7008% | 98.9828% | ≥90% |
| 高置信精确率 | 98.7008% | 98.9828% | ≥95% |
| Wilson 下界 | 98.1717% | 98.3541% | ≥90% |
| 人工操作下降 | 96.9043% | 97.5781% | ≥70% |

Overall 为 2,455 个标准答案、2,463 个提案、2,431 个正确提案；Out-of-sample 为 1,569 个标准答案、1,573 个提案、1,557 个正确提案。Release Holdout 未报告 Blocking 遗漏为 0，评估结果为 `releaseEligible=true`，没有 Issue Code。

## 50 MiB 性能

性能输入由已授权原创 `L1-C02` DXF 派生：保留原始 CAD 语义，只在终止记录前加入一个 DXF `999` 注释并填充到精确 52,428,800 bytes。它是标准 I/O 与工作流性能包络，不声称具有 50 MiB 客户复杂几何；原始 CAD 和派生 CAD 均留在仓库外。

- 标准 CAD SHA-256：`a587c323e5c8abf2213d4eaa416cf45df17c65392196207f9c8514325b7c9ae7`。
- 执行 1 次预热和 20 次稳定观察；预热按 ADR-0004 排除在分位数外，稳定运行失败数为 0。
- 50 MiB 到可审查提案 P95 为 0.038715917 分钟（约 2.323 秒），门槛为 15 分钟。
- 上传到首次 Ready 等价语义状态 P95 为 0.032280823 分钟（约 1.937 秒），门槛为 60 分钟。
- “可审查”要求确定性提案合成达到 `canEnterReview=true`；“首次 Ready”要求来源语义达到 `readyForConfirmation=true`。没有伪造批量确认、Apply、Draft 写入或生产部署。

## 证据与边界

- 正式 Manifest：[`golden-cad-formal-evidence-v1.0.0.json`](../acceptance/v1.3-ga/golden-cad-formal-evidence-v1.0.0.json)，SHA-256 `892ce026cb6a4d9924ddc492c07204d5e7c5e182290d812fc81c8263c75b9a46`。
- 业务评估报告由受控 URN `urn:cp6-space-ga-evidence:golden-cad:v1.0.0:primary:business-evaluation` 固定，报告 SHA-256 为 `f130e00eb7685a695dddd3c057c33920ea1aa6be62738fb8f3c5c6854552ade6`。
- 性能证据由受控 URN `urn:cp6-space-ga-evidence:golden-cad:v1.0.0:primary:performance` 固定，证据 SHA-256 为 `91555de65034e5a475fdc02fd0227d0c9bc4d144f354ebc068e6a3c15f28f17b`。
- 应用代码绑定提交为 `f0e123ead0211fb9a697f645b3f1e3746813501c`。原始 CAD、派生性能 CAD、完整评估请求、Worker 二进制和 Provider 凭据均未进入 Git。
- 本项只关闭 WP7。Core GA 仍为 `NoGo`：0 类外部输入、7 个 Gate 与 1 个 DeliveryOwner 签署 Pending；没有执行生产部署，也没有把受控本地评估冒充生产验收。
