using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <summary>
    /// MES Phase 4 — SQL Server ストアドプロシージャ + 索引追加（性能調優）
    /// </summary>
    /// <remarks>
    /// JD「SQL Server 存儲過程・性能調優」サンプル：
    /// - usp_GetMesDashboardSummary  本日 KPI を 1 回往復で取得
    /// - usp_GetMesDailyTrend         日別良品/不良数推移（@Days パラメータ化）
    /// - usp_GetMesProcessProgress    工程別進捗集計
    /// 加 + 複合インデックス（ProductionResult.CreateDate, WorkOrder.ActualEndDate）
    /// </remarks>
    public partial class AddMesStoredProcedures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─────────────────────────────────────────────────────
            //  Index 追加（重集計クエリ用）
            // ─────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_T_ProductionResult_CreateDate_IsDeleted')
    CREATE INDEX IX_T_ProductionResult_CreateDate_IsDeleted
        ON [T_ProductionResult] ([CreateDate], [IsDeleted]) INCLUDE ([GoodQty], [DefectQty], [MachineCd]);
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_T_WorkOrder_ActualEndDate')
    CREATE INDEX IX_T_WorkOrder_ActualEndDate
        ON [T_WorkOrder] ([ActualEndDate], [Status], [IsDeleted]);
");

            // ─────────────────────────────────────────────────────
            //  usp_GetMesDashboardSummary
            // ─────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.usp_GetMesDashboardSummary', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetMesDashboardSummary;
");
            migrationBuilder.Sql(@"
CREATE PROCEDURE dbo.usp_GetMesDashboardSummary
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @today date = CAST(GETDATE() AS date);
    DECLARE @tomorrow date = DATEADD(day, 1, @today);

    DECLARE @inProgress int, @completed int, @good decimal(21,8), @defect decimal(21,8), @delayed int;

    SELECT @inProgress = COUNT(*)
    FROM T_WorkOrder
    WHERE IsDeleted = 0 AND [Status] = 3;

    SELECT @completed = COUNT(*)
    FROM T_WorkOrder
    WHERE IsDeleted = 0
      AND [Status] IN (4, 6)
      AND ActualEndDate >= @today AND ActualEndDate < @tomorrow;

    SELECT
        @good   = ISNULL(SUM(GoodQty), 0),
        @defect = ISNULL(SUM(DefectQty), 0)
    FROM T_ProductionResult
    WHERE IsDeleted = 0
      AND CreateDate >= @today AND CreateDate < @tomorrow;

    SELECT @delayed = COUNT(*)
    FROM T_WorkOrder
    WHERE IsDeleted = 0
      AND PlanEndDate < @today
      AND [Status] NOT IN (4, 6, 9);

    SELECT
        InProgressCount = @inProgress,
        CompletedCount  = @completed,
        TotalGoodQty    = @good,
        TotalDefectQty  = @defect,
        DefectRate      = CASE WHEN (@good + @defect) > 0
                               THEN CAST(@defect / (@good + @defect) * 100 AS decimal(8,2))
                               ELSE 0 END,
        DelayedCount    = @delayed;
END
");

            // ─────────────────────────────────────────────────────
            //  usp_GetMesDailyTrend (@Days)
            // ─────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.usp_GetMesDailyTrend', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetMesDailyTrend;
");
            migrationBuilder.Sql(@"
CREATE PROCEDURE dbo.usp_GetMesDailyTrend
    @Days int = 30
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @to date = DATEADD(day, 1, CAST(GETDATE() AS date));
    DECLARE @from date = DATEADD(day, -@Days + 1, CAST(GETDATE() AS date));

    -- 日付シーケンス（@Days 件）を再帰 CTE で生成
    ;WITH Dates AS (
        SELECT @from AS d
        UNION ALL
        SELECT DATEADD(day, 1, d) FROM Dates WHERE d < DATEADD(day, -1, @to)
    ),
    Agg AS (
        SELECT
            CAST(CreateDate AS date) AS d,
            SUM(GoodQty)   AS GoodQty,
            SUM(DefectQty) AS DefectQty
        FROM T_ProductionResult
        WHERE IsDeleted = 0
          AND CreateDate >= @from AND CreateDate < @to
        GROUP BY CAST(CreateDate AS date)
    )
    SELECT
        CONVERT(varchar(10), D.d, 23) AS [Date],
        ISNULL(A.GoodQty, 0)   AS GoodQty,
        ISNULL(A.DefectQty, 0) AS DefectQty
    FROM Dates D
    LEFT JOIN Agg A ON D.d = A.d
    ORDER BY D.d
    OPTION (MAXRECURSION 366);
END
");

            // ─────────────────────────────────────────────────────
            //  usp_GetMesProcessProgress
            // ─────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.usp_GetMesProcessProgress', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetMesProcessProgress;
");
            migrationBuilder.Sql(@"
CREATE PROCEDURE dbo.usp_GetMesProcessProgress
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        ProcessCd,
        ProcessName,
        NotStarted = SUM(CASE WHEN ProcessStatus = 0 THEN 1 ELSE 0 END),
        InProgress = SUM(CASE WHEN ProcessStatus IN (1, 3) THEN 1 ELSE 0 END),
        Completed  = SUM(CASE WHEN ProcessStatus = 2 THEN 1 ELSE 0 END)
    FROM T_WorkOrderProcess
    WHERE IsDeleted = 0
    GROUP BY ProcessCd, ProcessName
    ORDER BY ProcessCd;
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF OBJECT_ID('dbo.usp_GetMesDashboardSummary', 'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetMesDashboardSummary;");
            migrationBuilder.Sql("IF OBJECT_ID('dbo.usp_GetMesDailyTrend', 'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetMesDailyTrend;");
            migrationBuilder.Sql("IF OBJECT_ID('dbo.usp_GetMesProcessProgress', 'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetMesProcessProgress;");
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_T_ProductionResult_CreateDate_IsDeleted') DROP INDEX IX_T_ProductionResult_CreateDate_IsDeleted ON [T_ProductionResult];");
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_T_WorkOrder_ActualEndDate') DROP INDEX IX_T_WorkOrder_ActualEndDate ON [T_WorkOrder];");
        }
    }
}
