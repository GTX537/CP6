# Task D-T1 报告：WfApiKeyHelper（生成/哈希/常量时间校验）

**STATUS: DONE**

波③ 事件触发 第 8/14 任务，开波 H-D（message/REST 触发器）。message 触发器 API key 基建：32 字节高熵随机 Base64Url 生成、SHA-256 hex 入库、常量时间校验（库内只存哈希）。

## Commit
- `010d6cd` feat(wfs-trigger): D-T1 WfApiKeyHelper 32字节随机+SHA-256入库+常量时间校验
- 已 push 到 `feat/wfs-event-trigger`（`0ec1b5b..010d6cd`）。

## 交付文件（surgical，仅简报两文件）
- Create `CP6.Core/Services/Wf/WfApiKeyHelper.cs`（28 行）
- Test `CP6.Tests/Wf/WfApiKeyHelperTests.cs`（43 行）
- `git show --stat HEAD` = 2 files changed, 71 insertions(+)，零多余文件。

## TDD 轨迹
1. **RED**：先写 4 测试，`--filter WfApiKeyHelperTests` 编译失败（CS0103 `WfApiKeyHelper` 不存在）。
2. **实现**：逐字复刻简报——`NewRawKey()`=RandomNumberGenerator.GetBytes(32)→Base64Url；`HashOf()`=Convert.ToHexString(SHA256.HashData(...))（恒大写 64 hex）；`Verify()`=null/空短路 + ToUpperInvariant 归一 + 先比长度 + CryptographicOperations.FixedTimeEquals（复刻 RefreshTokenService.NewRaw/HashOf + TwoFactorService.FixedTimeEquals 先例）。
3. **GREEN**：新测 4/4 通过。

## Gates
1. 全量 `dotnet test CP6.slnx`：**1943 passed / 5 skipped / 0 failed**（基线 1939 + 4 新测，符合预期）。
2. `dotnet ef migrations has-pending-model-changes`：**clean**（No changes have been made to the model since the last migration）。零迁移。
3. `git show --stat HEAD`：仅简报两文件。引擎零改动。

## 测试小结
新增 4 测试全绿：NewRawKey 高熵/Base64Url 无 +//= 字符、HashOf 确定性 64 hex、Verify 往返真+错 key/空 raw 假、Verify null/空 storedHash 假。

## Concerns
无。纯静态 helper，无 DI、无实体、无迁移。后续 H-D 波任务将消费此 helper（创建/重置响应显示明文一次，库内存 HashOf 结果）。
