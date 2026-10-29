using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;

namespace CP6.Core.Services.Wms;

public sealed class ReturnResaleOwnerV2(CP6Context db,OperationsOwnerV1 owner,ReturnQualityOwnerV1 quality,ReturnDispositionPolicyV2 policy,StockMovementService stock,StockQcService qc)
    : ReturnDispositionOwnerV2(db,owner,quality,policy,stock,qc) { protected override string Kind => "RESALE_STOCK"; }
public sealed class ReturnRepairTransferOwnerV2(CP6Context db,OperationsOwnerV1 owner,ReturnQualityOwnerV1 quality,ReturnDispositionPolicyV2 policy,StockMovementService stock,StockQcService qc)
    : ReturnDispositionOwnerV2(db,owner,quality,policy,stock,qc) { protected override string Kind => "REPAIR"; }
public sealed class ReturnScrapOwnerV2(CP6Context db,OperationsOwnerV1 owner,ReturnQualityOwnerV1 quality,ReturnDispositionPolicyV2 policy,StockMovementService stock,StockQcService qc)
    : ReturnDispositionOwnerV2(db,owner,quality,policy,stock,qc) { protected override string Kind => "SCRAP"; }
public sealed class SupplierReturnShipmentOwnerV2(CP6Context db,OperationsOwnerV1 owner,ReturnQualityOwnerV1 quality,ReturnDispositionPolicyV2 policy,StockMovementService stock,StockQcService qc)
    : ReturnDispositionOwnerV2(db,owner,quality,policy,stock,qc) { protected override string Kind => "SUPPLIER_RETURN"; }
public sealed class CustomerReturnShipmentOwnerV2(CP6Context db,OperationsOwnerV1 owner,ReturnQualityOwnerV1 quality,ReturnDispositionPolicyV2 policy,StockMovementService stock,StockQcService qc)
    : ReturnDispositionOwnerV2(db,owner,quality,policy,stock,qc) { protected override string Kind => "RETURN_TO_CUSTOMER"; }
