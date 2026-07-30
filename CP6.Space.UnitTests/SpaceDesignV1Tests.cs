using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceDesignV1Tests
{
    private const string Hash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Idempotency_record_keeps_replay_and_retention_boundaries()
    {
        var tenantId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        var replayUntil = new DateTime(
            2026,
            7,
            31,
            12,
            0,
            0,
            DateTimeKind.Utc);

        var record = SpaceIdempotencyRecord.Create(
            tenantId,
            principalId,
            "create-version:site",
            Hash.ToUpperInvariant(),
            Hash,
            """{"id":"result"}""",
            202,
            replayUntil,
            replayUntil.AddDays(89));

        Assert.Equal(tenantId, record.TenantId);
        Assert.Equal(principalId, record.PrincipalId);
        Assert.Equal(Hash, record.IdempotencyKeyHash);
        Assert.Equal(replayUntil, record.ReplayUntilUtc);
        Assert.Equal(replayUntil.AddDays(89), record.RetainUntilUtc);
    }

    [Fact]
    public void Idempotency_record_rejects_invalid_hash_json_and_retention()
    {
        var utc = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() =>
            SpaceIdempotencyRecord.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "operation",
                "not-a-hash",
                Hash,
                "{}",
                201,
                utc,
                utc.AddDays(1)));
        Assert.Throws<ArgumentException>(() =>
            SpaceIdempotencyRecord.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "operation",
                Hash,
                Hash,
                "{",
                201,
                utc,
                utc.AddDays(1)));
        Assert.Throws<ArgumentException>(() =>
            SpaceIdempotencyRecord.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "operation",
                Hash,
                Hash,
                "{}",
                201,
                utc,
                utc.AddSeconds(-1)));
    }

    [Fact]
    public void Problem_exception_exposes_stable_http_contract()
    {
        var problem = new SpaceProblemException(
            SpaceErrorCodes.IdempotencyConflict,
            409,
            "Conflict",
            "The key was reused.",
            "use-new-idempotency-key");

        Assert.Equal(SpaceErrorCodes.IdempotencyConflict, problem.Code);
        Assert.Equal(409, problem.StatusCode);
        Assert.Equal("The key was reused.", problem.Detail);
        Assert.Equal("use-new-idempotency-key", problem.RecoveryAction);
        Assert.False(problem.Retryable);
    }
}
