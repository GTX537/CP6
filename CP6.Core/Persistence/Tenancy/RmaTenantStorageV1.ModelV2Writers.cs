using Npgsql;
using static CP6.Core.Persistence.Tenancy.TenantStorageProvisioner;

namespace CP6.Core.Persistence.Tenancy;

internal static partial class RmaTenantStorageV1
{
    private const string ModelDraft = ModelSchema + ".create_draft(uuid,uuid,uuid,uuid,uuid,jsonb,bytea,text,bytea,jsonb)";
    private const string ModelSubmit = ModelSchema + ".submit_draft(uuid,uuid,uuid,uuid,integer,jsonb,bytea,text,bytea,jsonb)";
    private const string ModelClose = ModelSchema + ".close_operation(uuid,uuid,uuid,bytea,jsonb)";
    private const string ModelOpenWrite = ModelSchema + ".open_write(uuid)";

    // The original executor is a NOLOGIN role. Only these SECURITY DEFINER writers can
    // add the current tenant and transaction to the private, transaction-local write gate.
    private static async Task EnsureCoreModelV2WritersAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        if (await Scalar(connection, tx, $"SELECT EXISTS (SELECT FROM {ModelHistory})", ct) is true)
            return;

        await Sql(connection, tx, $$"""
            GRANT USAGE ON SCHEMA {{ModelSchema}} TO {{JournalExecutor}};
            GRANT SELECT ON {{ModelHistory}} TO {{JournalExecutor}};
            GRANT SELECT,INSERT,DELETE ON {{ModelWriters}} TO {{JournalExecutor}};

            CREATE FUNCTION {{ModelSchema}}.open_write(tenant uuid) RETURNS void
            LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $writer$
            BEGIN
                PERFORM {{DraftSchema}}.lock_draft($1);
                IF NOT EXISTS(SELECT FROM {{ModelHistory}} WHERE tenant_id=$1) THEN
                    RAISE EXCEPTION 'RMA_TENANT_MODEL_WRITER_REQUIRED' USING ERRCODE='42501';
                END IF;
                INSERT INTO {{ModelWriters}} VALUES($1,pg_current_xact_id());
            END $writer$;

            CREATE FUNCTION {{ModelSchema}}.require_writer() RETURNS trigger
            LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $writer$
            BEGIN
                IF NOT EXISTS(SELECT FROM {{ModelWriters}}
                    WHERE tenant_id=NEW.tenant_id AND writer_xid=pg_current_xact_id()) THEN
                    RAISE EXCEPTION 'RMA_TENANT_MODEL_WRITER_REQUIRED' USING ERRCODE='42501';
                END IF;
                RETURN NEW;
            END $writer$;

            CREATE FUNCTION {{ModelSchema}}.require_journal_writer() RETURNS trigger
            LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $writer$
            BEGIN
                IF EXISTS(SELECT FROM {{ModelHistory}} WHERE tenant_id=NEW.tenant_id)
                  AND NOT EXISTS(SELECT FROM {{ModelWriters}}
                      WHERE tenant_id=NEW.tenant_id AND writer_xid=pg_current_xact_id()) THEN
                    RAISE EXCEPTION 'RMA_TENANT_MODEL_WRITER_REQUIRED' USING ERRCODE='42501';
                END IF;
                RETURN NEW;
            END $writer$;
            CREATE TRIGGER rma_model_v2_journal_writer BEFORE INSERT ON {{JournalTable}}
                FOR EACH ROW WHEN (NEW.owner='{{RmaOwner}}')
                EXECUTE FUNCTION {{ModelSchema}}.require_journal_writer();
            ALTER TABLE {{JournalTable}} ENABLE ALWAYS TRIGGER rma_model_v2_journal_writer;

            CREATE FUNCTION {{ModelSchema}}.create_draft(tenant uuid, operation uuid, application uuid, actor uuid,
                outbound uuid, payload jsonb, input bytea, digest text, result bytea, audit jsonb) RETURNS void
            LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $writer$
            BEGIN
                PERFORM {{ModelSchema}}.open_write($1);
                PERFORM {{DraftSchema}}.create_draft($1,$2,$3,$4,$5,$6,$7,$8,$9,$10);
                DELETE FROM {{ModelWriters}} WHERE tenant_id=$1 AND writer_xid=pg_current_xact_id();
            END $writer$;

            CREATE FUNCTION {{ModelSchema}}.submit_draft(tenant uuid, operation uuid, application uuid, actor uuid,
                expected_version integer, payload jsonb, input bytea, digest text, result bytea, audit jsonb) RETURNS boolean
            LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $writer$
            DECLARE changed boolean;
            BEGIN
                PERFORM {{ModelSchema}}.open_write($1);
                changed := {{ModelSchema}}.call_submit($1,$2,$3,$4,$5,$6,$7,$8,$9,$10);
                DELETE FROM {{ModelWriters}} WHERE tenant_id=$1 AND writer_xid=pg_current_xact_id();
                RETURN changed;
            END $writer$;

            CREATE FUNCTION {{ModelSchema}}.close_operation(tenant uuid, operation uuid, actor uuid,
                result bytea, audit jsonb) RETURNS void
            LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $writer$
            BEGIN
                PERFORM {{ModelSchema}}.open_write($1);
                PERFORM {{ModelSchema}}.append_closed($1,$2,$3,$4,$5);
                DELETE FROM {{ModelWriters}} WHERE tenant_id=$1 AND writer_xid=pg_current_xact_id();
            END $writer$;

            CREATE FUNCTION {{ModelSchema}}.append_closed(tenant uuid, operation uuid, actor uuid,
                result bytea, audit jsonb) RETURNS void
            LANGUAGE sql SECURITY INVOKER SET search_path=pg_catalog,pg_temp
            RETURN {{JournalSchema}}.store($1,$2,$3,'CLOSED',NULL,NULL,$4,ARRAY[]::uuid[],$5);

            CREATE FUNCTION {{ModelSchema}}.call_submit(tenant uuid, operation uuid, application uuid, actor uuid,
                expected_version integer, payload jsonb, input bytea, digest text, result bytea, audit jsonb) RETURNS boolean
            LANGUAGE sql SECURITY INVOKER SET search_path=pg_catalog,pg_temp
            RETURN {{SubmitSchema}}.submit_draft($1,$2,$3,$4,$5,$6,$7,$8,$9,$10);

            ALTER FUNCTION {{ModelOpenWrite}} OWNER TO {{JournalExecutor}};
            ALTER FUNCTION {{ModelDraft}} OWNER TO {{JournalExecutor}};
            ALTER FUNCTION {{ModelSubmit}} OWNER TO {{JournalExecutor}};
            ALTER FUNCTION {{ModelClose}} OWNER TO {{JournalExecutor}};
            ALTER FUNCTION {{ModelSchema}}.append_closed(uuid,uuid,uuid,bytea,jsonb) OWNER TO {{JournalExecutor}};
            ALTER FUNCTION {{ModelSchema}}.call_submit(uuid,uuid,uuid,uuid,integer,jsonb,bytea,text,bytea,jsonb) OWNER TO {{JournalExecutor}};
            REVOKE ALL ON ALL FUNCTIONS IN SCHEMA {{ModelSchema}} FROM PUBLIC;
            """, ct);
    }
}
