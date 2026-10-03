using System.Text;

namespace CP6.Persistence.PostgreSql;

/// <summary>
/// Frozen raw contracts from 20260506103359_RestoreMissingOrderTablesPA070.
/// Defaults are absent from the EF model; the four raw global unique indexes also
/// survive in the final SQL catalog. Install only after the Core baseline tables.
/// </summary>
public static class PostgreSqlRestoredOrderDefaultsV1
{
    public readonly record struct DefaultRule(
        string Table, string Column, string Literal, bool IsClock, string PostgreSqlType);

    public readonly record struct UniqueRule(string Table, string Name, IReadOnlyList<string> Columns);

    // Logical source literals, not machine-generated SQL default-constraint names.
    // PostgreSQL types were checked against the frozen Core baseline; in particular
    // PlateMold.Need* are integer, while the attachment and transfer flags are boolean.
    public static IReadOnlyList<DefaultRule> DefaultRules { get; } = Array.AsReadOnly<DefaultRule>(
    [
        new("T_Order", "Status", "0", false, "integer"),
        new("T_Order", "McTransferFlg", "0", false, "boolean"),
        new("T_Order", "CreateDate", "GETDATE()", true, "timestamp without time zone"),
        new("T_Order", "IsDeleted", "0", false, "boolean"),
        new("T_OrderDetail", "Status", "0", false, "integer"),
        new("T_OrderDetail", "WfApprovalFlg", "0", false, "boolean"),
        new("T_OrderDetail", "McTransferFlg", "0", false, "boolean"),
        new("T_OrderDetail", "ProvisionalPriceFlg", "0", false, "boolean"),
        new("T_OrderDetail", "CreateDate", "GETDATE()", true, "timestamp without time zone"),
        new("T_OrderDetail", "IsDeleted", "0", false, "boolean"),
        new("T_OrderProcess", "MachineFixedFlg", "0", false, "boolean"),
        new("T_OrderProcess", "LossRate", "0", false, "numeric(21,8)"),
        new("T_OrderProcess", "MachineCount", "0", false, "numeric(21,8)"),
        new("T_OrderProcess", "LeadTimeDays", "0", false, "integer"),
        new("T_OrderProcess", "SortOrder", "0", false, "integer"),
        new("T_OrderProcess", "CreateDate", "GETDATE()", true, "timestamp without time zone"),
        new("T_OrderProcess", "IsDeleted", "0", false, "boolean"),
        new("T_OrderProcessNote", "CreateDate", "GETDATE()", true, "timestamp without time zone"),
        new("T_OrderProcessNote", "IsDeleted", "0", false, "boolean"),
        new("T_OrderMaterial", "MaterialTypeDiv", "N'3'", false, "bpchar"),
        new("T_OrderMaterial", "SupplyDiv", "N'1'", false, "bpchar"),
        new("T_OrderMaterial", "SupplyUnitPrice", "0", false, "numeric(21,8)"),
        new("T_OrderMaterial", "SortOrder", "0", false, "integer"),
        new("T_OrderMaterial", "CreateDate", "GETDATE()", true, "timestamp without time zone"),
        new("T_OrderMaterial", "IsDeleted", "0", false, "boolean"),
        new("T_SheetUnitPrice", "UnitPrice", "0", false, "numeric(15,4)"),
        new("T_SheetUnitPrice", "CreateDate", "GETDATE()", true, "timestamp without time zone"),
        new("T_SheetUnitPrice", "IsDeleted", "0", false, "boolean"),
        new("T_SheetUnitPriceEstimate", "UnitPrice", "0", false, "numeric(15,4)"),
        new("T_SheetUnitPriceEstimate", "CreateDate", "GETDATE()", true, "timestamp without time zone"),
        new("T_SheetUnitPriceEstimate", "IsDeleted", "0", false, "boolean"),
        new("T_PlateMold", "WdRev", "1", false, "integer"),
        new("T_PlateMold", "DuplicatePlateFlg", "0", false, "boolean"),
        new("T_PlateMold", "SheetWidth", "0", false, "numeric(15,4)"),
        new("T_PlateMold", "SheetFlow", "0", false, "numeric(15,4)"),
        new("T_PlateMold", "BladeWidth", "0", false, "numeric(15,4)"),
        new("T_PlateMold", "BladeFlow", "0", false, "numeric(15,4)"),
        new("T_PlateMold", "CompositionQty", "0", false, "integer"),
        new("T_PlateMold", "MfgQty", "1", false, "integer"),
        new("T_PlateMold", "ColorQty", "0", false, "integer"),
        new("T_PlateMold", "BookQty", "0", false, "integer"),
        new("T_PlateMold", "EstimateAmount", "0", false, "numeric(15,4)"),
        new("T_PlateMold", "DecisionAmount", "0", false, "numeric(15,4)"),
        new("T_PlateMold", "PurchaseAmount", "0", false, "numeric(15,4)"),
        new("T_PlateMold", "WdQty", "0", false, "integer"),
        new("T_PlateMold", "LimitWdQty", "0", false, "integer"),
        new("T_PlateMold", "AtachInfoSheetFront", "0", false, "boolean"),
        new("T_PlateMold", "AtachInfoSheetBack", "0", false, "boolean"),
        new("T_PlateMold", "AtachInfoActual", "0", false, "boolean"),
        new("T_PlateMold", "AtachInfoBaseplate", "0", false, "boolean"),
        new("T_PlateMold", "AtachInfoPositive", "0", false, "boolean"),
        new("T_PlateMold", "AtachInfoNegative", "0", false, "boolean"),
        new("T_PlateMold", "AtachInfoMo", "0", false, "boolean"),
        new("T_PlateMold", "AtachInfoFd", "0", false, "boolean"),
        new("T_PlateMold", "NeedDraft", "0", false, "integer"),
        new("T_PlateMold", "NeedMylar", "0", false, "integer"),
        new("T_PlateMold", "NeedGalley", "0", false, "integer"),
        new("T_PlateMold", "NeedProof", "0", false, "integer"),
        new("T_PlateMold", "NeedBlueprint", "0", false, "integer"),
        new("T_PlateMold", "NeedComp", "0", false, "integer"),
        new("T_PlateMold", "NeedDesignSheet", "0", false, "integer"),
        new("T_PlateMold", "Status", "0", false, "integer"),
        new("T_PlateMold", "McTransferFlg", "0", false, "boolean"),
        new("T_PlateMold", "CreateDate", "GETDATE()", true, "timestamp without time zone"),
        new("T_PlateMold", "IsDeleted", "0", false, "boolean"),
    ]);

    public static IReadOnlyList<UniqueRule> UniqueRules { get; } = Array.AsReadOnly<UniqueRule>(
    [
        new("T_OrderProcess", "UX_T_OrderProcess_Pk", Array.AsReadOnly<string>(["WebOrderNo", "WebOrderDetailNo", "ProductCd", "OperationCd"])),
        new("T_OrderProcessNote", "UX_T_OrderProcessNote_Pk", Array.AsReadOnly<string>(["WebOrderNo", "WebOrderDetailNo", "ProductCd", "OperationCd"])),
        new("T_OrderMaterial", "UX_T_OrderMaterial_Pk", Array.AsReadOnly<string>(["WebOrderNo", "WebOrderDetailNo", "ProductCd", "ProcessCd", "MaterialCd"])),
        new("T_PlateMold", "UX_T_PlateMold_NoRev", Array.AsReadOnly<string>(["WdPtnNo", "WdRev"])),
    ]);

    public static string InstallSql { get; } = BuildInstallSql();

    private static string BuildInstallSql()
    {
        if (DefaultRules.Count != 65 || DefaultRules.Select(r => (r.Table, r.Column)).Distinct().Count() != 65 ||
            DefaultRules.Select(r => r.Table).Distinct().Count() != 8 || UniqueRules.Count != 4)
            throw new InvalidOperationException("The frozen restored-order manifest is incomplete or duplicated.");

        var sql = new StringBuilder();
        foreach (var rule in DefaultRules)
            sql.Append("ALTER TABLE public.").Append(Quote(rule.Table)).Append(" ALTER COLUMN ")
                .Append(Quote(rule.Column)).Append(" SET DEFAULT ").Append(Expression(rule)).AppendLine(";");
        foreach (var rule in UniqueRules)
        {
            if (rule.Columns.Count == 0 || rule.Columns.Contains("TenantId"))
                throw new InvalidOperationException("Restored-order uniqueness must retain its original global columns.");
            sql.Append("CREATE UNIQUE INDEX ").Append(Quote(rule.Name)).Append(" ON public.")
                .Append(Quote(rule.Table)).Append('(').AppendJoin(',', rule.Columns.Select(Quote))
                .AppendLine(") NULLS NOT DISTINCT;");
        }
        return sql.ToString();
    }

    private static string Expression(DefaultRule rule)
    {
        if (rule.IsClock)
        {
            if (rule.Literal != "GETDATE()" || rule.PostgreSqlType != "timestamp without time zone")
                throw new InvalidOperationException("Unclassified restored-order clock default or timestamp type.");
            // GETDATE is evaluated at execution time, including inside a long transaction.
            return "clock_timestamp()::timestamp without time zone";
        }

        return (rule.PostgreSqlType, rule.Literal) switch
        {
            ("boolean", "0") => "FALSE",
            ("boolean", "1") => "TRUE",
            ("integer" or "numeric(21,8)" or "numeric(15,4)", "0" or "1") => rule.Literal,
            ("bpchar", "N'3'") => "'3'::bpchar",
            ("bpchar", "N'1'") => "'1'::bpchar",
            _ => throw new InvalidOperationException("Unclassified restored-order default literal or PostgreSQL type."),
        };
    }

    private static string Quote(string identifier)
    {
        if (identifier.Length == 0 || identifier.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Unclassified frozen restored-order identifier.");
        return '"' + identifier + '"';
    }
}
