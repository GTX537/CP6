using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static class VerifySameSourceFileContent
{
    private sealed record Target(string Server, int Port, string Database, long DatabaseOid, string SystemIdentifier);
    private sealed record Fixture(Target Source, Guid InstallationId, string Username, string DataDirectory,
        string AssetRoot, Guid TenantA, Guid TenantB);
    private sealed record Original(Guid Tenant, Guid Actor, int RoleId, int Backend,
        EngineeringProductCommandResultV1 Product, EngineeringRevisionResultV1 Revision,
        FileUploadV1 Upload, FileContentResultV1 Receipt, FileContentSnapshotV1 Snapshot, bool ForeignRejected);
    private static readonly (string Resource, string[] Actions)[] Grants =
    [
        (OperationsOwnerV1.EngineeringProduct, ["query", "add"]),
        (OperationsOwnerV1.EngineeringRevision, ["query", "add", "edit"]),
        (OperationsOwnerV1.FileContent, ["query", "add"]),
    ];

    public static async Task Main(string[] args)
    {
        Need(args.Length == 2, "Expected the sealed assembly directory and exact private source specification.");
        var assemblies = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var file = Path.Combine(assemblies, name.Name + ".dll");
            return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
        };
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllBytes(args[1]))!;
        await Exercise(fixture);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task Exercise(Fixture fixture)
    {
        Need(fixture.Source.Server == "127.0.0.1" && fixture.TenantA != fixture.TenantB
            && !Directory.Exists(fixture.AssetRoot), "Expected two distinct tenants and a fresh local asset root.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(125));
        var ct = deadline.Token;
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = fixture.Source.Server, Port = fixture.Source.Port, Database = fixture.Source.Database,
            Username = fixture.Username, SslMode = SslMode.Disable, Pooling = false,
            ApplicationName = "w420-owner-fixture",
        };
        await using var a = PrivacyRestoreNativeV1.Context(connection.ConnectionString, fixture.TenantA);
        await using var b = PrivacyRestoreNativeV1.Context(connection.ConnectionString, fixture.TenantB);
        var actual = await Guard(a, fixture, ct);
        await Guard(b, fixture, ct);
        var fileId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var files = new LocalFileStore(fixture.AssetRoot);
        var inspector = new CleanFixture();
        var first = await Create(a, files, inspector, fileId, operationId, "A", false, ct);
        var second = await Create(b, files, inspector, fileId, operationId, "B", true, ct);
        Need(first.Upload.Digest != second.Upload.Digest && first.Actor != second.Actor && inspector.Calls == 2,
            "Original tenant asset or replay identities were mixed.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Pid = Environment.ProcessId, Native = actual, A = first, B = second, Scanner = "EXPLICIT_SYNTHETIC_CLEAN",
            ScannerCalls = inspector.Calls, CoreAssembly = typeof(FileContentOwnerV1).Assembly.Location,
        }));
    }

    private static async Task<JsonElement> Guard(CP6Context db, Fixture fixture, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT json_build_object('Source',json_build_object('Server',host(inet_server_addr()),
                'Port',inet_server_port(),'Database',d.datname,'DatabaseOid',d.oid::bigint,
                'SystemIdentifier',p.system_identifier::text),'User',current_user,'Owner',pg_get_userbyid(d.datdba),
                'DataDirectory',current_setting('data_directory'),'Marker',coalesce(shobj_description(d.oid,'pg_database'),''),
                'Installation',(SELECT request_json::jsonb->>'InstallationId'
                   FROM cp6_rma_core_commission_v1.installation WHERE singleton))::text
            FROM pg_database d CROSS JOIN pg_control_system() p WHERE d.datname=current_database();
            """;
        using var result = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct))!);
        var native = result.RootElement;
        Need(native.GetProperty("Source").Deserialize<Target>() == fixture.Source
            && native.GetProperty("User").GetString() == fixture.Username
            && native.GetProperty("Owner").GetString() == fixture.Username
            && string.Equals(Path.GetFullPath(native.GetProperty("DataDirectory").GetString()!),
                Path.GetFullPath(fixture.DataDirectory), StringComparison.OrdinalIgnoreCase)
            && !native.GetProperty("Marker").GetString()!.StartsWith(PrivacyRestoreNativeV1.MarkerPrefix, StringComparison.Ordinal)
            && native.GetProperty("Installation").GetString() == fixture.InstallationId.ToString("D"),
            "Wrong writable physical clone or original installation.");
        return native.Clone();
    }

    private static async Task<Original> Create(CP6Context db, IFileStore files, CleanFixture inspector,
        Guid fileId, Guid operationId, string sample, bool checkForeign, CancellationToken ct)
    {
        var tenant = db.CurrentTenantId;
        var actor = await db.Database.SqlQuery<Guid>($"""
            SELECT DISTINCT actor_id AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={tenant} AND owner='Main.Wms.RmaApplication.v1' AND state='COMMITTED'
            """).SingleAsync(ct);
        var role = await db.Sys_Users.Where(user => user.Id == actor && user.Enable).Select(user => user.RoleId).SingleAsync(ct)
            ?? throw new InvalidOperationException("The original fixture actor needs a current primary role.");
        await Grant(db, role, ct);
        var authority = Authority(db, actor);
        var product = new EngineeringProductOwnerV1(db, authority);
        var productInput = new EngineeringProductCommandV1
        {
            OperationId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Action = "CREATE",
            ExpectedAuthorityVersion = (await product.CreationContextAsync(ct)).AuthorityVersion,
            Product = new() { Code = "W420-" + sample, Name = "Synthetic material " + sample, Category = "PART",
                Definition = "Material evidence\nThickness: 2mm", IdentificationSource = "W420 synthetic source", Enabled = true },
        };
        var productReceipt = await product.CommandAsync(productInput, ct);
        var productRef = (await product.ReadAsync(productInput.ProductId, null, ct)).Reference;
        var revision = new EngineeringRevisionOwnerV1(db, authority);
        var revisionInput = new EngineeringRevisionCommandV1
        {
            OperationId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Action = "CREATE",
            ExpectedAuthorityVersion = (await revision.CreationContextAsync(ct)).AuthorityVersion,
            Origin = new("NEW_PRODUCT", "No prior released product baseline"),
            Goal = "First working design", Reason = "Create a synthetic original parent", ProductRef = productRef,
        };
        var revisionReceipt = await revision.CommandAsync(revisionInput, ct);
        var parent = (await revision.ReadAsync(revisionInput.RevisionId, null, ct)).Reference;
        var content = new FileContentOwnerV1(db, authority, files, inspector);
        if (checkForeign)
        {
            try
            {
                await content.ReadAsync(fileId, ct: ct);
                throw new InvalidOperationException("The foreign original became visible before this tenant's upload.");
            }
            catch (OrderPriceCorrectionException error) when (error.Status == 403 && error.Message == "OBJECT_FORBIDDEN")
            {
                // This tenant has file query permission and cannot resolve the other tenant's id.
            }
        }
        var bytes = Encoding.UTF8.GetBytes($"Original technical content {sample}\r\n精确原件 {sample}\n");
        var input = new FileUploadV1(operationId, fileId, parent, $"技术原件-{sample}.txt", bytes.Length,
            FileContentBytesV1.Digest(bytes), "Synthetic original technical content upload");
        var receipt = await content.UploadAsync(input, new MemoryStream(bytes, false), ct);
        var replay = await content.UploadAsync(input, new MemoryStream(bytes, false), ct);
        var original = await content.OperationAsync(operationId, false, ct);
        var snapshot = await content.ReadAsync(fileId, ct: ct);
        var download = await content.DownloadAsync(fileId, "1", input.Digest, ct);
        Need(receipt == replay && original.State == "COMMITTED" && original.Result == receipt
            && snapshot.TenantId == tenant && snapshot.ActorId == actor && snapshot.Parent == parent
            && snapshot.StateVersion == "1" && snapshot.Availability == "AVAILABLE"
            && snapshot.ContentRef.Digest == input.Digest && download.Bytes.SequenceEqual(bytes),
            "The native original, parent, replay or exact stored bytes differ.");
        return new(tenant, actor, role, ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID,
            productReceipt, revisionReceipt, input, receipt, snapshot, checkForeign);
    }

    private static OperationsOwnerV1 Authority(CP6Context db, Guid actor)
    {
        var tenant = db.CurrentTenantId;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, actor.ToString("D")),
            new Claim("tenant_id", tenant.ToString("D")),
            new Claim(ClaimTypes.Name, "w420-fixture"),
        ], "test"));
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var (resource, _) in Grants)
        {
            catalog.RegisterScope(resource, resource, [1, 2, 3, 4, 5], 5);
            catalog.RegisterFields(resource, resource == OperationsOwnerV1.FileContent
                ? [new("Content", "Content"), new("FileName", "Name")] : []);
        }
        catalog.Seal();
        return new(db, http, _ => Task.FromResult(actor), catalog.Capture);
    }

    private static async Task Grant(CP6Context db, int role, CancellationToken ct)
    {
        var nextMenu = await db.Sys_Menus.MaxAsync(menu => menu.MenuId, ct);
        foreach (var (resource, actions) in Grants)
        {
            var menu = await db.Sys_Menus.SingleOrDefaultAsync(value => value.MenuKey == resource, ct);
            if (menu is null)
            {
                menu = new() { MenuId = checked(++nextMenu), MenuKey = resource, MenuName = resource };
                db.Sys_Menus.Add(menu);
            }
            foreach (var action in actions)
            {
                if (!await db.Sys_MenuActions.AnyAsync(row => row.MenuId == menu.MenuId && row.ActionCode == action, ct))
                    db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
                if (!await db.Sys_RoleActions.AnyAsync(row => row.MenuId == menu.MenuId && row.RoleId == role && row.ActionCode == action, ct))
                    db.Sys_RoleActions.Add(new() { MenuId = menu.MenuId, RoleId = role, ActionCode = action });
            }
            var scope = await db.Sys_RoleDataScopes.SingleOrDefaultAsync(row => row.ResourceKey == resource && row.RoleId == role, ct);
            if (scope is null)
                db.Sys_RoleDataScopes.Add(new() { RoleId = role, ResourceKey = resource, ScopeType = 5 });
            else
                Need(scope.ScopeType == 5, "An existing permission scope would need to be overwritten.");
        }
        if (!await db.Pub_DocSequences.AnyAsync(row => row.BizKey == "ENG_REV", ct))
            db.Pub_DocSequences.Add(new() { BizKey = "ENG_REV", Prefix = "ENG", SeqLength = 4, ResetCycle = 0, CurrentValue = 0 });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static void Need(bool value, string error)
    {
        if (!value) throw new InvalidOperationException(error);
    }

    private sealed class CleanFixture : IFileContentInspectorV1
    {
        internal int Calls { get; private set; }

        public Task<FileInspectionV1> InspectAsync(byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new FileInspectionV1("CLEAN", "EXPLICIT_SYNTHETIC_CLEAN", "fixture-engine",
                "fixture-signature", DateTimeOffset.UtcNow, FileContentBytesV1.Digest(bytes), "W420 synthetic local scanner"));
        }
    }
}
