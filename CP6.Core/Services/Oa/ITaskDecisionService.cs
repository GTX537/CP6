using System.Text.Json;

namespace CP6.Core.Services.Oa;

public sealed record TaskDecisionCommand(
    Guid TaskId,
    Guid ActualUserId,
    Guid EffectiveUserId,
    string Decision,
    string? Comment,
    JsonElement DataPatch,
    byte[]? ExpectedFormDataRowVersion);

public sealed record TaskDecisionResult(Guid TaskId, Guid InstanceId, int TaskStatus, byte[]? FormDataRowVersion);

public interface ITaskDecisionService
{
    Task<TaskDecisionResult> DecideAsync(TaskDecisionCommand command, CancellationToken ct = default);
}
