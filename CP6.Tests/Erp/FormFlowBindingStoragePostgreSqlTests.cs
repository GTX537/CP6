using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Task RawBindingActivation(Harness h,FlowActivationV2 before,Guid version)
    {
        var revision=Guid.NewGuid();var at=DateTimeOffset.UtcNow;
        return h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_workflow_authoring_v2.activation_history(tenant_id,environment_id,head_id,revision,previous_revision,enabled,published_version_id,actor_id,reason,changed_at)
            VALUES({h.Tenant},{ScopeEnvironment},{before.DefinitionId},{revision},{before.Revision},true,{version},{h.User},'storage test',{at});
            UPDATE cp6_workflow_authoring_v2.activation_current SET revision={revision},enabled=true,published_version_id={version},changed_at={at}
              WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND head_id={before.DefinitionId};
            """);
    }
    private const string RemoveFormBindingSchema = """
        DROP FUNCTION cp6_workflow_authoring_v2.form_binding_ready();
        DROP TRIGGER activation_form_binding ON cp6_workflow_authoring_v2.activation_current;
        DROP FUNCTION cp6_workflow_authoring_v2.activation_form_binding();
        ALTER TABLE cp6_workflow_authoring_v2.activation_current DROP COLUMN form_head_id,DROP COLUMN form_version_id;
        """;
    private static string FormBindingMigrationSql()=>Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
        Assert.Single(new CP6.Persistence.PostgreSql.Migrations.Core.FormFlowBindingV2().UpOperations)).Sql;
    [CurrentPermissionPostgresFact]
    public async Task FormBinding_database_guards_direct_duplicate_activation_and_missing_projection_protection()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);await ActiveDependencyForm(form);await ActiveDependencyFlow(flow,"a","form");var b=await BindingFlow(flow,"b");var before=(await flow.ActivationPreviewAsync("b")).Current;
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>RawBindingActivation(h,before,b.VersionId));
            Assert.Equal("23505",error.SqlState);Assert.Equal("ux_activation_form",error.ConstraintName);await tx.RollbackAsync();
        }
        Assert.Equal(before,(await flow.ActivationPreviewAsync("b")).Current);Assert.Empty(await flow.ActivationHistoryAsync("b"));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.activation_current DISABLE TRIGGER activation_form_binding");
        try {Assert.Equal("ACTIVATION_WRITER_COVERAGE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>flow.SetActivationAsync("b",ActivationCommand(before,b.VersionId)))).Message);}
        finally {await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.activation_current ENABLE TRIGGER activation_form_binding");}
        Assert.Single(await ActiveFormBindings(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task FormBinding_forward_upgrade_derives_existing_binding_without_rewriting_activation_history()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);var f=await ActiveDependencyForm(form);var p=await ActiveDependencyFlow(flow,"a","form");
        var active=(await flow.ActivationPreviewAsync("a")).Current;var history=await flow.ActivationHistoryAsync("a");
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.Database.ExecuteSqlRawAsync(RemoveFormBindingSchema);
            await h.Db.Database.ExecuteSqlRawAsync(FormBindingMigrationSql());
            var binding=Assert.Single(await ActiveFormBindings(h));Assert.Equal(p.DefinitionId,binding.Flow);Assert.Equal(f.VersionId,binding.FormVersion);Assert.Equal(active.Revision,binding.Revision);
            await tx.RollbackAsync();
        }
        Assert.Equal(active,(await flow.ActivationPreviewAsync("a")).Current);Assert.Equal(history,await flow.ActivationHistoryAsync("a"));
    }
    [CurrentPermissionPostgresFact]
    public async Task FormBinding_upgrade_refuses_existing_conflicting_activations_without_choosing_a_winner()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);await ActiveDependencyForm(form);await ActiveDependencyFlow(flow,"a","form");var b=await BindingFlow(flow,"b");var before=(await flow.ActivationPreviewAsync("b")).Current;
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.Database.ExecuteSqlRawAsync(RemoveFormBindingSchema);await RawBindingActivation(h,before,b.VersionId);
            var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(FormBindingMigrationSql()));Assert.Equal("23505",error.SqlState);await tx.RollbackAsync();
        }
        Assert.Single(await ActiveFormBindings(h));Assert.Equal(before,(await flow.ActivationPreviewAsync("b")).Current);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormBinding_same_key_in_another_tenant_is_independent_and_audit_failure_restores_prior_binding()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);await ActiveDependencyForm(form);var first=await ActiveDependencyFlow(flow,"a","form");
        await using var other=await Seed();var(foreign,foreignForm)=await PrepareDependencies(other);await ActiveDependencyForm(foreignForm);await ActiveDependencyFlow(foreign,"a","form");
        Assert.Single(await ActiveFormBindings(h));Assert.Single(await ActiveFormBindings(other));
        var current=(await flow.ActivationPreviewAsync("a")).Current;
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.audit ADD CONSTRAINT binding_audit_fault CHECK(command_id <> 'binding-fault') NOT VALID");
        try {await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>flow.SetActivationAsync("a",ActivationCommand(current,first.VersionId,false,"binding-fault")));}
        finally {await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.audit DROP CONSTRAINT binding_audit_fault");}
        Assert.Equal(current.Revision,Assert.Single(await ActiveFormBindings(h)).Revision);Assert.Equal(current,(await flow.ActivationPreviewAsync("a")).Current);Assert.Single(await flow.ActivationHistoryAsync("a"));
    }
}
