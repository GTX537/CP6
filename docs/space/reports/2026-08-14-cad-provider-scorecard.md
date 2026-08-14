# Space Studio WP3 CAD Provider 评分与选型工具

日期：2026-08-14
范围：ADR-0001 评分机器化与 Site 主备认证输入生成
接受状态：仓库实现完成；真实 Provider 选型与 Site GA 仍为 No-Go

## 交付结果

- 新增 `qualify-providers` 命令，严格读取单 Site 候选评分表并使用冻结规则 `cad-provider-adr-0001-v1` 计算 100 分总分。
- 六个维度分别限制为 25/20/15/15/15/10；越界、未知或大小写变体重复 JSON 字段、非法枚举、非法哈希均拒绝整个输入。
- 每个候选必须具备 DWG/DXF 覆盖、当前审批窗口、Licensing/Security/Data Region/Deletion-Retention 四项证据、试验 Preflight 哈希、资格证据和至少 80 分；云服务只接受 Secret 引用，不接收 Secret 值。
- 所有候选必须绑定同一黄金集、冻结 Worker 环境和评分规则。唯一最高分为 Primary，唯一第二名为 Backup；任一名次并列、合格候选不足两个或基线混用均为 No-Go。
- Pass 报告只生成两条受报告 `selectionSha256` 绑定的 `SpaceCadProviderCertificationInputDto`；No-Go 报告仍保留逐候选阻断原因，但认证输入为空。命令从不写 Site 配置。

## 自动化证据

- `dotnet test tools/CP6.Space.CadExperiment.Tests/CP6.Space.CadExperiment.Tests.csproj -c Release`
- 结果：34 passed / 0 failed / 0 skipped。
- 新增用例覆盖唯一第一/第二名、报告哈希确定性、主备输入、低分、硬门槛缺失、第一名并列、第二名并列、冻结基线不一致、评分越界、未知/重复 JSON 字段及 No-Go CLI 退出码。

## 未关闭门禁

本报告不包含任何真实 Provider 分数、客户审批、20 份授权黄金 CAD、冻结 Worker 运行或 Site 认证结果。下一步必须由实名 Owner 在同一正式黄金集和冻结环境运行真实候选，取得两个总分不低于 80 且排名唯一的合格链，再通过受控管理接口配置到目标 Site。缺少任一项时 WP3 与核心 GA 继续为 No-Go。
