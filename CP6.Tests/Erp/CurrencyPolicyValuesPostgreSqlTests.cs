using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task CurrencyPolicy_half_even_preserves_large_signed_inputs_and_exact_policy_digits()
    {
        await using var h = await Seed(); await GrantCurrencyPolicy(h); var owner = Currencies(h);
        var policy = await MakeCurrency(owner, CurrencyInput() with { MinorDigits = 2, PriceDigits = 6 });
        foreach (var sample in new[] { ("1.225", "1.22"), ("1.235", "1.24"), ("-1.225", "-1.22"),
            ("-1.235", "-1.24"), ("-0.005", "0.00"), ("999.999", "1000.00"), ("0", "0.00"),
            ("1000000000000000000000000000000000000.125", "1000000000000000000000000000000000000.12") })
        {
            var result = await owner.QuantizeAsync(new(policy.Reference, "AMOUNT", sample.Item1));
            Assert.Equal(sample.Item2, result.RoundedValue); Assert.Equal(sample.Item1, result.Value); Assert.Equal(policy.Reference, result.PolicyRef);
        }
        Assert.Equal("0.123456", (await owner.QuantizeAsync(new(policy.Reference, "PRICE", "0.1234565"))).RoundedValue);
        Assert.Equal("0.123458", (await owner.QuantizeAsync(new(policy.Reference, "PRICE", "0.1234575"))).RoundedValue);
        Assert.Equal("1.00", (await owner.QuantizeAsync(new(policy.Reference, "AMOUNT", "1." + new string('0', 1024)))).RoundedValue);
        foreach (var invalid in new[] { "", "1e2", "+1", "01", " 1", "-0", "NaN", "1." })
            Assert.Equal(422, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.QuantizeAsync(new(policy.Reference, "AMOUNT", invalid)))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.QuantizeAsync(new(policy.Reference, "FX", "1")))).Status);
        foreach (var invalid in new[] { CurrencyInput("USD") with { MinorDigits = null }, CurrencyInput("USD") with { MinorDigits = 5 },
            CurrencyInput("USD") with { PriceDigits = 7 }, CurrencyInput("USD") with { Rounding = "HALF_UP" }, CurrencyInput("USD") with { Enabled = null } })
            Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(async () => await owner.CommandAsync(await NewCurrency(owner, invalid)))).Status);
        var boundary = await MakeCurrency(owner, CurrencyInput("USD") with { MinorDigits = 4, PriceDigits = 0 });
        Assert.Equal("1.2346", (await owner.QuantizeAsync(new(boundary.Reference, "AMOUNT", "1.23455"))).RoundedValue);
        Assert.Equal("2", (await owner.QuantizeAsync(new(boundary.Reference, "PRICE", "2.5"))).RoundedValue);
    }

    [CurrentPermissionPostgresFact]
    public async Task CurrencyPolicy_exact_source_rejects_disabled_changed_ABA_foreign_digest_and_unheld_fence()
    {
        await using var h = await Seed(); await GrantCurrencyPolicy(h); await GrantProductIdentity(h);
        var owner = Currencies(h); var policy = await MakeCurrency(owner); var authority = CurrencyAuthority(h);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.QuantizeAsync(new(policy.Reference with { Owner = "FX" }, "AMOUNT", "1")))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.QuantizeAsync(new(policy.Reference with { Digest = new string('a', 64) }, "AMOUNT", "1")))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => authority.ExecuteAsync(
            OperationsOwnerV1.Product, "query", false, async (access, token) =>
            { var store = new CurrencyPolicyStoreV1(h.Db); await store.Fence(false, token); return await CurrencyPolicyOwnerV1.CurrentExact(access, store, policy.Reference, token); }))).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => authority.ExecuteAsync(
            OperationsOwnerV1.CurrencyPolicy, "query", false, (_, token) => new CurrencyPolicyStoreV1(h.Db).Read(policy.Reference.Id, null, token)))).Status);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => authority.ExecuteAsync(
            OperationsOwnerV1.CurrencyPolicy, "query", false, (access, token) => CurrencyPolicyOwnerV1.CurrentExact(access, new CurrencyPolicyStoreV1(peer), policy.Reference, token)))).Status);
        await owner.CommandAsync(ReviseCurrency(policy, policy.Policy with { Enabled = false }));
        var disabled = await owner.ReadAsync(policy.Reference.Id, null);
        Assert.Equal("CURRENCY_POLICY_DISABLED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.QuantizeAsync(new(disabled.Reference, "AMOUNT", "1")))).Message);
        await owner.CommandAsync(ReviseCurrency(disabled, policy.Policy));
        Assert.Equal("CURRENCY_POLICY_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.QuantizeAsync(new(policy.Reference, "AMOUNT", "1")))).Message);
        Assert.Equal(policy.OriginalJson, (await owner.ReadAsync(policy.Reference.Id, "1")).OriginalJson);
        Assert.Equal("1", (await owner.QuantizeAsync(new((await owner.ReadAsync(policy.Reference.Id, null)).Reference, "AMOUNT", "1.1"))).RoundedValue);
    }
}
