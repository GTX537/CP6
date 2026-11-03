using CP6.Core.Services.Sys;

namespace CP6.Core.Services.Wf;

public sealed record DefinitionDiagnosticV2(string Path,string Code,string Severity="ERROR",
    string? NodeId=null,string? EdgeId=null,string? FieldName=null,string? Message=null);

// The owning validator creates locations while it checks the invariant. Transport
// projects these facts; it must not guess a node from an already aggregated code.
public sealed class DefinitionDiagnosticExceptionV2(string code,DefinitionDiagnosticV2[] diagnostics,Exception? inner=null)
    : InvalidOperationException(code,inner)
{
    public DefinitionDiagnosticV2[] Diagnostics { get; }=diagnostics;
}
public static class DefinitionDiagnosticsV2
{
    public static ScopeAuthorizationExceptionV1 Error(string code,params DefinitionDiagnosticV2[] diagnostics)
        =>new(code,422,new DefinitionDiagnosticExceptionV2(code,diagnostics));
    public static DefinitionDiagnosticV2[] From(ScopeAuthorizationExceptionV1 error)
        =>error.InnerException is DefinitionDiagnosticExceptionV2 detail?detail.Diagnostics:[];
}
