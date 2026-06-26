using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 采购价表（供应商×物料 的阶梯价 + 有效期）。采购 章01 §3/§4。
/// 建 PO 时按"满足 MinQty≤数量 ∧ 当时有效"取 MinQty 最大的那档单价（<see cref="ISupplierPriceService.ResolvePriceAsync"/>）。
/// 供应商主数据复用 <see cref="CP6.Entity.DomainModels.Erp.BusinessPartner"/> 发注先 Tab，本表只补"价格"维度。
/// </summary>
/// <remarks>
/// 多租户：继承 <see cref="BaseBizEntity"/>，TenantId 由 SaveChanges 自动盖、查询全局过滤；
/// 唯一索引 (SupplierId,ItemId,MinQty,ValidFrom) 在 CP6Context 自动升级为 (TenantId,...) 复合唯一。
/// </remarks>
[Table("Pur_SupplierPrice")]
public class SupplierPrice : BaseBizEntity, IAuditable
{
    /// <summary>供应商 CD（= BusinessPartner.BpCd，发注先）</summary>
    [Required, MaxLength(20)]
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>物料 CD（= Product.ProductCd，业务键引用，不建 FK）</summary>
    [Required, MaxLength(40)]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>单价（原币）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal Price { get; set; }

    /// <summary>币种 CD（ISO，null/JPY=本位币，与供应商 CurrencyCd 一致）</summary>
    [MaxLength(3)]
    public string? CurrencyCd { get; set; }

    /// <summary>阶梯起始量（满足 MinQty≤采购数量 才适用本档；取满足者中 MinQty 最大）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal MinQty { get; set; } = 0m;

    /// <summary>生效日（含）</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>失效日（含，null=长期有效）</summary>
    public DateTime? ValidTo { get; set; }

    /// <summary>价格来源：manual=手工维护 / rfq=询价比价回写（采购 章06）</summary>
    [MaxLength(10)]
    public string? Source { get; set; } = "manual";
}
