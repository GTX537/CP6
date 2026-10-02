namespace CP6.DatabaseCompatibility.Probe;

internal static class ProbeDdl
{
    private static readonly string[] TokenTables = ["DatabaseTokenRows", "BusinessRecords", "OutboxRecords"];
    internal static readonly string[] PlatformTables = ["Cp6_OutboxMessage", "Cp6_InboxMessage", "Cp6_InboxAggregateCheckpoint", "Cp6_DeadLetterRecord"];
    private static readonly string[] Tables = [.. PlatformTables, "TriggerSourceRows", "AuditRecords", "OutboxRecords", "BusinessRecords", "DirectoryRecords", "TenantGenerations", "ApplicationTokenRows", "DatabaseTokenRows", "__cp6_compat_owner"];

    public static IEnumerable<string> PlatformTriggers(ProbeDatabase db)
    {
        if (db.Provider != ProbeProvider.PostgreSql) yield break;
        foreach (var table in PlatformTables)
        {
            yield return $"ALTER TABLE {db.Table(table)} ADD CHECK (octet_length(\"RowVersion\")=8)";
            yield return $"CREATE TRIGGER \"DatabaseTokenRowsToken\" BEFORE INSERT OR UPDATE ON {db.Table(table)} FOR EACH ROW EXECUTE FUNCTION {db.Table("SetToken")}()";
        }
    }

    public static IEnumerable<string> Create(ProbeDatabase db)
    {
        string C(string name) => db.Column(name);
        var uuid = db.Provider == ProbeProvider.PostgreSql ? "uuid" : "uniqueidentifier";
        var text = db.Provider == ProbeProvider.PostgreSql ? "text" : "nvarchar(200)";
        var token = db.Provider == ProbeProvider.PostgreSql ? $"bytea NOT NULL CHECK (octet_length({C("RowVersion")})=8)" : "rowversion NOT NULL";
        yield return $"CREATE TABLE {db.Table("__cp6_compat_owner")} ({C("OwnerId")} {uuid} PRIMARY KEY, {C("Task")} {text} NOT NULL)";
        yield return $"CREATE TABLE {db.Table("DatabaseTokenRows")} ({C("Id")} {uuid} PRIMARY KEY, {C("Value")} {text} NOT NULL, {C("LeaseOwner")} {text} NULL, {C("RowVersion")} {token})";
        yield return $"CREATE TABLE {db.Table("BusinessRecords")} ({C("Id")} {uuid} PRIMARY KEY, {C("TenantId")} {uuid} NOT NULL, {C("Value")} {text} NOT NULL, {C("RowVersion")} {token})";
        yield return $"CREATE TABLE {db.Table("OutboxRecords")} ({C("Id")} {uuid} PRIMARY KEY, {C("TenantId")} {uuid} NOT NULL, {C("BusinessId")} {uuid} NOT NULL, {C("Value")} {text} NOT NULL, {C("RowVersion")} {token})";
        var bytes = db.Provider == ProbeProvider.PostgreSql ? "bytea" : "binary(8)";
        yield return $"CREATE TABLE {db.Table("AuditRecords")} ({C("Id")} {uuid} PRIMARY KEY, {C("BusinessId")} {uuid} NOT NULL, {C("RowVersion")} {bytes} NOT NULL, {C("Value")} {text} NOT NULL)";
        yield return $"CREATE TABLE {db.Table("TenantGenerations")} ({C("TenantId")} {uuid} PRIMARY KEY, {C("Generation")} bigint NOT NULL CHECK ({C("Generation")}>=0))";
        var bit = db.Provider == ProbeProvider.PostgreSql ? "boolean" : "bit";
        yield return $"CREATE TABLE {db.Table("DirectoryRecords")} ({C("TenantId")} {uuid} NOT NULL, {C("Position")} bigint NOT NULL, {C("EventVersion")} bigint NOT NULL, {C("Value")} {text} NOT NULL, {C("IsDeleted")} {bit} NOT NULL, PRIMARY KEY ({C("TenantId")}, {C("Position")}))";
        yield return $"CREATE TABLE {db.Table("TriggerSourceRows")} ({C("Id")} {uuid} PRIMARY KEY, {C("TargetId")} {uuid} NOT NULL, {C("Value")} {text} NOT NULL)";
        if (db.Provider == ProbeProvider.PostgreSql)
        {
            yield return $"CREATE TABLE {db.Table("ApplicationTokenRows")} ({C("Id")} uuid PRIMARY KEY, {C("Value")} text NOT NULL, {C("LeaseOwner")} text NULL, {C("RowVersion")} bytea NOT NULL CHECK (octet_length({C("RowVersion")})=8))";
            yield return $"CREATE SEQUENCE {db.Table("TokenSequence")} AS bigint MINVALUE 1 NO CYCLE";
            yield return $"""
                CREATE FUNCTION {db.Table("SetToken")}() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                  NEW."RowVersion" := pg_catalog.int8send(nextval('"{db.Schema}"."TokenSequence"'::regclass));
                  RETURN NEW;
                END
                $body$
                """;
            foreach (var table in TokenTables)
                yield return $"CREATE TRIGGER {C("DatabaseTokenRowsToken")} BEFORE INSERT OR UPDATE ON {db.Table(table)} FOR EACH ROW EXECUTE FUNCTION {db.Table("SetToken")}()";
            yield return $"""
                CREATE FUNCTION {db.Table("AdvanceTenantGeneration")}() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE target uuid;
                BEGIN
                  target := CASE WHEN TG_OP = 'DELETE' THEN OLD."TenantId" ELSE NEW."TenantId" END;
                  UPDATE {db.Table("TenantGenerations")} SET "Generation"="Generation"+1 WHERE "TenantId"=target;
                  IF NOT FOUND THEN RAISE EXCEPTION 'Probe tenant generation owner is missing'; END IF;
                  IF TG_OP='UPDATE' AND OLD."TenantId"<>NEW."TenantId" THEN
                    UPDATE {db.Table("TenantGenerations")} SET "Generation"="Generation"+1 WHERE "TenantId"=OLD."TenantId";
                    IF NOT FOUND THEN RAISE EXCEPTION 'Probe tenant generation owner is missing'; END IF;
                  END IF;
                  RETURN NULL;
                END
                $body$
                """;
            yield return $"CREATE TRIGGER {C("DirectoryGeneration")} AFTER INSERT OR UPDATE OR DELETE ON {db.Table("DirectoryRecords")} FOR EACH ROW EXECUTE FUNCTION {db.Table("AdvanceTenantGeneration")}()";
            yield return $"""
                CREATE FUNCTION {db.Table("TriggerWrite")}() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                  UPDATE {db.Table("DatabaseTokenRows")} SET "Value"=NEW."Value" WHERE "Id"=NEW."TargetId";
                  RETURN NULL;
                END
                $body$
                """;
            yield return $"CREATE TRIGGER {C("TriggerSourceWrite")} AFTER INSERT OR UPDATE ON {db.Table("TriggerSourceRows")} FOR EACH ROW EXECUTE FUNCTION {db.Table("TriggerWrite")}()";
        }
        else
        {
            yield return $"""
                CREATE TRIGGER {db.Table("DirectoryGeneration")} ON {db.Table("DirectoryRecords")} AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS (SELECT [TenantId] FROM inserted UNION SELECT [TenantId] FROM deleted EXCEPT SELECT [TenantId] FROM {db.Table("TenantGenerations")})
                    THROW 51000, 'Probe tenant generation owner is missing', 1;
                  UPDATE g SET [Generation]=[Generation]+1 FROM {db.Table("TenantGenerations")} g
                    WHERE g.[TenantId] IN (SELECT [TenantId] FROM inserted UNION SELECT [TenantId] FROM deleted);
                END
                """;
            yield return $"""
                CREATE TRIGGER {db.Table("TriggerSourceWrite")} ON {db.Table("TriggerSourceRows")} AFTER INSERT, UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  UPDATE r SET [Value]=i.[Value] FROM {db.Table("DatabaseTokenRows")} r JOIN inserted i ON r.[Id]=i.[TargetId];
                END
                """;
        }
    }

    public static IEnumerable<string> Drop(ProbeDatabase db)
    {
        foreach (var table in Tables)
        {
            if (db.Provider == ProbeProvider.SqlServer && table == "ApplicationTokenRows") continue;
            yield return $"DROP TABLE {db.Table(table)}";
        }
        if (db.Provider == ProbeProvider.PostgreSql)
        {
            foreach (var function in new[] { "TriggerWrite", "AdvanceTenantGeneration", "SetToken" })
                yield return $"DROP FUNCTION {db.Table(function)}()";
            yield return $"DROP SEQUENCE {db.Table("TokenSequence")}";
        }
        yield return $"DROP SCHEMA {ProbeSafety.Quote(db.Schema, db.Provider)}";
    }
}
