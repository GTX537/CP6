using System.Security.Cryptography;
using CP6.Tests.Platform;
using Npgsql;

namespace CP6.Tests.Erp;

// CRM 在 WP03-19 安装产品 schema。本夹具在独立数据库执行相同的锁和最小授权合同。
internal sealed class ActFileGuardCrmFixtureV1
{
    internal const string Schema = """
        CREATE SCHEMA crm_act_file_guard_v1;
        REVOKE ALL ON SCHEMA crm_act_file_guard_v1 FROM PUBLIC;
        CREATE FUNCTION crm_act_file_guard_v1.database_oid_v1() RETURNS oid
        LANGUAGE sql STABLE SET search_path=pg_catalog,pg_temp AS $$
            SELECT oid FROM pg_database WHERE datname=current_database()
        $$;
        CREATE TABLE crm_act_file_guard_v1.guards (
            tenant_id uuid PRIMARY KEY,
            store_id uuid NOT NULL UNIQUE,
            cluster_id bigint NOT NULL DEFAULT (pg_catalog.pg_control_system()).system_identifier,
            database_oid oid NOT NULL DEFAULT (crm_act_file_guard_v1.database_oid_v1()),
            seal_login name NOT NULL,
            read_login name NOT NULL,
            activity_login name NOT NULL,
            epoch bigint NOT NULL CHECK (epoch > 0),
            receipt uuid NOT NULL
        );
        CREATE TABLE crm_act_file_guard_v1.advances (
            tenant_id uuid NOT NULL REFERENCES crm_act_file_guard_v1.guards(tenant_id),
            store_id uuid NOT NULL,
            previous_epoch bigint NOT NULL,
            previous_receipt uuid NOT NULL,
            epoch bigint NOT NULL CHECK(epoch=previous_epoch+1),
            receipt uuid NOT NULL UNIQUE,
            occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            writer name NOT NULL DEFAULT session_user,
            PRIMARY KEY (tenant_id,store_id,previous_epoch)
        );
        CREATE FUNCTION crm_act_file_guard_v1.advance_v1(t uuid,s uuid,e bigint,r uuid)
        RETURNS TABLE (epoch bigint, receipt uuid)
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp SET lock_timeout='8s'
        AS $$
        DECLARE current_guard crm_act_file_guard_v1.guards;
        DECLARE next_receipt uuid;
        BEGIN
            SELECT g.* INTO STRICT current_guard FROM crm_act_file_guard_v1.guards g
             WHERE g.tenant_id=t AND g.store_id=s AND g.seal_login=session_user
               AND g.cluster_id=(pg_catalog.pg_control_system()).system_identifier
               AND g.database_oid=crm_act_file_guard_v1.database_oid_v1() FOR UPDATE;
            IF current_guard.epoch=e+1 THEN
                RETURN QUERY SELECT a.epoch,a.receipt FROM crm_act_file_guard_v1.advances a
                 WHERE a.tenant_id=t AND a.store_id=s AND a.previous_epoch=e AND a.previous_receipt=r
                   AND a.receipt=current_guard.receipt;
                RETURN;
            END IF;
            IF current_guard.epoch<>e OR current_guard.receipt<>r THEN RETURN; END IF;
            IF current_setting('synchronous_commit') IN ('off','local') THEN
                PERFORM set_config('synchronous_commit','on',true);
            END IF;
            next_receipt := gen_random_uuid();
            UPDATE crm_act_file_guard_v1.guards g SET epoch=e+1,receipt=next_receipt WHERE g.tenant_id=t;
            INSERT INTO crm_act_file_guard_v1.advances(tenant_id,store_id,previous_epoch,previous_receipt,epoch,receipt)
                VALUES(t,s,e,r,e+1,next_receipt);
            RETURN QUERY SELECT e+1,next_receipt;
        END $$;
        CREATE FUNCTION crm_act_file_guard_v1.hold_v1(t uuid,s uuid)
        RETURNS TABLE (epoch bigint, receipt uuid)
        LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,pg_temp SET lock_timeout='8s'
        AS $$
            SELECT g.epoch,g.receipt FROM crm_act_file_guard_v1.guards g WHERE g.tenant_id=t AND g.store_id=s
                AND session_user IN (g.read_login,g.activity_login)
                AND g.cluster_id=(pg_catalog.pg_control_system()).system_identifier
                AND g.database_oid=crm_act_file_guard_v1.database_oid_v1() FOR SHARE
        $$;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA crm_act_file_guard_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA crm_act_file_guard_v1 FROM PUBLIC;
        """;

    internal Guid Tenant { get; }
    internal Guid Store { get; } = Guid.NewGuid();
    internal Guid InitialReceipt { get; } = Guid.NewGuid();
    private readonly CurrentPermissionPostgresFixture cluster;
    private readonly string database;
    private readonly string readLogin;
    private readonly string activityLogin;
    private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private ActFileGuardCrmFixtureV1(CurrentPermissionPostgresFixture cluster, Guid tenant)
    {
        this.cluster = cluster;
        Tenant = tenant;
        var tag = Guid.NewGuid().ToString("N")[..12];
        database = "act_crm_" + tag;
        readLogin = "act_read_" + tag;
        activityLogin = "act_consume_" + tag;
    }

    internal async Task<NpgsqlConnection> OpenCrmOwnerAsync() => await cluster.OpenOwnerAsync(database);
    internal Task<NpgsqlConnection> OpenActivityAsync() => cluster.OpenRawAsync(activityLogin, secret, database);
    internal Task<NpgsqlConnection> OpenSealAsync() => cluster.OpenRawAsync(SealLogin, secret, database);
    internal string ReadConnection => new NpgsqlConnectionStringBuilder
    {
        Host = "127.0.0.1", Port = CurrentPermissionPostgresFixture.Port, Database = database,
        Username = readLogin, Password = secret, SslMode = SslMode.Disable, Pooling = false
    }.ConnectionString;
    internal string ServerName => database;
    internal string SealRole => SealLogin;
    private string SealLogin => "act_seal_" + database["act_crm_".Length..];

    internal static async Task<ActFileGuardCrmFixtureV1> CreateAsync(CurrentPermissionPostgresFixture cluster, Guid tenant)
    {
        var fixture = new ActFileGuardCrmFixtureV1(cluster, tenant);
        await using (var root = await cluster.OpenOwnerAsync("postgres"))
        {
            await Scalar(root, $"CREATE DATABASE {fixture.database}");
            foreach (var login in new[] { fixture.SealLogin, fixture.activityLogin, fixture.readLogin })
                await Scalar(root, $"CREATE ROLE {login} LOGIN PASSWORD '{fixture.secret}'");
            await Scalar(root, $"ALTER ROLE {fixture.SealLogin} IN DATABASE {fixture.database} SET statement_timeout='12s'");
        }
        await using var crm = await fixture.OpenCrmOwnerAsync();
        await Scalar(crm, Schema);
        await Scalar(crm, $"""
            GRANT USAGE ON SCHEMA crm_act_file_guard_v1 TO {fixture.SealLogin},{fixture.readLogin},{fixture.activityLogin};
            GRANT EXECUTE ON FUNCTION crm_act_file_guard_v1.advance_v1(uuid,uuid,bigint,uuid) TO {fixture.SealLogin};
            GRANT EXECUTE ON FUNCTION crm_act_file_guard_v1.hold_v1(uuid,uuid) TO {fixture.readLogin},{fixture.activityLogin};
            CREATE TABLE public.activity_fixture (operation_id uuid PRIMARY KEY);
            GRANT SELECT,INSERT ON public.activity_fixture TO {fixture.activityLogin};
            INSERT INTO crm_act_file_guard_v1.guards(tenant_id,store_id,seal_login,read_login,activity_login,epoch,receipt)
                VALUES(@tenant,@store,'{fixture.SealLogin}','{fixture.readLogin}','{fixture.activityLogin}',1,@receipt);
            """, ("tenant", tenant), ("store", fixture.Store), ("receipt", fixture.InitialReceipt));
        return fixture;
    }

    internal async Task BindMainAsync(NpgsqlConnection main)
    {
        await Scalar(main, $"""
            CREATE EXTENSION IF NOT EXISTS dblink WITH SCHEMA cp6_act_file_transport;
            CREATE SERVER {ServerName} FOREIGN DATA WRAPPER dblink_fdw OPTIONS (
                host '127.0.0.1',port '{CurrentPermissionPostgresFixture.Port}',dbname '{database}',
                sslmode 'disable',connect_timeout '5',application_name 'cp6-act-file-seal-test');
            CREATE USER MAPPING FOR CURRENT_USER SERVER {ServerName} OPTIONS (user '{SealLogin}', password '{secret}');
            INSERT INTO cp6_act_file_v1.sources(tenant_id,store_id,server_name,epoch,receipt)
                VALUES(@tenant,@store,@server::name,1,@receipt);
            """, ("tenant", Tenant), ("store", Store), ("server", ServerName), ("receipt", InitialReceipt));
    }

    internal static async Task<object?> Scalar(NpgsqlConnection db, string sql, params (string Name, object Value)[] args)
    {
        await using var command = new NpgsqlCommand(sql, db);
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }
}
