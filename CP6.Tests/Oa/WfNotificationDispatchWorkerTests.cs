using System.Reflection;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Oa;

public sealed class WfNotificationDispatchWorkerTests
{
    [Fact]
    public void NotifyHub_requires_authentication()
    {
        Assert.NotNull(typeof(NotifyHub).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public async Task Dispatch_targets_only_the_notification_recipient()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseSqlite(connection)
            .Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Wf_Notification (
                Id TEXT NOT NULL PRIMARY KEY,
                TenantId TEXT NOT NULL,
                EventKey TEXT NULL,
                InAppRequested INTEGER NOT NULL,
                EmailRequested INTEGER NOT NULL,
                DispatchStatus INTEGER NOT NULL,
                DispatchAttempts INTEGER NOT NULL,
                NextAttemptAtUtc TEXT NULL,
                DispatchedAtUtc TEXT NULL,
                LastDispatchError TEXT NULL,
                UserId TEXT NOT NULL,
                Type INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Body TEXT NOT NULL,
                InstanceId TEXT NULL,
                TaskId TEXT NULL,
                FlowKey TEXT NULL,
                IsRead INTEGER NOT NULL,
                ReadAt TEXT NULL,
                Creator TEXT NULL,
                CreateDate TEXT NOT NULL,
                Modifier TEXT NULL,
                ModifyDate TEXT NULL
            );
            """);
        var userId = Guid.NewGuid();
        db.Wf_Notifications.Add(new Wf_Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = WfNotificationType.TodoCreated,
            Title = "Todo",
            Body = "A workflow task is ready.",
            InAppRequested = true,
            EmailRequested = false,
            DispatchStatus = 0,
        });
        await db.SaveChangesAsync();

        var hub = new RecordingHub();
        var services = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IHubContext<NotifyHub>>(hub)
            .AddSingleton<IEmailSender, NoopEmailSender>()
            .BuildServiceProvider();
        await using (services)
        {
            var worker = new WfNotificationDispatchWorker(
                services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<WfNotificationDispatchWorker>.Instance);

            await worker.DispatchBatchAsync(CancellationToken.None);
        }

        Assert.Equal(userId.ToString(), Assert.Single(hub.ClientsImpl.UserIds));
        Assert.Equal(0, hub.ClientsImpl.AllAccessCount);
    }

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string body) => Task.CompletedTask;
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public Task SendCoreAsync(
            string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingHubClients : IHubClients
    {
        private readonly RecordingClientProxy _proxy = new();
        public List<string> UserIds { get; } = new();
        public int AllAccessCount { get; private set; }

        public IClientProxy All
        {
            get
            {
                AllAccessCount++;
                return _proxy;
            }
        }

        public IClientProxy User(string userId)
        {
            UserIds.Add(userId);
            return _proxy;
        }

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Client(string connectionId) => _proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;
        public IClientProxy Group(string groupName) => _proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
    }

    private sealed class RecordingHub : IHubContext<NotifyHub>
    {
        public RecordingHubClients ClientsImpl { get; } = new();
        public IHubClients Clients => ClientsImpl;
        public IGroupManager Groups => null!;
    }
}
