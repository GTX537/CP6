using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.WebApi.Controllers;
using CP6.WebApi.Controllers.Platform;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CP6.Tests.Platform;

[CollectionDefinition("TenantManagementPostgres", DisableParallelization = true)]
public sealed class TenantManagementPostgresCollection : ICollectionFixture<TenantManagementPostgresFixture>;

internal sealed class TenantManagementPostgresTheoryAttribute : TheoryAttribute
{
    public TenantManagementPostgresTheoryAttribute()
    {
        if (!CurrentPermissionPostgresFactAttribute.Selected)
            Skip = "Explicit owned PostgreSQL component run requires CP6_PERMISSION_PG_ROOT and CP6_PERMISSION_PG_BIN.";
    }
}

// One owned cluster and one migrated database per run, reused by serial cases with distinct tenants.
// Real migration triggers are required for identity generation/RowVersion and priority Outbox rollback.
public sealed class TenantManagementPostgresFixture : IAsyncLifetime
{
    private readonly CurrentPermissionPostgresFixture _server = new();
    internal const string Database = "tenant_management_component";

    public async Task InitializeAsync()
    {
        await _server.InitializeAsync();
        if (!CurrentPermissionPostgresFactAttribute.Selected) return;
        try
        {
            await using (var owner = await _server.OpenOwnerAsync("postgres"))
                await CurrentPermissionPostgresFixture.Sql(owner, $"CREATE DATABASE {Database}");
            await using var core = Context(new TenantContext());
            await core.Database.MigrateAsync();
            await using var priority = PriorityContext();
            await priority.Database.MigrateAsync();
        }
        catch { await _server.DisposeAsync(); throw; }
    }

    internal CP6Context Context(ITenantContext tenant, CrmIdentityRuntime? identity = null, params IInterceptor[] interceptors)
    {
        using var template = _server.Context(tenant.CurrentTenantId, Database);
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database,
            template.Database.GetConnectionString()!, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        options.AddInterceptors(interceptors);
        var context = new CP6Context(options.Options, tenant, identity: identity);
        context.Database.SetCommandTimeout(60);
        return context;
    }

    internal IdentityMessagingContext PriorityContext()
    {
        using var template = _server.Context(TenantContext.DefaultTenant, Database);
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.IdentityPriority);
        return new IdentityMessagingContext(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<IdentityMessagingContext>(),
            database, template.Database.GetConnectionString()!, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options);
    }

    public Task DisposeAsync() => _server.DisposeAsync();
}

internal sealed class TenantManagementHttpHost(WebApplication app, HttpClient client, string secret) : IAsyncDisposable
{
    internal HttpClient Client => client;
    internal IServiceProvider Services => app.Services;
    internal string Token(Guid actor, Guid tenant, bool platform = true, bool impersonation = false)
        => JwtHelper.GenerateToken(actor.ToString(), "operator", secret, "tenant-component", "tenant-component", 5,
            tenant, isPlatformAdmin: platform, impersonatorId: impersonation ? Guid.NewGuid() : null);

    internal static async Task<TenantManagementHttpHost> Start(TenantManagementPostgresFixture fixture,
        CrmIdentityRuntime? identity = null, bool includeRoles = false, bool includePlatformAdmins = false,
        IInterceptor? interceptor = null)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "TenantManagementComponent", Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var services = builder.Services;
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped(sp => fixture.Context(sp.GetRequiredService<ITenantContext>(), identity,
            interceptor is null ? [] : [interceptor]));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<IPasswordHasher>(_ => new BCryptPasswordHasher(4));
        services.AddScoped<ITenantAdminService, TenantAdminService>();
        if (includeRoles || includePlatformAdmins)
        {
            services.AddDistributedMemoryCache();
            services.AddScoped<IPermissionAggregator, PermissionAggregator>();
            services.AddScoped<ICurrentPermissionContext, CurrentPermissionContext>();
            services.AddScoped<IPermissionService, PermissionService>();
        }
        if (includePlatformAdmins)
        {
            services.AddScoped<ISecurityAuditService, SecurityAuditService>();
            services.AddScoped<IPlatformAdminService, PlatformAdminService>();
            // These dependencies are not called by the tested no-password user update/delete.
            services.AddSingleton(Moq.Mock.Of<IRefreshTokenService>(Moq.MockBehavior.Strict));
            services.AddSingleton(Moq.Mock.Of<ITwoFactorService>(Moq.MockBehavior.Strict));
        }
        services.AddSingleton<IStringLocalizer>(EchoStringLocalizer.Instance);
        services.Configure<SecurityOptions>(options => options.Csrf.Enabled = true);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ValidIssuer = "tenant-component", ValidAudience = "tenant-component"
            };
        });
        services.AddAuthorization();
        services.AddControllers().AddApplicationPart(typeof(TenantController).Assembly)
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new OnlySelectedControllers(includeRoles, includePlatformAdmins)));
        var app = builder.Build();
        app.UseRouting(); app.UseAuthentication(); app.UseMiddleware<TenantMiddleware>();
        app.UseMiddleware<BizExceptionMiddleware>(); app.UseMiddleware<CsrfMiddleware>();
        app.UseMiddleware<MustChangePasswordMiddleware>(); app.UseAuthorization(); app.MapControllers();
        try
        {
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new TenantManagementHttpHost(app, new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) }, secret);
        }
        catch { await app.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync() { client.Dispose(); await app.StopAsync(); await app.DisposeAsync(); }

    private sealed class OnlySelectedControllers(bool includeRoles, bool includePlatformAdmins) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.Where(x => x.AsType() != typeof(TenantController)
                && (!includeRoles || x.AsType() != typeof(RoleController))
                && (!includePlatformAdmins || (x.AsType() != typeof(PlatformAdminController) && x.AsType() != typeof(UserController)))).ToArray())
                feature.Controllers.Remove(controller);
        }
    }
}
