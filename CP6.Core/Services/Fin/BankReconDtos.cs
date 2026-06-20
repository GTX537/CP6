namespace CP6.Core.Services.Fin;

/// <summary>导入预览（dryRun）报告。</summary>
public class BankImportPreviewResult
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int StrongDupCount { get; set; }
    public int SuspectedDupCount { get; set; }
    public string ImportBatchNo { get; set; } = string.Empty;
    public List<BankImportRow> Rows { get; set; } = new();
    public List<BankImportRowError> Errors { get; set; } = new();
}

/// <summary>解析后的候选行（内存，未落库）。</summary>
public class BankImportRow
{
    public int SourceLineNo { get; set; }
    public DateTime TxnDate { get; set; }
    public int Direction { get; set; }            // 1 Deposit / 2 Withdrawal
    public decimal Amount { get; set; }
    public string? CurrencyCd { get; set; }
    public string? Description { get; set; }
    public string? CounterpartyName { get; set; }
    public string? RefNo { get; set; }
    public decimal? BalanceAfter { get; set; }
    public string RawRowJson { get; set; } = string.Empty;
    public string RawRowHash { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string DupKind { get; set; } = "None";  // None / Strong(W-A4-IMPORT-SKIP) / Suspected(W-A4-IMPORT-DUP)
    public bool Importable { get; set; } = true;    // 强重复默认 false
}

public class BankImportRowError
{
    public int SourceLineNo { get; set; }
    public string Code { get; set; } = string.Empty;  // E-A4-IMPORT-001
    public string RawText { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>解析结果（Importer 输出，含致命失败标志）。</summary>
public class BankImportParseResult
{
    public List<BankImportRow> Rows { get; set; } = new();
    public List<BankImportRowError> Errors { get; set; } = new();
    public bool HasFatalParseError => Errors.Count > 0;
}

/// <summary>账面侧候选凭证行（含银行侧带方向金额 + 排序信号）。</summary>
public class BankCandidateLine
{
    public Guid JournalLineId { get; set; }
    public Guid JournalEntryId { get; set; }
    public string EntryNo { get; set; } = string.Empty;
    public DateTime VoucherDate { get; set; }
    public decimal BankSignedAmount { get; set; }    // Debit=+,Credit=−（本位币或外币原币按账户币种）
    public string? CurrencyCd { get; set; }
    public string? PartnerId { get; set; }
    public string? Memo { get; set; }
    public int Rank { get; set; }                    // 排序优先级（越小越优）
}

/// <summary>人工撮合请求。</summary>
public class ManualMatchRequest
{
    public Guid StatementId { get; set; }
    public List<Guid> StatementLineIds { get; set; } = new();
    public List<Guid> JournalLineIds { get; set; } = new();
    public string? Note { get; set; }
}

// ── D 阶段占位 DTO（D-1/D-3 填充字段）──
public class BankOnlyLineResult { public Guid LineId { get; set; } public bool Ok { get; set; } public string? Code { get; set; } public Guid? JournalEntryId { get; set; } }
public class ReconciliationStatementDto { }   // D-3 填充字段
