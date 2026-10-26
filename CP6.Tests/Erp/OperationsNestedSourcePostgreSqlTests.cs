using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Nested_source_final_failure_blocks_actual_product_unit_write_or_original_disclosure(bool write)
    {
        var hook = new CommandHook();
        await using var h = await Seed(hook);
        await GrantUomProfile(h); await GrantProductUnitEdit(h);
        var unit = await MakeUnit(Units(h)); var product = await SeedIdentityProduct(h);
        await h.Db.ProductMasters.Where(row => row.Id == product)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.QtyUnit, "EA"));
        var source = await ProductIdentity(h).ReadAsync(product); var owner = ProductUnits(h);
        var input = new CommercialProductUnitCommandV1 { OperationId = Guid.NewGuid(), ProductId = product,
            ExpectedBindingVersion = "absent", ProductVersion = source.Version, ProfileRef = unit.Reference };
        UomProfileCommandResultV1? receipt = write ? null : await owner.BindAsync(input);
        var sourceChecks = 0;
        hook.Before = command =>
        {
            // Fault only the final native object lookup, after the initial source lookup succeeded.
            if (command.CommandText.Contains("SELECT EXISTS") && command.CommandText.Contains("cp6_uom_profile_v1.heads")
                && ++sourceChecks == 2) throw new IOException("Injected final UOM object lookup unavailable");
            return Task.CompletedTask;
        };
        try
        {
            var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(async () =>
            {
                if (write) await owner.BindAsync(input);
                else await owner.OperationAsync(input.OperationId, false);
            });
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", error.Message);
            Assert.Equal(2, sourceChecks);
        }
        finally { hook.Before = null; }

        if (write)
        {
            Assert.Equal("absent", (await owner.EditorAsync(product)).ExpectedBindingVersion);
            Assert.Equal("UNKNOWN", (await owner.OperationAsync(input.OperationId, false)).State);
            Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM cp6_uom_profile_v1.product_versions
                WHERE tenant_id={h.Tenant} AND product_id={product}
                """).SingleAsync());
            // Audit and result share this native operation row; neither survives the rollback.
            Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM cp6_main_owner_v1.operations
                WHERE tenant_id={h.Tenant} AND owner='Main.Erp.ProductUnit.v1' AND operation_id={input.OperationId}
                """).SingleAsync());
            Assert.Equal("CLOSED", (await owner.OperationAsync(input.OperationId, true)).State);
            Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.BindAsync(input))).Message);
        }
        else Assert.Equal(receipt, (await owner.OperationAsync(input.OperationId, false)).Result);
    }

    [CurrentPermissionPostgresFact]
    public async Task Nested_source_deep_rejection_prevents_root_read_disclosure()
    {
        await using var h = await Seed(); await GrantUomProfile(h); await GrantProductUnitEdit(h);
        var unit = await MakeUnit(Units(h));
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => UnitAuthority(h).ExecuteAsync(
            OperationsOwnerV1.Product, "query", false, async (root, token) =>
            {
                var editor = await root.ActionAsync(OperationsOwnerV1.UomProfile, "edit", token);
                var source = await editor.SourceAsync(OperationsOwnerV1.UomProfile, token);
                var store = new UomProfileStoreV1(h.Db); await store.Fence(false, token);
                var result = await UomProfileOwnerV1.CurrentExact(source, store, unit.Reference, token);
                source.AtCommit(_ => throw new OrderPriceCorrectionException("SOURCE_FINAL_REJECTED", 403));
                return result;
            }));
        Assert.Equal("SOURCE_FINAL_REJECTED", error.Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Nested_source_cached_action_and_late_registered_checks_execute_once_before_success()
    {
        await using var h = await Seed(); await GrantUomProfile(h); await GrantProductUnitEdit(h);
        var rootChecks = 0; var childChecks = 0; var deepChecks = 0;
        var result = await UnitAuthority(h).ExecuteAsync(OperationsOwnerV1.Product, "query", false, async (root, token) =>
        {
            var child = await root.ActionAsync(OperationsOwnerV1.UomProfile, "edit", token);
            Assert.Same(child, await root.ActionAsync(OperationsOwnerV1.UomProfile, "edit", token));
            var source = await child.SourceAsync(OperationsOwnerV1.UomProfile, token);
            Assert.Same(source, await child.SourceAsync(OperationsOwnerV1.UomProfile, token));
            root.AtCommit(_ => { rootChecks++; return Task.CompletedTask; });
            child.AtCommit(_ => { childChecks++; return Task.CompletedTask; });
            source.AtCommit(_ => { deepChecks++; return Task.CompletedTask; });
            return "complete";
        });
        Assert.Equal("complete", result);
        Assert.Equal(1, rootChecks); Assert.Equal(1, childChecks); Assert.Equal(1, deepChecks);
    }
}
