using System.Text.Json;

namespace CP6.Core.Services.Wf;

public sealed record SubmitFormCommand(
    string FormKey,
    Guid ActorId,
    string SubmissionKey,
    JsonElement Data,
    Guid? DraftId);

public sealed record SubmitFormResult(
    Guid FormDataId,
    Guid FormDefVersionId,
    int FormVersion,
    Guid? FlowInstanceId,
    Guid? FlowDefVersionId,
    int? FlowVersion);
