using System.Text.Json;
using Npgsql;
using static CP6.Core.Persistence.Tenancy.TenantStorageProvisioner;

namespace CP6.Core.Persistence.Tenancy;

internal static partial class RmaTenantStorageV1
{
    private const string DraftProfile = "rma-core-draft-writer-v1";
    internal const string DraftSchema = "cp6_rma_draft_writer_v1";
    private const string DraftInstallation = DraftSchema + ".installation";
    private const string DraftLock = DraftSchema + ".lock_draft(uuid)";
    private const string DraftAppend = DraftSchema + ".append_original(uuid,uuid,uuid,uuid,bytea,text,bytea,jsonb)";
    private const string DraftCreate = DraftSchema + ".create_draft(uuid,uuid,uuid,uuid,uuid,jsonb,bytea,text,bytea,jsonb)";

    internal static async Task<string> InstallCoreDraftWriterAsync(NpgsqlConnection connection,
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
        var requestJson = CanonicalCoreRequest(request);
        var schemas = request.Tenants.OrderBy(tenant => tenant.TenantId).Select(tenant => "t_" + tenant.TenantId.ToString("N")).ToArray();
        if (await Scalar(connection, tx, $"SELECT pg_catalog.to_regnamespace('{DraftSchema}') IS NOT NULL", ct) is true)
        {
            var currentCatalog = await DraftCatalogSha256Async(connection, tx, schemas, ct);
            await using var read = new NpgsqlCommand($"""
                SELECT installation_id,request_json,catalog_sha256,result_json FROM {DraftInstallation} WHERE singleton
                """, connection, tx);
            string hash;
            string receipt;
            await using (var row = await read.ExecuteReaderAsync(ct))
            {
                if (!await row.ReadAsync(ct) || row.GetGuid(0) != request.InstallationId)
                    throw new TenantProvisioningException("draft-layout-mismatch");
                if (row.GetString(1) != requestJson) throw new TenantProvisioningException("operation-conflict");
                hash = row.GetString(2);
                receipt = row.GetString(3);
            }

            if (hash != currentCatalog)
                throw new TenantProvisioningException("draft-layout-mismatch");
            return receipt;
        }

        await RequireDraftTargetGrantsAsync(connection, tx, schemas, request.Tenants.Select(tenant => tenant.Login).ToArray(), ct);
        var logins = string.Join(", ", request.Tenants.OrderBy(tenant => tenant.TenantId).Select(tenant => Q(tenant.Login)));
        await Sql(connection, tx, DraftDdl(logins), ct);
        foreach (var schema in schemas)
        {
            await Sql(connection, tx, $"""
                GRANT USAGE ON SCHEMA {Q(schema)} TO {JournalExecutor};
                GRANT INSERT (tenant_id,id,actor_id,outbound_id,version) ON {Q(schema)}.heads TO {JournalExecutor};
                GRANT INSERT (tenant_id,id,version,state,payload) ON {Q(schema)}.versions TO {JournalExecutor};
                """, ct);
        }

        var catalog = await DraftCatalogSha256Async(connection, tx, schemas, ct);
        var result = JsonSerializer.Serialize(new
        {
            Outcome = "INSTALLED",
            Profile = DraftProfile,
            request.InstallationId,
            request.OperationId,
            request.Target.DatabaseOid,
            request.Target.SystemIdentifier,
            TenantCount = schemas.Length,
            CatalogSha256 = catalog,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await using var save = new NpgsqlCommand($"""
            INSERT INTO {DraftInstallation}(singleton,installation_id,request_json,catalog_sha256,result_json)
            VALUES(true,@installation,@request,@catalog,@result)
            """, connection, tx);
        save.Parameters.AddWithValue("installation", request.InstallationId);
        save.Parameters.AddWithValue("request", requestJson);
        save.Parameters.AddWithValue("catalog", catalog);
        save.Parameters.AddWithValue("result", result);
        await save.ExecuteNonQueryAsync(ct);
        await Commit(tx, ct);
        return result;
    }

    private static async Task RequireDraftTargetGrantsAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        string[] schemas, string[] logins, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT NOT EXISTS (
                SELECT FROM pg_catalog.pg_roles r
                JOIN pg_catalog.pg_namespace n ON n.nspname::text=ANY(@schemas)
                JOIN pg_catalog.pg_class c ON c.relnamespace=n.oid AND c.relname IN ('heads','versions')
                WHERE r.rolname::text=ANY(@logins)
                  AND (pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE')
                    OR pg_catalog.has_table_privilege(r.oid,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER')
                    OR pg_catalog.has_any_column_privilege(r.oid,c.oid,'INSERT,UPDATE'))
            ) AND NOT EXISTS (
                SELECT FROM pg_catalog.pg_default_acl d, LATERAL pg_catalog.aclexplode(d.defaclacl) a
                WHERE d.defaclrole=pg_catalog.to_regrole(pg_catalog.quote_ident(CURRENT_USER))
                  AND d.defaclnamespace=0 AND a.grantee<>d.defaclrole
                  AND NOT (d.defaclobjtype='f' AND a.grantee=0 AND a.privilege_type='EXECUTE')
            )
            """, connection, tx);
        command.Parameters.AddWithValue("schemas", schemas);
        command.Parameters.AddWithValue("logins", logins);
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new TenantProvisioningException("draft-layout-mismatch");
    }

    private static string DraftDdl(string logins) => $$"""
        CREATE SCHEMA {{DraftSchema}};
        REVOKE ALL ON SCHEMA {{DraftSchema}} FROM PUBLIC;
        CREATE TABLE {{DraftInstallation}} (
            singleton boolean PRIMARY KEY CHECK(singleton),
            installation_id uuid NOT NULL, request_json text NOT NULL,
            catalog_sha256 text NOT NULL, result_json text NOT NULL
        );
        REVOKE ALL ON TABLE {{DraftInstallation}} FROM PUBLIC;
        GRANT USAGE ON SCHEMA {{DraftSchema}} TO {{JournalExecutor}}, {{logins}};

        CREATE FUNCTION {{DraftSchema}}.lock_draft(tenant uuid) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $draft$
        BEGIN
            PERFORM cp6_authentication_v1.acquire();
            IF $1 IS DISTINCT FROM cp6_tenancy_v1.current_tenant_id() THEN
                RAISE EXCEPTION 'RMA_DRAFT_TENANT_BINDING_REJECTED' USING ERRCODE='42501';
            END IF;
        END $draft$;
        ALTER FUNCTION {{DraftLock}} OWNER TO {{JournalExecutor}};
        REVOKE ALL ON FUNCTION {{DraftLock}} FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION {{DraftLock}} TO {{logins}};

        -- Bind the original function at definition time. The executor needs no new journal schema ACL.
        CREATE FUNCTION {{DraftSchema}}.append_original(tenant uuid, operation uuid, actor uuid, application uuid,
            input bytea, digest text, result bytea, audit jsonb) RETURNS void
        LANGUAGE sql SECURITY INVOKER SET search_path=pg_catalog,pg_temp
        RETURN {{JournalSchema}}.store($1,$2,$3,'COMMITTED',$5,$6,$7,ARRAY[$4],$8);
        ALTER FUNCTION {{DraftAppend}} OWNER TO {{JournalExecutor}};
        REVOKE ALL ON FUNCTION {{DraftAppend}} FROM PUBLIC;

        CREATE FUNCTION {{DraftSchema}}.create_draft(tenant uuid, operation uuid, application uuid, actor uuid,
            outbound uuid, payload jsonb, input bytea, digest text, result bytea, audit jsonb) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $draft$
        DECLARE tenant_schema text;
        BEGIN
            PERFORM {{DraftSchema}}.lock_draft($1);
            tenant_schema := 't_' || pg_catalog.replace($1::text,'-','');
            EXECUTE pg_catalog.format(
                'INSERT INTO %I.heads(tenant_id,id,actor_id,outbound_id,version) VALUES($1,$2,$3,$4,1)',tenant_schema)
                USING $1,$3,$4,$5;
            EXECUTE pg_catalog.format(
                'INSERT INTO %I.versions(tenant_id,id,version,state,payload) VALUES($1,$2,1,''DRAFT'',$3)',tenant_schema)
                USING $1,$3,$6;
            PERFORM {{DraftSchema}}.append_original($1,$2,$4,$3,$7,$8,$9,$10);
        END $draft$;
        ALTER FUNCTION {{DraftCreate}} OWNER TO {{JournalExecutor}};
        REVOKE ALL ON FUNCTION {{DraftCreate}} FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION {{DraftCreate}} TO {{logins}};
        """;

    private static async Task<string> DraftCatalogSha256Async(NpgsqlConnection connection, NpgsqlTransaction tx,
        string[] schemas, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT pg_catalog.jsonb_build_array(n.oid,n.nspowner,n.nspacl,i.oid,i.relowner,i.relacl,
                (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(p.oid,p.proowner,p.proacl,pg_catalog.pg_get_functiondef(p.oid))
                                              ORDER BY p.proname)
                 FROM pg_catalog.pg_proc p WHERE p.pronamespace=n.oid),
                (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(s.oid,s.nspacl,c.oid,c.relacl,
                    (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(a.attnum,a.attacl) ORDER BY a.attnum)
                     FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped))
                     ORDER BY s.nspname,c.relname)
                 FROM pg_catalog.pg_namespace s JOIN pg_catalog.pg_class c ON c.relnamespace=s.oid
                 WHERE s.nspname::text=ANY(@schemas) AND c.relname IN ('heads','versions'))
            )::text
            FROM pg_catalog.pg_namespace n
            JOIN pg_catalog.pg_class i ON i.relnamespace=n.oid AND i.relname='installation'
            WHERE n.nspname='{DraftSchema}' AND n.nspowner=i.relowner
              AND i.relowner=pg_catalog.to_regrole(pg_catalog.quote_ident(CURRENT_USER)) AND i.relkind='r'
              AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(i.relacl) a WHERE a.grantee<>i.relowner)
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_attribute a, LATERAL pg_catalog.aclexplode(a.attacl) acl
                              WHERE a.attrelid=i.oid AND acl.grantee<>i.relowner)
            """, connection, tx);
        command.Parameters.AddWithValue("schemas", schemas);
        return await command.ExecuteScalarAsync(ct) is string catalog
            ? Sha256(catalog) : throw new TenantProvisioningException("draft-layout-mismatch");
    }

    internal static async Task RequireCoreDraftBindingAsync(NpgsqlConnection authority, NpgsqlTransaction tx,
        Guid installation, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT true FROM {DraftInstallation} WHERE singleton AND installation_id=@installation FOR SHARE
            """, authority, tx);
        command.Parameters.AddWithValue("installation", installation);
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
    }
}
