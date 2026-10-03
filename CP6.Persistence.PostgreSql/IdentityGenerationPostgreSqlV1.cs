namespace CP6.Persistence.PostgreSql;

internal static class IdentityGenerationPostgreSqlV1
{
    public const string Sql = """
        INSERT INTO public."CrmIdentityTenantGenerations"("TenantId","Generation")
        SELECT DISTINCT "TenantId",1 FROM crm_identity."Snapshot";
        CREATE FUNCTION cp6_storage_core.advance_identity_generation_v1()
        RETURNS trigger LANGUAGE plpgsql AS $cp6$
        BEGIN
          IF TG_OP <> 'INSERT' THEN
            INSERT INTO public."CrmIdentityTenantGenerations" AS generation("TenantId","Generation")
            VALUES(OLD."TenantId",1)
            ON CONFLICT("TenantId") DO UPDATE SET "Generation"=generation."Generation"+1;
          END IF;
          IF TG_OP='INSERT' OR (TG_OP='UPDATE' AND NEW."TenantId" IS DISTINCT FROM OLD."TenantId") THEN
            INSERT INTO public."CrmIdentityTenantGenerations" AS generation("TenantId","Generation")
            VALUES(NEW."TenantId",1)
            ON CONFLICT("TenantId") DO UPDATE SET "Generation"=generation."Generation"+1;
          END IF;
          RETURN NULL;
        END $cp6$;
        CREATE TRIGGER cp6_identity_generation_v1 AFTER INSERT OR UPDATE OR DELETE
        ON crm_identity."Snapshot" FOR EACH ROW EXECUTE FUNCTION cp6_storage_core.advance_identity_generation_v1();
        """;
}
