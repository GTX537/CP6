using System.Runtime.CompilerServices;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;

namespace CP6.Core.Services.Oa;

/// <summary>Closed deployment identities. SQL identifiers and credential destinations are
/// selected here, never from a business body, receipt URL or arbitrary configuration string.</summary>
public sealed class ApprovalBusinessProfileV2
{
    public const string ToolOwner="Main.Wms.ToolDisposition.v1",DocumentOwner="Main.Plm.ControlledDocument.v1";
    public const string RequirementSetOwner="Main.Plm.RequirementSet.v1";
    public string Owner {get;}
    public string BizType=>Owner==ToolOwner?"WMS_TOOL_DISCARD":Owner==DocumentOwner?"PLM_DOCUMENT_REVIEW":"PLM_REQUIREMENT_SET_REVIEW";
    public string SubjectKind=>Owner==ToolOwner?"TOOL_DISPOSITION_REQUEST":Owner==DocumentOwner?"DOCUMENT_REVISION":"REQUIREMENT_SET_REVISION";
    public string DecisionRoute=>Owner==ToolOwner?"/internal/wms/approval-results/v2":Owner==DocumentOwner?"/internal/plm/document-review-results/v1":"/internal/plm/requirement-set-review-results/v1";
    public string ApplicationRoute=>Owner==ToolOwner?"/internal/wms/approval-application-streams/":Owner==DocumentOwner?"/internal/plm/document-review-application-streams/v1/":"/internal/plm/requirement-set-review-application-streams/v1/";
    private ApprovalBusinessProfileV2(string owner){Owner=owner;}
    public static ApprovalBusinessProfileV2 For(string owner)=>owner is ToolOwner or DocumentOwner or RequirementSetOwner
        ?new(owner):throw new ScopeAuthorizationExceptionV1("BUSINESS_EVIDENCE_OWNER_UNAVAILABLE",422);
    internal string SubmissionText(string sql)=>Owner==ToolOwner?sql:sql.Replace("cp6_tool_approval_v1",Owner==DocumentOwner?"cp6_document_review_v1":"cp6_requirement_set_review_v1",StringComparison.Ordinal)
        .Replace("'WMS_TOOL_DISCARD'","'"+BizType+"'",StringComparison.Ordinal).Replace("'TOOL_DISPOSITION_REQUEST'","'"+SubjectKind+"'",StringComparison.Ordinal);
    internal FormattableString SubmissionSql(FormattableString sql)=>FormattableStringFactory.Create(SubmissionText(sql.Format),sql.GetArguments());
    internal string FactText(string sql)=>Owner==ToolOwner?sql:sql.Replace("cp6_tool_approval_fact_v2",Owner==DocumentOwner?"cp6_document_review_fact_v1":"cp6_requirement_set_review_fact_v1",StringComparison.Ordinal)
        .Replace("cp6_tool_application_v2",Owner==DocumentOwner?"cp6_document_review_application_v1":"cp6_requirement_set_review_application_v1",StringComparison.Ordinal);
    internal FormattableString FactSql(FormattableString sql)=>FormattableStringFactory.Create(FactText(sql.Format),sql.GetArguments());
    internal Task FenceSubmission(CP6Context db,Guid environment,CancellationToken ct)=>Owner==ToolOwner
        ?new ToolDispositionApprovalStoreV1(db,environment).Fence(false,ct)
        :Owner==DocumentOwner?new ControlledDocumentReviewStoreV1(db,environment).Fence(false,ct):new RequirementSetReviewStoreV1(db,environment).Fence(false,ct);
}
