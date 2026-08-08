using System.Text.Json;

namespace CP6.Space.Application;

public sealed record SpaceAiInheritedLockedFact(
    Guid LockedFactId,
    Guid RunId,
    Guid BasedOnRunId,
    Guid SourceProposalId,
    Guid SourceDecisionId,
    string SourceHash,
    string SourceKey,
    string ProposalType,
    string FieldPath,
    JsonElement Value,
    string MatchMethod,
    decimal MatchScore,
    bool IsConfirmed);

public interface ISpaceAiLockedFactService
{
    Task<IReadOnlyList<SpaceAiInheritedLockedFact>> MaterializeAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpaceAiInheritedLockedFact>> GetAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}
