using System.Text.Json.Serialization;
namespace CP6.Core.Services.Erp;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolOrderLinkCommandV1(Guid OperationId, PlateMoldSourceRefV1 Tool,
    string ExpectedNativeRowVersion, string? OrderDepartment = null);
public sealed record ToolNativeOrderRefV1(Guid OrderId, Guid LineId, string Number, int LineNumber,
    string ArrangeNumber, string OrderRowVersion, string LineRowVersion, bool Deleted);
public sealed record ToolOrderLinkReceiptV1(Guid OperationId, Guid TenantId, PlateMoldSourceRefV1 Tool,
    string NativeToolRowVersion, ToolNativeOrderRefV1 Order, string Effect,
    string SourceKind = "NATIVE_ORDER_LINK_ONLY", string PeState = "NOT_EXECUTED");
public sealed record ToolOrderLinkViewV1(ToolOrderLinkReceiptV1? Original, bool CurrentApplicable, string State);
public sealed record ToolOrderLinkOperationV1(Guid OperationId, string State, ToolOrderLinkReceiptV1? Result = null);
