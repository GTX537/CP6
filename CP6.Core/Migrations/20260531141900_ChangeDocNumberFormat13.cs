using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class ChangeDocNumberFormat13 : Migration
    {
        // 採番列の桁拡張（11/15→20）に伴い、当該列を参照する FK を一旦削除する必要がある（SQL Server 制約）。
        // 列が UNIQUE/PK 索引に含まれていても nvarchar の桁拡張は索引を落とさず実行可能なため、
        // EF の AlterColumn（索引を自動 DROP/CREATE する）を使わず、生 SQL で ALTER COLUMN する。
        private static readonly (string Fk, string Child, string Col, string Parent, string PCol)[] _docNoFks = new[]
        {
            ("FK_T_EstimateCalcProcess_T_EstimateCalc_QtnCalcNo", "T_EstimateCalcProcess", "QtnCalcNo", "T_EstimateCalc", "QtnCalcNo"),
            ("FK_T_ProductCoProduct_T_ProductMaster_ProductCd", "T_ProductCoProduct", "ProductCd", "T_ProductMaster", "ProductCd"),
            ("FK_T_ProductLotPrice_T_ProductMaster_ProductCd", "T_ProductLotPrice", "ProductCd", "T_ProductMaster", "ProductCd"),
            ("FK_T_ProductMaterial_T_ProductMaster_ProductCd", "T_ProductMaterial", "ProductCd", "T_ProductMaster", "ProductCd"),
            ("FK_T_ProductProcess_T_ProductMaster_ProductCd", "T_ProductProcess", "ProductCd", "T_ProductMaster", "ProductCd"),
            ("FK_T_QuotationCalc_T_Quotation_QtnNo", "T_QuotationCalc", "QtnNo", "T_Quotation", "QtnNo"),
            ("FK_T_QuotationDetail_T_Quotation_QtnNo", "T_QuotationDetail", "QtnNo", "T_Quotation", "QtnNo"),
        };

        // (テーブル, 列, NULL 許可)。Up=nvarchar(20) / Down=元の桁。
        private static readonly (string Table, string Col, bool Nullable)[] _cols = new[]
        {
            // ProductCd 系（元 nvarchar(15)）
            ("T_WorkOrder", "ProductCd", false),
            ("T_ProductProcess", "ProductCd", false),
            ("T_ProductMaterial", "ProductCd", false),
            ("T_ProductLotPrice", "ProductCd", false),
            ("T_ProductCoProduct", "ProductCd", false),
            ("T_ProductMaster", "ProductCd", false),
            ("T_ProductMaster", "SetProductCd", false),
            ("T_OrderProcessNote", "ProductCd", false),
            ("T_OrderProcess", "ProductCd", false),
            ("T_OrderMaterial", "ProductCd", false),
            ("T_OrderDetail", "ProductCd", false),
            ("T_OrderDetail", "SetProductCd", true),
            // 採番（文書番号）系（元 nvarchar(11)）
            ("T_QuotationDetail", "QtnNo", false),
            ("T_QuotationDetail", "QtnCalcNo", true),
            ("T_QuotationCalc", "QtnNo", false),
            ("T_QuotationCalc", "QtnCalcNo", false),
            ("T_Quotation", "QtnNo", false),
            ("T_Quotation", "RefQtnNo", true),
            ("T_ProductMaster", "QuotationNo", true),
            ("T_ProductMaster", "EstimateCalcNo", true),
            ("T_ProductMaster", "RefEstimateCalcNo", true),
            ("T_OrderDetail", "QuotationNo", true),
            ("T_OrderDetail", "EstimateCalcNo", true),
            ("T_OrderDetail", "RefEstimateCalcNo", true),
            ("T_FscChecklist", "QtnNo", false),
            ("T_FscChecklist", "QtnCalcNo", false),
            ("T_EstimateCalcProcess", "QtnCalcNo", false),
            ("T_EstimateCalc", "QtnCalcNo", false),
            ("T_EstimateCalc", "RefQtnCalcNo", true),
        };

        // ProductCd/SetProductCd は元 15、それ以外（採番）は元 11
        private static int OrigLen(string col) =>
            (col == "ProductCd" || col == "SetProductCd") ? 15 : 11;

        private static void AlterCols(MigrationBuilder b, int len)
        {
            foreach (var c in _cols)
            {
                var nullSql = c.Nullable ? "NULL" : "NOT NULL";
                b.Sql($"ALTER TABLE [{c.Table}] ALTER COLUMN [{c.Col}] nvarchar({len}) {nullSql};");
            }
        }

        private static void RevertCols(MigrationBuilder b)
        {
            foreach (var c in _cols)
            {
                var nullSql = c.Nullable ? "NULL" : "NOT NULL";
                b.Sql($"ALTER TABLE [{c.Table}] ALTER COLUMN [{c.Col}] nvarchar({OrigLen(c.Col)}) {nullSql};");
            }
        }

        private static void DropDocNoFks(MigrationBuilder b)
        {
            foreach (var f in _docNoFks)
                b.DropForeignKey(name: f.Fk, table: f.Child);
        }

        private static void AddDocNoFks(MigrationBuilder b)
        {
            foreach (var f in _docNoFks)
                b.AddForeignKey(
                    name: f.Fk, table: f.Child, column: f.Col,
                    principalTable: f.Parent, principalColumn: f.PCol,
                    onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropDocNoFks(migrationBuilder);
            AlterCols(migrationBuilder, 20);
            AddDocNoFks(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "T_DocSequence",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "uniqueidentifier", nullable: false),
                    FuncCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    LastSeq = table.Column<int>(type: "int", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<System.DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<System.DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_DocSequence", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_DocSequence_FuncCode",
                table: "T_DocSequence",
                column: "FuncCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "T_DocSequence");

            DropDocNoFks(migrationBuilder);
            RevertCols(migrationBuilder);
            AddDocNoFks(migrationBuilder);
        }
    }
}
