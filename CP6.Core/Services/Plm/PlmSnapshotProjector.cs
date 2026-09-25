using CP6.Entity.DomainModels.Erp;

namespace CP6.Core.Services.Plm;

public sealed record PlmField(string Name, object? Value, string Kind, int Limit = 0, int Precision = 0, bool Required = false);
public sealed record PlmProjectedItem(
    string ItemType, string SourceOwnerType,
    IReadOnlyList<(string Name, string Value)> Identity,
    IReadOnlyList<PlmField> Content,
    Guid SourceOwnerId, byte[]? SourceRowVersion,
    string ProductCd, string? ProcessCd, string? MaterialCd, string? TaskCd, int? SortOrder);

public static class PlmSnapshotProjector
{
    public static PlmProjectedItem Product(ProductMaster x) => new(
        "PRODUCT", "ProductMaster",
        [("productCd", x.ProductCd)],
        [
            new("itemCd", x.ItemCd, "string", 15, 0, true),
            new("setProductCd", x.SetProductCd, "string", 20, 0, true),
            new("parentChildDiv", x.ParentChildDiv, "string", 1, 0, true),
            new("setRatio", x.SetRatio, "decimal", 0, 18, true),
            new("cpItemName1", x.CpItemName1, "string", 100, 0, false),
            new("cpItemName2", x.CpItemName2, "string", 100, 0, false),
            new("productShape", x.ProductShape, "string", 4, 0, false),
            new("adShape", x.AdShape, "string", 4, 0, false),
            new("qualityDiv", x.QualityDiv, "string", 4, 0, false),
            new("shipInspection", x.ShipInspection, "string", 4, 0, false),
            new("sheetFlute", x.SheetFlute, "string", 4, 0, false),
            new("paperCdF", x.PaperCdF, "string", 20, 0, false),
            new("printCdF", x.PrintCdF, "string", 20, 0, false),
            new("embossCdF", x.EmbossCdF, "string", 20, 0, false),
            new("makerCdF", x.MakerCdF, "string", 20, 0, false),
            new("paperCdC", x.PaperCdC, "string", 20, 0, false),
            new("printCdC", x.PrintCdC, "string", 20, 0, false),
            new("embossCdC", x.EmbossCdC, "string", 20, 0, false),
            new("paperCdB", x.PaperCdB, "string", 20, 0, false),
            new("printCdB", x.PrintCdB, "string", 20, 0, false),
            new("embossCdB", x.EmbossCdB, "string", 20, 0, false),
            new("makerCdB", x.MakerCdB, "string", 20, 0, false),
            new("sheetPrint", x.SheetPrint, "string", 20, 0, false),
            new("bladeWidth", x.BladeWidth, "decimal", 0, 21, false),
            new("bladeFlow", x.BladeFlow, "decimal", 0, 21, false),
            new("gutterFb", x.GutterFb, "decimal", 0, 21, false),
            new("gutterLr", x.GutterLr, "decimal", 0, 21, false),
            new("sheetDimW", x.SheetDimW, "decimal", 0, 21, false),
            new("sheetDimF", x.SheetDimF, "decimal", 0, 21, false),
            new("finalMachineProcess", x.FinalMachineProcess, "string", 20, 0, false),
            new("qtyUnit", x.QtyUnit, "string", 4, 0, false),
            new("trackingMode", x.TrackingMode, "integer", 0, 0, true),
            new("mfgNote", x.MfgNote, "string", 100, 0, false),
            new("printNote", x.PrintNote, "string", 100, 0, false),
            new("fscProductDiv", x.FscProductDiv, "string", 4, 0, false),
            new("fscMaterialDiv", x.FscMaterialDiv, "string", 4, 0, false),
            new("fscManagementNo", x.FscManagementNo, "string", 20, 0, false),
            new("foodSafety", x.FoodSafety, "string", 4, 0, false),
            new("fourMContract", x.FourMContract, "string", 4, 0, false),
            new("paperUsageG", x.PaperUsageG, "decimal", 0, 21, false),
            new("plasticUsageG", x.PlasticUsageG, "decimal", 0, 21, false),
            new("glassUsageG", x.GlassUsageG, "decimal", 0, 21, false),
            new("petUsageG", x.PetUsageG, "decimal", 0, 21, false),
            new("packPaperUsageG", x.PackPaperUsageG, "decimal", 0, 21, false),
            new("packPlasticUsageG", x.PackPlasticUsageG, "decimal", 0, 21, false),
        ],
        x.Id, x.RowVersion, x.ProductCd, null, null, null, null);

    public static PlmProjectedItem Bom(ProductMaterial x) => new(
        "BOM", "ProductMaterial",
        [("productCd", x.ProductCd), ("processCd", x.ProcessCd), ("materialCd", x.MaterialCd)],
        [
            new("materialTypeDiv", x.MaterialTypeDiv, "string", 1, 0, true),
            new("itemCd", x.ItemCd, "string", 15, 0, false),
            new("branch1", x.Branch1, "string", 10, 0, false),
            new("branch2", x.Branch2, "string", 10, 0, false),
            new("branch3", x.Branch3, "string", 10, 0, false),
            new("usageType", x.UsageType, "integer", 0, 0, true),
            new("unitUsage", x.UnitUsage, "decimal", 0, 21, false),
            new("usageUnit", x.UsageUnit, "string", 10, 0, false),
            new("sortOrder", x.SortOrder, "integer", 0, 0, true),
        ],
        x.Id, x.RowVersion, x.ProductCd, x.ProcessCd, x.MaterialCd, null, x.SortOrder);

    public static PlmProjectedItem Routing(ProductProcess x) => new(
        "ROUTING", "ProductProcess",
        [("productCd", x.ProductCd), ("taskCd", x.TaskCd)],
        [
            new("processCd", x.ProcessCd, "string", 10, 0, true),
            new("topItemCd", x.TopItemCd, "string", 15, 0, false),
            new("topBranch1", x.TopBranch1, "string", 10, 0, false),
            new("topBranch2", x.TopBranch2, "string", 10, 0, false),
            new("topBranch3", x.TopBranch3, "string", 10, 0, false),
            new("itemCd", x.ItemCd, "string", 15, 0, false),
            new("branch1", x.Branch1, "string", 10, 0, false),
            new("branch2", x.Branch2, "string", 10, 0, false),
            new("branch3", x.Branch3, "string", 10, 0, false),
            new("wgCd", x.WgCd, "string", 10, 0, false),
            new("machineOrVendor", x.MachineOrVendor, "string", 20, 0, false),
            new("machineFixedFlg", x.MachineFixedFlg, "boolean", 0, 0, true),
            new("spec01", x.Spec01, "string", 20, 0, false),
            new("spec02", x.Spec02, "string", 20, 0, false),
            new("spec03", x.Spec03, "string", 20, 0, false),
            new("spec04", x.Spec04, "string", 20, 0, false),
            new("spec05", x.Spec05, "string", 20, 0, false),
            new("spec06", x.Spec06, "string", 20, 0, false),
            new("spec07", x.Spec07, "string", 20, 0, false),
            new("spec08", x.Spec08, "string", 20, 0, false),
            new("spec09", x.Spec09, "string", 20, 0, false),
            new("spec10", x.Spec10, "string", 20, 0, false),
            new("plateNo1", x.PlateNo1, "string", 20, 0, false),
            new("plateNo2", x.PlateNo2, "string", 20, 0, false),
            new("plateNo3", x.PlateNo3, "string", 20, 0, false),
            new("consumable1", x.Consumable1, "string", 32, 0, false),
            new("consumable2", x.Consumable2, "string", 32, 0, false),
            new("consumable3", x.Consumable3, "string", 32, 0, false),
            new("lossRate", x.LossRate, "decimal", 0, 21, false),
            new("machineCount", x.MachineCount, "decimal", 0, 21, false),
            new("leadTime", x.LeadTime, "decimal", 0, 21, false),
            new("setupHour", x.SetupHour, "decimal", 0, 21, false),
            new("cycleTime", x.CycleTime, "decimal", 0, 21, false),
            new("standardCrewSize", x.StandardCrewSize, "decimal", 0, 21, false),
            new("processNote1", x.ProcessNote1, "string", 100, 0, false),
            new("processNote2", x.ProcessNote2, "string", 100, 0, false),
            new("sortOrder", x.SortOrder, "integer", 0, 0, true),
        ],
        x.Id, x.RowVersion, x.ProductCd, null, null, x.TaskCd, x.SortOrder);

    public static IReadOnlyList<PlmField> Schema(string type) => type switch
    {
        "PRODUCT" => Product(new ProductMaster()).Content,
        "BOM" => Bom(new ProductMaterial()).Content,
        "ROUTING" => Routing(new ProductProcess()).Content,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
