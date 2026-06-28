using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.WebApi.Seed;

/// <summary>
/// OA 请假申请演示表单 + 流程种子（幂等，按 FormKey/FlowKey 判存）。
/// OA 模块首个「可填單」演示内容：leave 表单（填單目录可见，DynamicForm 渲染）
/// + leave-flow 流程（start 填單 → approval 主管审批[Specified=admin] → end）。
/// 让「填單 → 提交 → 信箱审批」闭环 out-of-box 可演示（采购 budget/po/pr 流程是 biz 触发、无 start 节点、不在填單目录）。
/// 流程 schema 即流程设计器输出的同构格式（start/approval/end），印证设计器产物可被引擎执行。
/// </summary>
public static class OaLeaveFormSeed
{
    public static void Seed(CP6Context db, Guid? approverUserId = null)
    {
        var approver = approverUserId
            ?? db.Sys_Users.Where(u => u.UserName == "admin").Select(u => (Guid?)u.Id).FirstOrDefault()
            ?? Guid.Empty;

        if (!db.Wf_FormDefs.Any(f => f.FormKey == "leave"))
        {
            var formSchema = new
            {
                fields = new object[]
                {
                    new { name = "days",     label = "请假天数", type = "number",   required = true },
                    new { name = "dateFrom", label = "起始日期", type = "date",     required = true },
                    new { name = "dateTo",   label = "结束日期", type = "date",     required = true },
                    new { name = "reason",   label = "事由",     type = "textarea", required = true, maxLength = 200 },
                },
            };
            db.Wf_FormDefs.Add(new Wf_FormDef
            {
                Id = Guid.NewGuid(), FormKey = "leave", FormName = "请假申请",
                SchemaJson = JsonSerializer.Serialize(formSchema), Version = 1, Enable = true,
                Category = "人事行政", SubCategory = "考勤",
            });
        }

        if (!db.Wf_FlowDefs.Any(f => f.FormKey == "leave"))
        {
            var flowSchema = new FlowSchema
            {
                Start = "start",
                Nodes =
                {
                    new FlowNode { Id = "start",    Type = "start",    Name = "填單" },
                    new FlowNode { Id = "approval", Type = "approval", Name = "主管审批", ApproverStrategy = "Specified", ApproverUserId = approver },
                    new FlowNode { Id = "end",      Type = "end",      Name = "结束" },
                },
                Edges =
                {
                    new FlowEdge { From = "start",    To = "approval" },
                    new FlowEdge { From = "approval", To = "end" },
                },
            };
            db.Wf_FlowDefs.Add(new Wf_FlowDef
            {
                Id = Guid.NewGuid(), FlowKey = "leave-flow", FlowName = "请假审批流程", FormKey = "leave",
                SchemaJson = JsonSerializer.Serialize(flowSchema), Version = 1, Enable = true,
            });
        }

        db.SaveChanges();
    }
}
