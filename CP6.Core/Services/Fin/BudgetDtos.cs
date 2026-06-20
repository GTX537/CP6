using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>带数据的结果（既有 FinResult 无 Data 时用）。</summary>
public class FinResult<T>
{
    public bool Ok { get; init; }
    public string? Code { get; init; }
    public object[]? Args { get; init; }
    public T? Data { get; init; }
    public static FinResult<T> Pass(T data) => new() { Ok = true, Data = data };
    public static FinResult<T> Fail(string code, params object[] args) => new() { Ok = false, Code = code, Args = args };
}

public class BudgetLineDto
{
    public Guid? Id { get; set; }
    public Guid VersionId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? CostObjectType { get; set; }
    public string? CostObjectId { get; set; }
    public decimal AnnualAmount { get; set; }
    public string SpreadMode { get; set; } = "even";   // even | seasonal | manual
    public decimal[]? Periods { get; set; }             // manual/seasonal: 12 values (seasonal=weights)
    public BudgetControlMode? ControlMode { get; set; }
    public BudgetControlBasis? ControlBasis { get; set; }
    public string? Memo { get; set; }
    public byte[]? RowVersion { get; set; }
}

public class BudgetLineGridRow
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? CostObjectType { get; set; }
    public string? CostObjectId { get; set; }
    public decimal AnnualAmount { get; set; }
    public decimal[] Periods { get; set; } = new decimal[12];
    public BudgetControlMode? ControlMode { get; set; }
    public BudgetControlBasis? ControlBasis { get; set; }
    public string? Memo { get; set; }
    public byte[]? RowVersion { get; set; }
}

// C-3 import placeholder types (defined here to avoid forward-reference compile errors; C-3 will flesh these out)
public class BudgetImportPreviewResult
{
    public List<BudgetImportRow> Rows { get; set; } = new();
    public bool HasFatal => Rows.Any(r => !r.Ok);
}

public class BudgetImportRow
{
    public int RowNo { get; set; }
    public string AccountCode { get; set; } = "";
    public string? CostCenterCode { get; set; }
    public string? CostObjectType { get; set; }
    public string? CostObjectId { get; set; }
    public decimal[] Periods { get; set; } = new decimal[12];
    public bool Ok { get; set; }
    public string? Error { get; set; }
}

// F-1 预算对比实际报告 DTOs (spec §9)
public class BudgetVsActualRow
{
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public Guid? CostCenterId { get; set; }
    public string? CostObjectType { get; set; }
    public string? CostObjectId { get; set; }
    public decimal Budget { get; set; }
    public decimal Actual { get; set; }
    public decimal Variance => Budget - Actual;
    public decimal? VariancePct => Budget == 0 ? null : Math.Round((Budget - Actual) / Budget * 100, 2);
    public decimal[] BudgetPeriods { get; set; } = new decimal[12];
    public decimal[] ActualPeriods { get; set; } = new decimal[12];
    public bool IsUnbudgeted { get; set; }
}

public class BudgetVsActualReport
{
    public int FiscalYear { get; set; }
    public Guid? VersionId { get; set; }
    public List<BudgetVsActualRow> Rows { get; set; } = new();
    public decimal TotalBudget => Rows.Sum(r => r.Budget);
    public decimal TotalActual => Rows.Sum(r => r.Actual);
}

public class BudgetWarningDto
{
    public string AccountCode { get; set; } = "";
    public decimal Budget { get; set; }
    public decimal Used { get; set; }
    public decimal Incoming { get; set; }
    public bool IsBlock { get; set; }
}
