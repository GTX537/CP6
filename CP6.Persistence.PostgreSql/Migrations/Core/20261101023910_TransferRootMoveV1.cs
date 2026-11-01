using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261101023910_TransferRootMoveV1")]
public sealed class TransferRootMoveV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_transfer_root_v1;
        CREATE TABLE cp6_transfer_root_v1.requests(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,canonical text COLLATE "C" NOT NULL,
          order_owner text COLLATE "C" NOT NULL,order_id text COLLATE "C" NOT NULL,selection_version text COLLATE "C" NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,canonical),UNIQUE(tenant_id,environment_id,order_owner,order_id,selection_version));
        CREATE TABLE cp6_transfer_root_v1.source_ranges(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,canonical text COLLATE "C" NOT NULL,segment text COLLATE "C" NOT NULL,
          demand_from numeric(21,8) NOT NULL,demand_to numeric(21,8) NOT NULL,request_from numeric(21,8) NOT NULL,request_to numeric(21,8) NOT NULL,
          root_id text COLLATE "C" NOT NULL,root_from numeric(21,8) NOT NULL,root_to numeric(21,8) NOT NULL,
          CHECK(demand_from>=0 AND demand_to>demand_from AND request_from>=0 AND request_to>request_from AND root_from>=0 AND root_to>root_from
            AND demand_to-demand_from=request_to-request_from AND request_to-request_from=root_to-root_from),
          PRIMARY KEY(tenant_id,environment_id,canonical,segment),FOREIGN KEY(tenant_id,environment_id,canonical) REFERENCES cp6_transfer_root_v1.requests,
          FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_stock_move_v1.roots);
        CREATE TABLE cp6_transfer_root_v1.heads(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,canonical text COLLATE "C" NOT NULL,state text NOT NULL,
          approver_id uuid,performer_id uuid,device text COLLATE "C",occurrence_key text COLLATE "C",receipt bytea,
          PRIMARY KEY(tenant_id,environment_id,canonical),FOREIGN KEY(tenant_id,environment_id,canonical) REFERENCES cp6_transfer_root_v1.requests);
        CREATE TABLE cp6_transfer_root_v1.devices(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,device text COLLATE "C" NOT NULL,performer_id uuid NOT NULL,enabled boolean NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,device,performer_id));
        CREATE TABLE cp6_transfer_root_v1.occurrences(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,device text COLLATE "C" NOT NULL,occurrence_key text COLLATE "C" NOT NULL,canonical text COLLATE "C" NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,device,occurrence_key),UNIQUE(tenant_id,environment_id,canonical),
          FOREIGN KEY(tenant_id,environment_id,canonical) REFERENCES cp6_transfer_root_v1.requests);
        CREATE TABLE cp6_transfer_root_v1.events(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,canonical text COLLATE "C" NOT NULL,action text NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),FOREIGN KEY(tenant_id,environment_id,canonical) REFERENCES cp6_transfer_root_v1.requests);
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_root_v1.requests FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_root_v1.source_ranges FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_root_v1.occurrences FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_root_v1.events FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_transfer_root_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward-only source execution history.");
}
