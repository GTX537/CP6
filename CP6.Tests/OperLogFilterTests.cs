using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels;
using CP6.WebApi.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CP6.Tests;

/// <summary>
/// OperLogFilter 单元测试（操作ログ = Kafka 専任）
///
/// 要点：
/// 1. OperLogFilter は単一 IOperLogTransport（Kafka 実装）へ投げる。
/// 2. transport 就绪 → 投递；不可用 → 降级写 DB（保证不丢）。
/// 3. /api/auth・/api/operlog は常にスキップ。GET は OperLog:IncludeGet=true のみ記録。
/// </summary>
public class OperLogFilterTests
{
    /// <summary>构造仅含 IncludeGet 开关的配置（默认 false，GET 不记录）。</summary>
    private static IConfiguration BuildConfig(bool includeGet = false)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OperLog:IncludeGet"] = includeGet ? "true" : "false"
            })
            .Build();
    }

    /// <summary>创建 Mock 的 IOperLogTransport（可自定义 IsConnected）。</summary>
    private static Mock<IOperLogTransport> CreateMockTransport(bool isConnected)
    {
        var mock = new Mock<IOperLogTransport>();
        mock.SetupGet(m => m.Name).Returns("Kafka");
        mock.SetupGet(m => m.IsConnected).Returns(isConnected);
        mock.Setup(m => m.PublishAsync(It.IsAny<Sys_OperLog>())).Returns(Task.CompletedTask);
        return mock;
    }

    /// <summary>
    /// 构造模拟的 ActionExecutingContext + ActionExecutionDelegate
    /// 模拟一个 POST /api/dict/addType 请求
    /// </summary>
    private static (ActionExecutingContext context, ActionExecutionDelegate next) CreateMockContext(
        string method = "POST",
        string path = "/api/dict/addType",
        string controller = "Dict",
        string action = "AddType")
    {
        // 模拟 HttpContext
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Request.Path = path;
        // 模拟登录用户
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "testUser")
            }, "TestAuth"));

        // 路由数据
        var routeData = new RouteData();
        routeData.Values["controller"] = controller;
        routeData.Values["action"] = action;

        // ActionContext
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        // ActionExecutingContext（Filter 接收的上下文）
        var executingContext = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?> { { "entity", new { Name = "test" } } },
            controller: null!);

        // ActionExecutionDelegate（模拟 next() 返回正常结果）
        var executedContext = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller: null!)
        {
            Result = new OkObjectResult("ok")
        };
        ActionExecutionDelegate next = () => Task.FromResult(executedContext);

        return (executingContext, next);
    }

    [Fact]
    public async Task POST_WhenTransportDisconnected_ShouldWriteToDb()
    {
        // Arrange：Kafka 不可用 → 应该降级到直接写 DB
        var context = TestHelper.CreateInMemoryContext();
        var kafka = CreateMockTransport(isConnected: false);

        var filter = new OperLogFilter(context, kafka.Object, BuildConfig());
        var (ctx, next) = CreateMockContext();

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert：日志写入了 DB
        Assert.Equal(1, context.Sys_OperLogs.Count());
        var log = context.Sys_OperLogs.First();
        Assert.Equal("POST", log.HttpMethod);
        Assert.Equal("/api/dict/addType", log.RequestUrl);
        Assert.Equal("testUser", log.UserName);
        Assert.Equal("Dict", log.Controller);

        // Verify：通道没有被投递
        kafka.Verify(m => m.PublishAsync(It.IsAny<Sys_OperLog>()), Times.Never);
    }

    [Fact]
    public async Task POST_WhenTransportConnected_ShouldPublishAndSkipDb()
    {
        // Arrange：Kafka 可用 → 投 Kafka，不写 DB
        var context = TestHelper.CreateInMemoryContext();
        var kafka = CreateMockTransport(isConnected: true);

        var filter = new OperLogFilter(context, kafka.Object, BuildConfig());
        var (ctx, next) = CreateMockContext();

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert：DB 中没有写入日志（走了 Kafka）
        Assert.Equal(0, context.Sys_OperLogs.Count());

        // Verify：投递了 1 次
        kafka.Verify(m => m.PublishAsync(It.IsAny<Sys_OperLog>()), Times.Once);
    }

    [Fact]
    public async Task GET_WithoutIncludeGet_ShouldBeSkipped()
    {
        // Arrange：默认 IncludeGet=false → GET 请求不应该记录日志
        var context = TestHelper.CreateInMemoryContext();
        var kafka = CreateMockTransport(isConnected: false);

        var filter = new OperLogFilter(context, kafka.Object, BuildConfig(includeGet: false));
        var (ctx, next) = CreateMockContext(method: "GET", path: "/api/dict/getTypes");

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert：DB 无日志，通道也没调用
        Assert.Equal(0, context.Sys_OperLogs.Count());
        kafka.Verify(m => m.PublishAsync(It.IsAny<Sys_OperLog>()), Times.Never);
    }

    [Fact]
    public async Task GET_WithIncludeGet_ShouldBeRecorded()
    {
        // Arrange：IncludeGet=true → GET 也记录（"任何操作都记录"）
        var context = TestHelper.CreateInMemoryContext();
        var kafka = CreateMockTransport(isConnected: false);

        var filter = new OperLogFilter(context, kafka.Object, BuildConfig(includeGet: true));
        var (ctx, next) = CreateMockContext(method: "GET", path: "/api/dict/getTypes");

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert：通道断开 → 降级写 DB，GET 被记录
        Assert.Equal(1, context.Sys_OperLogs.Count());
        Assert.Equal("GET", context.Sys_OperLogs.First().HttpMethod);
    }

    [Fact]
    public async Task AuthPath_ShouldBeSkipped()
    {
        // Arrange：/api/auth/login 不应该记录日志（防止密码泄露）
        var context = TestHelper.CreateInMemoryContext();
        var kafka = CreateMockTransport(isConnected: false);

        var filter = new OperLogFilter(context, kafka.Object, BuildConfig());
        var (ctx, next) = CreateMockContext(method: "POST", path: "/api/auth/login");

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert：DB 无日志（登录请求被过滤）
        Assert.Equal(0, context.Sys_OperLogs.Count());
    }

    [Fact]
    public async Task OperLogPath_ShouldBeSkipped()
    {
        // Arrange：/api/operlog 自身的操作不应该记录（避免死循环）
        var context = TestHelper.CreateInMemoryContext();
        var kafka = CreateMockTransport(isConnected: false);

        var filter = new OperLogFilter(context, kafka.Object, BuildConfig());
        var (ctx, next) = CreateMockContext(method: "DELETE", path: "/api/operlog/delete");

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert：DB 无日志
        Assert.Equal(0, context.Sys_OperLogs.Count());
    }
}
