namespace CP6.Persistence.PostgreSql;

/// <summary>Audited final objects absent from the shared EF snapshot.</summary>
internal static class PostgreSqlCoreObjectsV1
{
    public static string OidcSql => $$"""
        CREATE TABLE public."CrmOidcGrant" (
          "Id" uuid NOT NULL CONSTRAINT "PK_CrmOidcGrant" PRIMARY KEY,
          {{Text("CodeHash", 64, false)}}, {{Text("ClientId", 100, false)}},
          {{Text("RedirectUri", 2048, true)}}, {{Text("Challenge", 43, false)}},
          {{Text("Nonce", 256, true)}}, "SubjectId" uuid NOT NULL, "OrganizationId" uuid NOT NULL,
          {{Text("SourceJti", 100, false)}}, {{Text("SourceRefreshHash", 128, true)}},
          {{Text("SecurityStamp", 64, false)}},
          "ExpiresAtUtc" timestamp with time zone NOT NULL,
          "SourceExpiresAtUtc" timestamp with time zone NOT NULL,
          "AccessExpiresAtUtc" timestamp with time zone NOT NULL,
          "Consumed" boolean NOT NULL, "Revoked" boolean NOT NULL,
          CONSTRAINT "UX_CrmOidcGrant_CodeHash" UNIQUE("CodeHash")
        );
        CREATE INDEX "IX_CrmOidcGrant_AccessExpiresAtUtc" ON public."CrmOidcGrant"("AccessExpiresAtUtc");
        CREATE TABLE public."CrmOidcLogout" (
          {{Text("TicketHash", 64, false)}} CONSTRAINT "PK_CrmOidcLogout" PRIMARY KEY,
          "GrantId" uuid NOT NULL, {{Text("RedirectUri", 2048, true)}},
          "ExpiresAtUtc" timestamp with time zone NOT NULL
        );
        ALTER TABLE public."Sys_Users" ALTER COLUMN "AuthenticationEpoch"
          SET DEFAULT '00000000-0000-0000-0000-000000000000'::uuid;
        """;

    private static string Text(string name, int capacity, bool unicode)
    {
        var raw = $"convert_from(pg_catalog.bpcharsend(\"{name}\"),'UTF8')";
        var length = unicode ? $"char_length(regexp_replace({raw} COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g'))"
            : $"public.cp6_cp936_length_v1({raw})";
        return $"\"{name}\" bpchar COLLATE \"C\" NOT NULL CHECK({length}<={capacity})";
    }

    public const string JournalSql = """
        CREATE FUNCTION cp6_storage_core.guard_journal_line_v1()
        RETURNS trigger LANGUAGE plpgsql AS $cp6$
        DECLARE parent_status integer;
        BEGIN
          -- SHARE conflicts with the posting UPDATE's row lock. KEY SHARE would
          -- permit a non-key Status UPDATE and still allow the draft MVCC race.
          SELECT "Status" INTO parent_status FROM public."Fin_JournalEntry"
            WHERE "Id"=OLD."EntryId" FOR SHARE;
          IF parent_status IN(2,4) THEN
            RAISE EXCEPTION 'E-FIN-160' USING ERRCODE='P0001', CONSTRAINT='trg_FinJournalLine_NoMutate';
          END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; END IF;
          RETURN NEW;
        END $cp6$;
        CREATE TRIGGER "trg_FinJournalLine_NoMutate" BEFORE UPDATE OR DELETE
        ON public."Fin_JournalLine" FOR EACH ROW EXECUTE FUNCTION cp6_storage_core.guard_journal_line_v1();
        """;

    public const string MesSql = """
        CREATE INDEX "IX_T_ProductionResult_CreateDate_IsDeleted"
          ON public."T_ProductionResult"("CreateDate","IsDeleted") INCLUDE("GoodQty","DefectQty","MachineCd");
        CREATE INDEX "IX_T_WorkOrder_ActualEndDate"
          ON public."T_WorkOrder"("ActualEndDate","Status","IsDeleted");

        -- Caller can supply its existing business wall-clock date explicitly.
        CREATE FUNCTION public.cp6_mes_dashboard_summary_v1(business_day date DEFAULT CURRENT_DATE)
        RETURNS TABLE("InProgressCount" integer,"CompletedCount" integer,"TotalGoodQty" numeric,
                      "TotalDefectQty" numeric,"DefectRate" numeric,"DelayedCount" integer)
        LANGUAGE plpgsql STABLE AS $cp6$
        DECLARE good numeric(21,8); defect numeric(21,8);
        BEGIN
          SELECT COALESCE(SUM("GoodQty"),0),COALESCE(SUM("DefectQty"),0) INTO good,defect
            FROM public."T_ProductionResult" WHERE NOT "IsDeleted"
              AND "CreateDate">=business_day AND "CreateDate"<business_day+1;
          RETURN QUERY SELECT
            (SELECT COUNT(*)::integer FROM public."T_WorkOrder" WHERE NOT "IsDeleted" AND "Status"=3),
            (SELECT COUNT(*)::integer FROM public."T_WorkOrder" WHERE NOT "IsDeleted" AND "Status" IN(4,6)
               AND "ActualEndDate">=business_day AND "ActualEndDate"<business_day+1),
            good,defect,CASE WHEN good+defect>0 THEN (defect/(good+defect)*100)::numeric(8,2) ELSE 0::numeric END,
            (SELECT COUNT(*)::integer FROM public."T_WorkOrder" WHERE NOT "IsDeleted" AND "PlanEndDate"<business_day
               AND "Status" NOT IN(4,6,9));
        END $cp6$;

        CREATE FUNCTION public.cp6_mes_daily_trend_v1(days integer DEFAULT 30,business_day date DEFAULT CURRENT_DATE)
        RETURNS TABLE("Date" text,"GoodQty" numeric,"DefectQty" numeric)
        LANGUAGE plpgsql STABLE AS $cp6$
        BEGIN
          IF days<1 OR days>366 THEN RAISE EXCEPTION 'CP6 MES day range must be 1..366' USING ERRCODE='22023'; END IF;
          RETURN QUERY WITH dates AS
            (SELECT business_day-days+1+offset_day AS day FROM generate_series(0,days-1) AS offset_day),
          totals AS (SELECT "CreateDate"::date AS day,SUM("GoodQty") AS good,SUM("DefectQty") AS defect
            FROM public."T_ProductionResult" WHERE NOT "IsDeleted" AND "CreateDate">=business_day-days+1
              AND "CreateDate"<business_day+1 GROUP BY "CreateDate"::date)
          SELECT to_char(dates.day,'YYYY-MM-DD'),COALESCE(totals.good,0),COALESCE(totals.defect,0)
            FROM dates LEFT JOIN totals ON dates.day=totals.day ORDER BY dates.day;
        END $cp6$;

        CREATE FUNCTION public.cp6_mes_process_progress_v1()
        RETURNS TABLE("ProcessCd" bpchar,"ProcessName" bpchar,"NotStarted" integer,"InProgress" integer,"Completed" integer)
        LANGUAGE sql STABLE AS $cp6$
          SELECT "ProcessCd","ProcessName",
            (COUNT(*) FILTER(WHERE "ProcessStatus"=0))::integer,
            (COUNT(*) FILTER(WHERE "ProcessStatus" IN(1,3)))::integer,
            (COUNT(*) FILTER(WHERE "ProcessStatus"=2))::integer
          FROM public."T_WorkOrderProcess" WHERE NOT "IsDeleted"
          GROUP BY "ProcessCd","ProcessName" ORDER BY "ProcessCd"
        $cp6$;
        """;
}
