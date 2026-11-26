using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Pub;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static CP6.Tests.Erp.ActFileGuardCrmFixtureV1;
using static CP6.Core.Services.Pub.ActFileReferenceV1;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [Fact]
    public void ActFile_intent_accepts_complete_window_and_opaque_external_keys()
    {
        var tenant = Guid.NewGuid();
        var example = new ActFileIdentityV1(tenant, Guid.NewGuid(), "attachment-reference", Guid.NewGuid(), new string('a', 64),
            new("Account", Guid.NewGuid(), "外部+ anchor/v1"), Guid.NewGuid(), "3", "2", null, "文件+不透明键", "版本/v1+",
            new string('b', 64), Purpose, "ADD", null);
        var references = Enumerable.Range(0, 150).Select(index => example with
        {
            AttachmentId = $"{example.AttachmentId}/{index}",
            Action = index < 100 ? "RETAIN" : "ADD",
            ReferenceId = index < 100 ? Guid.NewGuid() : null
        }).ToArray();
        var input = new ActFileProofRequestV1(LocalGuard, references);
        var encoded = JsonSerializer.SerializeToElement(input, Wire);
        var received = Read<ActFileProofRequestV1>(encoded);
        Assert.Equal(references[0], Validate(received, tenant));
        Assert.Equal(references, received.References);
    }

    [CurrentPermissionPostgresFact]
    public async Task ActFile_Main_native_hold_waits_for_CRM_commit_then_invalidates_the_original_guard()
    {
        await using var h = await Seed();
        var bytes = "实际文件"u8.ToArray();
        var input = await ContentInput(h, bytes);
        var owner = ContentOwner(h, new ContentStore(), new ContentInspector());
        await owner.UploadAsync(input, new MemoryStream(bytes));
        var saved = await owner.ReadAsync(input.FileId);

        var crm = await CreateAsync(server.LocalCluster, h.Tenant);
        await using var main = await server.LocalCluster.OpenOwnerAsync(TenantManagementPostgresFixture.Database);
        await crm.BindMainAsync(main);
        await using var activity = await crm.OpenActivityAsync();
        await using var activityTx = await activity.BeginTransactionAsync();
        Assert.Equal(1L, await Scalar(activity, "SELECT epoch FROM crm_act_file_guard_v1.hold_v1(@tenant,@store)",
            ("tenant", h.Tenant), ("store", crm.Store)));
        await Scalar(activity, "INSERT INTO public.activity_fixture VALUES (@operation)", ("operation", Guid.NewGuid()));

        var hold = owner.CommandAsync(ContentCommand(saved, "HOLD"));
        try
        {
            await using var observe = await crm.OpenCrmOwnerAsync();
            await WaitForActFileLockAsync(observe, "cp6-act-file-seal-test");
            Assert.False(hold.IsCompleted);
            Assert.Equal(1L, await Scalar(main, "SELECT epoch FROM cp6_act_file_v1.sources WHERE tenant_id=@tenant", ("tenant", h.Tenant)));
        }
        finally
        {
            await activityTx.CommitAsync();
        }
        await hold;

        Assert.Equal("HELD", (await owner.ReadAsync(input.FileId)).Availability);
        Assert.Equal(2L, await Scalar(activity, "SELECT epoch FROM crm_act_file_guard_v1.hold_v1(@tenant,@store)",
            ("tenant", h.Tenant), ("store", crm.Store)));
        await using var seal = await crm.OpenSealAsync();
        var wrong = await Assert.ThrowsAsync<PostgresException>(() =>
            Scalar(seal, "INSERT INTO public.activity_fixture VALUES (@operation)", ("operation", Guid.NewGuid())));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, wrong.SqlState);
        var direct = await Assert.ThrowsAsync<PostgresException>(() =>
            Scalar(seal, "UPDATE crm_act_file_guard_v1.guards SET epoch=0"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, direct.SqlState);
    }

    [CurrentPermissionPostgresFact]
    public async Task ActFile_proof_is_atomic_binds_exact_identity_and_replays_original_after_revocation()
    {
        await using var fixture = await CreateActFilesAsync();
        var initialIdentity = fixture.Identity("ADD");
        var input = initialIdentity with { ContentSha256 = null, Primary = initialIdentity.Primary with { Version = null } };
        var retainFile = await fixture.AddFileAsync();
        var unlinkFile = await fixture.AddFileAsync();
        var retained = input with { Action = "RETAIN", ReferenceId = Guid.NewGuid(), AttachmentId = retainFile };
        var removed = input with { Action = "REMOVE", ReferenceId = Guid.NewGuid(), AttachmentId = unlinkFile };
        var request = new ActFileProofRequestV1(LocalGuard, [input, retained, removed]);
        var first = await fixture.Owner.ProveAsync(fixture.Caller, request, default);
        ActFileGuardStampV1 stamp;
        ActFileEvidenceV1 signed;
        await using (var activity = await fixture.Crm.OpenActivityAsync())
        {
            await using var tx = await activity.BeginTransactionAsync();
            stamp = await fixture.ReadCrmGuardAsync(activity);
            signed = fixture.Verify(first, request, stamp);
            await Scalar(activity, "INSERT INTO public.activity_fixture VALUES (@key)", ("key", input.OperationKey));
            await tx.CommitAsync();
        }
        Assert.Equal(new[] { "ADD", "RETAIN", "REMOVE" }, signed.Decisions.Select(item => item.Identity.Action));
        Assert.All(signed.Decisions, d => Assert.NotEqual(Guid.Empty, d.PolicyVersion));
        Assert.All(signed.Decisions, d => Assert.Equal(fixture.Input.Digest, d.ContentSha256));
        Assert.NotNull(signed.Decisions[1].RetentionDecisionRef);
        Assert.Equal("CLEAN", signed.Decisions[0].ScanState);
        Assert.Equal(1L, await fixture.ProofCountAsync());

        var changed = request with { References = [input with { AttachmentId = Guid.NewGuid().ToString("D") }] };
        var conflict = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => fixture.Owner.ProveAsync(fixture.Caller, changed, default));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", conflict.Message);
        var mismatched = request with { References = [input with { Primary = input.Primary with { Version = "anchor-other" } }, retained, removed] };
        Assert.Throws<OrderPriceCorrectionException>(() => fixture.Verify(first, mismatched, stamp));
        var tampered = first with { Body = first.Body.ToArray() };
        tampered.Body[^1] ^= 1;
        Assert.Throws<OrderPriceCorrectionException>(() => fixture.Verify(tampered, request, stamp));

        await fixture.SetGrantAsync(add: false, download: false);
        var current = await fixture.ReadCrmGuardAsync();
        Assert.NotEqual(stamp, current);
        var replay = await fixture.Owner.ProveAsync(fixture.Caller, request, default);
        Assert.Equal(first.Body, replay.Body);
        Assert.Equal(first.Signature, replay.Signature);
        Assert.Throws<OrderPriceCorrectionException>(() => fixture.Verify(replay, request, current));
        await using (var activity = await fixture.Crm.OpenActivityAsync())
        {
            await using var tx = await activity.BeginTransactionAsync();
            if (fixture.MatchesCurrent(replay, request, await fixture.ReadCrmGuardAsync(activity)))
                await Scalar(activity, "INSERT INTO public.activity_fixture VALUES (@key)", ("key", input.OperationKey));
            await tx.CommitAsync();
            Assert.Equal(1L, await Scalar(activity, "SELECT count(*) FROM public.activity_fixture"));
        }

        var rejected = request with
        {
            References = NewActOperation(retained, input)
        };
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => fixture.Owner.ProveAsync(fixture.Caller, rejected, default))).Status);
        Assert.Equal(1L, await fixture.ProofCountAsync());
        var historical = request with { References = NewActOperation(retained, removed) };
        var historicalProof = await fixture.Owner.ProveAsync(fixture.Caller, historical, default);
        Assert.Equal(2, fixture.Verify(historicalProof, historical, current).Decisions.Length);
        Assert.Equal(fixture.Bytes, (await fixture.Native.DownloadAsync(fixture.Input.FileId, input.Version, fixture.Input.Digest)).Bytes);
    }

    [CurrentPermissionPostgresFact]
    public async Task ActFile_rejects_remote_modes_mismatched_identity_pending_and_quarantine()
    {
        await using var fixture = await CreateActFilesAsync("PENDING");
        var input = fixture.Identity("ADD");
        foreach (var mode in new[] { "REMOTE_GET", "TTL", "RESERVE_HOLD_CONFIRM" })
        {
            var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(
                async () => await fixture.Owner.ProveAsync(fixture.Caller, new(mode, [input]), default));
            Assert.Equal("NO_COMMIT_GUARANTEE", error.Message);
        }
        foreach (var reference in new[]
        {
            input, input with { TenantId = Guid.NewGuid() }, input with { Version = "2" },
            input with { Purpose = "OTHER_USE" }, input with { SubjectId = Guid.NewGuid() },
            input with { ContentSha256 = new string('0', 64) }, input with { AttachmentId = "不透明键+外部/版本" }
        })
        {
            Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
                async () => await fixture.Owner.ProveAsync(fixture.Caller, new(LocalGuard, [reference]), default))).Status);
        }
        Assert.Equal(0L, await fixture.ProofCountAsync());
        fixture.Scanner.Status = "NOT_CLEAN";
        var state = await fixture.Native.ReadAsync(fixture.Input.FileId);
        await fixture.Native.CommandAsync(ContentCommand(state, "INSPECT"));
        var quarantined = fixture.Identity("DOWNLOAD");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => fixture.Owner.ProveAsync(fixture.Caller, new(GuardedStream, [quarantined]), default))).Status);
        var retained = fixture.Identity("RETAIN");
        var onlyRetain = new ActFileProofRequestV1(LocalGuard, [retained]);
        Assert.Equal("BLOCKED", Assert.Single(fixture.Verify(await fixture.Owner.ProveAsync(fixture.Caller,
            onlyRetain, default), onlyRetain, await fixture.ReadCrmGuardAsync()).Decisions).ScanState);
    }

    [CurrentPermissionPostgresFact]
    public async Task ActFile_writers_fail_closed_and_recover_original_CRM_advance_after_unknown_reply()
    {
        await using var fixture = await CreateActFilesAsync();
        var input = fixture.Input;
        var state = await fixture.Native.ReadAsync(input.FileId);
        var cmd = ContentCommand(state, "HOLD");
        var old = await fixture.ReadCrmGuardAsync();
        var db = fixture.Main;
        await Scalar(db, $"ALTER SERVER {fixture.Crm.ServerName} OPTIONS (SET dbname 'missing_act_guard_fixture')");
        try
        {
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => fixture.Native.CommandAsync(cmd));
            await Assert.ThrowsAsync<PostgresException>(() => fixture.SetGrantAsync(add: true, download: false));
            await Assert.ThrowsAsync<PostgresException>(() => Scalar(db,
                """UPDATE public."Sys_Users" SET "Enable"=false WHERE "Id"=@actor""", ("actor", fixture.CallerActor)));
        }
        finally
        {
            await Scalar(db, $"ALTER SERVER {fixture.Crm.ServerName} OPTIONS (SET dbname '{fixture.Crm.ServerName}')");
        }
        Assert.Equal(old, await fixture.ReadCrmGuardAsync());
        Assert.Equal(state, await fixture.Native.ReadAsync(input.FileId));
        Assert.Equal("UNKNOWN", (await fixture.Native.OperationAsync(cmd.OperationId, false)).State);
        Assert.Equal(true, await Scalar(db, """SELECT "Enable" FROM public."Sys_Users" WHERE "Id"=@actor""",
            ("actor", fixture.CallerActor)));

        // 用真实远端事务保存撤权，模拟 Main 未取得可确认的远端回执。
        await using var crmOwner = await fixture.Crm.OpenCrmOwnerAsync();
        await Scalar(crmOwner, $"""
            ALTER FUNCTION crm_act_file_guard_v1.advance_v1(uuid,uuid,bigint,uuid) RENAME TO real_advance;
            CREATE FUNCTION crm_act_file_guard_v1.advance_v1(t uuid,s uuid,e bigint,r uuid)
            RETURNS TABLE(epoch bigint,receipt uuid)
            LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
            BEGIN
              PERFORM crm_act_file_guard_v1.real_advance(t,s,e,r);
              RETURN;
            END $$;
            REVOKE ALL ON FUNCTION crm_act_file_guard_v1.advance_v1(uuid,uuid,bigint,uuid) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION crm_act_file_guard_v1.advance_v1(uuid,uuid,bigint,uuid) TO {fixture.Crm.SealRole};
            """);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => fixture.Native.CommandAsync(cmd));
        var sealedStamp = await fixture.ReadCrmGuardAsync();
        Assert.NotEqual(old, sealedStamp);
        Assert.Equal(state, await fixture.Native.ReadAsync(input.FileId));
        Assert.Equal("UNKNOWN", (await fixture.Native.OperationAsync(cmd.OperationId, false)).State);
        var proof = new ActFileProofRequestV1(LocalGuard, [fixture.Identity("ADD")]);
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => fixture.Owner.ProveAsync(fixture.Caller, proof, default));
        Assert.Equal("COMMIT_WINDOW_UNPROVEN", error.Message);

        await Scalar(crmOwner, """
            DROP FUNCTION crm_act_file_guard_v1.advance_v1(uuid,uuid,bigint,uuid);
            ALTER FUNCTION crm_act_file_guard_v1.real_advance(uuid,uuid,bigint,uuid) RENAME TO advance_v1;
            """);
        var original = await fixture.Native.CommandAsync(cmd);
        Assert.Equal(original, await fixture.Native.CommandAsync(cmd));
        Assert.Equal(sealedStamp, await fixture.ReadCrmGuardAsync());
        Assert.Equal(sealedStamp.Receipt, await Scalar(db,
            "SELECT receipt FROM cp6_act_file_v1.sources WHERE tenant_id=@tenant", ("tenant", fixture.Caller.TenantId)));
        Assert.Equal(1L, await Scalar(crmOwner, "SELECT count(*) FROM crm_act_file_guard_v1.advances WHERE receipt=@receipt", ("receipt", sealedStamp.Receipt)));
    }

    [CurrentPermissionPostgresFact]
    public async Task ActFile_retention_unlink_and_disposition_writers_use_the_same_guard_with_narrow_privileges()
    {
        await using var fixture = await CreateActFilesAsync();
        var db = fixture.Main;
        var tenant = fixture.Caller.TenantId;
        var file = fixture.Input.FileId;
        var initial = await fixture.ReadCrmGuardAsync();
        var login = "act_main_writer_" + Guid.NewGuid().ToString("N");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await Scalar(db, $"""
            CREATE ROLE {login} LOGIN PASSWORD '{password}';
            GRANT USAGE ON SCHEMA cp6_act_file_v1 TO {login};
            GRANT SELECT, UPDATE(can_download) ON cp6_act_file_v1.grants TO {login};
            UPDATE cp6_act_file_v1.sources SET grant_login='{login}' WHERE tenant_id=@tenant;
            """, ("tenant", tenant));
        await using var writer = await server.LocalCluster.OpenRawAsync(login, password, TenantManagementPostgresFixture.Database);
        foreach (var sql in new[]
        {
            "ALTER TABLE cp6_act_file_v1.grants DISABLE TRIGGER act_file_grant_change",
            "UPDATE cp6_act_file_v1.sources SET epoch=1"
        })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() => Scalar(writer, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        await Scalar(writer, """
            UPDATE cp6_act_file_v1.grants SET can_download=false WHERE tenant_id=@tenant AND file_id=@file
            """, ("tenant", tenant), ("file", file));
        var afterGrant = await fixture.ReadCrmGuardAsync();
        Assert.NotEqual(initial, afterGrant);
        await Scalar(writer, """
            UPDATE cp6_act_file_v1.grants SET can_download=false WHERE tenant_id=@tenant AND file_id=@file
            """, ("tenant", tenant), ("file", file));
        Assert.Equal(afterGrant, await fixture.ReadCrmGuardAsync());

        await Scalar(db, """
            INSERT INTO cp6_audit_v1.safeguards
              (tenant_id,retention_class,native_id,reference_id,kind,active,version,owner_id)
            VALUES(@tenant,'TECHNICAL_CONTENT_EVIDENCE',@native,@ref,'HOLD',true,@version,@owner)
            """, ("tenant", tenant), ("native", file.ToString("D")), ("ref", Guid.NewGuid()),
            ("version", Guid.NewGuid()), ("owner", fixture.CallerActor));
        var afterRetention = await fixture.ReadCrmGuardAsync();
        Assert.NotEqual(afterGrant, afterRetention);
        var unlink = new ActFileProofRequestV1(LocalGuard, [fixture.Identity("REMOVE")]);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => fixture.Owner.ProveAsync(fixture.Caller, unlink, default))).Status);
        var deniedDisposition = await Assert.ThrowsAsync<PostgresException>(() => Scalar(db, """
            INSERT INTO cp6_file_content_v1.dispositions(tenant_id,file_id,operation_id,initial_state,storage_id)
            VALUES(@tenant,@file,@operation,'DISPOSAL_PENDING',@store)
            """, ("tenant", tenant), ("file", file), ("operation", Guid.NewGuid()), ("store", Guid.NewGuid())));
        Assert.Equal("ACT_FILE_REFERENCE_GRAPH_UNPROVEN", deniedDisposition.MessageText);
        Assert.Equal(afterRetention, await fixture.ReadCrmGuardAsync());
        Assert.Equal("AVAILABLE", (await fixture.Native.ReadAsync(file)).Availability);
        Assert.Equal(0L, await Scalar(db, "SELECT count(*) FROM cp6_file_content_v1.dispositions WHERE tenant_id=@tenant", ("tenant", tenant)));
    }

    [CurrentPermissionPostgresFact]
    public async Task ActFile_stream_returns_exact_hash_and_stops_after_first_slice_when_DL_is_revoked()
    {
        await using var fixture = await CreateActFilesAsync();
        var input = fixture.Identity("DOWNLOAD");
        var proof = await fixture.Owner.ProveAsync(fixture.Caller, new(GuardedStream, [input]), default);
        var download = new ActFileDownloadRequestV1(input, proof);
        using var full = new MemoryStream();
        await fixture.Owner.DownloadAsync(fixture.Caller, download, async (slice, token) =>
        {
            Assert.Equal(input.ContentSha256, slice.Sha256);
            await full.WriteAsync(slice.Bytes, token);
        }, default);
        Assert.Equal(fixture.Bytes, full.ToArray());
        Assert.Equal(input.ContentSha256, FileContentBytesV1.Digest(full.ToArray()));
        Assert.Equal(1L, await fixture.AuditCountAsync("COMPLETED", fixture.Bytes.Length));

        using var partial = new MemoryStream();
        Task? revoke = null;
        await using var writer = await server.LocalCluster.OpenOwnerAsync(TenantManagementPostgresFixture.Database);
        await using var monitor = await server.LocalCluster.OpenOwnerAsync(TenantManagementPostgresFixture.Database);
        await Scalar(writer, "SET application_name='cp6-act-file-download-revoke-test'");
        try
        {
            var failure = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
                fixture.Owner.DownloadAsync(fixture.Caller, download, async (slice, token) =>
                {
                    await partial.WriteAsync(slice.Bytes, token);
                    Assert.Equal(0, slice.Offset);
                    revoke = fixture.SetGrantAsync(add: true, download: false, writer);
                    await WaitForActFileLockAsync(monitor, "cp6-act-file-download-revoke-test");
                }, default));
            Assert.Equal(403, failure.Status);
        }
        finally
        {
            if (revoke is not null) await revoke;
        }
        Assert.Equal(fixture.Bytes.Take(16 * 1024).ToArray(), partial.ToArray());
        Assert.Equal(2L, await fixture.AuditCountAsync("STARTED", 0));
        Assert.Equal(1L, await fixture.AuditCountAsync("REVOKED", partial.Length));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            fixture.Owner.DownloadAsync(fixture.Caller, download, (_, _) => throw new InvalidOperationException(), default));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(
            () => fixture.Owner.ProveAsync(fixture.Caller, new(GuardedStream, [input]), default))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task ActFile_forward_migration_preserves_an_existing_file_and_receipt_without_CRM_registration()
    {
        var database = await server.CreateIsolatedDatabaseAsync("20261126031000_ResourceAssignmentAndCapabilityApprovalV1");
        await using var h = await SeedForDatabase(database);
        var bytes = "迁移之前的原件"u8.ToArray();
        var input = await ContentInput(h, bytes);
        var owner = ContentOwner(h, new ContentStore(), new ContentInspector());
        var original = await owner.UploadAsync(input, new MemoryStream(bytes));
        await h.Db.GetService<IMigrator>().MigrateAsync();
        Assert.Equal(original, (await owner.OperationAsync(input.OperationId, false)).Result);
        Assert.Equal(bytes, (await owner.DownloadAsync(input.FileId, "1", input.Digest)).Bytes);
        Assert.Equal("HELD", (await owner.ReadAsync((await owner.CommandAsync(ContentCommand(await owner.ReadAsync(input.FileId), "HOLD"))).ObjectId)).Availability);
        await using var main = await server.LocalCluster.OpenOwnerAsync(database);
        Assert.Equal(0L, await Scalar(main, "SELECT count(*) FROM pg_extension WHERE extname='dblink'"));
    }

    [CurrentPermissionPostgresFact]
    public async Task ActFile_actual_TLS_requires_registered_service_and_streams_exact_file()
    {
        const string route = "/internal/main/v1/act-file-references";
        var storage = Path.Combine(Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!,
            "act-http-files");
        await using var fixture = await CreateActFilesAsync(localRoot: storage, length: 1_298_768);
        await using var setup = await ExportServiceFixture.Create(fixture.Context);
        using var pin = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string key = "act-http-fixture";
        setup.EnableActFile(fixture.Crm, storage, key, pin.ExportPkcs8PrivateKeyPem());
        await using var app = await setup.Start();
        Assert.Equal("https", app.Origin.Scheme);
        var identity = fixture.Identity("DOWNLOAD") with { ContentSha256 = null };
        var input = new ActFileProofRequestV1(GuardedStream, [identity]);
        var nativeUrl = $"/api/file-content/owner-v1/{identity.AttachmentId}/download?version=1&digest={fixture.Input.Digest}";

        using (var bad = setup.Http("/proofs", input, bearer: false, route: route))
        using (var unauthenticated = await app.Send(bad))
            Assert.Equal(HttpStatusCode.Forbidden, unauthenticated.StatusCode);
        using (var invalidService = setup.Http("/proofs", input, bearer: false, route: route))
        {
            invalidService.Headers.Authorization = new("Bearer", "invalid-fixture-token");
            using var invalid = await app.Send(invalidService);
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        }
        using (var mismatchedTenant = setup.Http("/proofs", input, tenant: Guid.NewGuid(), route: route))
        using (var forbiddenTenant = await app.Send(mismatchedTenant))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenTenant.StatusCode);
        using (var oldMode = setup.Http("/proofs", route: route))
        using (var unsupported = await app.Send(oldMode))
            Assert.Equal(HttpStatusCode.MethodNotAllowed, unsupported.StatusCode);
        using (var legacyFromCrm = setup.Http(nativeUrl, route: ""))
        using (var forbidden = await app.Send(legacyFromCrm))
            Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
        var cookies = await Login(app, fixture.Context);
        using (var legacy = OperationsRequest(cookies, HttpMethod.Get, nativeUrl))
        using (var existing = await app.Send(legacy))
        {
            Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
            Assert.Equal(fixture.Bytes, await existing.Content.ReadAsByteArrayAsync());
        }
        using var prove = setup.Http("/proofs", input, route: route);
        using var response = await app.Send(prove);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proof = await response.Content.ReadFromJsonAsync<ActFileAttestationV1>();
        Assert.NotNull(proof);
        var verified = ActFileSignerV1.VerifyFor(proof, key, Convert.ToBase64String(pin.ExportSubjectPublicKeyInfo()), input,
            ExportClient, await fixture.ReadCrmGuardAsync());
        var verifiedDownload = Assert.Single(verified.Decisions);
        Assert.Equal(identity, verifiedDownload.Identity);
        Assert.Equal(fixture.Input.Digest, verifiedDownload.ContentSha256);
        Assert.Equal(fixture.Bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), verifiedDownload.Download?.Bytes);
        Assert.Equal(fixture.Input.FileName, verifiedDownload.Download?.SafeFileName);
        using (var download = setup.Http("/downloads", new ActFileDownloadRequestV1(identity, proof), route: route))
        using (var bytes = await app.Send(download))
        {
            Assert.Equal(HttpStatusCode.OK, bytes.StatusCode);
            Assert.Equal("attachment", bytes.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal(verifiedDownload.Download?.MediaType, bytes.Content.Headers.ContentType?.MediaType);
            Assert.Equal(verifiedDownload.ContentSha256, Assert.Single(bytes.Headers.GetValues("X-Content-SHA256")));
            Assert.Equal(fixture.Bytes, await bytes.Content.ReadAsByteArrayAsync());
        }
        using var stream = setup.Http("/downloads", new ActFileDownloadRequestV1(identity, proof), route: route);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var streamResponse = await app.SendHeaders(stream, deadline.Token);
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        await using var body = await streamResponse.Content.ReadAsStreamAsync(deadline.Token);
        var buffer = new byte[16 * 1024];
        var received = await body.ReadAsync(buffer, deadline.Token);
        Assert.True(received > 0);
        await fixture.SetGrantAsync(add: true, download: false);
        await Assert.ThrowsAnyAsync<IOException>(async () =>
        {
            int count;
            while ((count = await body.ReadAsync(buffer, deadline.Token)) > 0) received += count;
        });
        Assert.True(received < fixture.Bytes.Length);
        using var revoked = setup.Http("/downloads", new ActFileDownloadRequestV1(identity, proof), route: route);
        using var denied = await app.Send(revoked);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private static ActFileIdentityV1[] NewActOperation(params ActFileIdentityV1[] references)
    {
        var key = Guid.NewGuid();
        return references.Select(item => item with { OperationKey = key }).ToArray();
    }

    private static async Task WaitForActFileLockAsync(NpgsqlConnection observer, string application)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        while (await Scalar(observer, """
            SELECT EXISTS(SELECT FROM pg_stat_activity
              WHERE application_name=@application AND cardinality(pg_blocking_pids(pid))>0)
            """, ("application", application)) is not true)
            await Task.Delay(35, deadline.Token);
    }

    private async Task<ActFileTest> CreateActFilesAsync(string scan = "CLEAN", string? localRoot = null, int length = 62000)
    {
        var h = await Seed();
        try
        {
            var bytes = Enumerable.Range(0, length).Select(index => (byte)(index % 251)).ToArray();
            var input = await ContentInput(h, bytes);
            IFileStore storage = localRoot is null ? new ContentStore() : new LocalFileStore(localRoot);
            var scanner = new ContentInspector { Status = scan };
            var native = ContentOwner(h, storage, scanner);
            await native.UploadAsync(input, new MemoryStream(bytes));
            var caller = new ActFileCallerV1(h.Tenant, "crm-act-fixture", "https://issuer.example.test",
                Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow.AddMinutes(10));
            h.Db.CrmServiceTokenRecords.Add(new() { TenantId = h.Tenant, Issuer = caller.Issuer, ClientId = caller.ClientId,
                Jti = caller.TokenId, ExpiresAtUtc = caller.ExpiresAt });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
            var crm = await CreateAsync(server.LocalCluster, h.Tenant);
            var main = await server.LocalCluster.OpenOwnerAsync(TenantManagementPostgresFixture.Database);
            try
            {
                await crm.BindMainAsync(main);
                await Scalar(main, """
                    INSERT INTO cp6_act_file_v1.grants
                      (tenant_id,actor_id,file_id,purpose,can_add,can_retain,can_unlink,can_download,retention_ref)
                    VALUES(@tenant,@actor,@file,'ACTIVITY_EVIDENCE',true,true,true,true,@retention)
                    """, ("tenant", h.Tenant), ("actor", h.User), ("file", input.FileId), ("retention", Guid.NewGuid()));
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var signer = new ActFileSignerV1("wp02-14c-fixture", key.ExportPkcs8PrivateKeyPem());
                var guard = new ActFileGuardReadV1(new Dictionary<Guid, ActFileGuardBindingV1>
                    { [h.Tenant] = new(crm.Store, crm.ReadConnection) });
                return new(h, main, crm, guard, signer, native, scanner, new(h.Db, storage, guard, signer), caller, input, bytes);
            }
            catch { await main.DisposeAsync(); throw; }
        }
        catch { await h.DisposeAsync(); throw; }
    }

    private sealed class ActFileTest(Harness h, NpgsqlConnection main, ActFileGuardCrmFixtureV1 crm, ActFileGuardReadV1 guard,
        ActFileSignerV1 signer, FileContentOwnerV1 native, ContentInspector scanner, ActFileReferenceOwnerV1 owner,
        ActFileCallerV1 caller, FileUploadV1 input, byte[] bytes) : IAsyncDisposable
    {
        internal ActFileGuardCrmFixtureV1 Crm => crm;
        internal Harness Context => h;
        internal NpgsqlConnection Main => main;
        internal FileContentOwnerV1 Native => native;
        internal ContentInspector Scanner => scanner;
        internal ActFileReferenceOwnerV1 Owner => owner;
        internal ActFileCallerV1 Caller => caller;
        internal Guid CallerActor => h.User;
        internal FileUploadV1 Input => input;
        internal byte[] Bytes => bytes;

        internal ActFileIdentityV1 Identity(string action) => new(h.Tenant, h.User, "attachment-reference", Guid.NewGuid(),
            FileContentBytesV1.Digest("intent"u8.ToArray()), new("Account", Guid.NewGuid(), "anchor-v1"),
            Guid.NewGuid(), "3", "2", action == "ADD" ? null : Guid.NewGuid(), input.FileId.ToString("D"), "1", input.Digest,
            Purpose, action, action == "DOWNLOAD" ? "activity-access-v1" : null);

        internal ActFileEvidenceV1 Verify(ActFileAttestationV1 proof, ActFileProofRequestV1 request, ActFileGuardStampV1 held) =>
            ActFileSignerV1.VerifyFor(proof, signer.KeyId, signer.PublicKeySpkiBase64, request, caller.ClientId, held);

        internal bool MatchesCurrent(ActFileAttestationV1 proof, ActFileProofRequestV1 expected, ActFileGuardStampV1 stamp)
        {
            try { Verify(proof, expected, stamp); return true; }
            catch (OrderPriceCorrectionException error) when (error.Status == 403) { return false; }
        }

        internal async Task<ActFileGuardStampV1> ReadCrmGuardAsync(NpgsqlConnection? activity = null)
        {
            if (activity is null)
            {
                await using var connection = await crm.OpenActivityAsync();
                return await ReadCrmGuardAsync(connection);
            }
            await using var command = new NpgsqlCommand("SELECT epoch,receipt FROM crm_act_file_guard_v1.hold_v1(@tenant,@store)", activity);
            command.Parameters.AddWithValue("tenant", h.Tenant);
            command.Parameters.AddWithValue("store", crm.Store);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new(crm.Store, reader.GetInt64(0), reader.GetGuid(1));
        }

        internal Task<object?> SetGrantAsync(bool add, bool download, NpgsqlConnection? writer = null) =>
            Scalar(writer ?? main, """
                UPDATE cp6_act_file_v1.grants SET can_add=@add,can_download=@download
                  WHERE tenant_id=@tenant AND actor_id=@actor AND file_id=@file
                """, ("add", add), ("download", download), ("tenant", h.Tenant), ("actor", h.User), ("file", input.FileId));

        internal Task<object?> ProofCountAsync() =>
            Scalar(main, "SELECT count(*) FROM cp6_act_file_v1.proof_receipts WHERE tenant_id=@tenant", ("tenant", h.Tenant));

        internal Task<object?> AuditCountAsync(string state, long count) =>
            Scalar(main, "SELECT count(*) FROM cp6_act_file_v1.stream_audit WHERE tenant_id=@tenant AND state=@state AND confirmed_bytes=@count",
                ("tenant", h.Tenant), ("state", state), ("count", count));

        internal async Task<string> AddFileAsync()
        {
            var extra = input with { FileId = Guid.NewGuid(), OperationId = Guid.NewGuid() };
            await native.UploadAsync(extra, new MemoryStream(bytes));
            await Scalar(main, """
                INSERT INTO cp6_act_file_v1.grants
                  (tenant_id,actor_id,file_id,purpose,can_add,can_retain,can_unlink,can_download,retention_ref)
                VALUES(@tenant,@actor,@file,'ACTIVITY_EVIDENCE',false,true,true,false,@retention)
                """, ("tenant", h.Tenant), ("actor", h.User), ("file", extra.FileId), ("retention", Guid.NewGuid()));
            return extra.FileId.ToString("D");
        }

        public async ValueTask DisposeAsync()
        {
            await guard.DisposeAsync();
            await main.DisposeAsync();
            await h.DisposeAsync();
        }
    }
}
