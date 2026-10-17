using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.DatabaseCompatibility;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit.Abstractions;

namespace CP6.Tests.Sys;

[Collection(Wp5ReportsRelationalCollection.Name)]
public sealed class DashboardTenantIsolationTests(Wp5ReportsRelationalFixture fixture, ITestOutputHelper output)
{
    [Wp5ReportsFact]
    public async Task Native_http_aggregates_recent_orders_and_warm_cache_stay_with_the_authenticated_tenant()
    {
        output.WriteLine(fixture.SetupSummary);
        await using var host = await Host.Start(fixture);
        var expected = new Dictionary<Guid, DashboardController.SummaryDto>
        {
            [host.A] = new(9, 9, 2, 1, 1, 1, 2, 2),
            [host.B] = new(2, 2, 4, 3, 3, 3, 4, 4),
            [host.C] = new(0, 0, 0, 0, 0, 0, 0, 0)
        };
        foreach (var tenant in new[] { host.A, host.B, host.C })
        {
            var actual = await host.Read(tenant);
            Assert.Equal(expected[tenant], actual.Summary);
            var orders = host.Rows.OfType<Order>().Where(o => o.TenantId == tenant).OrderByDescending(o => o.CreateDate).Take(8);
            Assert.Equal(orders.Select(o => new DashboardController.RecentOrderDto(o.WebOrderNo, o.CustomerCd, o.Quantity, o.OrderDate, o.ShipStatus)), actual.RecentOrders);
            Assert.Equal(host.Rows.OfType<WorkOrder>().Where(w => w.TenantId == tenant).GroupBy(w => w.Status).OrderBy(g => g.Key)
                .Select(g => new DashboardController.StatusCountDto(g.Key, g.Count())), actual.WorkOrderStatus);
        }
        Assert.Equal(3, host.Cache.Writes.Count);
        Assert.All(host.Cache.Writes, w => Assert.Equal(TimeSpan.FromMinutes(1), w.Value.SlidingExpiration));

        // Change the database after warming all three tenants: actual cache hits must keep each snapshot.
        var added = new Order { TenantId = host.A, WebOrderNo = host.Key(), CustomerCd = "ONLY-A", OrderType = "0100",
            CreateDate = host.Today.AddDays(2), Quantity = 123.45678901m };
        await host.Save(added);
        foreach (var tenant in new[] { host.C, host.B, host.A, host.B, host.C, host.A })
        {
            var actual = await host.Read(tenant, $"?tenantId={host.B}", host.B);
            Assert.Equal(expected[tenant], actual.Summary);
            Assert.DoesNotContain(actual.RecentOrders, o => o.WebOrderNo == added.WebOrderNo);
        }
        Assert.Equal(3, host.Cache.Writes.Count); // No factories ran on those six warm accesses.
        host.Cache.Clear();
        var fresh = await host.Read(host.A);
        Assert.Equal(expected[host.A] with { TodayOrders = 10, MonthOrders = 10 }, fresh.Summary);
        Assert.Equal(added.Quantity, fresh.RecentOrders[0].Quantity);
        Assert.Equal(expected[host.B], (await host.Read(host.B)).Summary);
        Assert.Equal(expected[host.C], (await host.Read(host.C)).Summary);
        Assert.Equal(host.Today, DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified));
        output.WriteLine("A/B/empty C: every KPI, tenant-local top8/decimal/status, cold and alternating warm reads, spoofed query/header, one-minute sliding cache verified.");
    }

    [Wp5ReportsFact]
    public async Task Legacy_global_dashboard_cache_is_never_a_source_for_an_empty_tenant()
    {
        await using var host = await Host.Start(fixture);
        var poison = new DashboardController.DashboardData(new(99, 99, 99, 99, 99, 99, 99, 99),
            [new("OTHER-TENANT", "PRIVATE", 1.25m, host.Today, 0)], [new(3, 99)]);
        await host.Cache.SetStringAsync("dashboard:summary", JsonSerializer.Serialize(poison));
        var actual = await host.Read(host.C);
        Assert.Equal(new DashboardController.SummaryDto(0, 0, 0, 0, 0, 0, 0, 0), actual.Summary);
        Assert.Empty(actual.RecentOrders);
        Assert.Empty(actual.WorkOrderStatus);
    }

    [Wp5ReportsFact]
    public async Task Warm_cache_cannot_bypass_missing_invalid_cross_tenant_pending_or_disabled_subjects()
    {
        await using var host = await Host.Start(fixture);
        await host.Read(host.A);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Send(null)).StatusCode);
        var id = host.Users[host.A].ToString();
        var tenant = host.A.ToString();
        foreach (var token in new[] {
            host.Token(id, null), host.Token(id, "invalid"), host.Token(id, Guid.Empty.ToString()),
            host.Token(null, tenant), host.Token("admin", tenant), host.Token(Guid.Empty.ToString(), tenant),
            host.Token(Guid.NewGuid().ToString(), tenant), host.Token(id, host.B.ToString()),
            host.Token(id, tenant, new Claim("sub", host.Users[host.B].ToString())),
            host.Token(id, tenant, new Claim("tenant_id", host.B.ToString())),
            host.Token(id, tenant, new Claim("token_use", "2fa_verify")),
            host.Token(id, host.B.ToString(), new Claim("is_platform_admin", "true")) })
            Assert.Equal(HttpStatusCode.Forbidden, (await host.Send(token, $"?tenantId={host.A}", host.A)).StatusCode);
        await using (var db = fixture.CreateContext(host.A))
        {
            var user = await db.Sys_Users.SingleAsync(u => u.Id == host.Users[host.A]);
            user.Enable = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Send(host.Token(id, tenant))).StatusCode);
        Assert.Single(host.Cache.Writes);
    }

    [Wp5ReportsFact]
    public async Task Jwt_tenant_must_match_the_server_database_context()
    {
        await using var host = await Host.Start(fixture, mismatchContext: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Send(host.Token(host.Users[host.A].ToString(), host.A.ToString()))).StatusCode);
        Assert.Empty(host.Cache.Writes);
    }

    private sealed class RecordingCache : IDistributedCache, IDisposable
    {
        private readonly MemoryDistributedCache inner = new(Options.Create(new MemoryDistributedCacheOptions()));
        public Dictionary<string, DistributedCacheEntryOptions> Writes { get; } = [];
        public byte[]? Get(string key) => inner.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { Writes[key] = options; inner.Set(key, value, options); }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { Set(key, value, options); return Task.CompletedTask; }
        public void Refresh(string key) => inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);
        public void Remove(string key) => inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
        public void Clear() { foreach (var key in Writes.Keys) inner.Remove(key); Writes.Clear(); }
        public void Dispose() => Clear();
    }

    private sealed class OnlyDashboard : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { feature.Controllers.Clear(); feature.Controllers.Add(typeof(DashboardController).GetTypeInfo()); }
    }

    private sealed class Host(Wp5ReportsRelationalFixture fixture) : IAsyncDisposable
    {
        public Guid A { get; } = TenantContext.DefaultTenant;
        public Guid B { get; } = Guid.NewGuid();
        public Guid C { get; } = Guid.NewGuid();
        public DateTime Today { get; } = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        public Dictionary<Guid, Guid> Users { get; } = [];
        public List<BaseBizEntity> Rows { get; } = [];
        public RecordingCache Cache { get; } = new();
        private readonly List<Guid> createdTenants = [];
        private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        private WebApplication? app;
        private HttpClient? client;
        public string Key() => Guid.NewGuid().ToString("N")[..14];

        public static async Task<Host> Start(Wp5ReportsRelationalFixture fixture, bool mismatchContext = false)
        {
            var host = new Host(fixture);
            try { await host.Initialize(mismatchContext); return host; }
            catch { await host.DisposeAsync(); throw; }
        }
        public async Task Save(params BaseBizEntity[] rows)
        {
            Rows.AddRange(rows);
            foreach (var group in rows.GroupBy(r => r.TenantId))
            {
                await using var db = fixture.CreateContext(group.Key);
                db.AddRange(group);
                await db.SaveChangesAsync();
            }
        }
        private async Task Initialize(bool mismatchContext)
        {
            await fixture.RequireEmptyReportTablesAsync();
            foreach (var tenant in new[] { A, B, C })
            {
                await using var db = fixture.CreateContext(tenant);
                if (!await db.Sys_Tenants.AnyAsync(t => t.Id == tenant))
                {
                    createdTenants.Add(tenant);
                    db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = Key(), TenantName = "BUG257 isolated" });
                }
                var user = new Sys_User { Id = Guid.NewGuid(), UserName = Key(), Password = "not-a-login-credential", Enable = true };
                Users.Add(tenant, user.Id);
                db.Sys_Users.Add(user);
                await db.SaveChangesAsync();
            }
            var month = new DateTime(Today.Year, Today.Month, 1);
            foreach (var (tenant, count) in new[] { (A, 9), (B, 2) })
                for (var i = 0; i < count; i++)
                    await Save(new Order { TenantId = tenant, WebOrderNo = Key(), CustomerCd = tenant == A ? "A" : "B", OrderType = "0100",
                        CreateDate = Today.AddMinutes(i + (tenant == A ? 1 : 30)), OrderDate = Today, Quantity = i + 0.12345678m, IsDeleted = i == 8 });
            await Save(new Order { TenantId = A, WebOrderNo = Key(), OrderType = "0100", CreateDate = month.AddSeconds(-1), Quantity = 999m });
            WorkOrder Work(Guid tenant, int status, DateTime? end = null) => new() { TenantId = tenant, WorkOrderNo = Key(), ProductCd = "ISOLATED", Status = status, ActualEndDate = end };
            await Save(Work(A, 1), Work(A, 4, month), Work(A, 6, month.AddSeconds(-1)), Work(A, 9),
                Work(B, 2), Work(B, 3), Work(B, 5), Work(B, 6, Today), Work(B, 4, Today.AddDays(1)), Work(B, 6, month), Work(B, 0));
            foreach (var (tenant, statuses) in new[] { (A, new[] { 1, 4 }), (B, new[] { 1, 2, 3, 4 }) })
                foreach (var status in statuses) await Save(new OutboundOrder { TenantId = tenant, OutboundNo = Key(), WarehouseCd = "ISOLATED", Status = status });
            foreach (var (tenant, quantities) in new[] { (A, new[] { 0m, 1m }), (B, new[] { -1m, -2m, 0m, 1m }) })
                foreach (var quantity in quantities) await Save(new Stock { TenantId = tenant, WarehouseCd = "ISOLATED", LocationCd = Key(), ProductCd = "ISOLATED", LotNo = Key(), PhysicalQty = Math.Max(0m, quantity), AllocatedQty = Math.Max(0m, -quantity), AvailableQty = quantity });
            foreach (var (tenant, statuses) in new[] { (A, new[] { 0, 1 }), (B, new[] { 0, 0, 1, 1 }) })
                foreach (var status in statuses)
                {
                    var code = Key();
                    await Save(new ProductMaster { TenantId = tenant, ProductCd = code, ItemCd = code, CustomerCd = "ISOLATED", SetProductCd = code, Status = status });
                }
            foreach (var tenant in new[] { A, B, B })
                await Save(new StockTake { TenantId = tenant, StockTakeNo = Key(), TargetWarehouseCd = "ISOLATED", Status = 3 });

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "DashboardIsolationTests", Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:18257");
            var services = builder.Services;
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddDbContext<CP6Context>((sp, o) => {
                var profile = DatabaseMigrationProfile.For(fixture.Database, DatabaseContextKind.Core);
                DatabaseContextOptions.Configure(o, fixture.Database, fixture.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
            });
            services.AddScoped<IDbConnection>(_ => fixture.CreateConnection());
            services.AddSingleton<IDistributedCache>(Cache); services.AddSingleton<CacheService>(); services.AddHttpContextAccessor();
            services.AddScoped<ICurrentPermissionContext, CurrentPermissionContext>(); services.AddScoped<IPermissionAggregator, PermissionAggregator>();
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => {
                o.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = "bug257", ValidAudience = "bug257", IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) };
            });
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(DashboardController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyDashboard()));
            app = builder.Build();
            app.UseRouting(); app.UseAuthentication(); app.UseMiddleware<TenantMiddleware>();
            if (mismatchContext) app.Use(async (ctx, next) => { ctx.RequestServices.GetRequiredService<ITenantContext>().CurrentTenantId = B; await next(); });
            app.UseAuthorization(); app.MapControllers();
            await app.StartAsync();
            client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:18257"), Timeout = TimeSpan.FromSeconds(20) };
        }
        public string Token(string? user, string? tenant, params Claim[] extra)
        {
            var claims = new List<Claim>(extra);
            if (user != null) claims.Add(new(ClaimTypes.NameIdentifier, user));
            if (tenant != null) claims.Add(new("tenant_id", tenant));
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("bug257", "bug257", claims, expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256)));
        }
        public async Task<HttpResponseMessage> Send(string? token, string query = "", Guid? spoof = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard" + query);
            if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (spoof.HasValue) request.Headers.Add("X-Tenant-Id", spoof.Value.ToString());
            return await client!.SendAsync(request);
        }
        public async Task<DashboardController.DashboardData> Read(Guid tenant, string query = "", Guid? spoof = null)
        {
            using var response = await Send(Token(Users[tenant].ToString(), tenant.ToString()), query, spoof);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonSerializer.Deserialize<DashboardController.DashboardData>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        public async ValueTask DisposeAsync()
        {
            client?.Dispose();
            if (app != null) { await app.StopAsync(); await app.DisposeAsync(); }
            Cache.Dispose();
            await using var db = fixture.CreateContext(A);
            var ids = Rows.Select(r => r.Id).ToArray();
            var users = Users.Values.ToArray();
            var tenants = new[] { A, B, C };
            async Task Delete<T>() where T : BaseBizEntity => await db.Set<T>().IgnoreQueryFilters().Where(r => tenants.Contains(r.TenantId) && ids.Contains(r.Id)).ExecuteDeleteAsync();
            await Delete<Order>(); await Delete<WorkOrder>(); await Delete<OutboundOrder>(); await Delete<Stock>(); await Delete<ProductMaster>(); await Delete<StockTake>();
            await db.Sys_Users.IgnoreQueryFilters().Where(u => users.Contains(u.Id) && tenants.Contains(u.TenantId)).ExecuteDeleteAsync();
            var keys = ids.Concat(users).Concat(createdTenants).Select(id => id.ToString()).ToArray();
            await db.Sys_FieldAuditLogs.IgnoreQueryFilters().Where(r => tenants.Contains(r.TenantId) && keys.Contains(r.EntityKey)).ExecuteDeleteAsync();
            await db.Sys_Tenants.Where(t => createdTenants.Contains(t.Id)).ExecuteDeleteAsync();
        }
    }
}
