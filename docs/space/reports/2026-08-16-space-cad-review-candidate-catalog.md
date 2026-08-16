# Space Studio 历史 CAD 审核结果目录交付报告

日期：2026-08-16
范围：Design V1 CAD Review Candidate Catalog 与 Space Studio 显式重新关联
结论：仓库实现完成；WP4 仍为 Partial/Pending；核心 GA 保持 72% / `NoGo`

## 1. 用户结果

仓库主数据人员可以在 Space Studio 的来源模式中打开“选择已有 CAD 结果”，浏览当前楼层已有的成功 DWG/DXF 解析记录，不再复制或填写 SourceId、ParseJobId、FloorId、Revision 等内部标识。

- 与当前 Draft Base Content Revision/Hash 一致且工件完整的结果可以直接恢复 Job 监控并加载既有 Review Workspace。
- 历史 Revision 的结果仍展示来源、格式、首选 Provider 路由、启动/完成时间和基线信息，但只提供“重新解析”；首选路由不冒充故障切换后的实际执行 Provider。
- 重新解析沿用原受控 Source，重新进入单位、坐标、Mapping Profile 与显式确认向导；旧结果不能直接 Apply 到新的 Draft Revision。
- 切换候选前清理旧 CAD Workspace、Excel Source、Preflight Job、Match Job、问题选择和相关 URL 查询，避免把跨会话状态错误拼接。
- 只读用户可以审查目录；重新解析继续受编辑状态、页面租约及后续解析/Apply Fence 约束。

## 2. 服务端权威与失败关闭

新增接口：

`GET /api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/cad-review-candidates?limit=50`

目录执行以下限制：

1. 仅内部主体并要求 `space:model:read`；继续执行租户、Site、Model、Version 与活动 Floor 可读性检查。
2. 只查询同 Version 的成功 `CadParse` Job，并在内存中验证持久 Payload 的 Version、Source、Floor、格式和 schema。
3. 只接受 DWG/DXF 来源；损坏、旧 schema、身份不一致或无法反序列化的 Job 不进入目录。
4. `isCurrentRevision` 同时比较 Parse 启动时冻结的 Base Content Revision 和 Base Content Hash，不能用请求时当前 Revision 重新密封旧结果。
5. `canLoadReview` 还要求来源为 `PreviewReady` 且 PreviewSet Artifact 存在。目录本身不代表 Artifact 已通过完整信任校验。
6. 实际加载 Review Workspace 时继续复核 Clean/PreviewReady、Source SHA、Transform/Mapping SHA、Job/Source/Version/Floor/Tenant 身份链和 Artifact Hash；候选目录不能绕过既有 Trust Boundary。

## 3. 前端状态规则

- 当前候选选择后复用既有 `monitorCadParseJob` 与 `loadCadReviewWorkspace`，成功后可继续当前 CAD + Excel 权威匹配流程。
- 历史候选选择后只保留原 Source，打开 CAD Start Wizard，并明确提示必须针对当前 Draft 重新解析。
- 只读模式仍提供目录入口和历史审计信息，但禁用重新解析动作。
- 当前/历史判断、加载权限和按钮状态来自服务端合同；客户端不自行计算权威 Revision/Hash。

## 4. 自动化证据

- Space CAD Integration：15/15。
- OpenAPI 与权限聚焦：95/95。
- C#/TypeScript SDK 生成漂移：通过。
- Web Vitest：882/882。
- Space Studio Playwright：26/26，包含当前候选加载与历史候选重新解析。
- Vue TypeScript：通过。
- Web production build：通过。

浏览器自动化使用受控 API fixture，只证明 UI、合同和状态转换。它不计入真实 DWG/DXF、黄金 CAD、Provider、CP6 WMS、性能或 Pilot 接受证据。

## 5. AutoCAD 与剩余 GA 门禁

本机 `D:\AutoCAD 2025\accoreconsole.exe` 已验证可用于实验型 DWG→DXF→CAD IR 开发合同，但没有注册为 Site 生产 Provider，也没有完成客户批准、网络隔离、许可证边界或主备 Provider 同黄金集认证。

仍需完成：

- 两条 Site 已批准且同合同认证的生产 Provider 链。
- 20 份授权真实 CAD 的 10/5/5 黄金集、准确率与 50 MiB/Ready P95。
- 真实 DWG、DXF、Excel、PDF/图片、CP6 WMS 与 Published Viewer 端到端证据。
- 双仓各连续 14 天 Pilot、缺陷关闭和五方实名签字。

因此本纵切只关闭历史 CAD 结果可发现与安全重新关联的仓库缺口，不调整核心 GA 72% / `NoGo`。
