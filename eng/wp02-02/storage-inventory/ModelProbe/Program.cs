using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

if (args.Length != 2) throw new ArgumentException("Expected adopted package directory and output JSON path.");
var package = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
    AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(package, name.Name + ".dll"));
Assembly.GetExecutingAssembly().GetType("ModelExtractor")!.GetMethod("Run")!.Invoke(null, [args[1]]);

public static class ModelExtractor
{
    public static void Run(string output)
    {
        var contexts = new List<object>();
        using var core = new CP6Context(Options<CP6Context>());
        using var space = new SpaceContext(Options<SpaceContext>(), new InventoryExecution(), new SystemSpaceClock());
        using var identity = new IdentityMessagingContext(Options<IdentityMessagingContext>());
        using var erp = new ErpIntegrationContext(Options<ErpIntegrationContext>());
        using var stock = new StockOutboxContextV1(Options<StockOutboxContextV1>());
        using var movement = Derived("StockMovementWriteContextV1");
        using var directory = Derived("TenantIdentityDirectoryReader+DirectoryContext");
        using var stockRead = Derived("TenantStockReadStore+StockReadContext");
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=metadata_only;Username=metadata_only");
        using var rmaV1 = Rma(connection, "rma-application-core-tenant-v1", "rma-tenant-storage-0001");
        using var rmaV2 = Rma(connection, "rma-application-core-tenant-v2", "rma-tenant-storage-0002");
        foreach (var context in new DbContext[] { core, space, identity, erp, stock, movement, directory, stockRead, rmaV1, rmaV2 })
        {
            var model = context.GetService<IDesignTimeModel>().Model;
            contexts.Add(new
            {
                context = context.GetType().FullName,
                profile = ReferenceEquals(context, rmaV1) ? "rma-application-core-tenant-v1" :
                    ReferenceEquals(context, rmaV2) ? "rma-application-core-tenant-v2" : "fixed-shared",
                assembly = context.GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                provider = context.Database.ProviderName,
                tables = model.GetRelationalModel().Tables.OrderBy(t => t.Schema).ThenBy(t => t.Name).Select(t => new
                {
                    schema = t.Schema ?? "public", name = t.Name,
                    entities = model.GetEntityTypes().Where(e => e.GetTableName() == t.Name && e.GetSchema() == t.Schema)
                        .Select(e => e.Name).Distinct().Order().ToArray(),
                    dbsets = context.GetType().GetProperties().Where(p => p.PropertyType.IsGenericType &&
                        p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>) &&
                        model.GetEntityTypes().Any(e => e.GetTableName() == t.Name && e.GetSchema() == t.Schema &&
                            e.ClrType == p.PropertyType.GenericTypeArguments[0])).Select(p => p.Name).Order().ToArray(),
                    columns = t.Columns.Select(c => new { name = c.Name, type = c.StoreType, nullable = c.IsNullable }).OrderBy(c => c.name).ToArray(),
                    unique_keys = t.UniqueConstraints.Select(k => new { name = k.Name, primary = k == t.PrimaryKey,
                        columns = k.Columns.Select(c => c.Name).ToArray() }).OrderBy(k => k.name).ToArray(),
                    unique_indexes = t.Indexes.Where(i => i.IsUnique).Select(i => new
                    {
                        name = i.Name, columns = i.Columns.Select(c => c.Name).ToArray(), filter = i.Filter,
                        nulls_not_distinct = i.MappedIndexes.Any(index => index.FindAnnotation("Npgsql:NullsDistinct")?.Value is false)
                    }).OrderBy(i => i.name).ToArray()
                }).ToArray(),
                views = model.GetRelationalModel().Views.OrderBy(v => v.Schema).ThenBy(v => v.Name).Select(v => new
                {
                    schema = v.Schema ?? "public", name = v.Name,
                    entities = model.GetEntityTypes().Where(e => e.GetViewName() == v.Name && e.GetViewSchema() == v.Schema)
                        .Select(e => e.Name).Order().ToArray(),
                    columns = v.Columns.Select(c => new { name = c.Name, type = c.StoreType, nullable = c.IsNullable }).OrderBy(c => c.name).ToArray()
                }).ToArray()
            });
        }
        var discovered = new[] { typeof(CP6Context).Assembly, typeof(SpaceContext).Assembly }
            .SelectMany(a => a.GetTypes()).Where(t => !t.IsAbstract && typeof(DbContext).IsAssignableFrom(t))
            .Select(t => t.FullName).Order().ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(new { contexts, discovered_context_types = discovered },
            new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    static DbContextOptions<T> Options<T>() where T : DbContext => DatabaseContextOptions.Configure(
        new DbContextOptionsBuilder<T>(), new DatabaseOptions(DatabaseProvider.PostgreSql),
        "Host=127.0.0.1;Database=metadata_only;Username=metadata_only").Options;

    private sealed class InventoryExecution : ISpaceExecutionContext
    {
        public Guid TenantId => Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        public Guid ActorId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    }

    static DbContext Derived(string name)
    {
        var type = typeof(CP6Context).Assembly.GetType("CP6.Core.Persistence.Tenancy." + name, throwOnError: true)!;
        return (DbContext)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, [Options<CP6Context>(), Guid.Parse("00000000-0000-0000-0000-0000000000a1")], culture: null)!;
    }

    static DbContext Rma(NpgsqlConnection connection, string model, string migration)
    {
        var assembly = typeof(CP6Context).Assembly;
        var bindingType = assembly.GetType("CP6.Core.Persistence.Tenancy.TenantStorageBindingV1", true)!;
        var stateType = assembly.GetType("CP6.Core.Persistence.Tenancy.TenantStorageState", true)!;
        var binding = Activator.CreateInstance(bindingType,
            [Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "inventory", "inventory", "cp6-pg-tenancy-v1",
             model, "inventory-r1", "inventory-c1", "inventory-principal", Array.Empty<string>(), Enum.Parse(stateType, "Ready"), migration]);
        var type = assembly.GetType("CP6.Core.Persistence.Tenancy.RmaTenantReadContext", true)!;
        var options = (DbContextOptionsBuilder)type.GetMethod("Options", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [connection])!;
        return (DbContext)Activator.CreateInstance(type, [options.Options, binding])!;
    }
}
