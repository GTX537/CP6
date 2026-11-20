using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;

namespace CP6.Core.Services.Erp;

internal static class BusinessPartnerProfileFieldsV1
{
    internal sealed record Field(string Original, string Name, string Type, bool Writable,
        bool Sensitive = false, int? Length = null, int? Precision = null, int? Scale = null, bool Dictionary = false)
    {
        private PropertyInfo Column { get; } = typeof(BusinessPartner).GetProperty(Original)
            ?? throw new InvalidOperationException($"交易伙伴字段 {Original} 缺少持久化列。");

        internal bool Nullable => Type != "BOOL";
        internal string? SourceOwner => Dictionary ? BusinessPartnerWorkspaceV1.DirectoryOwner : null;

        internal object? Value(BusinessPartner row) => Column.GetValue(row) switch
        {
            decimal number => number.ToString("0.####", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            var value => value
        };

        internal void Assign(BusinessPartner row, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null && Nullable)
            {
                Column.SetValue(row, null);
                return;
            }
            var cooked = Type switch
            {
                "BOOL" when value.ValueKind is JsonValueKind.True or JsonValueKind.False => (object)value.GetBoolean(),
                "INT" => ParseInteger(value),
                "DECIMAL" => ParseDecimal(value),
                "TIME" => ParseTime(value),
                "STRING" => ParseString(value),
                _ => throw BusinessPartnerWorkspaceV1.Invalid("FIELD_INVALID")
            };
            Column.SetValue(row, cooked);
        }

        private object? ParseString(JsonElement value)
        {
            var text = BusinessPartnerWorkspaceV1.OptionalText(value, Length!.Value);
            if (text is null) return null;
            if (Name == "zipCd" && !(text.Length == 7 && text.All(char.IsAsciiDigit) ||
                text.Length == 8 && text[3] == '-' && text[..3].All(char.IsAsciiDigit) && text[4..].All(char.IsAsciiDigit)) ||
                Name is "tel" or "fax" && text.Any(c => !char.IsAsciiDigit(c) && c is not (' ' or '+' or '-' or '(' or ')')))
                throw BusinessPartnerWorkspaceV1.Invalid("FIELD_INVALID");
            return text;
        }

        private object? ParseTime(JsonElement value)
        {
            var text = BusinessPartnerWorkspaceV1.OptionalText(value, 8);
            if (text is not null && !TimeOnly.TryParseExact(text, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw BusinessPartnerWorkspaceV1.Invalid("FIELD_INVALID");
            return text;
        }

        private object ParseInteger(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Number ||
                !int.TryParse(value.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out var i) ||
                Name.EndsWith("Day1", StringComparison.Ordinal) && i is not (>= 1 and <= 31 or 99) ||
                Name == "collectionPlannedDay" && i is not (>= 1 and <= 31 or 99) ||
                Name == "paymentScheduleDelayDays" && i > 366)
                throw BusinessPartnerWorkspaceV1.Invalid("FIELD_INVALID");
            return i;
        }

        private object ParseDecimal(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String) throw BusinessPartnerWorkspaceV1.Invalid("FIELD_INVALID");
            var s = value.GetString()!;
            var parts = s.Split('.');
            if (parts.Length is < 1 or > 2 || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)) ||
                parts[0].Length > Precision!.Value - Scale!.Value ||
                parts[0].Length > 1 && parts[0][0] == '0' ||
                parts.Length == 2 && (parts[1].Length > Scale.Value || parts[1][^1] == '0') ||
                !decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                throw BusinessPartnerWorkspaceV1.Invalid("FIELD_INVALID");
            return number;
        }
    }

    // 来源：ERP-BP-01 的 F001–F143。按原序保留列名、wire 名、类型、容量和写入责任。
    internal static readonly Field[] Original =
    [
        new("StdCoCd", "stdCoCd", "STRING", true, Length: 20), // F001
        new("Ein", "ein", "STRING", true, Sensitive: true, Length: 13), // F002
        new("EinType", "einType", "STRING", true, Sensitive: true, Length: 2), // F003
        new("LocalPublicCd", "localPublicCd", "STRING", true, Length: 5, Dictionary: true), // F004
        new("DenzaiNo", "denzaiNo", "STRING", true, Length: 9), // F005
        new("ZipCd", "zipCd", "STRING", true, Length: 10), // F006
        new("Addr1", "addr1", "STRING", true, Sensitive: true, Length: 40), // F007
        new("Addr2", "addr2", "STRING", true, Sensitive: true, Length: 40), // F008
        new("Addr3", "addr3", "STRING", true, Sensitive: true, Length: 40), // F009
        new("Addr4", "addr4", "STRING", true, Sensitive: true, Length: 40), // F010
        new("Tel", "tel", "STRING", true, Sensitive: true, Length: 30), // F011
        new("Fax", "fax", "STRING", true, Sensitive: true, Length: 30), // F012
        new("AreaCd", "areaCd", "STRING", true, Length: 3, Dictionary: true), // F013
        new("SalesStaffCd", "salesStaffCd", "STRING", true, Length: 20, Dictionary: true), // F014
        new("BusinessStaffCd", "businessStaffCd", "STRING", true, Length: 20, Dictionary: true), // F015
        new("BpClass01", "bpClass01", "STRING", true, Length: 10, Dictionary: true), // F016
        new("BpClass02", "bpClass02", "STRING", true, Length: 10, Dictionary: true), // F017
        new("BpClass03", "bpClass03", "STRING", true, Length: 10, Dictionary: true), // F018
        new("BpClass04", "bpClass04", "STRING", true, Length: 10, Dictionary: true), // F019
        new("BpClass05", "bpClass05", "STRING", true, Length: 10, Dictionary: true), // F020
        new("BpClass06", "bpClass06", "STRING", true, Length: 10, Dictionary: true), // F021
        new("BpClass07", "bpClass07", "STRING", true, Length: 10, Dictionary: true), // F022
        new("BpClass08", "bpClass08", "STRING", true, Length: 10, Dictionary: true), // F023
        new("BpClass09", "bpClass09", "STRING", true, Length: 10, Dictionary: true), // F024
        new("BpClass10", "bpClass10", "STRING", true, Length: 10, Dictionary: true), // F025
        new("SalesAnalysis1", "salesAnalysis1", "STRING", true, Length: 10, Dictionary: true), // F026
        new("SalesAnalysis2", "salesAnalysis2", "STRING", true, Length: 10, Dictionary: true), // F027
        new("SalesAnalysis3", "salesAnalysis3", "STRING", true, Length: 10, Dictionary: true), // F028
        new("ParentCustomerCd", "parentCustomerCd", "STRING", false, Length: 20), // F029
        new("UserConverterDiv", "userConverterDiv", "STRING", true, Length: 4, Dictionary: true), // F030
        new("AccountsReceivableCd", "accountsReceivableCd", "STRING", false, Length: 20), // F031
        new("CreditMgmtCd", "creditMgmtCd", "STRING", false, Length: 20), // F032
        new("CustomerDept", "customerDept", "STRING", true, Length: 50), // F033
        new("CustomerContact", "customerContact", "STRING", true, Sensitive: true, Length: 50), // F034
        new("CustomerContactTitle", "customerContactTitle", "STRING", true, Sensitive: true, Length: 20), // F035
        new("RecyclingTarget", "recyclingTarget", "STRING", true, Length: 4, Dictionary: true), // F036
        new("SalesPostingDiv", "salesPostingDiv", "STRING", true, Length: 4, Dictionary: true), // F037
        new("SheetSalesCalcMethod", "sheetSalesCalcMethod", "STRING", true, Length: 4, Dictionary: true), // F038
        new("SalesCalcDivM2", "salesCalcDivM2", "STRING", true, Length: 4), // F039
        new("SalesCalcDivPiece", "salesCalcDivPiece", "STRING", true, Length: 4), // F040
        new("FractionCalcDiv", "fractionCalcDiv", "STRING", true, Length: 4, Dictionary: true), // F041
        new("FullSheetSalesDiv", "fullSheetSalesDiv", "STRING", true, Length: 4, Dictionary: true), // F042
        new("SlitterBillingDiv", "slitterBillingDiv", "STRING", true, Length: 4, Dictionary: true), // F043
        new("SlitterBillingUnitPrice", "slitterBillingUnitPrice", "DECIMAL", true, Precision: 18, Scale: 4), // F044
        new("SlitterMaxFlow", "slitterMaxFlow", "DECIMAL", true, Precision: 18, Scale: 4), // F045
        new("PrintMinBilling", "printMinBilling", "STRING", true, Length: 4), // F046
        new("PrintMinBillingBelow", "printMinBillingBelow", "DECIMAL", true, Precision: 18, Scale: 4), // F047
        new("PrintMinBillingUnit", "printMinBillingUnit", "STRING", true, Length: 4, Dictionary: true), // F048
        new("LaminateMinBilling", "laminateMinBilling", "STRING", true, Length: 4), // F049
        new("LaminateMinBillingBelow", "laminateMinBillingBelow", "DECIMAL", true, Precision: 18, Scale: 4), // F050
        new("LaminateMinBillingUnit", "laminateMinBillingUnit", "STRING", true, Length: 4, Dictionary: true), // F051
        new("LaminateAddRate", "laminateAddRate", "DECIMAL", true, Sensitive: true, Precision: 18, Scale: 4), // F052
        new("LaminateAddDisplay", "laminateAddDisplay", "STRING", true, Length: 4), // F053
        new("ProcessingMinEstimate", "processingMinEstimate", "STRING", true, Length: 4), // F054
        new("NewSheetUnitPriceBase", "newSheetUnitPriceBase", "STRING", true, Length: 4, Dictionary: true), // F055
        new("DeliverySlipOutDiv", "deliverySlipOutDiv", "STRING", true, Length: 4, Dictionary: true), // F056
        new("DeliverySlipIssueDiv", "deliverySlipIssueDiv", "STRING", true, Length: 4, Dictionary: true), // F057
        new("SpecialSlipDiv", "specialSlipDiv", "STRING", true, Length: 4, Dictionary: true), // F058
        new("NightLoadDiv", "nightLoadDiv", "STRING", true, Length: 4, Dictionary: true), // F059
        new("SizePrintDiv", "sizePrintDiv", "STRING", true, Length: 4, Dictionary: true), // F060
        new("DeliveryCalcOutDiv", "deliveryCalcOutDiv", "STRING", true, Length: 4, Dictionary: true), // F061
        new("DeliveryCalcIssueDiv", "deliveryCalcIssueDiv", "STRING", true, Length: 4, Dictionary: true), // F062
        new("DeliveryCalcOutDiv2", "deliveryCalcOutDiv2", "STRING", true, Length: 4), // F063
        new("DeliveryCalcAddressee", "deliveryCalcAddressee", "STRING", true, Sensitive: true, Length: 100), // F064
        new("DeliveryCalcSender", "deliveryCalcSender", "STRING", true, Length: 50), // F065
        new("DeliveryCalcZipCd", "deliveryCalcZipCd", "STRING", true, Length: 10, Dictionary: true), // F066
        new("DeliveryCalcAddr1", "deliveryCalcAddr1", "STRING", true, Sensitive: true, Length: 40), // F067
        new("DeliveryCalcAddr2", "deliveryCalcAddr2", "STRING", true, Sensitive: true, Length: 40), // F068
        new("DeliveryCalcAddr3", "deliveryCalcAddr3", "STRING", true, Sensitive: true, Length: 40), // F069
        new("DeliveryCalcAddr4", "deliveryCalcAddr4", "STRING", true, Sensitive: true, Length: 40), // F070
        new("BillingCd", "billingCd", "STRING", false, Length: 20), // F071
        new("BillingName", "billingName", "STRING", false, Length: 100), // F072
        new("ReceiptCd", "receiptCd", "STRING", false, Length: 20), // F073
        new("ReceiptName", "receiptName", "STRING", false, Length: 100), // F074
        new("CreditMgmtArCd", "creditMgmtArCd", "STRING", false, Length: 20), // F075
        new("BillingClosingDay1", "billingClosingDay1", "INT", true), // F076
        new("BillingPrintDiv", "billingPrintDiv", "STRING", true, Length: 4, Dictionary: true), // F077
        new("BillingSealDiv", "billingSealDiv", "STRING", true, Length: 4, Dictionary: true), // F078
        new("ElectronicBilling", "electronicBilling", "STRING", true, Length: 4), // F079
        new("BillingAddressee", "billingAddressee", "STRING", true, Sensitive: true, Length: 100), // F080
        new("BillingSender", "billingSender", "STRING", true, Length: 50), // F081
        new("BillingZipCd", "billingZipCd", "STRING", true, Length: 10, Dictionary: true), // F082
        new("BillingAddr1", "billingAddr1", "STRING", true, Sensitive: true, Length: 40), // F083
        new("BillingAddr2", "billingAddr2", "STRING", true, Sensitive: true, Length: 40), // F084
        new("BillingAddr3", "billingAddr3", "STRING", true, Sensitive: true, Length: 40), // F085
        new("BillingAddr4", "billingAddr4", "STRING", true, Sensitive: true, Length: 40), // F086
        new("RemittanceName", "remittanceName", "STRING", true, Sensitive: true, Length: 100), // F087
        new("BankCd", "bankCd", "STRING", false, Sensitive: true, Length: 20), // F088
        new("BankBranchCd", "bankBranchCd", "STRING", false, Sensitive: true, Length: 20), // F089
        new("RemittanceAccount", "remittanceAccount", "STRING", false, Sensitive: true, Length: 50), // F090
        new("TempAccountDiv", "tempAccountDiv", "STRING", true, Length: 4, Dictionary: true), // F091
        new("MainAccountRegDate", "mainAccountRegDate", "DATE", false), // F092
        new("Drawer", "drawer", "STRING", true, Sensitive: true, Length: 50), // F093
        new("CollectionDiv", "collectionDiv", "STRING", true, Length: 4, Dictionary: true), // F094
        new("CollectionPlannedDay", "collectionPlannedDay", "INT", true), // F095
        new("BillRotationDiv", "billRotationDiv", "STRING", true, Length: 4, Dictionary: true), // F096
        new("ReceiptAddressee", "receiptAddressee", "STRING", true, Sensitive: true, Length: 100), // F097
        new("ReceiptSender", "receiptSender", "STRING", true, Length: 50), // F098
        new("ReceiptZipCd", "receiptZipCd", "STRING", true, Length: 10, Dictionary: true), // F099
        new("ReceiptAddr1", "receiptAddr1", "STRING", true, Sensitive: true, Length: 40), // F100
        new("ReceiptAddr2", "receiptAddr2", "STRING", true, Sensitive: true, Length: 40), // F101
        new("ReceiptAddr3", "receiptAddr3", "STRING", true, Sensitive: true, Length: 40), // F102
        new("ReceiptAddr4", "receiptAddr4", "STRING", true, Sensitive: true, Length: 40), // F103
        new("CollectionNote", "collectionNote", "STRING", true, Sensitive: true, Length: 200), // F104
        new("LogisticsGroupCd", "logisticsGroupCd", "STRING", true, Length: 10, Dictionary: true), // F105
        new("LogisticsGroupName", "logisticsGroupName", "STRING", false, Length: 100), // F106
        new("DeliveryDept", "deliveryDept", "STRING", true, Length: 50), // F107
        new("DeliveryContact", "deliveryContact", "STRING", true, Sensitive: true, Length: 50), // F108
        new("DeliveryContactTitle", "deliveryContactTitle", "STRING", true, Sensitive: true, Length: 20), // F109
        new("TruckLengthLimit", "truckLengthLimit", "DECIMAL", true, Precision: 10, Scale: 2), // F110
        new("DeliveryTimeFrom", "deliveryTimeFrom", "TIME", true, Length: 8), // F111
        new("DeliveryTimeTo", "deliveryTimeTo", "TIME", true, Length: 8), // F112
        new("PlannedShipTime", "plannedShipTime", "TIME", true, Length: 8), // F113
        new("SupplierPattern", "supplierPattern", "STRING", true, Length: 4, Dictionary: true), // F114
        new("SubcontractTargetFlg", "subcontractTargetFlg", "BOOL", true), // F115
        new("SubcontractPriceDiv", "subcontractPriceDiv", "STRING", true, Length: 4, Dictionary: true), // F116
        new("SupplyPostingDiv", "supplyPostingDiv", "STRING", true, Length: 4, Dictionary: true), // F117
        new("SupplyPriceChangeAllowFlg", "supplyPriceChangeAllowFlg", "BOOL", true), // F118
        new("DeliveryConfirmFlg", "deliveryConfirmFlg", "BOOL", true), // F119
        new("PurchaseFractionDiv", "purchaseFractionDiv", "STRING", true, Length: 4, Dictionary: true), // F120
        new("PurchaseTaxFractionDiv", "purchaseTaxFractionDiv", "STRING", true, Length: 4, Dictionary: true), // F121
        new("PurchaseTaxCd", "purchaseTaxCd", "STRING", true, Length: 10, Dictionary: true), // F122
        new("PurchaseTaxPriorityFlg", "purchaseTaxPriorityFlg", "BOOL", true), // F123
        new("SupplierCalendarCd", "supplierCalendarCd", "STRING", true, Length: 10, Dictionary: true), // F124
        new("SupplyConsignDiv", "supplyConsignDiv", "STRING", true, Length: 4, Dictionary: true), // F125
        new("PurchaseLotSplitDiv", "purchaseLotSplitDiv", "STRING", true, Length: 4, Dictionary: true), // F126
        new("PurchasePostingDiv", "purchasePostingDiv", "STRING", true, Length: 4, Dictionary: true), // F127
        new("PaidSupplyFractionDiv", "paidSupplyFractionDiv", "STRING", true, Length: 4, Dictionary: true), // F128
        new("PaidSupplyTaxCd", "paidSupplyTaxCd", "STRING", true, Length: 4, Dictionary: true), // F129
        new("PaidSupplyTaxPriorityFlg", "paidSupplyTaxPriorityFlg", "BOOL", true), // F130
        new("PaidSupplyAmountFractionDiv", "paidSupplyAmountFractionDiv", "STRING", true, Length: 4, Dictionary: true), // F131
        new("PaidSupplyTaxFractionDiv", "paidSupplyTaxFractionDiv", "STRING", true, Length: 4, Dictionary: true), // F132
        new("PaidSupplyTaxCalcDiv", "paidSupplyTaxCalcDiv", "STRING", true, Length: 4, Dictionary: true), // F133
        new("FscCertificationDiv", "fscCertificationDiv", "STRING", true, Length: 4, Dictionary: true), // F134
        new("PaymentScheduleCd", "paymentScheduleCd", "STRING", false, Length: 20), // F135
        new("PaymentCd", "paymentCd", "STRING", false, Length: 20), // F136
        new("PaymentScheduleDeptCd", "paymentScheduleDeptCd", "STRING", true, Length: 20, Dictionary: true), // F137
        new("PaidEachTimeFlg", "paidEachTimeFlg", "BOOL", true), // F138
        new("PaymentClosingDay1", "paymentClosingDay1", "INT", true), // F139
        new("PaymentTaxCalcFlg", "paymentTaxCalcFlg", "BOOL", true), // F140
        new("PaymentTaxFractionDiv", "paymentTaxFractionDiv", "STRING", true, Length: 4, Dictionary: true), // F141
        new("BatchPaymentScheduleFlg", "batchPaymentScheduleFlg", "BOOL", true), // F142
        new("PaymentScheduleDelayDays", "paymentScheduleDelayDays", "INT", true), // F143
    ];

    internal static readonly Field[] Writable = Original.Where(field => field.Writable).ToArray();
    internal static readonly IReadOnlyDictionary<string, Field> ByName = Original.ToDictionary(field => field.Name, StringComparer.Ordinal);
    internal static readonly string[] Core = ["BpCd", "BpName", "BpAbbrev", "BaseCd", "CurrencyCd", "Roles"];
    internal static readonly FieldRegistry.Field[] Fields = BusinessPartnerFinanceReferenceReader.Fields
        .Concat(Core.Select(x => new FieldRegistry.Field(x, x)))
        .Concat(Original.Select(x => new FieldRegistry.Field(x.Original, x.Name)))
        .Concat([new("Relations", "交易伙伴关系"), new("FinanceLinks", "财务引用")])
        .DistinctBy(x => x.Name, StringComparer.Ordinal)
        .OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
}
