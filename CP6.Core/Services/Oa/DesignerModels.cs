using System.Collections.Generic;

namespace CP6.Core.Services.Oa;

public record SaveFlowRequest(string FlowKey, string FlowName, string FormKey,
    string? FunctionId, string? FlowCode, string SchemaJson, byte[]? RowVersion = null);
public record CloneRequest(string SourceFlowKey, string NewFlowKey, string NewFlowName);
public record FlowDefSummary(string FlowKey, string FlowName, string? FormKey,
    string? FunctionId, string? FlowCode, int Version, bool Enable);

// 服务目录（P1-6）：设计器拉取可绑定的回写动作 + 连接器。每项 {name, label(DisplayName)}。
public record ServiceCatalogItem(string Name, string Label);
public record ServiceCatalog(
    IReadOnlyList<ServiceCatalogItem> Actions,
    IReadOnlyList<ServiceCatalogItem> Connectors);
