using System.Net;
using System.Text.Json;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task W692_center_use_second_batch_failure_rolls_back_all_cost_and_adoption_rows()
    {
        await using var h = await Seed();
        var number = await SeedNativeCostCollect(h);
        await GrantNativeCostCollect(h);
        var order = await h.Db.WorkOrders.AsNoTracking().SingleAsync(row => row.WorkOrderNo == number);
        var press = await h.Db.WorkCenters.AsNoTracking().SingleAsync(row => row.WgCd == "W686-A");
        var day = new DateOnly(2026, 6, 30);
        var ledger = Guid.NewGuid();
        var policy = Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers
                (tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{ledger},'w692-batches','REAL','LE1','MFG','CNY',{day});
            INSERT INTO cp6_fin_mfg_v1.members (tenant_id,ledger_id,subject_id) VALUES ({h.Tenant},{ledger},{h.User});
            INSERT INTO cp6_fin_mfg_v1.work_orders (tenant_id,work_order_id,ledger_id) VALUES ({h.Tenant},{order.Id},{ledger});
            INSERT INTO cp6_fin_mfg_v1.policies
                (tenant_id,ledger_id,version_id,approval_id,currency,currency_scale,rounding,rate_date_basis,valuation,variance,valid_from)
            VALUES ({h.Tenant},{ledger},{policy},{Guid.NewGuid()},'CNY',2,'HALF_UP','APPROVED_ACCOUNTING_DATE',
                'STANDARD_OUTPUT_WITH_VARIANCE','VAR_TO_EXPENSE',{day});
            """);

        var other = NewOperator("fixture-center-reviewer");
        h.Db.Sys_Users.Add(other);
        await h.Db.WorkOrderProcesses.Where(row => row.WorkOrderNo == number).ExecuteDeleteAsync();
        var operations = Enumerable.Range(1, 33).Select(index => new ProductProcess
        {
            Id = Guid.NewGuid(), ProductCd = order.ProductCd, ProcessCd = "COST", TaskCd = $"OP{index:D2}",
            SetupHour = 1m, StandardCrewSize = 1m,
        }).ToArray();
        var centers = operations.Select(op => new CostCenter { Id = Guid.NewGuid(), Code = op.TaskCd, Name = op.TaskCd,
            Type = CostCenterType.Process }).ToArray();
        h.Db.AddRange(operations);
        h.Db.AddRange(centers);
        h.Db.AddRange(operations.Select((op, index) => new WorkOrderProcess
        {
            WorkOrderNo = number, ProcessCd = op.ProcessCd, TaskCd = op.TaskCd, WgCd = press.WgCd,
            SortOrder = index + 1, Creator = "price-owner", ActualLaborHour = 1m, ActualMachineHour = 1m,
        }));
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        // 模拟已经批准的上游版本。实际采用和业务结果均由签名 HTTP 调用生产入口写入。
        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[index];
            var center = centers[index];
            var edition = Guid.NewGuid();
            var rate = Guid.NewGuid();
            var basis = Guid.NewGuid();
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_mfg_v1.center_submissions
                    (tenant_id,ledger_id,id,root_id,operation_id,operation_version,code,name,valid_from,maker,submission_key,submission,
                     state,revision,reviewer,review_qualification_id,review_key,review,reviewed_at,publisher,release_qualification_id,release_key,released_at)
                VALUES ({h.Tenant},{ledger},{edition},{center.Id},{operation.Id},{operation.RowVersion},{center.Code},{center.Name},{day},
                    {h.User},{Guid.NewGuid()},jsonb_build_object('Fixture','W692'),'RELEASED',3,{other.Id},{Guid.NewGuid()},
                    {Guid.NewGuid()},jsonb_build_object('Decision','APPROVE'),clock_timestamp(),{other.Id},{Guid.NewGuid()},{Guid.NewGuid()},clock_timestamp());
                INSERT INTO cp6_fin_mfg_v1.center_versions
                    (tenant_id,ledger_id,root_id,version_id,version,code,name,valid_from,approval_id,submission_id)
                VALUES ({h.Tenant},{ledger},{center.Id},{edition},1,{center.Code},{center.Name},{day},{edition},{edition});
                INSERT INTO cp6_fin_mfg_v1.rate_bases
                    (tenant_id,ledger_id,id,policy_version_id,approval_id,work_center_id,operation_id,valid_from)
                VALUES ({h.Tenant},{ledger},{basis},{policy},{Guid.NewGuid()},{press.Id},{operation.Id},{day});
                INSERT INTO cp6_fin_mfg_v1.rate_heads (tenant_id,ledger_id,work_center_id,operation_id,revision)
                VALUES ({h.Tenant},{ledger},{press.Id},{operation.Id},1);
                INSERT INTO cp6_fin_mfg_v1.rate_versions
                    (tenant_id,ledger_id,id,work_center_id,operation_id,center_id,center_version_id,policy_version_id,basis_id,
                     labor_rate,overhead_rate,valid_from,maker,state,revision,reviewer,review_qualification_id,publisher,release_qualification_id,
                     submitted_at,reviewed_at,review_original_actor)
                VALUES ({h.Tenant},{ledger},{rate},{press.Id},{operation.Id},{center.Id},{edition},{policy},{basis},2,3,{day},
                    {h.User},'RELEASED',4,{other.Id},{Guid.NewGuid()},{other.Id},{Guid.NewGuid()},clock_timestamp(),clock_timestamp(),true);
                INSERT INTO cp6_fin_mfg_v1.timeline (tenant_id,ledger_id,work_center_id,operation_id,rate_id,valid_from)
                VALUES ({h.Tenant},{ledger},{press.Id},{operation.Id},{rate},{day});
                """);
        }

        var environment = new Dictionary<string, string>(IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant))
        {
            ["FinanceCosting__EnvironmentId"] = "w692-batches",
        };
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment);
        var cookies = await Login(host, h);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cp6_fin_mfg_v1.w692_reject_second_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.batch_no=2 AND EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.ledgers WHERE tenant_id=NEW.tenant_id AND id=NEW.ledger_id
                    AND environment_id='w692-batches') THEN
                    IF NOT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_use_receipts r
                        WHERE (r.tenant_id,r.ledger_id,r.effect_id,r.batch_no)=(NEW.tenant_id,NEW.ledger_id,NEW.effect_id,1)
                          AND jsonb_array_length(r.adopted)=64) THEN RAISE EXCEPTION 'W692_FIRST_BATCH_MISSING'; END IF;
                    RETURN NULL;
                END IF;
                RETURN NEW;
            END; $$;
            CREATE TRIGGER w692_reject_second_receipt BEFORE INSERT ON cp6_fin_mfg_v1.center_use_receipts
                FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.w692_reject_second_receipt();
            """);
        try
        {
            using var failed = await Collect();
            Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
            Assert.Contains("FINANCE_CENTER_USE_SOURCE_UNAVAILABLE", await failed.Content.ReadAsStringAsync());
            Assert.False(await h.Db.CostSheets.AnyAsync());
            Assert.False(await h.Db.CostSheetLines.AnyAsync());
            Assert.False(await h.Db.Database.SqlQuery<bool>($"""
                SELECT (EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_use_effects WHERE tenant_id={h.Tenant})
                   OR EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_use_intents WHERE tenant_id={h.Tenant})
                   OR EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_use_receipts WHERE tenant_id={h.Tenant})
                   OR EXISTS(SELECT 1 FROM cp6_fin_cost_basis_v1.versions WHERE tenant_id={h.Tenant})) AS "Value"
                """).SingleAsync());
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER w692_reject_second_receipt ON cp6_fin_mfg_v1.center_use_receipts;
                DROP FUNCTION cp6_fin_mfg_v1.w692_reject_second_receipt();
                """);
        }

        using var committed = await Collect();
        Assert.True(committed.StatusCode == HttpStatusCode.OK, await committed.Content.ReadAsStringAsync());
        var sheet = await h.Db.CostSheets.AsNoTracking().Include(row => row.Lines).SingleAsync();
        Assert.Equal((66m, 99m), (sheet.LaborActual, sheet.OverheadActual));
        var effect = await h.Db.Database.SqlQuery<Guid>($"""
            SELECT center_use_effect_id AS "Value" FROM cp6_fin_cost_basis_v1.versions
            WHERE (tenant_id,cost_sheet_id,version)=({h.Tenant},{sheet.Id},1)
            """).SingleAsync();
        var batches = await h.Db.Database.SqlQuery<string>($"""
            SELECT adopted::text AS "Value" FROM cp6_fin_mfg_v1.center_use_receipts
            WHERE (tenant_id,ledger_id,effect_id)=({h.Tenant},{ledger},{effect}) ORDER BY batch_no
            """).ToListAsync();
        Assert.Equal(2, batches.Count);
        using var first = JsonDocument.Parse(batches[0]);
        using var second = JsonDocument.Parse(batches[1]);
        Assert.Equal((64, 2), (first.RootElement.GetArrayLength(), second.RootElement.GetArrayLength()));
        var adopted = first.RootElement.EnumerateArray().Concat(second.RootElement.EnumerateArray());
        Assert.Equal(sheet.Lines.Where(line => line.Element != CostElement.Material).Select(line => line.Id).Order(),
            adopted.Select(item => item.GetProperty("Item").GetProperty("ItemId").GetGuid()).Order());
        var past = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.center_use_intents (tenant_id,ledger_id,effect_id,batch_no,items)
            SELECT tenant_id,ledger_id,effect_id,batch_no,items FROM cp6_fin_mfg_v1.center_use_intents
            WHERE (tenant_id,ledger_id,effect_id,batch_no)=({h.Tenant},{ledger},{effect},1)
            """));
        Assert.Equal("FINANCE_CENTER_USE_TRANSACTION_REQUIRED", past.MessageText);
        Assert.False(await h.Db.JournalEntries.AnyAsync());

        Task<HttpResponseMessage> Collect() => host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/collect", new { }));
    }
}
