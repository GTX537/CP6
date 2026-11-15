using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public async Task PGT017_registered_department_seal_cold_refusal_and_original_recovery()
    {
        var move = Environment.GetEnvironmentVariable("CP6_W662_DEPT_MODE") == "move";
        await using var h = await Seed();
        var pair = await GrantPrivacy(h, 4);
        await SeedDepartmentEditor(h);
        await CopyRole71ForIndependentInquiry(h);
        var unrelated = Guid.NewGuid();
        var destination = Guid.NewGuid();
        h.Db.Sys_Depts.AddRange(
            new Sys_Dept { Id = unrelated, DeptCode = "W662-U", DeptName = "Unrelated department", Path = $"/{unrelated:D}/", Enable = true },
            new Sys_Dept { Id = destination, DeptCode = "W662-D", DeptName = "Move destination", Path = $"/{destination:D}/", Enable = true });
        foreach (var row in await h.Db.Sys_RoleDataScopes.Where(x =>
            (x.RoleId == 71 || x.RoleId == 72) && (x.ResourceKey == "crm-lead" || x.ResourceKey == "crm-privacy")).ToListAsync())
        {
            row.ScopeType = 4;
            row.CustomDeptIds = (row.RoleId == 71 ? h.Child : unrelated).ToString("D");
        }
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var options = InquiryPermissionSigningTests.Options();
        using var key = InquiryPermissionSigningTests.Signer(options);
        var runtime = ContextRuntime(h.Tenant, options.Issuer);
        await new IdentityBootstrapService(h.Db, runtime).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();

        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        await InstallDepartmentSealFixture(crm, seal, h.Tenant, store);
        await using var binding = new CrmInquiryTenantPauseV1(new Dictionary<Guid, CrmInquiryTenantPauseBindingV1>
        {
            [h.Tenant] = new() { StoreId = store, ConnectionString = seal.ConnectionString }
        });

        var services = ServiceRegistration(h, Guid.NewGuid());
        services.Oidc.Issuer = options.Issuer;
        services.Source.IdentityPublicationEnabled = true;
        services.Source.IdentityPublicationRevision = 1;
        var affected = services.Source.Registrations.Single();
        affected.DepartmentId = unrelated;
        affected.AuthenticationEpoch = Guid.NewGuid();
        const string independentClient = "w662-independent-export";
        var independentSecret = CrmOidcCrypto.RandomToken();
        var independent = new InquiryServicePrincipalRegistrationV1
        {
            ClientId = independentClient, SubjectId = Guid.NewGuid(), DepartmentId = unrelated, RoleIds = [72],
            AuthenticationEpoch = Guid.NewGuid()
        };
        services.Source.Registrations.Add(independent);
        services.Oidc.ServiceClients.Add(new()
        {
            ClientId = independentClient, TenantId = h.Tenant, Enabled = true,
            SecretSha256 = CrmOidcCrypto.Hash(independentSecret), AllowedScopes = ["cp6.services"]
        });
        var registry = new InquiryServicePrincipalRegistryV1(services.Source, services.Oidc);
        var publisherTenant = new TenantContext();
        await using (var db = server.Context(publisherTenant, runtime))
            await new InquiryServiceIdentityPublisherV1(db, publisherTenant, registry, runtime).PublishAsync(h.Tenant);

        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=owned-W662-Main", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(new Uri(options.Issuer).Host);
        names.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(names.Build());
        using var created = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = new X509Certificate2(created.Export(X509ContentType.Pfx));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        var signerPort = int.Parse(Environment.GetEnvironmentVariable("CP6_W662_ISSUER_HTTP_PORT") ?? "0");
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, signerPort, address => address.UseHttps(certificate)));
        builder.Services.AddScoped<TenantContext>();
        builder.Services.AddScoped(sp => server.Context(sp.GetRequiredService<TenantContext>(), runtime));
        builder.Services.AddScoped(sp => new InquiryServicePermissionIssuerV1(sp.GetRequiredService<CP6Context>(),
            sp.GetRequiredService<TenantContext>(), registry, key, PrivacyCatalog, runtime, binding));
        builder.Services.AddControllers().AddApplicationPart(typeof(CrmOidcController).Assembly)
            .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new ServiceContextControllers())).AddControllersAsServices();
        builder.Services.RemoveAll<CrmOidcController>();
        builder.Services.AddTransient(sp => new CrmOidcController(services.Oidc, null!, null!, null!, builder.Configuration, null!, null!,
            inquiryServicePermissions: sp.GetRequiredService<InquiryServicePermissionIssuerV1>()));
        await using var signer = builder.Build();
        signer.MapControllers();
        await signer.StartAsync();
        using var http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
            ServerCertificateCustomValidationCallback = (_, provided, _, _) => provided?.Thumbprint == certificate.Thumbprint
        })
        {
            BaseAddress = new Uri(signer.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };

        var environment = IdentityEnvironment(h.Tenant);
        environment["CrmOidc__Issuer"] = options.Issuer;
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        var prefix = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[prefix + "ConnectionString"] = seal.ConnectionString;
        environment[prefix + "StoreId"] = store.ToString("D");
        Task<AuthenticationApiProcess> Start() => AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment);

        await using var host = await Start();
        var login = await Login(host, h);
        const string route = "api/iam/departments";
        using var editorResponse = await host.Send(Request(HttpMethod.Get, route + "/editor", login));
        Assert.True(editorResponse.StatusCode == HttpStatusCode.OK, await editorResponse.Content.ReadAsStringAsync());
        var editor = (await editorResponse.Content.ReadFromJsonAsync<IamDepartmentEditorV1>())!;
        Assert.Equal(h.ActorId, editor.ActorId);
        var department = Assert.Single(editor.Departments, item => item.Id == h.Parent);
        var kind = move ? "dept-move" : "dept-update";
        var operationId = Guid.NewGuid();
        var writePath = $"{route}/{h.Parent:D}" + (move ? "/move" : "");
        var operationPath = $"{route}/operations/{operationId:D}?kind={kind}";
        const string reason = "W662 simulated original department action";
        using var previewResponse = move ? await host.Send(Request(HttpMethod.Get, $"{route}/{h.Parent:D}/preview?parentId={destination:D}", login)) : null;
        if (previewResponse is not null)
            Assert.True(previewResponse.StatusCode == HttpStatusCode.OK, await previewResponse.Content.ReadAsStringAsync());
        var preview = previewResponse is null ? null : await previewResponse.Content.ReadFromJsonAsync<IamDepartmentPreviewV1>();
        var input = new IamDepartmentCommandV1
        {
            OperationId = operationId, Id = h.Parent, Reason = reason, ExpectedVersion = department.Version,
            ExpectedTreeVersion = editor.TreeVersion, ExpectedReferencesVersion = editor.ReferencesVersion,
            PreviewVersion = preview?.PreviewVersion, ParentId = destination
        };
        var disable = new DeptDto
        {
            OperationId = operationId, Reason = reason, ExpectedVersion = department.Version, ExpectedTreeVersion = editor.TreeVersion,
            ExpectedReferencesVersion = editor.ReferencesVersion, DeptCode = department.DeptCode, DeptName = department.DeptName,
            ParentId = department.ParentId, LeaderId = department.LeaderId, Sort = department.Sort, Enable = false
        };
        object originalInput = move ? input : disable;
        var departments = new[] { h.Parent, h.Child, unrelated, destination };
        var aggregates = departments.Select(id => $"department:{id:D}").ToArray();
        var before = await State();
        var floors = await Floors();
        Assert.Equal(new[] { 0, 0, 0, 0 }, floors);
        var beforeAffected = await ReadIssue(affected, ServiceSecret, h.Child);
        var beforeIndependent = await ReadIssue(independent, independentSecret, unrelated);
        RequireFence(beforeAffected);
        RequireFence(beforeIndependent);

        await SealLogin(false);
        try
        {
            await using (var denied = new NpgsqlConnection(seal.ConnectionString))
            {
                var failure = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
                Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, failure.SqlState);
            }
            using var failed = await Write(host);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            using var error = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", error.RootElement.GetProperty("code").GetString());
            var cold = await State();
            var coldFloors = await Floors();
            Assert.Equal(before, cold);
            Assert.Equal(floors, coldFloors);
            using var waiting = await host.Send(Request(HttpMethod.Get, operationPath, login));
            var original = (await waiting.Content.ReadFromJsonAsync<IamOperationV1>())!;
            Assert.Equal(HttpStatusCode.OK, waiting.StatusCode);
            Assert.Equal((operationId, kind, "UNKNOWN"), (original.OperationId, original.Kind, original.State));
            var coldAffected = await ReadIssue(affected, ServiceSecret, h.Child);
            var coldIndependent = await ReadIssue(independent, independentSecret, unrelated);
            RequireFence(coldIndependent);
            await Save("department-cold.json", new
            {
                Operation = original, Actor = h.ActorId, Tenant = h.Tenant, Store = store, NativeSqlState = "28000",
                Status = (int)failed.StatusCode, Code = error.RootElement.GetProperty("code").GetString(),
                Source = originalInput,
                Before = before, Cold = cold, Floors = coldFloors, MainPid = host.ProcessId,
                Affected = new { affected.SubjectId, affected.DepartmentId, Before = beforeAffected, Cold = coldAffected },
                Independent = new { independent.SubjectId, independent.DepartmentId, Before = beforeIndependent, Cold = coldIndependent }
            });
            Assert.Null(coldAffected.Fence);

            var pid = host.ProcessId;
            await host.StopAsync();
            await using var restarted = await Start();
            Assert.NotEqual(pid, restarted.ProcessId);
            using var stillWaiting = await restarted.Send(Request(HttpMethod.Get, operationPath, login));
            Assert.Equal(HttpStatusCode.OK, stillWaiting.StatusCode);
            Assert.Equal(original, await stillWaiting.Content.ReadFromJsonAsync<IamOperationV1>());
            Assert.Null((await ReadIssue(affected, ServiceSecret, h.Child)).Fence);
            RequireFence(await ReadIssue(independent, independentSecret, unrelated));
            Assert.Equal(before, await State());
            Assert.Equal(coldFloors, await Floors());

            await SealLogin(true);
            using var retried = await Write(restarted);
            Assert.True(retried.StatusCode == HttpStatusCode.OK, await retried.Content.ReadAsStringAsync());
            var receipt = (await retried.Content.ReadFromJsonAsync<IamOperationV1>())!;
            Assert.Equal((operationId, kind, "COMMITTED", (Guid?)h.Parent), (receipt.OperationId, receipt.Kind, receipt.State, receipt.EntityId));
            var after = await State();
            var afterFloors = await Floors();
            using var afterJson = JsonDocument.Parse(after);
            using var beforeJson = JsonDocument.Parse(before);
            var committed = move ? 2 : 1;
            Assert.Equal(beforeJson.RootElement.GetProperty("Generation").GetInt64() + committed,
                afterJson.RootElement.GetProperty("Generation").GetInt64());
            Assert.Equal(1, afterJson.RootElement.GetProperty("Audit").GetInt32());
            Assert.Equal(1, afterJson.RootElement.GetProperty("Receipts").GetInt32());
            var beforeRows = beforeJson.RootElement.GetProperty("Departments");
            var afterRows = afterJson.RootElement.GetProperty("Departments");
            for (var i = 0; i < departments.Length; i++)
            {
                if (i < committed)
                {
                    var version = beforeRows[i].GetProperty("Version").GetInt32() + 1;
                    Assert.Equal(version, afterRows[i].GetProperty("Version").GetInt32());
                    Assert.Equal(version, afterFloors[i]);
                    Assert.Equal(beforeRows[i].GetProperty("Outbox").GetInt32() + 1, afterRows[i].GetProperty("Outbox").GetInt32());
                }
                else Assert.Equal(beforeRows[i].GetRawText(), afterRows[i].GetRawText());
            }
            Assert.Equal(floors.Skip(committed), afterFloors.Skip(committed));
            Assert.Equal(move, afterRows[0].GetProperty("Enabled").GetBoolean());
            if (move)
            {
                Assert.Equal(destination, afterRows[0].GetProperty("Parent").GetGuid());
                Assert.Equal($"/{destination:D}/{h.Parent:D}/{h.Child:D}/", afterRows[1].GetProperty("Path").GetString());
                RequireFence(await ReadIssue(affected, ServiceSecret, h.Child));
            }
            else
            {
                using var affectedAfter = await Issue(affected.ClientId, ServiceSecret);
                Assert.Equal(HttpStatusCode.Forbidden, affectedAfter.StatusCode);
            }
            RequireFence(await ReadIssue(independent, independentSecret, unrelated));

            using var resolved = await restarted.Send(Request(HttpMethod.Get, operationPath, login));
            Assert.Equal(receipt, await resolved.Content.ReadFromJsonAsync<IamOperationV1>());
            using var replay = await Write(restarted, legacy: true);
            Assert.True(replay.StatusCode == HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
            Assert.Equal(receipt, await replay.Content.ReadFromJsonAsync<IamOperationV1>());
            Assert.Equal(after, await State());
            Assert.Equal(afterFloors, await Floors());
            await Save("department-recovery.json", new { Receipt = receipt, After = after, Floors = afterFloors, RestartPid = restarted.ProcessId });
        }
        finally { await SealLogin(true); }

        Task<HttpResponseMessage> Write(AuthenticationApiProcess api, bool legacy = false)
        {
            var method = move ? HttpMethod.Post : HttpMethod.Put;
            if (!legacy) return api.Send(Request(method, writePath, login, originalInput));
            var path = $"api/pub/dept/{h.Parent:D}" + (move ? "/move" : "");
            object body = move ? new DeptController.MoveReq(destination)
            {
                OperationId = operationId, Reason = reason, ExpectedVersion = input.ExpectedVersion, ExpectedTreeVersion = input.ExpectedTreeVersion,
                ExpectedReferencesVersion = input.ExpectedReferencesVersion, PreviewVersion = input.PreviewVersion
            } : disable;
            return api.Send(Request(method, path, login, body));
        }

        async Task<HttpResponseMessage> Issue(string client, string secret)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/inquiry-service-context-v2")
            {
                Content = JsonContent.Create(new InquiryServiceContextRequestV1("privacy:export", pair.SiteId, pair.FormId, ServiceNonce()))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client}:{secret}")));
            return await http.SendAsync(request);
        }

        async Task<DepartmentSealIssuedProjection> ReadIssue(InquiryServicePrincipalRegistrationV1 registration, string secret, Guid managed)
        {
            using var response = await Issue(registration.ClientId, secret);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
            var body = JsonNode.Parse(raw)!.AsObject();
            Assert.True(ContextSignature(body, options.PublicKeyBase64, InquiryPermissionSigningKeyV1.PrivacyExportServiceContextVersion));
            Assert.Equal(registration.ClientId, body["clientId"]!.GetValue<string>());
            Assert.Equal(registration.SubjectId, body["subjectId"]!.GetValue<Guid>());
            Assert.Equal(registration.RoleIds.Select(role => "permission:" + IdentityEventContracts.RoleSubject(role)),
                body["roleSources"]!.AsArray().Select(role => role!["aggregateId"]!.GetValue<string>()));
            using var permission = JsonDocument.Parse(body["permissionJson"]!.GetValue<string>());
            var ids = permission.RootElement.GetProperty("managedDepartmentIds").EnumerateArray().Select(value => value.GetGuid()).ToArray();
            Assert.Equal(managed, Assert.Single(ids));
            var fence = response.Headers.TryGetValues("X-CP6-Inquiry-Tenant-Fence", out var values) ? Assert.Single(values) : null;
            return new((int)response.StatusCode, managed, fence);
        }

        void RequireFence(DepartmentSealIssuedProjection issued) => Assert.Equal($"{store:D}/1", issued.Fence);

        Task<string> State() => h.Db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object('Departments', (
                SELECT jsonb_agg(jsonb_build_object('Id', d."Id", 'Parent', d."ParentId", 'Path', d."Path",
                    'Enabled', d."Enable", 'Version', s."Version", 'Payload', s."PayloadSha256",
                    'Outbox', (SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage"
                               WHERE "TenantId"={h.Tenant} AND "AggregateId"=s."AggregateId" COLLATE "C") +
                              (SELECT count(*) FROM crm_identity."Cp6_OutboxMessage"
                               WHERE "TenantId"={h.Tenant} AND "AggregateId"=s."AggregateId" COLLATE "C"))
                    ORDER BY array_position({departments}, d."Id"))
                FROM public."Sys_Dept" d JOIN crm_identity."Snapshot" s ON s."TenantId"=d."TenantId"
                    AND s."AggregateId"=('department:'||d."Id"::text) COLLATE "C"
                WHERE d."TenantId"={h.Tenant} AND d."Id"=ANY({departments})),
                'Generation', (SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={h.Tenant}),
                'Audit', (SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={h.Tenant}
                    AND "ContextJson"::jsonb->>'operationId'={operationId.ToString("D")}),
                'Receipts', (SELECT count(*) FROM cp6_authentication_v1.iam_operations WHERE tenant_id={h.Tenant} AND operation_id={operationId}))::text AS "Value"
            """).SingleAsync();

        async Task<int[]> Floors()
        {
            await using var sql = new NpgsqlCommand("""
                SELECT ARRAY(SELECT coalesce(f.minimum_version,0) FROM unnest(@aggregates::text[]) WITH ORDINALITY a(aggregate,idx)
                    LEFT JOIN crm_inquiry_tenant_pause_v1.identity_fences f ON f.tenant_id=@tenant AND f.aggregate_id=a.aggregate ORDER BY a.idx)
                """, crm);
            sql.Parameters.AddWithValue("tenant", h.Tenant);
            sql.Parameters.AddWithValue("aggregates", aggregates);
            return (int[])(await sql.ExecuteScalarAsync())!;
        }

        async Task SealLogin(bool enable)
        {
            using var quote = new NpgsqlCommandBuilder();
            var role = quote.QuoteIdentifier(seal.Username!);
            await using var sql = new NpgsqlCommand($"ALTER ROLE {role} {(enable ? "LOGIN" : "NOLOGIN")}", crm);
            await sql.ExecuteNonQueryAsync();
        }

        static Task Save(string file, object evidence) => File.WriteAllTextAsync(
            Path.Combine(Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!, file), JsonSerializer.Serialize(evidence));
    }

    private sealed record DepartmentSealIssuedProjection(int Status, Guid ManagedDepartment, string? Fence);

    private static async Task InstallDepartmentSealFixture(NpgsqlConnection crm, NpgsqlConnectionStringBuilder seal, Guid tenant, Guid store)
    {
        var directory = Environment.GetEnvironmentVariable("CP6_W662_CRM_SCHEMA_SQL")!;
        await using var tx = await crm.BeginTransactionAsync();
        foreach (var file in new[] { "SessionSchemaV1", "BaselineSchemaV1", "ProjectionSchemaV1", "IdentityLockSchemaV1",
            "InquiryTenantPauseSchemaV1", "InquiryIdentityFenceSchemaV1", "InquiryDepartmentFenceSchemaV1", "InquiryStoreTimelineSchemaV1" })
        {
            await using var install = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(directory, file + ".sql")), crm, tx);
            await install.ExecuteNonQueryAsync();
        }
        using var quote = new NpgsqlCommandBuilder();
        var login = quote.QuoteIdentifier(seal.Username!);
        var database = quote.QuoteIdentifier(crm.Database);
        Assert.Matches("^[a-f0-9]{64}$", seal.Password!);
        await using var register = new NpgsqlCommand($"""
            CREATE ROLE {login} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS PASSWORD '{seal.Password}';
            GRANT CONNECT ON DATABASE {database} TO {login};
            GRANT USAGE ON SCHEMA crm_inquiry_tenant_pause_v1 TO {login};
            GRANT EXECUTE ON FUNCTION crm_inquiry_tenant_pause_v1.advance_department_v1(uuid,uuid,uuid,integer) TO {login};
            INSERT INTO crm_inquiry_tenant_pause_v1.fences(tenant_id,store_id,seal_login,reopened_at)
            VALUES (@tenant,@store,@login,1);
            INSERT INTO crm_auth."IdentityFenceV1"("TenantId","Revision") VALUES (@tenant,1);
            """, crm, tx);
        register.Parameters.AddWithValue("tenant", tenant);
        register.Parameters.AddWithValue("store", store);
        register.Parameters.AddWithValue("login", seal.Username!);
        await register.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }
}
