using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private async Task ExerciseCrmExportServiceRevocation(Harness h, string bridge, Task childExited, Guid subject,
        InquiryServicePrincipalRegistryV1 removed, CrmIdentityRuntime runtime)
    {
        var aggregate = $"service-principal:{subject:D}";
        var before = await State();
        Assert.Equal(1, before.Version);
        Assert.True(before.Enabled);
        Assert.False(before.Deleted);
        Assert.False(removed.IsPublished(h.Tenant));
        await Stage("ready");
        Task change;
        if (Environment.GetEnvironmentVariable("CP6_PGT017_ORDER") == "pause-first")
        {
            await Stage("permission-held");
            change = Publish();
        }
        else
        {
            await Stage("owner-held");
            change = Publish();
            try
            {
                await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
                await crm.OpenAsync();
                await using var query = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database()
                        AND application_name='cp6-main-inquiry-tenant-pause-v1'
                        AND wait_event_type='Lock' AND cardinality(pg_blocking_pids(pid))>0)
                    """, crm);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                while (!(bool)(await query.ExecuteScalarAsync(deadline.Token))!)
                {
                    if (change.IsCompleted)
                    {
                        await change;
                        Assert.Fail("Main service registration committed while the CRM Owner was waiting.");
                    }
                    await Task.Delay(25, deadline.Token);
                }
                Assert.Equal(before, await State());
                Assert.False(removed.IsPublished(h.Tenant));
            }
            finally { await File.WriteAllTextAsync(Path.Combine(bridge, "release-owner"), "PASS"); }
        }
        await change;
        var after = await State();
        Assert.True(removed.IsPublished(h.Tenant));
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal(before.Generation + 1, after.Generation);
        Assert.Equal(before.PriorityOutbox + 1, after.PriorityOutbox);
        Assert.Equal(before.StandardOutbox, after.StandardOutbox);
        Assert.False(after.Enabled);
        Assert.True(after.Deleted);
        Assert.Equal(2, after.RegistrationRevision);
        Assert.True(after.TenantEnabled);
        Assert.True(after.ExportAction);
        await Publish();
        Assert.Equal(after, await State());
        await File.WriteAllTextAsync(Path.Combine(bridge, "paused"), "PASS");
        await Stage("finished");

        async Task Publish()
        {
            var tenant = new TenantContext();
            await using var db = server.Context(tenant, runtime);
            await new InquiryServiceIdentityPublisherV1(db, tenant, removed, runtime).PublishAsync(h.Tenant);
        }

        async Task Stage(string marker)
        {
            var wait = WaitConfigurationBridge(bridge, marker);
            if (await Task.WhenAny(wait, childExited) == childExited)
                Assert.True(File.Exists(Path.Combine(bridge, marker)), $"CRM exited before reaching {marker}.");
            await wait;
        }

        async Task<ServiceSourceState> State()
        {
            var tenant = h.Tenant;
            var json = await h.Db.Database.SqlQuery<string>($"""
                SELECT jsonb_build_object(
                    'Version', s."Version", 'Deleted', s."IsDeleted", 'Enabled', s."PayloadJson"::jsonb->'enabled',
                    'RegistrationRevision', s."PayloadJson"::jsonb->'registrationRevision',
                    'Generation', (SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={tenant}),
                    'TenantEnabled', (SELECT "Enable" FROM public."Sys_Tenants" WHERE "Id"={tenant}),
                    'ExportAction', EXISTS(SELECT 1 FROM public."Sys_RoleAction" a JOIN public."Sys_Menus" m
                       ON m."MenuId"=a."MenuId"
                       WHERE a."TenantId"={tenant} AND a."RoleId"=71 AND a."ActionCode"='export' AND m."MenuKey"='crm-privacy'),
                    'PriorityOutbox', (SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage"
                       WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}),
                    'StandardOutbox', (SELECT count(*) FROM crm_identity."Cp6_OutboxMessage"
                       WHERE "TenantId"={tenant} AND "AggregateId"={aggregate}))::text AS "Value"
                FROM crm_identity."Snapshot" s WHERE s."TenantId"={tenant} AND s."AggregateId"={aggregate}
                """).SingleAsync();
            return JsonSerializer.Deserialize<ServiceSourceState>(json)!;
        }
    }

    private sealed record ServiceSourceState(int Version, bool Enabled, bool Deleted, long RegistrationRevision,
        long Generation, bool TenantEnabled, bool ExportAction, long PriorityOutbox, long StandardOutbox);
}
