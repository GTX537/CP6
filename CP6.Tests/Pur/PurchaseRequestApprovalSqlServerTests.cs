using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pur.Contracts;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Pur;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Infra;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CP6.Tests.Pur;

public sealed class PurchaseRequestApprovalSqlServerTests
{
    private readonly string? _connectionString;
    private static readonly Guid Actor = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid Approver = Guid.Parse("40000000-0000-0000-0000-000000000002");

    public PurchaseRequestApprovalSqlServerTests()
    {
        _connectionString = OaP0SharedStageSqlServer.GetValidatedConnectionString();
    }

    private CP6Context NewContext() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseSqlServer(_connectionString!).Options);

    private PurchaseRequestService Service(CP6Context db)
    {
        var engine = new FlowEngine(db, new ApproverResolver(db));
        var approval = new ApprovalServiceAdapter(new ApprovalService(db, engine), db);
        return new PurchaseRequestService(
            db, new SeqService(db), approval, Mock.Of<IPurchaseOrderService>(), new DataScopeFilter(db));
    }

    [SqlServerFact]
    public async Task P0_AC_P06_ConcurrentDoubleSubmitReturnsOneActiveInstance()
    {
        var prNo = $"P0R{Guid.NewGuid():N}"[..19];
        var flowKey = $"oa-p0-race-{Guid.NewGuid():N}";
        Guid bindingId = default;
        var insertedBinding = false;
        string? originalFlowKey = null;
        string? originalConditionJson = null;
        bool? originalEnable = null;
        try
        {
            using (var seed = NewContext())
            {
                var schema = new FlowSchema
                {
                    Nodes =
                    {
                        new FlowNode
                        {
                            Id = "approve", Type = "approval", ApproverStrategy = "Specified",
                            ApproverUserId = Approver
                        },
                        new FlowNode { Id = "end", Type = "end" },
                    },
                    Edges = { new FlowEdge { From = "approve", To = "end" } },
                };
                var head = new Wf_FlowDef
                {
                    Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = "PR",
                    FormKey = "", SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
                };
                seed.Wf_FlowDefs.Add(head);
                seed.Wf_FlowDefVersions.Add(new Wf_FlowDefVersion
                {
                    Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1,
                    Status = WfDefinitionVersionStatus.Published,
                    FlowNameSnapshot = "PR", SchemaJson = head.SchemaJson,
                });
                var binding = await seed.Wf_ApprovalBindings
                    .SingleOrDefaultAsync(x => x.BizType == "PUR_PR");
                if (binding == null)
                {
                    binding = new Wf_ApprovalBinding
                        { Id = Guid.NewGuid(), BizType = "PUR_PR", FlowKey = head.FlowKey, Enable = true };
                    seed.Wf_ApprovalBindings.Add(binding);
                    insertedBinding = true;
                }
                else
                {
                    originalFlowKey = binding.FlowKey;
                    originalConditionJson = binding.ConditionJson;
                    originalEnable = binding.Enable;
                    binding.FlowKey = head.FlowKey;
                    binding.ConditionJson = null;
                    binding.Enable = true;
                }
                bindingId = binding.Id;
                seed.PurchaseRequests.Add(new PurchaseRequest
                {
                    PrNo = prNo, RequesterId = "alice", RequestDate = DateTime.UtcNow,
                    Status = PrStatus.Draft, Source = PrSource.Manual, Creator = "alice",
                });
                seed.PurchaseRequestLines.Add(new PurchaseRequestLine
                {
                    PrNo = prNo, LineNo = 1, ItemId = "ITEM", Qty = 2, EstPrice = 10,
                    Status = 0, Creator = "alice",
                });
                await seed.SaveChangesAsync();
            }

            var permission = new UserPermissionContext
            {
                UserId = Actor, UserName = "alice", DataScopes = { ["pur-pr"] = 5 },
            };
            async Task<PurchaseRequest> Submit()
            {
                await using var db = NewContext();
                return await Service(db).SubmitForApprovalAsync(prNo, Actor, "alice", permission);
            }

            var results = await Task.WhenAll(Task.Run(Submit), Task.Run(Submit));

            await using var verify = NewContext();
            var active = await verify.Wf_FlowInstances.Where(x =>
                x.BizType == "PUR_PR" && x.BizId == prNo &&
                (x.Status == FlowInstanceStatus.Running || x.Status == FlowInstanceStatus.Suspended)).ToListAsync();
            var instance = Assert.Single(active);
            Assert.All(results, x => Assert.Equal(instance.Id.ToString(), x.ApprovalRef));
        }
        finally
        {
            if (bindingId != default)
            {
                await using var cleanup = NewContext();
                var binding = await cleanup.Wf_ApprovalBindings.SingleAsync(x => x.Id == bindingId);
                if (insertedBinding)
                {
                    cleanup.Wf_ApprovalBindings.Remove(binding);
                }
                else
                {
                    binding.FlowKey = originalFlowKey!;
                    binding.ConditionJson = originalConditionJson;
                    binding.Enable = originalEnable!.Value;
                }
                await cleanup.SaveChangesAsync();
            }
        }
    }
}
