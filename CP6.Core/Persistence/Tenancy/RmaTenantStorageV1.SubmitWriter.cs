using System.Text.Json;
using Npgsql;
using static CP6.Core.Persistence.Tenancy.TenantStorageProvisioner;

namespace CP6.Core.Persistence.Tenancy;

internal static partial class RmaTenantStorageV1
{
    private const string SubmitProfile = "rma-core-submit-writer-v1";
    private const string SubmitSchema = "cp6_rma_submit_writer_v1";
    private const string SubmitInstallation = SubmitSchema + ".installation";
    private const string SubmitFunction = SubmitSchema + ".submit_draft(uuid,uuid,uuid,uuid,integer,jsonb,bytea,text,bytea,jsonb)";

    private sealed record SubmitOverlay(string Request, string DraftRequest, string DraftBefore, string DraftAfter, string Result);

    internal static async Task<string> InstallCoreSubmitWriterAsync(NpgsqlConnection connection,
        RmaCoreCommissionRequestV1 request, CancellationToken ct)
    {
        ValidateCoreRequest(request);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await RequireControlTarget(connection, tx, request.Target, ct);
        TenantLoginDirectoryV1.ValidateArguments(connection, tx, request.Tenants);
        await Sql(connection, tx, $"""
            SELECT pg_catalog.pg_advisory_xact_lock({TenantLoginDirectoryV1.DirectoryLockNamespace},4);
            SELECT cp6_authentication_v1.acquire();
            SELECT pg_catalog.pg_advisory_xact_lock_shared({TenantLoginDirectoryV1.DirectoryLockNamespace},1);
            """, ct);

        var commission = await RequireJournalCommissionAsync(connection, tx, request, ct);
        var journal = await ReadJournalOverlayAsync(connection, tx, ct)
            ?? throw new TenantProvisioningException("draft-journal-required");
        await RequireJournalOverlayAsync(connection, tx, journal, CanonicalCoreRequest(commission), ct);
        if (await Scalar(connection, tx, $"SELECT pg_catalog.to_regnamespace('{DraftSchema}') IS NULL", ct) is true)
            throw new TenantProvisioningException("submit-draft-required");

        var tenants = request.Tenants.OrderBy(tenant => tenant.TenantId).ToArray();
        var schemas = tenants.Select(tenant => "t_" + tenant.TenantId.ToString("N")).ToArray();
        var currentDraft = await DraftCatalogSha256Async(connection, tx, schemas, ct);
        var draft = await ReadDraftOverlayAsync(connection, tx, request.InstallationId, ct);
        var requestJson = CanonicalCoreRequest(request);
        var prior = await ReadSubmitOverlayAsync(connection, tx, request.InstallationId, ct);
        if (prior is not null)
        {
            if (prior.Request != requestJson) throw new TenantProvisioningException("operation-conflict");
            RequireSubmitTransition(prior, draft, currentDraft);
            return prior.Result;
        }

        if (draft.Catalog != currentDraft) throw new TenantProvisioningException("draft-layout-mismatch");
        await RequireDraftTargetGrantsAsync(connection, tx, schemas, tenants.Select(tenant => tenant.Login).ToArray(), ct);
        var logins = string.Join(", ", tenants.Select(tenant => Q(tenant.Login)));
        await Sql(connection, tx, SubmitDdl(logins), ct);
        foreach (var schema in schemas)
        {
            await Sql(connection, tx, $"""
                GRANT SELECT (tenant_id,id,version), UPDATE (version) ON {Q(schema)}.heads TO {JournalExecutor};
                GRANT SELECT (tenant_id,id,version,state) ON {Q(schema)}.versions TO {JournalExecutor};
                """, ct);
        }

        // Keep W364's original catalog representation and seal only this exact additive change.
        var draftAfter = await DraftCatalogSha256Async(connection, tx, schemas, ct);
        if (draftAfter == draft.Catalog) throw new TenantProvisioningException("submit-layout-mismatch");
        var submitCatalog = await SubmitCatalogSha256Async(connection, tx, ct);
        var result = JsonSerializer.Serialize(new
        {
            Outcome = "INSTALLED",
            Profile = SubmitProfile,
            request.InstallationId,
            request.OperationId,
            request.Target.DatabaseOid,
            request.Target.SystemIdentifier,
            TenantCount = schemas.Length,
            DraftCatalogBefore = draft.Catalog,
            DraftCatalogAfter = draftAfter,
            CatalogSha256 = submitCatalog,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await using var save = new NpgsqlCommand($"""
            INSERT INTO {SubmitInstallation}(singleton,installation_id,request_json,draft_request_json,draft_catalog_before,
                draft_catalog_after,catalog_sha256,result_json)
            VALUES(true,@installation,@request,@draft,@before,@after,@catalog,@result)
            """, connection, tx);
        save.Parameters.AddWithValue("installation", request.InstallationId);
        save.Parameters.AddWithValue("request", requestJson);
        save.Parameters.AddWithValue("draft", draft.Request);
        save.Parameters.AddWithValue("before", draft.Catalog);
        save.Parameters.AddWithValue("after", draftAfter);
        save.Parameters.AddWithValue("catalog", submitCatalog);
        save.Parameters.AddWithValue("result", result);
        await save.ExecuteNonQueryAsync(ct);
        await Commit(tx, ct);
        return result;
    }

    private static string SubmitDdl(string logins) => $$"""
        CREATE SCHEMA {{SubmitSchema}};
        REVOKE ALL ON SCHEMA {{SubmitSchema}} FROM PUBLIC;
        CREATE TABLE {{SubmitInstallation}} (
            singleton boolean PRIMARY KEY CHECK(singleton),
            installation_id uuid NOT NULL, request_json text NOT NULL, draft_request_json text NOT NULL,
            draft_catalog_before text NOT NULL, draft_catalog_after text NOT NULL,
            catalog_sha256 text NOT NULL, result_json text NOT NULL
        );
        REVOKE ALL ON TABLE {{SubmitInstallation}} FROM PUBLIC;
        GRANT USAGE ON SCHEMA {{SubmitSchema}} TO {{logins}};

        CREATE FUNCTION {{SubmitSchema}}.submit_draft(tenant uuid, operation uuid, application uuid, actor uuid,
            expected_version integer, payload jsonb, input bytea, digest text, result bytea, audit jsonb) RETURNS boolean
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $submit$
        DECLARE tenant_schema text;
                changed integer;
        BEGIN
            PERFORM {{DraftSchema}}.lock_draft($1);
            tenant_schema := 't_' || pg_catalog.replace($1::text,'-','');
            EXECUTE pg_catalog.format(
                'UPDATE %I.heads h SET version=$3+1 WHERE h.tenant_id=$1 AND h.id=$2 AND h.version=$3
                    AND EXISTS (SELECT FROM %I.versions v WHERE v.tenant_id=$1 AND v.id=$2 AND v.version=$3 AND v.state=''DRAFT'')',
                tenant_schema,tenant_schema) USING $1,$3,$5;
            GET DIAGNOSTICS changed = ROW_COUNT;
            IF changed <> 1 THEN RETURN false; END IF;

            -- The existing deferred head/version FK is satisfied before this transaction can commit.
            EXECUTE pg_catalog.format(
                'INSERT INTO %I.versions(tenant_id,id,version,state,payload) VALUES($1,$2,$3+1,''SUBMITTED'',$4)',
                tenant_schema) USING $1,$3,$5,$6;
            PERFORM {{DraftSchema}}.append_original($1,$2,$4,$3,$7,$8,$9,$10);
            RETURN true;
        END $submit$;
        ALTER FUNCTION {{SubmitFunction}} OWNER TO {{JournalExecutor}};
        REVOKE ALL ON FUNCTION {{SubmitFunction}} FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION {{SubmitFunction}} TO {{logins}};
        """;

    private static async Task<SubmitOverlay?> ReadSubmitOverlayAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid installation, CancellationToken ct)
    {
        if (await Scalar(connection, tx, $"SELECT pg_catalog.to_regnamespace('{SubmitSchema}') IS NULL", ct) is true)
            return null;

        var catalog = await SubmitCatalogSha256Async(connection, tx, ct);
        await using var command = new NpgsqlCommand($"""
            SELECT request_json,draft_request_json,draft_catalog_before,draft_catalog_after,catalog_sha256,result_json
            FROM {SubmitInstallation} WHERE singleton AND installation_id=@installation
            """, connection, tx);
        command.Parameters.AddWithValue("installation", installation);
        await using var row = await command.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct) || row.GetString(4) != catalog)
            throw new TenantProvisioningException("submit-layout-mismatch");
        return new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(5));
    }

    private static void RequireSubmitTransition(SubmitOverlay submit, DraftOverlay draft, string currentDraft)
    {
        if (submit.DraftRequest != draft.Request || submit.DraftBefore != draft.Catalog || submit.DraftAfter != currentDraft)
            throw new TenantProvisioningException("submit-layout-mismatch");
    }

    private static async Task<string> SubmitCatalogSha256Async(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        var catalog = await Scalar(connection, tx, $"""
            SELECT pg_catalog.jsonb_build_array(n.oid,n.nspowner,n.nspacl,i.oid,i.relowner,i.relacl,
                p.oid,p.proowner,p.proacl,pg_catalog.pg_get_functiondef(p.oid))::text
            FROM pg_catalog.pg_namespace n
            JOIN pg_catalog.pg_class i ON i.relnamespace=n.oid AND i.relname='installation'
            JOIN pg_catalog.pg_proc p ON p.pronamespace=n.oid AND p.oid=pg_catalog.to_regprocedure('{SubmitFunction}')
            JOIN pg_catalog.pg_roles r ON r.oid=p.proowner AND r.rolname='{JournalExecutor}'
            WHERE n.nspname='{SubmitSchema}' AND n.nspowner=i.relowner AND p.prosecdef
              AND i.relowner=pg_catalog.to_regrole(pg_catalog.quote_ident(CURRENT_USER)) AND i.relkind='r' AND NOT i.relrowsecurity
              AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(i.relacl) a WHERE a.grantee<>i.relowner)
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_attribute a, LATERAL pg_catalog.aclexplode(a.attacl) acl
                              WHERE a.attrelid=i.oid AND acl.grantee<>i.relowner)
            """, ct);
        return catalog is string definition ? Sha256(definition) : throw new TenantProvisioningException("submit-layout-mismatch");
    }

    internal static async Task RequireCoreSubmitBindingAsync(NpgsqlConnection authority, NpgsqlTransaction tx,
        Guid installation, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT true FROM {SubmitInstallation} WHERE singleton AND installation_id=@installation FOR SHARE
            """, authority, tx);
        command.Parameters.AddWithValue("installation", installation);
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
    }
}
