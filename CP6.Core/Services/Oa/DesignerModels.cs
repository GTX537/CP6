namespace CP6.Core.Services.Oa;

public record SaveFlowRequest(string FlowKey, string FlowName, string FormKey,
    string? FunctionId, string? FlowCode, string SchemaJson);
public record CloneRequest(string SourceFlowKey, string NewFlowKey, string NewFlowName);
public record FlowDefSummary(string FlowKey, string FlowName, string FormKey,
    string? FunctionId, string? FlowCode, int Version, bool Enable);
