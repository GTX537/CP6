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
using CP6.Tests.Erp;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
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
        var mode = Environment.GetEnvironmentVariable("CP6_W662_DEPT_MODE");
        var move = mode == "move";
        var leader = mode == "leader";
        var control = mode == "control";
        var checkIssuance = !move && !leader && !control;
        await using var h = await Seed();
        var pair = await GrantPrivacy(h, 4);
        if (checkIssuance) await GrantRetention(h);
        await SeedDepartmentEditor(h);
        if (control)
        {
            var menuId = await h.Db.Sys_Menus.Where(menu => menu.MenuKey == "pub-dept").Select(menu => menu.MenuId).SingleAsync();
            h.Db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 71, MenuId = menuId, ActionCode = "add" });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
        }
        await CopyRole71ForIndependentInquiry(h);
        if (checkIssuance)
        {
            var leadMenu = await h.Db.Sys_Menus.Where(menu => menu.MenuKey == "crm-lead").Select(menu => menu.MenuId).SingleAsync();
            h.Db.Sys_RoleActions.Remove(await h.Db.Sys_RoleActions.SingleAsync(action =>
                action.RoleId == 72 && action.MenuId == leadMenu && action.ActionCode == "edit"));
            var actor = await h.Db.Sys_Users.SingleAsync(user => user.Id == h.ActorId);
            actor.AuthenticationEpoch = Guid.NewGuid();
        }
        var unrelated = Guid.NewGuid();
        var destination = Guid.NewGuid();
        const string otherEditor = "w662-independent-editor";
        h.Db.Sys_Depts.AddRange(
            new Sys_Dept { Id = unrelated, DeptCode = "W662-U", DeptName = "Unrelated department", Path = $"/{unrelated:D}/", Enable = true },
            new Sys_Dept { Id = destination, DeptCode = "W662-D", DeptName = "Move destination", Path = $"/{destination:D}/", Enable = true });
        h.Db.Sys_Users.Add(new Sys_User
        {
            UserName = otherEditor, Enable = true, DeptId = unrelated, RoleId = 72,
            Password = (await h.Db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == h.ActorId)).Password,
            AuthenticationEpoch = Guid.NewGuid()
        });
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
        var crmAck = Environment.GetEnvironmentVariable("CP6_W662_CRM_ACK_PORT");
        await using var crmRelay = crmAck is null ? null : new StockPostgresCommitRelay(seal.Port, seal.Username!, int.Parse(crmAck));
        var sealEndpoint = new NpgsqlConnectionStringBuilder(seal.ConnectionString);
        if (crmRelay is not null) sealEndpoint.Port = crmRelay.Port;
        await using var binding = new CrmInquiryTenantPauseV1(new Dictionary<Guid, CrmInquiryTenantPauseBindingV1>
        {
            [h.Tenant] = new() { StoreId = store, ConnectionString = sealEndpoint.ConnectionString }
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
        const string oidcSecret = "owned-w662-oidc-client";
        const string redirect = "https://crm.cp6.test/callback";
        if (checkIssuance)
        {
            environment["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(oidcSecret);
            environment["CrmOidc__Clients__0__RedirectUris__0"] = redirect;
            environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
            environment["InquiryPermissionSigning__Enabled"] = "true";
            environment["InquiryPermissionSigning__Issuer"] = options.Issuer;
            environment["InquiryPermissionSigning__KeyId"] = options.KeyId;
            environment["InquiryPermissionSigning__PrivateKeyBase64"] = options.PrivateKeyBase64;
            environment["InquiryPermissionSigning__PublicKeyBase64"] = options.PublicKeyBase64;
            environment["InquiryPermissionSigning__ActiveFromUtc"] = options.ActiveFromUtc.ToString("O");
        }
        var prefix = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[prefix + "ConnectionString"] = sealEndpoint.ConnectionString;
        environment[prefix + "StoreId"] = store.ToString("D");
        var mainConnection = h.Db.Database.GetConnectionString()!;
        var mainEndpoint = new NpgsqlConnectionStringBuilder(mainConnection);
        var mainAck = Environment.GetEnvironmentVariable("CP6_W662_MAIN_ACK_PORT");
        await using var mainRelay = mainAck is null ? null : new StockPostgresCommitRelay(mainEndpoint.Port, mainEndpoint.Username!, int.Parse(mainAck));
        if (mainRelay is not null)
        {
            mainEndpoint.Port = mainRelay.Port;
            mainEndpoint.SslMode = SslMode.Disable;
        }
        Task<AuthenticationApiProcess> Start() => AuthenticationApiProcess.Start(mainEndpoint.ConnectionString, environment, allowAutoRedirect: false);

        await using var host = await Start();
        var login = await Login(host, h);
        var otherLogin = await Login(host, h, userName: otherEditor);
        var human = checkIssuance ? await Exchange(host, login) : null;
        var independentHuman = checkIssuance ? await Exchange(host, otherLogin) : null;
        const string route = "api/iam/departments";
        using var editorResponse = await host.Send(Request(HttpMethod.Get, route + "/editor", login));
        Assert.True(editorResponse.StatusCode == HttpStatusCode.OK, await editorResponse.Content.ReadAsStringAsync());
        var editor = (await editorResponse.Content.ReadFromJsonAsync<IamDepartmentEditorV1>())!;
        Assert.Equal(h.ActorId, editor.ActorId);
        var department = Assert.Single(editor.Departments, item => item.Id == h.Parent);
        var kind = move ? "dept-move" : leader ? "dept-leader" : "dept-update";
        var operationId = Guid.NewGuid();
        var subroute = move ? "/move" : leader ? "/leader" : "";
        var writePath = $"{route}/{h.Parent:D}" + subroute;
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
            PreviewVersion = preview?.PreviewVersion, ParentId = move ? destination : null, LeaderId = leader ? h.ActorId : null
        };
        var disable = new DeptDto
        {
            OperationId = operationId, Reason = reason, ExpectedVersion = department.Version, ExpectedTreeVersion = editor.TreeVersion,
            ExpectedReferencesVersion = editor.ReferencesVersion, DeptCode = department.DeptCode, DeptName = department.DeptName,
            ParentId = department.ParentId, LeaderId = department.LeaderId, Sort = department.Sort, Enable = control && department.Enable
        };
        object originalInput = move || leader ? input : disable;
        var departments = new[] { h.Parent, h.Child, unrelated, destination };
        var aggregates = departments.Select(id => $"department:{id:D}").ToArray();
        var before = await State();
        var floors = await Floors();
        Assert.Equal(new[] { 0, 0, 0, 0 }, floors);
        var beforeAffected = await ReadIssue(affected, ServiceSecret, h.Child);
        var beforeIndependent = await ReadIssue(independent, independentSecret, unrelated);
        RequireFence(beforeAffected);
        RequireFence(beforeIndependent);
        var openFence = $"{store:D}/1";
        if (checkIssuance)
        {
            Assert.Equal(openFence, await HumanFence(host, human!, "privacy:export", h.Child));
            Assert.Equal(openFence, await HumanFence(host, independentHuman!, "privacy:export", unrelated));
            Assert.Equal(openFence, await ServiceFence(affected, ServiceSecret, "privacy:retention-worker"));
            Assert.Equal(openFence, await ServiceFence(independent, independentSecret, "privacy:retention-worker"));
            Assert.Equal(openFence, await ServiceFence(independent, independentSecret, "public:receive"));
        }

        await SealLogin(false);
        try
        {
            await using (var denied = new NpgsqlConnection(seal.ConnectionString))
            {
                var failure = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
                Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, failure.SqlState);
            }
            if (control)
            {
                using var same = await Write(host);
                Assert.True(same.StatusCode == HttpStatusCode.OK, await same.Content.ReadAsStringAsync());
                var sameReceipt = (await same.Content.ReadFromJsonAsync<IamOperationV1>())!;
                Assert.Equal((operationId, "COMMITTED"), (sameReceipt.OperationId, sameReceipt.State));
                Assert.Null(await Pending());
                using var initial = JsonDocument.Parse(before);
                using var unchanged = JsonDocument.Parse(await State());
                Assert.Equal(initial.RootElement.GetProperty("Departments").GetRawText(), unchanged.RootElement.GetProperty("Departments").GetRawText());
                Assert.Equal(initial.RootElement.GetProperty("Generation").GetInt64(), unchanged.RootElement.GetProperty("Generation").GetInt64());
                Assert.Equal(1, unchanged.RootElement.GetProperty("Audit").GetInt32());
                Assert.Equal(1, unchanged.RootElement.GetProperty("Receipts").GetInt32());

                var create = new DeptDto
                {
                    OperationId = Guid.NewGuid(), ExpectedVersion = "absent", ExpectedTreeVersion = sameReceipt.TreeVersion,
                    ExpectedReferencesVersion = sameReceipt.ReferencesVersion, Reason = "W662 simulated ordinary new department",
                    DeptCode = "W662-NEW", DeptName = "New department", ParentId = unrelated, Enable = true
                };
                using var createdResponse = await host.Send(Request(HttpMethod.Post, route, login, create));
                Assert.True(createdResponse.StatusCode == HttpStatusCode.OK, await createdResponse.Content.ReadAsStringAsync());
                var createdReceipt = (await createdResponse.Content.ReadFromJsonAsync<IamOperationV1>())!;
                Assert.Equal((create.OperationId, "dept-create", "COMMITTED"), (createdReceipt.OperationId, createdReceipt.Kind, createdReceipt.State));
                var id = createdReceipt.EntityId!.Value;
                var row = await h.Db.Sys_Depts.AsNoTracking().SingleAsync(item => item.Id == id);
                Assert.Equal((h.Tenant, create.DeptCode, (Guid?)unrelated, $"/{unrelated:D}/{id:D}/", true),
                    (row.TenantId, row.DeptCode, row.ParentId, row.Path, row.Enable));
                var aggregate = $"department:{id:D}";
                Assert.Equal(1, await h.Db.CrmIdentitySnapshots.Where(item => item.TenantId == h.Tenant && item.AggregateId == aggregate)
                    .Select(item => item.Version).SingleAsync());
                Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""
                    SELECT count(*)::integer AS "Value" FROM crm_identity."Cp6_OutboxMessage"
                    WHERE "TenantId"={h.Tenant} AND "AggregateId"={aggregate}
                    """).SingleAsync());
                using var alias = await host.Send(Request(HttpMethod.Post, "api/pub/dept", login, create));
                Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
                Assert.Equal(createdReceipt, await alias.Content.ReadFromJsonAsync<IamOperationV1>());
                Assert.Equal(floors, await Floors());
                await Save("department-original-controls.json", new { Same = sameReceipt, Created = createdReceipt, row.Path, Floors = await Floors() });
                return;
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
            object? extraIssuance = null;
            if (checkIssuance)
            {
                var affectedHuman = await HumanFence(host, human!, "privacy:export", h.Child);
                var healthyHuman = await HumanFence(host, independentHuman!, "privacy:export", unrelated);
                var readFence = await HumanFence(host, human!, "intake:list", h.Child);
                var review = await ServiceFence(affected, ServiceSecret, "privacy:retention-worker");
                var maintenance = await ServiceFence(independent, independentSecret, "privacy:retention-worker");
                var pendingPublic = await ServiceFence(independent, independentSecret, "public:receive");
                Assert.Null(affectedHuman);
                Assert.Equal(openFence, healthyHuman);
                Assert.Null(readFence);
                Assert.Null(review);
                Assert.Equal(openFence, maintenance);
                Assert.Null(pendingPublic);
                extraIssuance = new { AffectedHumanFence = affectedHuman, IndependentHumanFence = healthyHuman,
                    OriginalReadStatus = 200, ReviewFence = review, MaintenanceFence = maintenance, PublicFence = pendingPublic };
            }
            await Save("department-cold.json", new
            {
                Operation = original, Actor = h.ActorId, Tenant = h.Tenant, Store = store, NativeSqlState = "28000",
                Status = (int)failed.StatusCode, Code = error.RootElement.GetProperty("code").GetString(),
                Source = originalInput,
                Before = before, Cold = cold, Floors = coldFloors, MainPid = host.ProcessId,
                Affected = new { affected.SubjectId, affected.DepartmentId, Before = beforeAffected, Cold = coldAffected },
                Independent = new { independent.SubjectId, independent.DepartmentId, Before = beforeIndependent, Cold = coldIndependent },
                ExtraIssuance = extraIssuance
            });
            Assert.Null(coldAffected.Fence);
            var staged = await Pending();
            Assert.NotNull(staged);
            using var stagedJson = JsonDocument.Parse(staged);
            var values = stagedJson.RootElement;
            Assert.Equal((h.Parent, h.ActorId, store, kind, false), (values.GetProperty("Root").GetGuid(), values.GetProperty("Actor").GetGuid(),
                values.GetProperty("Store").GetGuid(), values.GetProperty("Kind").GetString(), values.GetProperty("Started").GetBoolean()));
            var committed = move ? 2 : 1;
            var originalTargets = values.GetProperty("Seals").EnumerateArray().ToDictionary(value => value.GetProperty("departmentId").GetGuid());
            Assert.Equal(departments.Take(committed).Order(), originalTargets.Keys.Order());
            using var beforeJson = JsonDocument.Parse(before);
            var beforeRows = beforeJson.RootElement.GetProperty("Departments");
            var expectedFloors = floors.ToArray();
            for (var i = 0; i < committed; i++)
            {
                var expected = originalTargets[departments[i]];
                expectedFloors[i] = beforeRows[i].GetProperty("Version").GetInt32() + 1;
                Assert.Equal(expectedFloors[i], expected.GetProperty("version").GetInt32());
                Assert.Equal(beforeRows[i].GetProperty("Payload").GetString(), expected.GetProperty("predecessorHash").GetString());
            }

            var pid = host.ProcessId;
            await host.StopAsync();
            await using var restarted = await Start();
            Assert.NotEqual(pid, restarted.ProcessId);
            using var stillWaiting = await restarted.Send(Request(HttpMethod.Get, operationPath, login));
            Assert.Equal(HttpStatusCode.OK, stillWaiting.StatusCode);
            Assert.Equal(original, await stillWaiting.Content.ReadFromJsonAsync<IamOperationV1>());
            Assert.Null((await ReadIssue(affected, ServiceSecret, h.Child)).Fence);
            RequireFence(await ReadIssue(independent, independentSecret, unrelated));
            if (checkIssuance)
            {
                Assert.Null(await HumanFence(restarted, human!, "privacy:export", h.Child));
                Assert.Equal(openFence, await HumanFence(restarted, independentHuman!, "privacy:export", unrelated));
            }
            Assert.Equal(before, await State());
            Assert.Equal(coldFloors, await Floors());
            Assert.Equal(staged, await Pending());

            var method = move ? HttpMethod.Post : HttpMethod.Put;
            using var wrongActor = await restarted.Send(Request(method, writePath, otherLogin, originalInput));
            await RequireError(wrongActor, HttpStatusCode.Forbidden, "IAM_OPERATION_OWNER_MISMATCH");
            var changed = JsonSerializer.SerializeToNode(originalInput)!.AsObject();
            changed["Reason"] = "W662 simulated altered repeat";
            using var wrongInput = await restarted.Send(Request(method, writePath, login, changed));
            await RequireError(wrongInput, HttpStatusCode.Conflict, "IAM_OPERATION_INPUT_CONFLICT");
            changed["OperationId"] = Guid.NewGuid();
            using var competing = await restarted.Send(Request(method, writePath, login, changed));
            await RequireError(competing, HttpStatusCode.Conflict, "IAM_CRM_DEPARTMENT_SEAL_PENDING");
            Assert.Equal(staged, await Pending());

            if (Environment.GetEnvironmentVariable("CP6_W662_STALE_BEFORE_SEAL") == "1")
            {
                var other = await RenameUnrelated(restarted);
                using var stale = await Write(restarted);
                await RequireError(stale, HttpStatusCode.PreconditionFailed, "IAM_REFERENCES_CHANGED");
                Assert.Equal(before, await State());
                Assert.Equal(floors, await Floors());
                Assert.Equal(staged, await Pending());
                using var close = await restarted.Send(Request(HttpMethod.Post, $"{route}/operations/{operationId:D}/close?kind={kind}", login));
                Assert.Equal(HttpStatusCode.OK, close.StatusCode);
                var closed = (await close.Content.ReadFromJsonAsync<IamOperationV1>())!;
                Assert.Equal((operationId, "CLOSED"), (closed.OperationId, closed.State));
                Assert.Null(await Pending());
                using var replayClosed = await Write(restarted);
                await RequireError(replayClosed, HttpStatusCode.Conflict, "IAM_OPERATION_CLOSED");
                RequireFence(await ReadIssue(affected, ServiceSecret, h.Child));
                await Save("department-stale-before-seal.json", new { Original = original, Other = other, Closed = closed, Floors = await Floors() });
                return;
            }

            await SealLogin(true);
            if (crmRelay is not null)
            {
                crmRelay.Arm();
                using var unconfirmed = await Write(restarted);
                await crmRelay.Dropped.WaitAsync(TimeSpan.FromSeconds(10));
                await RequireError(unconfirmed, HttpStatusCode.ServiceUnavailable, "AUTHORITY_FENCE_UNAVAILABLE");
                Assert.Equal(before, await State());
                Assert.Equal(expectedFloors, await Floors());
                using var close = await restarted.Send(Request(HttpMethod.Post, $"{route}/operations/{operationId:D}/close?kind={kind}", login));
                await RequireError(close, HttpStatusCode.ServiceUnavailable, "IAM_OPERATION_UNKNOWN");
                Assert.Null((await ReadIssue(affected, ServiceSecret, h.Child)).Fence);
                RequireFence(await ReadIssue(independent, independentSecret, unrelated));
                var started = Assert.IsType<string>(await Pending());
                using var startedJson = JsonDocument.Parse(started);
                Assert.True(startedJson.RootElement.GetProperty("Started").GetBoolean());
                Assert.Equal(values.GetProperty("Seals").GetRawText(), startedJson.RootElement.GetProperty("Seals").GetRawText());
                await Save("department-remote-ack.json", new { Original = original, Main = before, Floors = await Floors(), Pending = started });
                if (Environment.GetEnvironmentVariable("CP6_W662_STALE_AFTER_ACK") == "1")
                {
                    var otherReceipt = await RenameUnrelated(restarted);
                    using var stale = await Write(restarted);
                    await RequireError(stale, HttpStatusCode.ServiceUnavailable, "IAM_OPERATION_UNKNOWN");
                    using var unresolved = await restarted.Send(Request(HttpMethod.Get, operationPath, login));
                    Assert.Equal(original, await unresolved.Content.ReadFromJsonAsync<IamOperationV1>());
                    Assert.Equal(before, await State());
                    Assert.Equal(expectedFloors, await Floors());
                    Assert.Equal(started, await Pending());
                    RequireFence(await ReadIssue(independent, independentSecret, unrelated));
                    await Save("department-stale-after-ack.json", new { Original = original, Other = otherReceipt, Floors = await Floors(), Pending = started });
                    return;
                }
            }

            IamOperationV1 receipt;
            if (mainRelay is null)
            {
                using var retried = await Write(restarted);
                Assert.True(retried.StatusCode == HttpStatusCode.OK, await retried.Content.ReadAsStringAsync());
                receipt = (await retried.Content.ReadFromJsonAsync<IamOperationV1>())!;
            }
            else
            {
                await using var holder = new NpgsqlConnection(mainConnection);
                await holder.OpenAsync();
                await using var transaction = await holder.BeginTransactionAsync();
                await using var hold = new NpgsqlCommand("""
                    SELECT 1 FROM public."Sys_Dept" WHERE "TenantId"=@tenant AND "Id"=@department FOR UPDATE
                    """, holder, transaction);
                hold.Parameters.AddWithValue("tenant", h.Tenant);
                hold.Parameters.AddWithValue("department", h.Parent);
                Assert.Equal(1, await hold.ExecuteScalarAsync());
                await using var observer = new NpgsqlConnection(mainConnection);
                await observer.OpenAsync();
                var completing = Write(restarted);
                try
                {
                    await WaitForMainBlock(observer, holder.ProcessID, "%UPDATE%Sys_Dept%", completing);
                    Assert.Equal(before, await State());
                    mainRelay.Arm();
                }
                finally { await transaction.RollbackAsync(); }
                using var unconfirmed = await completing;
                await mainRelay.Dropped.WaitAsync(TimeSpan.FromSeconds(10));
                await RequireError(unconfirmed, HttpStatusCode.ServiceUnavailable, "IAM_OPERATION_UNKNOWN");
                using var recovered = await restarted.Send(Request(HttpMethod.Get, operationPath, login));
                Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
                receipt = (await recovered.Content.ReadFromJsonAsync<IamOperationV1>())!;
            }
            Assert.Equal((operationId, kind, "COMMITTED", (Guid?)h.Parent), (receipt.OperationId, receipt.Kind, receipt.State, receipt.EntityId));
            Assert.Null(await Pending());
            var after = await State();
            var afterFloors = await Floors();
            using var afterJson = JsonDocument.Parse(after);
            Assert.Equal(beforeJson.RootElement.GetProperty("Generation").GetInt64() + committed,
                afterJson.RootElement.GetProperty("Generation").GetInt64());
            Assert.Equal(1, afterJson.RootElement.GetProperty("Audit").GetInt32());
            Assert.Equal(1, afterJson.RootElement.GetProperty("Receipts").GetInt32());
            var afterRows = afterJson.RootElement.GetProperty("Departments");
            for (var i = 0; i < departments.Length; i++)
            {
                if (i < committed)
                {
                    var version = beforeRows[i].GetProperty("Version").GetInt32() + 1;
                    Assert.Equal(version, afterRows[i].GetProperty("Version").GetInt32());
                    Assert.Equal(version, afterFloors[i]);
                    Assert.Equal(originalTargets[departments[i]].GetProperty("successorHash").GetString(), afterRows[i].GetProperty("Payload").GetString());
                    Assert.Equal(beforeRows[i].GetProperty("Outbox").GetInt32() + 1, afterRows[i].GetProperty("Outbox").GetInt32());
                }
                else Assert.Equal(beforeRows[i].GetRawText(), afterRows[i].GetRawText());
            }
            Assert.Equal(floors.Skip(committed), afterFloors.Skip(committed));
            Assert.Equal(move || leader, afterRows[0].GetProperty("Enabled").GetBoolean());
            if (move)
            {
                Assert.Equal(destination, afterRows[0].GetProperty("Parent").GetGuid());
                Assert.Equal($"/{destination:D}/{h.Parent:D}/{h.Child:D}/", afterRows[1].GetProperty("Path").GetString());
                RequireFence(await ReadIssue(affected, ServiceSecret, h.Child));
            }
            else if (leader)
            {
                Assert.Equal(h.ActorId, afterRows[0].GetProperty("Leader").GetGuid());
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
            await Save("department-recovery.json", new { Receipt = receipt, After = after, Floors = afterFloors, RestartPid = restarted.ProcessId,
                RemoteCommitAckLost = crmRelay is not null, MainCommitAckLost = mainRelay is not null });
        }
        finally { await SealLogin(true); }

        Task<HttpResponseMessage> Write(AuthenticationApiProcess api, bool legacy = false)
        {
            var method = move ? HttpMethod.Post : HttpMethod.Put;
            if (!legacy) return api.Send(Request(method, writePath, login, originalInput));
            var path = $"api/pub/dept/{h.Parent:D}" + subroute;
            object body = move ? new DeptController.MoveReq(destination)
            {
                OperationId = operationId, Reason = reason, ExpectedVersion = input.ExpectedVersion, ExpectedTreeVersion = input.ExpectedTreeVersion,
                ExpectedReferencesVersion = input.ExpectedReferencesVersion, PreviewVersion = input.PreviewVersion
            } : leader ? new DeptController.LeaderReq(input.LeaderId)
            {
                OperationId = operationId, Reason = reason, ExpectedVersion = input.ExpectedVersion, ExpectedTreeVersion = input.ExpectedTreeVersion,
                ExpectedReferencesVersion = input.ExpectedReferencesVersion
            } : disable;
            return api.Send(Request(method, path, login, body));
        }

        async Task<HttpResponseMessage> Issue(string client, string secret, string purpose = "privacy:export")
        {
            var path = purpose == "privacy:export" ? "/connect/inquiry-service-context-v2" : "/connect/inquiry-service-context";
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(new InquiryServiceContextRequestV1(purpose, pair.SiteId, pair.FormId, ServiceNonce()))
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

        async Task<IamOperationV1> RenameUnrelated(AuthenticationApiProcess api)
        {
            using var fresh = await api.Send(Request(HttpMethod.Get, route + "/editor", login));
            Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
            var current = (await fresh.Content.ReadFromJsonAsync<IamDepartmentEditorV1>())!;
            var unchanged = Assert.Single(current.Departments, item => item.Id == unrelated);
            var rename = new DeptDto
            {
                OperationId = Guid.NewGuid(), Reason = "W662 independent simulated display name change", ExpectedVersion = unchanged.Version,
                ExpectedTreeVersion = current.TreeVersion, ExpectedReferencesVersion = current.ReferencesVersion,
                DeptCode = unchanged.DeptCode, DeptName = "Independent renamed department", ParentId = unchanged.ParentId,
                LeaderId = unchanged.LeaderId, Sort = unchanged.Sort, Enable = unchanged.Enable
            };
            using var renamed = await api.Send(Request(HttpMethod.Put, $"{route}/{unrelated:D}", login, rename));
            Assert.True(renamed.StatusCode == HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
            var receipt = (await renamed.Content.ReadFromJsonAsync<IamOperationV1>())!;
            Assert.Equal((rename.OperationId, "COMMITTED"), (receipt.OperationId, receipt.State));
            Assert.NotEqual(current.TreeVersion, receipt.TreeVersion);
            Assert.NotEqual(current.ReferencesVersion, receipt.ReferencesVersion);
            Assert.Equal(rename.DeptName, await h.Db.Sys_Depts.Where(row => row.Id == unrelated).Select(row => row.DeptName).SingleAsync());
            return receipt;
        }

        async Task<string?> ServiceFence(InquiryServicePrincipalRegistrationV1 registration, string secret, string purpose)
        {
            using var response = await Issue(registration.ClientId, secret, purpose);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
            var signed = JsonNode.Parse(raw)!.AsObject();
            Assert.True(ContextSignature(signed, options.PublicKeyBase64));
            Assert.Equal(purpose, signed["purpose"]!.GetValue<string>());
            Assert.Equal(registration.SubjectId, signed["subjectId"]!.GetValue<Guid>());
            return response.Headers.TryGetValues("X-CP6-Inquiry-Tenant-Fence", out var values) ? Assert.Single(values) : null;
        }

        async Task<string?> HumanFence(AuthenticationApiProcess api, string access, string purpose, Guid departmentId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-human-context")
            {
                Content = JsonContent.Create(new InquiryHumanContextRequestV1(purpose, pair.SiteId, pair.FormId, ServiceNonce()))
            };
            request.Headers.Authorization = new("Bearer", access);
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await api.Send(request);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
            var signed = JsonNode.Parse(raw)!.AsObject();
            Assert.True(ContextSignature(signed, options.PublicKeyBase64, InquiryPermissionSigningKeyV1.HumanContextVersion));
            using var permission = JsonDocument.Parse(signed["permissionJson"]!.GetValue<string>());
            Assert.Equal(purpose, permission.RootElement.GetProperty("purpose").GetString());
            Assert.Equal(departmentId, Assert.Single(permission.RootElement.GetProperty("managedDepartmentIds").EnumerateArray()).GetGuid());
            return response.Headers.TryGetValues("X-CP6-Inquiry-Tenant-Fence", out var values) ? Assert.Single(values) : null;
        }

        async Task<string> Exchange(AuthenticationApiProcess api, Dictionary<string, string> cookies)
        {
            var verifier = CrmOidcCrypto.RandomToken();
            var query = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = "CP6.Web", ["redirect_uri"] = redirect, ["response_type"] = "code", ["code_challenge_method"] = "S256",
                ["code_challenge"] = CrmOidcCrypto.Challenge(verifier), ["state"] = CrmOidcCrypto.RandomToken(),
                ["nonce"] = CrmOidcCrypto.RandomToken(), ["scope"] = "openid profile crm"
            });
            using var entry = await api.Send(Request(HttpMethod.Get, query, cookies));
            Assert.Equal(HttpStatusCode.Redirect, entry.StatusCode);
            using var approved = await api.Send(Request(HttpMethod.Get, entry.Headers.Location!.PathAndQuery, cookies));
            Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
            var code = QueryHelpers.ParseQuery(approved.Headers.Location!.Query)["code"].ToString();
            Assert.NotEmpty(code);
            using var exchange = new HttpRequestMessage(HttpMethod.Post, "connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier })
            };
            exchange.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + oidcSecret)));
            using var response = await api.Send(exchange);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return payload.RootElement.GetProperty("access_token").GetString()!;
        }

        Task<string?> Pending() => h.Db.Database.SqlQuery<string?>($"""
            SELECT jsonb_build_object('Root',department_id,'Actor',actor_id,'Store',store_id,'Kind',kind,
                'Started',seal_started,'Seals',source_seals)::text AS "Value"
            FROM cp6_authentication_v1.pending_crm_department_seals WHERE tenant_id={h.Tenant} AND operation_id={operationId}
            """).SingleOrDefaultAsync();

        Task<string> State() => h.Db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object('Departments', (
                SELECT jsonb_agg(jsonb_build_object('Id', d."Id", 'Parent', d."ParentId", 'Path', d."Path",
                    'Enabled', d."Enable", 'Leader', d."LeaderId", 'Version', s."Version", 'Payload', s."PayloadSha256",
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

        static async Task RequireError(HttpResponseMessage response, HttpStatusCode status, string code)
        {
            var content = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == status, $"Expected {(int)status}, got {(int)response.StatusCode}: {content}");
            using var json = JsonDocument.Parse(content);
            Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        }
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
