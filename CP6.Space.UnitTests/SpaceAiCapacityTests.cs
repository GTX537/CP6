using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceAiCapacityTests
{
    private static readonly DateTime Now =
        new(2026, 7, 30, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Work_slot_acquires_renews_and_releases_with_fencing()
    {
        var tenantId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var slot = SpaceTenantAiWorkSlot.CreateAvailable(tenantId, 1);

        slot.Acquire(runId, "worker-1", Now, TimeSpan.FromSeconds(60));
        Assert.Equal(runId, slot.RunId);
        Assert.Equal(Now.AddSeconds(60), slot.LeaseExpiresAtUtc);

        slot.Renew(
            runId,
            "worker-1",
            Now.AddSeconds(20),
            TimeSpan.FromSeconds(60));
        Assert.Equal(Now.AddSeconds(80), slot.LeaseExpiresAtUtc);
        Assert.Throws<SpaceAiCapacityLeaseLostException>(() =>
            slot.Release(runId, "worker-2"));

        slot.Release(runId, "worker-1");
        Assert.Null(slot.RunId);
        Assert.True(slot.IsAvailable(Now));
    }

    [Fact]
    public void Expired_work_slot_can_be_reclaimed_but_not_renewed()
    {
        var slot = SpaceTenantAiWorkSlot.CreateAvailable(
            Guid.NewGuid(),
            2);
        var firstRun = Guid.NewGuid();
        slot.Acquire(
            firstRun,
            "worker-1",
            Now,
            TimeSpan.FromSeconds(60));

        Assert.Throws<SpaceAiCapacityLeaseLostException>(() =>
            slot.Renew(
                firstRun,
                "worker-1",
                Now.AddSeconds(60),
                TimeSpan.FromSeconds(60)));

        var secondRun = Guid.NewGuid();
        slot.Acquire(
            secondRun,
            "worker-2",
            Now.AddSeconds(60),
            TimeSpan.FromSeconds(60));
        Assert.Equal(secondRun, slot.RunId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Work_slot_number_is_bounded_by_platform_limit(int slotNo)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpaceTenantAiWorkSlot.CreateAvailable(
                Guid.NewGuid(),
                slotNo));
    }

    [Fact]
    public void Budget_reservation_follows_send_report_reconcile_flow()
    {
        var reservation = Reservation();

        reservation.MarkSubmitted();
        reservation.Report(75);
        reservation.Reconcile();

        Assert.Equal(
            SpaceAiBudgetReservationStatus.Reconciled,
            reservation.Status);
        Assert.Equal(75, reservation.ActualCostMinor);
        Assert.Equal(75, reservation.EffectiveCostMinor);
        reservation.Report(75);
        reservation.Reconcile();
        Assert.Throws<SpaceAiCapacityStateException>(() =>
            reservation.Report(76));
    }

    [Fact]
    public void Submitted_budget_cannot_be_released_or_expired()
    {
        var reservation = Reservation();
        reservation.MarkSubmitted();

        Assert.Throws<SpaceAiCapacityStateException>(
            reservation.Release);
        Assert.False(
            reservation.ReleaseIfExpired(Now.AddMinutes(30)));
        Assert.Equal(
            SpaceAiBudgetReservationStatus.Submitted,
            reservation.Status);
        Assert.Equal(80, reservation.EffectiveCostMinor);
    }

    [Fact]
    public void Unsent_expired_budget_releases_without_cost()
    {
        var reservation = Reservation();

        Assert.True(
            reservation.ReleaseIfExpired(Now.AddMinutes(15)));
        Assert.Equal(
            SpaceAiBudgetReservationStatus.Released,
            reservation.Status);
        Assert.Equal(0, reservation.EffectiveCostMinor);
        reservation.Release();
    }

    [Fact]
    public void Budget_limits_normalize_currency_and_reject_bad_ranges()
    {
        var limits = new SpaceAiBudgetLimits(100, 1_000, " usd ")
            .Validate();

        Assert.Equal("USD", limits.Currency);
        Assert.Throws<ArgumentException>(() =>
            new SpaceAiBudgetLimits(1_001, 1_000, "USD").Validate());
        Assert.Throws<ArgumentException>(() =>
            new SpaceAiBudgetLimits(100, null, null).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SpaceAiBudgetLimits(-1, null, "USD").Validate());
    }

    [Fact]
    public void Tenant_policy_pins_budget_and_concurrency_limits()
    {
        var tenantId = Guid.NewGuid();
        var policy = SpaceAiTenantPolicy.Enabled(
            tenantId,
            SpaceAiDataPolicy.StructuredFeatures,
            [Guid.NewGuid()],
            ["local-v1"],
            maxConcurrentRuns: 2,
            budgetLimits: new(500, 5_000, "usd"));

        Assert.Equal(2, policy.MaxConcurrentRuns);
        Assert.Equal(500, policy.BudgetLimits.DailyBudgetMinor);
        Assert.Equal(5_000, policy.BudgetLimits.MonthlyBudgetMinor);
        Assert.Equal("USD", policy.BudgetLimits.Currency);
        Assert.Equal(
            SpaceAiBudgetLimits.Unpriced,
            SpaceAiTenantPolicy.Disabled(tenantId).BudgetLimits);
    }

    [Fact]
    public void Capacity_options_require_positive_lease_windows()
    {
        new SpaceAiCapacityOptions().Validate();
        Assert.Throws<InvalidOperationException>(() =>
            new SpaceAiCapacityOptions
            {
                WorkSlotLeaseDuration = TimeSpan.Zero,
            }.Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new SpaceAiCapacityOptions
            {
                BudgetReservationDuration = TimeSpan.Zero,
            }.Validate());
    }

    private static SpaceAiBudgetReservation Reservation() =>
        SpaceAiBudgetReservation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('a', 64),
            DateOnly.FromDateTime(Now),
            202607,
            80,
            "usd",
            Now.AddMinutes(15));
}
