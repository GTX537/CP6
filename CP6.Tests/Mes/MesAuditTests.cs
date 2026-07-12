using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Xunit;

namespace CP6.Tests.Mes;

/// <summary>
/// MES 生产事实 / 成本喂料 / 品質判定 / 设备主数据 关键实体字段级审计接线冒烟（M-MES 横切 T5）。
/// 照 <see cref="CP6.Tests.Erp.ErpAuditTests"/> / <see cref="CP6.Tests.Wms.WmsAuditTests"/> 范式：
/// InMemory + 假当前用户。验证贴 IAuditable 的 MES 实体（指図头/工程/材料明细·报工实绩·工序费率·
/// 工作中心/设备主数据·不良/検査记录）在 create/update 随业务行同周期写入 Sys_FieldAuditLogs
/// （真实断言实体名/字段/新旧值）；跨波票 WMS PlateMoldStock（含 MadeCost 製作費）同样坐实；
/// 并回归确认追加/派生型（OeeDaily 日次派生集計 / MesSequence 採番计数器，均未贴）不产生审计行（负测试）。
/// </summary>
public class MesAuditTests
{
    private sealed class FakeUser : ICurrentUserAccessor
    {
        public FakeUser(Guid? id, string? name) { UserId = id; UserName = name; }
        public Guid? UserId { get; }
        public string? UserName { get; }
    }

    private sealed record DiffRow(string Field, string? Old, string? New);

    private static List<DiffRow> ParseChanges(string json)
        => JsonSerializer.Deserialize<List<DiffRow>>(json) ?? new();

    private static readonly Guid _userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CP6Context Ctx()
        => TestHelper.CreateInMemoryContext(new FakeUser(_userId, "alice"));

    // ── 製造指図ヘッダ（WorkOrder）— 生産数量/完了数量 不可逆生産事実 ─────────────
    [Fact]
    public void Create_WorkOrder_writes_op1_audit_row()
    {
        using var db = Ctx();
        var wo = new WorkOrder { WorkOrderNo = "WO20260712-0001", ProductCd = "P001", ProductionQty = 1000m };
        db.Set<WorkOrder>().Add(wo);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(WorkOrder), rows[0].EntityName);
        Assert.Equal(wo.Id.ToString(), rows[0].EntityKey);
    }

    [Fact]
    public void Update_WorkOrder_completedQty_writes_op2_diff()
    {
        using var db = Ctx();
        var wo = new WorkOrder { WorkOrderNo = "WO20260712-0002", ProductCd = "P001", ProductionQty = 1000m, CompletedQty = 0m };
        db.Set<WorkOrder>().Add(wo);
        db.SaveChanges();

        wo.CompletedQty = 800m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(WorkOrder), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "CompletedQty" && d.Old == "0" && d.New == "800");
    }

    // ── 指図工程明細（WorkOrderProcess）— 实绩工时 直喂制造费用 ───────────────────
    [Fact]
    public void Update_WorkOrderProcess_actualMachineHour_writes_op2_diff()
    {
        using var db = Ctx();
        var wp = new WorkOrderProcess
        {
            WorkOrderNo = "WO20260712-0003",
            ProcessCd = "PC01",
            TaskCd = "TK01",
            ActualMachineHour = 2m,
        };
        db.Set<WorkOrderProcess>().Add(wp);
        db.SaveChanges();

        wp.ActualMachineHour = 3.5m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(WorkOrderProcess), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "ActualMachineHour" && d.Old == "2" && d.New == "3.5");
    }

    // ── 指図材料明細（WorkOrderMaterial）— 実績消費数量 不可逆消耗事実（喂材料成本）─
    [Fact]
    public void Update_WorkOrderMaterial_actualQty_writes_op2_diff()
    {
        using var db = Ctx();
        var wm = new WorkOrderMaterial
        {
            WorkOrderNo = "WO20260712-0004",
            ProcessCd = "PC01",
            MaterialCd = "M001",
            ActualQty = 0m,
        };
        db.Set<WorkOrderMaterial>().Add(wm);
        db.SaveChanges();

        wm.ActualQty = 120m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(WorkOrderMaterial), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "ActualQty" && d.New == "120");
    }

    // ── 製造実績（ProductionResult）— 报工事实 良品/不良数 + 工时喂成本 ──────────────
    [Fact]
    public void Create_ProductionResult_writes_op1_audit_row()
    {
        using var db = Ctx();
        var pr = new ProductionResult
        {
            ResultNo = "PR20260712-0001",
            WorkOrderNo = "WO20260712-0005",
            ProcessCd = "PC01",
            OperatorCd = "OP01",
            GoodQty = 500m,
        };
        db.Set<ProductionResult>().Add(pr);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(ProductionResult), rows[0].EntityName);
        Assert.Equal(pr.Id.ToString(), rows[0].EntityKey);
    }

    [Fact]
    public void Update_ProductionResult_laborHour_writes_op2_diff()
    {
        using var db = Ctx();
        var pr = new ProductionResult
        {
            ResultNo = "PR20260712-0002",
            WorkOrderNo = "WO20260712-0006",
            ProcessCd = "PC01",
            OperatorCd = "OP01",
            LaborHour = 1m,
        };
        db.Set<ProductionResult>().Add(pr);
        db.SaveChanges();

        pr.LaborHour = 2.25m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(ProductionResult), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "LaborHour" && d.Old == "1" && d.New == "2.25");
    }

    // ── 工序费率（ProcessCostRate）— 人工/制造费率 元/h 货币定价（T1 高危、直喂成本归集）────
    [Fact]
    public void Update_ProcessCostRate_laborRate_writes_op2_diff()
    {
        using var db = Ctx();
        var rate = new ProcessCostRate
        {
            WgCd = "WG01",
            LaborRate = 30m,
            OverheadRate = 50m,
            ValidFrom = new DateTime(2026, 7, 1),
        };
        db.Set<ProcessCostRate>().Add(rate);
        db.SaveChanges();

        rate.LaborRate = 35m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(ProcessCostRate), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "LaborRate" && d.Old == "30" && d.New == "35");
    }

    // ── 工作中心主数据（WorkCenter）— 日可用产能（CRP 地基）主数据留痕 ────────────────
    [Fact]
    public void Update_WorkCenter_dailyCapacity_writes_op2_diff()
    {
        using var db = Ctx();
        var wc = new WorkCenter { WgCd = "WG01", WgName = "印刷", DailyCapacityHours = 16m };
        db.Set<WorkCenter>().Add(wc);
        db.SaveChanges();

        wc.DailyCapacityHours = 20m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(WorkCenter), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "DailyCapacityHours" && d.Old == "16" && d.New == "20");
    }

    // ── 設備マスタ（Machine）— 能力/循环时间 主数据（喂 OEE 性能计算）────────────────
    [Fact]
    public void Create_Machine_writes_op1_audit_row()
    {
        using var db = Ctx();
        var m = new Machine { MachineCd = "MC01", MachineName = "印刷機1号" };
        db.Set<Machine>().Add(m);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Machine), rows[0].EntityName);
        Assert.Equal(m.Id.ToString(), rows[0].EntityKey);
    }

    // ── 設備停止記録（MachineDowntime）— 停止事実（OEE 可用率元数据）不可逆生産事実 ──────
    [Fact]
    public void Update_MachineDowntime_downtimeMinutes_writes_op2_diff()
    {
        using var db = Ctx();
        var dt = new MachineDowntime
        {
            DowntimeNo = "DT20260712-0001",
            MachineCd = "MC01",
            StartTime = new DateTime(2026, 7, 12, 9, 0, 0),
            DowntimeMinutes = 0,
        };
        db.Set<MachineDowntime>().Add(dt);
        db.SaveChanges();

        dt.DowntimeMinutes = 45;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(MachineDowntime), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "DowntimeMinutes" && d.Old == "0" && d.New == "45");
    }

    // ── 不良品記録（DefectRecord）— 不良数/是正処置 不可逆生産事実 + 处置状态机 ──────────
    [Fact]
    public void Update_DefectRecord_defectQty_writes_op2_diff()
    {
        using var db = Ctx();
        var dr = new DefectRecord
        {
            DefectNo = "DF20260712-0001",
            WorkOrderNo = "WO20260712-0007",
            CategoryCd = "D01",
            DefectQty = 10m,
            DefectDescription = "寸法不良",
        };
        db.Set<DefectRecord>().Add(dr);
        db.SaveChanges();

        dr.DefectQty = 15m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(DefectRecord), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "DefectQty" && d.Old == "10" && d.New == "15");
    }

    // ── 不良分類マスタ（DefectCategory）— 分类主数据留痕 ───────────────────────────
    [Fact]
    public void Create_DefectCategory_writes_op1_audit_row()
    {
        using var db = Ctx();
        var dc = new DefectCategory { CategoryCd = "D01", DetailCd = "D0101", CategoryName = "寸法不良" };
        db.Set<DefectCategory>().Add(dc);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(DefectCategory), rows[0].EntityName);
        Assert.Equal(dc.Id.ToString(), rows[0].EntityKey);
    }

    // ── 品質検査ヘッダ（QualityInspection）— 総合判定/処置 品質事実 ────────────────────
    [Fact]
    public void Update_QualityInspection_overallResult_writes_op2_diff()
    {
        using var db = Ctx();
        var qi = new QualityInspection
        {
            InspectionNo = "QC20260712-0001",
            WorkOrderNo = "WO20260712-0008",
            InspectorCd = "IN01",
            OverallResult = 1,
        };
        db.Set<QualityInspection>().Add(qi);
        db.SaveChanges();

        qi.OverallResult = 2;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(QualityInspection), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "OverallResult" && d.Old == "1" && d.New == "2");
    }

    // ── 品質検査項目明細（QualityInspectionItem）— 計測値/判定 品質計測事実 ─────────────
    [Fact]
    public void Update_QualityInspectionItem_measuredValue_writes_op2_diff()
    {
        using var db = Ctx();
        var item = new QualityInspectionItem
        {
            InspectionNo = "QC20260712-0002",
            ItemSeqNo = 1,
            MeasuredValue = 10.0m,
        };
        db.Set<QualityInspectionItem>().Add(item);
        db.SaveChanges();

        item.MeasuredValue = 10.5m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(QualityInspectionItem), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "MeasuredValue" && d.Old == "10.0" && d.New == "10.5");
    }

    // ── 検査項目テンプレート（InspectionTemplate）— 規格/上下限 品質判定基准主数据 ────────
    [Fact]
    public void Update_InspectionTemplate_upperLimit_writes_op2_diff()
    {
        using var db = Ctx();
        var tpl = new InspectionTemplate
        {
            TemplateCd = "TPL01",
            ItemSeqNo = 1,
            ItemName = "巾寸法",
            UpperLimit = 100m,
        };
        db.Set<InspectionTemplate>().Add(tpl);
        db.SaveChanges();

        tpl.UpperLimit = 102m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(InspectionTemplate), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "UpperLimit" && d.Old == "100" && d.New == "102");
    }

    // ── 跨波票：印版・木型 在庫（WMS PlateMoldStock）— MadeCost 製作費 decimal(18,2)货币 ───
    [Fact]
    public void Update_PlateMoldStock_madeCost_writes_op2_diff()
    {
        using var db = Ctx();
        var pm = new PlateMoldStock
        {
            PlateNo = "PLT-000001",
            PlateType = "PLATE",
            MadeCost = 30000m,
        };
        db.Set<PlateMoldStock>().Add(pm);
        db.SaveChanges();

        pm.MadeCost = 32000m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(PlateMoldStock), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "MadeCost" && d.Old == "30000" && d.New == "32000");
    }

    // ── 负测试：OEE 日次派生集計（OeeDaily）未贴 IAuditable → 零审计行 ─────────────────
    [Fact]
    public void Create_OeeDaily_writes_no_audit_row()
    {
        // 派生型日次集計：全字段皆由停止/实绩重算派生，无源真值、日频重算，豁免 IAuditable。
        using var db = Ctx();
        var oee = new OeeDaily
        {
            OeeDate = new DateTime(2026, 7, 12),
            MachineCd = "MC01",
            GoodQty = 900m,
            DefectQty = 10m,
            Oee = 85.5m,
        };
        db.Set<OeeDaily>().Add(oee);
        db.SaveChanges();

        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // ── 负测试：MES 採番計数器（MesSequence）未贴 IAuditable → 零审计行 ─────────────────
    [Fact]
    public void Update_MesSequence_writes_no_audit_row()
    {
        // 純採番計数器：每次採番自增，无业务字段/无货币，豁免 IAuditable。
        using var db = Ctx();
        var seq = new MesSequence { SeqKey = "WO", SeqDate = "2026-07-12", CurrentValue = 1 };
        db.Set<MesSequence>().Add(seq);
        db.SaveChanges();

        seq.CurrentValue = 2;
        db.SaveChanges();

        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }
}
