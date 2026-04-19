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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

/// <summary>
/// OperLogFilter 单元测试（用 Moq 模拟 RabbitMQ）
///
/// 面试要点：
/// 1. Moq 框架的核心 API：
///    - Mock&lt;T&gt;() 创建模拟对象
///    - Setup() 配置方法/属性返回值
///    - Verify() 验证方法是否被调用
/// 2. 测试 ActionFilter 需要手动构造 ActionExecutingContext（模拟 HTTP 请求）
/// 3. 降级策略测试：MQ 可用走 MQ，MQ 不可用走 DB
/// </summary>
public class OperLogFilterTests
{
    // 创建空配置（RabbitMQ 未配置 → 构造函数安全退出，IsConnected=false）
    private static readonly IConfiguration _emptyConfig =
        new ConfigurationBuilder().AddInMemoryCollection().Build();
    private static readonly NullLogger<RabbitMQService> _nullLogger = new();

    /// <summary>
    /// 创建 Mock 的 RabbitMQService（可自定义 IsConnected）
    /// </summary>
    private static Mock<RabbitMQService> CreateMockMq(bool isConnected)
    {
        var mock = new Mock<RabbitMQService>(_emptyConfig, _nullLogger);
        mock.Setup(m => m.IsConnected).Returns(isConnected);
        if (isConnected)
        {
            mock.Setup(m => m.PublishAsync(
                It.IsAny<string>(), It.IsAny<Sys_OperLog>()))
                .Returns(Task.CompletedTask);
        }
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
    public async Task POST_WhenMqDisconnected_ShouldWriteToDb()
    {
        // Arrange：MQ 不可用 → 应该降级到直接写 DB
        var context = TestHelper.CreateInMemoryContext();
        var mockMq = CreateMockMq(isConnected: false);

        var filter = new OperLogFilter(context, mockMq.Object);
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

        // Verify：MQ 的 PublishAsync 没有被调用
        mockMq.Verify(m => m.PublishAsync(
            It.IsAny<string>(), It.IsAny<Sys_OperLog>()), Times.Never);
    }

    [Fact]
    public async Task POST_WhenMqConnected_ShouldPublishToMq()
    {
        // Arrange：MQ 可用 → 应该发消息到 MQ，不写 DB
        var context = TestHelper.CreateInMemoryContext();
        var mockMq = CreateMockMq(isConnected: true);

        var filter = new OperLogFilter(context, mockMq.Object);
        var (ctx, next) = CreateMockContext();

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert：DB 中没有写入日志（走了 MQ）
        Assert.Equal(0, context.Sys_OperLogs.Count());

        // Verify：PublishAsync 被调用了 1 次，队列名正确
        mockMq.Verify(m => m.PublishAsync(
            RabbitMQService.OperLogQueue, It.IsAny<Sys_OperLog>()), Times.Once);
    }

    [Fact]
    public async Task GET_ShouldBeSkipped()
    {
        // Arrange：GET 请求不应该记录日志
        var context = TestHelper.CreateInMemoryContext();
        var mockMq = CreateMockMq(isConnected: false);

        var filter = new OperLogFilter(context, mockMq.Object);
        var (ctx, next) = CreateMockContext(method: "GET", path: "/api/dict/getTypes");

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert：DB 无日志，MQ 也没调用
        Assert.Equal(0, context.Sys_OperLogs.Count());
        mockMq.Verify(m => m.PublishAsync(
            It.IsAny<string>(), It.IsAny<Sys_OperLog>()), Times.Never);
    }

    [Fact]
    public async Task AuthPath_ShouldBeSkipped()
    {
        // Arrange：/api/auth/login 不应该记录日志（防止密码泄露）
        var context = TestHelper.CreateInMemoryContext();
        var mockMq = CreateMockMq(isConnected: false);

        var filter = new OperLogFilter(context, mockMq.Object);
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
        var mockMq = CreateMockMq(isConnected: false);

        var filter = new OperLogFilter(context, mockMq.Object);
        var (ctx, next) = CreateMockContext(method: "DELETE", path: "/api/operlog/delete");

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert：DB 无日志
        Assert.Equal(0, context.Sys_OperLogs.Count());
    }
}
