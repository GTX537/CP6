### Task A-T3: PersistentWfNotifier 接矩阵偏好（逐收件人 × 逐通道）

**Files:**
- Modify: `CP6.WebApi/Services/PersistentWfNotifier.cs`
- Test: `CP6.Tests/Oa/PersistentWfNotifierTests.cs`

**Interfaces:**
- Consumes: `IPrefService.IsEnabledAsync`（A-T2）、`NotifyMatrix.ChannelInApp/ChannelEmail`（A-T1）。
- Produces: 行为变更——`inApp=false` → 跳过该收件人的持久化+SignalR；`email=false` → 跳过该收件人的邮件。方法签名零变化（`IWfNotifier` 不动）。**不回溯**：既有 `Wf_Notification` 行不动（本来就只影响新发送）。

> 铁律沿袭（文件头注释①②③保留并更新③措辞）：持久化仅 Add 不 SaveChanges；SignalR/邮件 best-effort 各自吞异常；**新③：偏好按 收件人×类型×通道 独立生效（矩阵）**。
> `TodoCreatedAsync` 每次调用只有一个收件人，但引擎对多审批人会逐人调用——偏好天然逐收件人生效（spec §2.2 口径）。
> 若 hardening 的 `BranchPrunedAsync` 已合入本文件：同口径改造（type key `"branchPruned"`）；未合入：本任务不创建该方法（矩阵行由 A-T1 反射自动出现与否）。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/PersistentWfNotifierTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.Hubs;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CP6.Tests.Oa;

public class PersistentWfNotifierTests
{
    // ── 手写 fakes ──────────────────────────────────────────────────────────
    private sealed class RecordingNotif : INotificationService
    {
        public readonly List<(Guid UserId, int Type)> Created = new();
        public Task CreateAsync(Guid userId, int type, string title, string body, Guid? instanceId, Guid? taskId, string? flowKey)
        { Created.Add((userId, type)); return Task.CompletedTask; }
        public Task<IReadOnlyList<NotificationItem>> ListAsync(Guid userId, bool unreadOnly, int page, int pageSize)
            => Task.FromResult<IReadOnlyList<NotificationItem>>(Array.Empty<NotificationItem>());
        public Task<int> UnreadCountAsync(Guid userId) => Task.FromResult(0);
        public Task MarkReadAsync(Guid userId, Guid id) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
    }

    private sealed class RecordingEmail : IEmailSender
    {
        public readonly List<(string To, string Subject)> Sent = new();
        public Task SendAsync(string to, string subject, string body)
        { Sent.Add((to, subject)); return Task.CompletedTask; }
    }

    private sealed class FakeClientProxy : IClientProxy
    {
        public int SendCount;
        public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
        { SendCount++; return Task.CompletedTask; }
    }

    private sealed class FakeHubClients : IHubClients
    {
        public readonly FakeClientProxy Proxy = new();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> x) => Proxy;
        public IClientProxy Client(string x) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> x) => Proxy;
        public IClientProxy Group(string x) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> x) => Proxy;
        public IClientProxy GroupExcept(string x, IReadOnlyList<string> y) => Proxy;
        public IClientProxy User(string x) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> x) => Proxy;
    }

    private sealed class FakeHub : IHubContext<NotifyHub>
    {
        public readonly FakeHubClients FakeClients = new();
        public IHubClients Clients => FakeClients;
        public IGroupManager Groups => null!;   // 通知器不触达 Groups
    }

    // ── 脚手架 ──────────────────────────────────────────────────────────────
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private sealed record Rig(CP6Context Db, PersistentWfNotifier Notifier,
        RecordingNotif Notif, RecordingEmail Email, FakeHub Hub);

    private static async Task<Rig> BuildAsync(Guid user, string? prefsJson)
    {
        var db = NewDb();
        db.Sys_Users.Add(new Sys_User { Id = user, UserName = "u1", NickName = "用户一", Password = "x", Email = "u1@cp6.local" });
        if (prefsJson is not null)
            db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = user, PrefsJson = prefsJson });
        await db.SaveChangesAsync();
        var notif = new RecordingNotif();
        var email = new RecordingEmail();
        var hub = new FakeHub();
        var notifier = new PersistentWfNotifier(db, notif, new PrefService(db), email, hub,
            NullLogger<PersistentWfNotifier>.Instance);
        return new Rig(db, notifier, notif, email, hub);
    }

    // ── 跳过矩阵（spec §7）──────────────────────────────────────────────────
    [Fact]
    public async Task Default_NoPrefRow_PersistsPushesAndEmails()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, prefsJson: null);
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.Equal(WfNotificationType.TodoCreated, r.Notif.Created[0].Type);
        Assert.Equal(1, r.Hub.FakeClients.Proxy.SendCount);
        Assert.Single(r.Email.Sent);
    }

    [Fact]
    public async Task InAppOff_SkipsPersistAndPush_EmailStillSent()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"todoCreated":{"inApp":false,"email":true}}}""");
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Empty(r.Notif.Created);
        Assert.Equal(0, r.Hub.FakeClients.Proxy.SendCount);
        Assert.Single(r.Email.Sent);                       // 通道独立：邮件照发
    }

    [Fact]
    public async Task EmailOff_PersistsAndPushes_NoEmail()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"flowApproved":{"email":false}}}""");
        await r.Notifier.FlowApprovedAsync(user, Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.Equal(WfNotificationType.FlowApproved, r.Notif.Created[0].Type);
        Assert.Equal(1, r.Hub.FakeClients.Proxy.SendCount);
        Assert.Empty(r.Email.Sent);
    }

    [Fact]
    public async Task BothOff_SkipsEverything()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"flowRejected":{"inApp":false,"email":false}}}""");
        await r.Notifier.FlowRejectedAsync(user, Guid.NewGuid(), "leave", "缺附件");
        Assert.Empty(r.Notif.Created);
        Assert.Equal(0, r.Hub.FakeClients.Proxy.SendCount);
        Assert.Empty(r.Email.Sent);
    }

    [Fact]
    public async Task TypesIndependent_RejectedOff_TodoStillFull()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"flowRejected":{"inApp":false,"email":false}}}""");
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.Single(r.Email.Sent);
    }

    // ── 遗留扁平数据回归（C2：旧用户已存开关不失效）──
    [Fact]
    public async Task LegacyFlat_TodoOff_SkipsAllChannels()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"todo":false,"email":true}}""");
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Empty(r.Notif.Created);
        Assert.Empty(r.Email.Sent);
    }

    [Fact]
    public async Task LegacyFlat_GlobalEmailOff_SkipsOnlyEmail()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"email":false}}""");
        await r.Notifier.FlowApprovedAsync(user, Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.Empty(r.Email.Sent);
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter PersistentWfNotifierTests`。预期：`InAppOff_SkipsPersistAndPush_EmailStillSent` / `EmailOff_*` 等 FAIL（现状事件关=整跳、email 全局）。（Default/BothOff/Legacy 用例在现状下即绿——它们是回归保护。）

- [ ] **Step 3: 实现** — 三个方法统一改为矩阵口径（题头注释③同步更新）。以 `TodoCreatedAsync` 为例，**逐字**：

```csharp
    /// <inheritdoc />
    public async Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey)
    {
        // 1. 逐收件人 × 逐通道查矩阵偏好（per-request 缓存在 IPrefService 内）
        var inApp = await _pref.IsEnabledAsync(assigneeId, "todoCreated", NotifyMatrix.ChannelInApp);
        var email = await _pref.IsEnabledAsync(assigneeId, "todoCreated", NotifyMatrix.ChannelEmail);
        if (!inApp && !email) return;

        const string title = "您有新的待办";
        var body = $"您有新的待办：{flowKey}";

        if (inApp)
        {
            // 2. 持久化（仅 Add，不 SaveChanges）
            await _notif.CreateAsync(
                assigneeId, WfNotificationType.TodoCreated,
                title, body, instanceId, taskId, flowKey);

            // 3. SignalR（best-effort）
            try
            {
                await _hub.Clients.All.SendAsync("WfNotification", new
                {
                    type       = WfNotificationType.TodoCreated,
                    userId     = assigneeId,
                    instanceId,
                    taskId,
                    flowKey
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SignalR WfNotification(TodoCreated) 失败，忽略（用户 {UserId}）", assigneeId);
            }
        }

        // 4. 邮件（best-effort，独立通道）
        if (email)
            await TrySendEmailAsync(assigneeId, title, body);
    }
```

`FlowApprovedAsync` / `FlowRejectedAsync` 同构改造：把 `var prefs = await _pref.GetNotifyPrefsAsync(...); if (!prefs.Approved) return;` 换成上面的双通道查询（type key 分别为 `"flowApproved"` / `"flowRejected"`），持久化+SignalR 包进 `if (inApp)`，`if (prefs.Email)` 换 `if (email)`。title/body/payload 逐字保留现状。文件头加 `using CP6.Core.Services.Oa;`（NotifyMatrix 命名空间）。

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter PersistentWfNotifierTests`。

- [ ] **Step 5: 回归闸 + commit** — 既有 `NotificationEngineHookTests`（引擎钩子）与 `TimeoutScanTests` 必须照绿（`GetNotifyPrefsAsync`/`ParseNotifyPrefs` 保留未删，仅 notifier 停用）：

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Oa|Wf"
git add -A && git commit -m "feat(wfs-inbox): A-T3 PersistentWfNotifier 接矩阵偏好 逐收件人×逐通道独立跳过"
```

---


---
## 附: 共享契约(plan全局)
## 共享契约（所有 Task 用这些**精确**名字）

```csharp
// CP6.Core/Services/Oa/NotifyMatrix.cs
public record NotifyMatrixRow(string TypeKey, int TypeValue, bool InAppSupported, bool EmailSupported);
public static class NotifyMatrix
{
    public const string ChannelInApp = "inApp";
    public const string ChannelEmail = "email";
    public static bool IsEnabled(string prefsJson, string type, string channel);
    public static IReadOnlyList<NotifyMatrixRow> Rows();
}

// IPrefService 新增
Task<bool> IsEnabledAsync(Guid userId, string type, string channel);  // per-request 缓存（Scoped 实例内字典）
Task SaveMergeAsync(Guid userId, string partialJson);                 // 顶层键合并；patch 值为 null → 删除该键
Task<string> GetRowModeAsync(Guid userId);                            // "merged" | "expanded"，缺省 merged

// IInboxService 变更/新增
Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null);
Task<BatchTransferReport> BatchTransferAsync(Guid actorId, Guid fromUserId, Guid toUserId, string? comment, BatchTransferFilter? filter = null);
Task<BatchTransferPreview> BatchTransferPreviewAsync(Guid fromUserId, BatchTransferFilter? filter = null);

// InboxModels.cs 新增（批量上限常量在 InboxService：private const int MaxBatchTransfer = 500;）
public record BatchTransferFilter(string? FlowKey = null, DateTime? BeforeUtc = null, IReadOnlyList<Guid>? TaskIds = null);
public record BatchTransferItemResult(Guid TaskId, string FlowKey, bool Ok, string? Error);
public record BatchTransferReport(int Total, int Succeeded, IReadOnlyList<BatchTransferItemResult> Failed);
public record BatchTransferPreview(int Total, IReadOnlyList<InboxPendingItem> Sample);   // Sample = 前 10 条
```

```ts
// cp6.web/src/views/oa/settings/notifyMatrixModel.ts
export interface NotifyMatrixRow { typeKey: string; typeValue: number; inAppSupported: boolean; emailSupported: boolean }
export type MatrixState = Record<string, { inApp: boolean; email: boolean }>
export function buildMatrixState(prefsJson: string, rows: NotifyMatrixRow[]): MatrixState
export function toNotifyPatch(state: MatrixState): string        // → '{"notify":{...}}'

// cp6.web/src/views/oa/inbox/inboxModel.ts 新增
export function parseRowMode(prefsJson: string | undefined): 'merged' | 'expanded'
```

- 端点：`POST /api/oa/pref/save`（`SavePrefReq(string PrefsJson, bool Merge = false)`）、`GET /api/oa/pref/notify-matrix`、`GET /api/oa/inbox/pending?rowMode=&page=&pageSize=`、`POST /api/oa/inbox/batch-transfer`、`POST /api/oa/inbox/batch-transfer/preview`。
- 业务错误 i18n 键（不占 E-WF 码，走既有「message=键、前端 t(raw)」口径）：`oa.bt.errSameUser` / `oa.bt.errTargetInvalid` / `oa.bt.errTooMany` / `oa.pref.errBadJson`。
- 通知类型键（camelCase 枚举名）：`todoCreated` / `flowApproved` / `flowRejected` / `timeout` / （`branchPruned` 若枚举已合入）。

## 附: R1通知栈
### R1 通知栈现状

- `WfNotificationType`（`CP6.Entity/DomainModels/Wf/WfNotificationType.cs`）**实际值域 4 个 const int**：`TodoCreated=1, FlowApproved=2, FlowRejected=3, Timeout=4`。`BranchPruned` **尚未合入**（hardening spec §4.2 同期新增）。
- `IWfNotifier`（`CP6.Core/Services/Wf/IWfNotifier.cs`）只有 3 个方法：`TodoCreatedAsync / FlowApprovedAsync / FlowRejectedAsync`。**没有 TimeoutAsync**。
- **邮件动作清单**（矩阵格子禁用依据）：`PersistentWfNotifier`（`CP6.WebApi/Services/PersistentWfNotifier.cs`）3 个方法都有 `TrySendEmailAsync` 邮件动作 → **todoCreated / flowApproved / flowRejected 双通道有效**；`Timeout(4)` **全库无生产者**（`WfTimeoutService.ScanOnceAsync` 的 remind/escalate 均调 `TodoCreatedAsync`，以 Type=1 发出）→ **timeout 行 inApp+email 双格子禁用**（带提示，数据驱动，将来接独立发送路径自动点亮）。
- **既有偏好机制**（关键）：`IPrefService.GetNotifyPrefsAsync` → `PrefService.ParseNotifyPrefs`（`CP6.Core/Services/Oa/PrefService.cs:38-62`）已解析 `PrefsJson.notify` 键，但是**扁平形态** `{"notify":{"todo":bool,"approved":bool,"rejected":bool,"timeout":bool,"email":bool}}`——事件开关 + 单一全局 email 开关，非矩阵。`notify` 键已被占用。
