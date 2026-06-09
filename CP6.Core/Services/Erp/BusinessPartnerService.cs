using System.Text;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

/// <summary>
/// MSBBPA110/120 Web 取引先マスタ業務サービス
/// </summary>
/// <remarks>
/// 仕様書 §8 — 21 条のバリデーションを実装。
/// FLG 変更不可チェック（MSG-018）は §4 注記の「13 種 FLG」を対象に訂正時のみ。
/// </remarks>
public class BusinessPartnerService : IBusinessPartnerService
{
    private readonly CP6Context _db;
    private static readonly Regex ZipPattern = new(@"^\d{3}-?\d{4}$", RegexOptions.Compiled);
    private static readonly Regex TelPattern = new(@"^[\d\-\(\)\s]+$", RegexOptions.Compiled);

    public BusinessPartnerService(CP6Context db) { _db = db; }

    // ═══════════════════════════════════════════════════════════
    //  CRUD
    // ═══════════════════════════════════════════════════════════

    public async Task<BusinessPartnerDto?> GetByCdAsync(string bpCd, bool includeDeleted = false)
    {
        var q = _db.BusinessPartners.AsNoTracking().Where(x => x.BpCd == bpCd);
        if (!includeDeleted) q = q.Where(x => !x.IsDeleted);
        var e = await q.FirstOrDefaultAsync();
        return e == null ? null : ToDto(e);
    }

    public Task<bool> ExistsAsync(string bpCd)
        => _db.BusinessPartners.AsNoTracking().AnyAsync(x => x.BpCd == bpCd && !x.IsDeleted);

    public async Task<string> CreateAsync(BusinessPartnerDto dto, string? userName, bool preRegister = false)
    {
        if (string.IsNullOrWhiteSpace(dto.BpCd))
            throw new InvalidOperationException("E10022: 取引先 CD は必須です。");
        if (await ExistsAsync(dto.BpCd))
            throw new InvalidOperationException($"E10035: 取引先コード '{dto.BpCd}' が既に存在します。");

        var errors = new List<string>();
        Validate(dto, isEdit: false, before: null, errors);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" / ", errors));

        var now = DateTime.Now;
        dto.Status = preRegister ? 0 : 1;
        var entity = FromDto(dto);
        entity.Creator = userName;
        entity.CreateDate = now;
        entity.Modifier = userName;
        entity.ModifyDate = now;
        entity.GifuInterfaceFlg = false;       // 岐阜 IF 連携 — 初期 false
        entity.McTransferFlg = false;
        _db.BusinessPartners.Add(entity);
        await _db.SaveChangesAsync();
        return entity.BpCd;
    }

    public async Task UpdateAsync(string bpCd, BusinessPartnerDto dto, string? userName)
    {
        var entity = await _db.BusinessPartners
            .FirstOrDefaultAsync(x => x.BpCd == bpCd && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"取引先 '{bpCd}' が見つかりません。");

        if (dto.RowVersion != null && dto.RowVersion.Length > 0)
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = dto.RowVersion;

        // FLG 変更不可チェック（仕様書 §4 注記 — MSG-018）
        var beforeDto = ToDto(entity);
        var flgCheck = CheckFlgChange(beforeDto, dto);
        if (!flgCheck.Ok)
            throw new InvalidOperationException($"E10033: {flgCheck.Message}");

        // 21 条バリデーション
        var errors = new List<string>();
        Validate(dto, isEdit: true, before: beforeDto, errors);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" / ", errors));

        ApplyDto(entity, dto);
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;
        entity.McTransferFlg = false;          // 内容変更 → mc 再転送対象
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string bpCd, byte[]? rowVersion, string? userName)
    {
        var entity = await _db.BusinessPartners
            .FirstOrDefaultAsync(x => x.BpCd == bpCd && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"取引先 '{bpCd}' が見つかりません。");
        if (rowVersion != null && rowVersion.Length > 0)
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;
        entity.IsDeleted = true;
        entity.Status = 9;
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  バリデーション（仕様書 §8 — 21 条）
    // ═══════════════════════════════════════════════════════════

    public void Validate(BusinessPartnerDto d, bool isEdit, BusinessPartnerDto? before, IList<string> errors)
    {
        // 1: 取引先名 / 拠点 CD 必須
        if (string.IsNullOrWhiteSpace(d.BpName)) errors.Add("E10022: 取引先名は必須です");
        if (string.IsNullOrWhiteSpace(d.BaseCd)) errors.Add("E10022: 拠点 CD は必須です");

        // 2: 9 FLG 全未チェック
        if (!d.CustomerFlg && !d.AccountsReceivableFlg && !d.BillingFlg && !d.ReceiptFlg
            && !d.DeliveryFlg && !d.SupplierFlg && !d.AccountsPayableFlg
            && !d.PaymentScheduleFlg && !d.PaymentFlg)
            errors.Add("E10030: 9 個の属性 FLG のいずれかを選択してください");

        // 3: 得意先 FLG=ON → 売掛先/営業担当/業務担当 必須
        if (d.CustomerFlg)
        {
            if (string.IsNullOrWhiteSpace(d.AccountsReceivableCd)) errors.Add("E10022: 得意先選択時、売掛先は必須");
            if (string.IsNullOrWhiteSpace(d.SalesStaffCd))         errors.Add("E10022: 得意先選択時、営業担当は必須");
            if (string.IsNullOrWhiteSpace(d.BusinessStaffCd))      errors.Add("E10022: 得意先選択時、業務担当は必須");
        }

        // 4: 売掛先 FLG=ON + 請求先未入力
        if (d.AccountsReceivableFlg && string.IsNullOrWhiteSpace(d.BillingCd))
            errors.Add("E10022: 売掛先選択時、請求先は必須");

        // 5: 請求先 FLG=ON + 入金先未入力
        if (d.BillingFlg && string.IsNullOrWhiteSpace(d.ReceiptCd))
            errors.Add("E10022: 請求先選択時、入金先は必須");

        // 6: 納品先 FLG=ON + 物流グループ未入力
        if (d.DeliveryFlg && string.IsNullOrWhiteSpace(d.LogisticsGroupCd))
            errors.Add("E10022: 納品先選択時、物流グループ CD は必須");

        // 7: 発注先 FLG=ON + 必須項目
        if (d.SupplierFlg)
        {
            if (string.IsNullOrWhiteSpace(d.SupplierPattern))         errors.Add("E10022: 発注先選択時、発注先パターンは必須");
            if (string.IsNullOrWhiteSpace(d.PaymentScheduleCd))       errors.Add("E10022: 発注先選択時、買掛先は必須");
            if (string.IsNullOrWhiteSpace(d.PurchaseFractionDiv))     errors.Add("E10022: 発注先選択時、仕入金額端数処理区分は必須");
            if (string.IsNullOrWhiteSpace(d.PurchaseTaxFractionDiv))  errors.Add("E10022: 発注先選択時、仕入税額端数処理区分は必須");
            if (string.IsNullOrWhiteSpace(d.SupplyConsignDiv))        errors.Add("E10022: 発注先選択時、支給積送管理区分は必須");
            if (string.IsNullOrWhiteSpace(d.PurchaseLotSplitDiv))     errors.Add("E10022: 発注先選択時、仕入ロット明細分割区分は必須");

            // 8: 発注先 + 有償支給先 FLG=ON
            if (d.PaidSupplyFlg)
            {
                if (string.IsNullOrWhiteSpace(d.PaidSupplyAmountFractionDiv)) errors.Add("E10022: 有償支給先 ON 時、支給金額端数処理区分は必須");
                if (string.IsNullOrWhiteSpace(d.PaidSupplyTaxFractionDiv))    errors.Add("E10022: 有償支給先 ON 時、支給税額端数処理区分は必須");
                if (string.IsNullOrWhiteSpace(d.PaidSupplyTaxCalcDiv))        errors.Add("E10022: 有償支給先 ON 時、支給税計算区分は必須");

                // 9: 発注先 + 有償支給先 + 買戻義務 FLG=ON
                if (d.RebuyObligationFlg && string.IsNullOrWhiteSpace(d.PaidSupplyTaxCd))
                    errors.Add("E10022: 買戻義務 ON 時、支給税 CD は必須");
            }
        }

        // 10: 買掛先 FLG=ON + 支払予定管理先未入力
        if (d.AccountsPayableFlg && string.IsNullOrWhiteSpace(d.PaymentScheduleCd))
            errors.Add("E10022: 買掛先選択時、支払予定管理先は必須");

        // 11: 支払予定管理先 FLG=ON + 支払先/担当部門未入力
        if (d.PaymentScheduleFlg)
        {
            if (string.IsNullOrWhiteSpace(d.PaymentCd))             errors.Add("E10022: 支払予定管理先選択時、支払先は必須");
            if (string.IsNullOrWhiteSpace(d.PaymentScheduleDeptCd)) errors.Add("E10022: 支払予定管理先選択時、担当部門 CD は必須");

            // 12: 支払予定管理先 + 都度支払 OFF + 支払締日 1 未入力
            if (!d.PaidEachTimeFlg && !d.PaymentClosingDay1.HasValue)
                errors.Add("E10022: 都度支払 OFF 時、支払締日 1 は必須");

            // 13: 支払予定管理先 + 支払税計算 ON + 支払税額端数処理区分未入力
            if (d.PaymentTaxCalcFlg && string.IsNullOrWhiteSpace(d.PaymentTaxFractionDiv))
                errors.Add("E10022: 支払税計算 ON 時、支払税額端数処理区分は必須");

            // 14: バッチ支払予定締 ON + 遅延日数未入力
            if (d.BatchPaymentScheduleFlg && !d.PaymentScheduleDelayDays.HasValue)
                errors.Add("E10022: バッチ支払予定締 ON 時、支払予定締遅延日数は必須");
        }

        // 15: 郵便番号パターン
        if (!string.IsNullOrEmpty(d.ZipCd) && !ZipPattern.IsMatch(d.ZipCd))
            errors.Add("E10031: 郵便番号のパターンが不正です");

        // 16: TEL/FAX パターン
        if (!string.IsNullOrEmpty(d.Tel) && !TelPattern.IsMatch(d.Tel))
            errors.Add("E10031: TEL のパターンが不正です");
        if (!string.IsNullOrEmpty(d.Fax) && !TelPattern.IsMatch(d.Fax))
            errors.Add("E10031: FAX のパターンが不正です");

        // 17: 締日 1〜31, 99
        Action<int?, string> chkDay = (v, n) =>
        {
            if (!v.HasValue) return;
            if (!((v >= 1 && v <= 31) || v == 99))
                errors.Add($"E10032: {n} は 1〜31 または 99 を指定してください");
        };
        chkDay(d.BillingClosingDay1, "請求締日 1");
        chkDay(d.PaymentClosingDay1, "支払締日 1");
        chkDay(d.CollectionPlannedDay, "集金予定日");
    }

    // ═══════════════════════════════════════════════════════════
    //  FLG 変更不可チェック（仕様書 §4 注記 — MSG-018）
    // ═══════════════════════════════════════════════════════════

    public BpFlgChangeCheckResult CheckFlgChange(BusinessPartnerDto before, BusinessPartnerDto after)
    {
        // 仕様書 §4 注記の 13 種 FLG を対象（変更があれば全部リスト）
        var changes = new List<string>();
        void Cmp(bool a, bool b, string label) { if (a != b) changes.Add(label); }
        Cmp(before.CustomerFlg, after.CustomerFlg, "得意先FLG");
        Cmp(before.AccountsReceivableFlg, after.AccountsReceivableFlg, "売掛先FLG");
        Cmp(before.BillingFlg, after.BillingFlg, "請求先FLG");
        Cmp(before.ReceiptFlg, after.ReceiptFlg, "入金先FLG");
        Cmp(before.SupplierFlg, after.SupplierFlg, "発注先FLG");
        Cmp(before.AccountsPayableFlg, after.AccountsPayableFlg, "買掛先FLG");
        Cmp(before.PaymentScheduleFlg, after.PaymentScheduleFlg, "支払予定管理先FLG");
        Cmp(before.PaymentFlg, after.PaymentFlg, "支払先FLG");
        Cmp(before.CreditMgmtFlg, after.CreditMgmtFlg, "与信管理先FLG");
        Cmp(before.DeliveryFlg, after.DeliveryFlg, "納品先FLG");
        Cmp(before.MakerFlg, after.MakerFlg, "メーカFLG");
        Cmp(before.PaidSupplyFlg, after.PaidSupplyFlg, "有償支給先FLG");
        Cmp(before.RebuyObligationFlg, after.RebuyObligationFlg, "買戻義務FLG");

        return changes.Count == 0
            ? new BpFlgChangeCheckResult { Ok = true }
            : new BpFlgChangeCheckResult
            {
                Ok = false,
                ChangedFlgs = changes,
                Message = $"以下の FLG は訂正時に変更できません：{string.Join(", ", changes)}",
            };
    }

    // ═══════════════════════════════════════════════════════════
    //  PA120 一覧
    // ═══════════════════════════════════════════════════════════

    /// <summary>取引先一覧 排序白名单（前端 el-table prop → BusinessPartner 属性名）</summary>
    private static readonly IReadOnlyDictionary<string, string> BpSortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = "Status",
            ["bpCd"] = "BpCd",
            ["bpName"] = "BpName",
            ["bpAbbrev"] = "BpAbbrev",
            ["salesStaffCd"] = "SalesStaffCd",
            ["businessStaffCd"] = "BusinessStaffCd",
            ["ein"] = "Ein",
            ["stdCoCd"] = "StdCoCd",
            ["createDate"] = "CreateDate",
        };

    public async Task<(List<BpListItemDto>, int)> SearchAsync(BpQueryDto q)
    {
        var iq = _db.BusinessPartners.AsNoTracking().Where(x => !x.IsDeleted);

        // 11 個の属性 FLG（OR 結合）— 全て OFF はクエリ前にバリデーションすべきだがガードとして空集合を返す
        var anyFlg = (q.IncludeCustomer ?? false) || (q.IncludeAccountsReceivable ?? false) || (q.IncludeBilling ?? false)
                  || (q.IncludeReceipt ?? false) || (q.IncludeDelivery ?? false) || (q.IncludeCreditMgmt ?? false)
                  || (q.IncludeSupplier ?? false) || (q.IncludeAccountsPayable ?? false) || (q.IncludePaymentSchedule ?? false)
                  || (q.IncludePayment ?? false) || (q.IncludeMaker ?? false);
        if (!anyFlg) return (new(), 0);

        iq = iq.Where(x =>
            ((q.IncludeCustomer ?? false) && x.CustomerFlg) ||
            ((q.IncludeAccountsReceivable ?? false) && x.AccountsReceivableFlg) ||
            ((q.IncludeBilling ?? false) && x.BillingFlg) ||
            ((q.IncludeReceipt ?? false) && x.ReceiptFlg) ||
            ((q.IncludeDelivery ?? false) && x.DeliveryFlg) ||
            ((q.IncludeCreditMgmt ?? false) && x.CreditMgmtFlg) ||
            ((q.IncludeSupplier ?? false) && x.SupplierFlg) ||
            ((q.IncludeAccountsPayable ?? false) && x.AccountsPayableFlg) ||
            ((q.IncludePaymentSchedule ?? false) && x.PaymentScheduleFlg) ||
            ((q.IncludePayment ?? false) && x.PaymentFlg) ||
            ((q.IncludeMaker ?? false) && x.MakerFlg));

        // ステータス
        var statusList = new List<int>();
        if (q.IncludePreRegistered ?? false) statusList.Add(0);
        if (q.IncludeRegistered ?? false) statusList.Add(1);
        if (statusList.Count > 0)
            iq = iq.Where(x => statusList.Contains(x.Status));

        if (q.RegisteredDateFrom.HasValue) iq = iq.Where(x => x.CreateDate >= q.RegisteredDateFrom.Value);
        if (q.RegisteredDateTo.HasValue) iq = iq.Where(x => x.CreateDate < q.RegisteredDateTo.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q.BpCd)) iq = iq.Where(x => x.BpCd.Contains(q.BpCd));
        if (!string.IsNullOrWhiteSpace(q.BpName)) iq = iq.Where(x => x.BpName.Contains(q.BpName));
        if (!string.IsNullOrWhiteSpace(q.Ein)) iq = iq.Where(x => x.Ein != null && x.Ein.Contains(q.Ein));
        if (!string.IsNullOrWhiteSpace(q.EinType)) iq = iq.Where(x => x.EinType == q.EinType);
        if (!string.IsNullOrWhiteSpace(q.StdCoCd)) iq = iq.Where(x => x.StdCoCd != null && x.StdCoCd.Contains(q.StdCoCd));
        if (!string.IsNullOrWhiteSpace(q.ZipCd)) iq = iq.Where(x => x.ZipCd != null && x.ZipCd.Contains(q.ZipCd));
        if (!string.IsNullOrWhiteSpace(q.Addr))
        {
            var p = q.Addr;
            iq = iq.Where(x => (x.Addr1 ?? "").Contains(p) || (x.Addr2 ?? "").Contains(p) || (x.Addr3 ?? "").Contains(p) || (x.Addr4 ?? "").Contains(p));
        }
        if (!string.IsNullOrWhiteSpace(q.Tel)) iq = iq.Where(x => x.Tel != null && x.Tel.Contains(q.Tel));
        if (!string.IsNullOrWhiteSpace(q.Fax)) iq = iq.Where(x => x.Fax != null && x.Fax.Contains(q.Fax));
        if (!string.IsNullOrWhiteSpace(q.AreaCd)) iq = iq.Where(x => x.AreaCd == q.AreaCd);
        if (!string.IsNullOrWhiteSpace(q.SalesStaffCd)) iq = iq.Where(x => x.SalesStaffCd == q.SalesStaffCd);
        if (!string.IsNullOrWhiteSpace(q.BusinessStaffCd)) iq = iq.Where(x => x.BusinessStaffCd == q.BusinessStaffCd);
        if (!string.IsNullOrWhiteSpace(q.BpClass01)) iq = iq.Where(x => x.BpClass01 == q.BpClass01);
        if (!string.IsNullOrWhiteSpace(q.BpClass02)) iq = iq.Where(x => x.BpClass02 == q.BpClass02);
        if (!string.IsNullOrWhiteSpace(q.BpClass03)) iq = iq.Where(x => x.BpClass03 == q.BpClass03);
        if (!string.IsNullOrWhiteSpace(q.BpClass04)) iq = iq.Where(x => x.BpClass04 == q.BpClass04);
        if (!string.IsNullOrWhiteSpace(q.BpClass05)) iq = iq.Where(x => x.BpClass05 == q.BpClass05);
        if (!string.IsNullOrWhiteSpace(q.BpClass06)) iq = iq.Where(x => x.BpClass06 == q.BpClass06);
        if (!string.IsNullOrWhiteSpace(q.BpClass07)) iq = iq.Where(x => x.BpClass07 == q.BpClass07);
        if (!string.IsNullOrWhiteSpace(q.BpClass08)) iq = iq.Where(x => x.BpClass08 == q.BpClass08);
        if (!string.IsNullOrWhiteSpace(q.BpClass09)) iq = iq.Where(x => x.BpClass09 == q.BpClass09);
        if (!string.IsNullOrWhiteSpace(q.BpClass10)) iq = iq.Where(x => x.BpClass10 == q.BpClass10);

        var total = await iq.CountAsync();
        if (q.MaxRows.HasValue && total > q.MaxRows.Value)
            throw new InvalidOperationException($"E10013: 検索件数が上限 {q.MaxRows.Value} 件を超えました（{total} 件）");

        iq = QuerySort.Apply(iq, q.SortField, q.SortOrder, BpSortMap, s => s.OrderBy(x => x.BpCd));

        var rows = await iq
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .Select(x => new BpListItemDto
            {
                Status = x.Status,
                BpCd = x.BpCd,
                BpName = x.BpName,
                BpAbbrev = x.BpAbbrev,
                SalesStaffCd = x.SalesStaffCd,
                BusinessStaffCd = x.BusinessStaffCd,
                Ein = x.Ein,
                StdCoCd = x.StdCoCd,
                Addr1 = x.Addr1, Addr2 = x.Addr2, Addr3 = x.Addr3, Addr4 = x.Addr4,
                CustomerFlg = x.CustomerFlg,
                AccountsReceivableFlg = x.AccountsReceivableFlg,
                BillingFlg = x.BillingFlg,
                ReceiptFlg = x.ReceiptFlg,
                DeliveryFlg = x.DeliveryFlg,
                CreditMgmtFlg = x.CreditMgmtFlg,
                SupplierFlg = x.SupplierFlg,
                AccountsPayableFlg = x.AccountsPayableFlg,
                PaymentScheduleFlg = x.PaymentScheduleFlg,
                PaymentFlg = x.PaymentFlg,
                MakerFlg = x.MakerFlg,
                CreateDate = x.CreateDate,
                Creator = x.Creator,
            })
            .ToListAsync();

        for (int i = 0; i < rows.Count; i++) rows[i].RowNo = (q.Page - 1) * q.PageSize + i + 1;
        return (rows, total);
    }

    public async Task<byte[]> ExportListCsvAsync(BpQueryDto q)
    {
        // 全件取得（ページング無視）
        var qry = new BpQueryDto
        {
            IncludeCustomer = q.IncludeCustomer, IncludeAccountsReceivable = q.IncludeAccountsReceivable,
            IncludeBilling = q.IncludeBilling, IncludeReceipt = q.IncludeReceipt,
            IncludeDelivery = q.IncludeDelivery, IncludeCreditMgmt = q.IncludeCreditMgmt,
            IncludeSupplier = q.IncludeSupplier, IncludeAccountsPayable = q.IncludeAccountsPayable,
            IncludePaymentSchedule = q.IncludePaymentSchedule, IncludePayment = q.IncludePayment,
            IncludeMaker = q.IncludeMaker,
            RegisteredDateFrom = q.RegisteredDateFrom, RegisteredDateTo = q.RegisteredDateTo,
            BpCd = q.BpCd, BpName = q.BpName, Ein = q.Ein, EinType = q.EinType, StdCoCd = q.StdCoCd,
            ZipCd = q.ZipCd, Addr = q.Addr, Tel = q.Tel, Fax = q.Fax, AreaCd = q.AreaCd,
            SalesWg = q.SalesWg, SalesStaffCd = q.SalesStaffCd,
            BusinessWg = q.BusinessWg, BusinessStaffCd = q.BusinessStaffCd,
            BpClass01 = q.BpClass01, BpClass02 = q.BpClass02, BpClass03 = q.BpClass03,
            BpClass04 = q.BpClass04, BpClass05 = q.BpClass05, BpClass06 = q.BpClass06,
            BpClass07 = q.BpClass07, BpClass08 = q.BpClass08, BpClass09 = q.BpClass09, BpClass10 = q.BpClass10,
            IncludePreRegistered = q.IncludePreRegistered, IncludeRegistered = q.IncludeRegistered,
            Page = 1, PageSize = int.MaxValue,
        };
        var (rows, _) = await SearchAsync(qry);

        var sb = new StringBuilder();
        sb.AppendLine("№,ステータス,取引先,取引先名,取引先別名1,営業担当,業務担当,法人番号,標準企業コード,住所1,住所2,得意先,売掛先,請求先,入金先,納品先,与信管理先,発注先,買掛先,支払予定管理先,支払先,メーカ,登録日,登録担当");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(',', new[] {
                r.RowNo.ToString(), r.Status.ToString(),
                E(r.BpCd), E(r.BpName), E(r.BpAbbrev),
                E(r.SalesStaffCd), E(r.BusinessStaffCd),
                E(r.Ein), E(r.StdCoCd), E(r.Addr1), E(r.Addr2),
                B(r.CustomerFlg), B(r.AccountsReceivableFlg), B(r.BillingFlg), B(r.ReceiptFlg),
                B(r.DeliveryFlg), B(r.CreditMgmtFlg), B(r.SupplierFlg), B(r.AccountsPayableFlg),
                B(r.PaymentScheduleFlg), B(r.PaymentFlg), B(r.MakerFlg),
                r.CreateDate?.ToString("yyyy-MM-dd") ?? "", E(r.Creator),
            }));
        }
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var output = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, output, preamble.Length, body.Length);
        return output;
    }

    private static string E(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
    private static string B(bool b) => b ? "○" : "";

    // ═══════════════════════════════════════════════════════════
    //  Mapping
    // ═══════════════════════════════════════════════════════════

    private static BusinessPartnerDto ToDto(BusinessPartner e) => new()
    {
        BpCd = e.BpCd, BpName = e.BpName, BpAbbrev = e.BpAbbrev, BaseCd = e.BaseCd, Status = e.Status,
        StdCoCd = e.StdCoCd, Ein = e.Ein, EinType = e.EinType, LocalPublicCd = e.LocalPublicCd, DenzaiNo = e.DenzaiNo,
        ZipCd = e.ZipCd, Addr1 = e.Addr1, Addr2 = e.Addr2, Addr3 = e.Addr3, Addr4 = e.Addr4,
        Tel = e.Tel, Fax = e.Fax, AreaCd = e.AreaCd,
        SalesStaffCd = e.SalesStaffCd, BusinessStaffCd = e.BusinessStaffCd,
        CustomerFlg = e.CustomerFlg, AccountsReceivableFlg = e.AccountsReceivableFlg,
        BillingFlg = e.BillingFlg, ReceiptFlg = e.ReceiptFlg,
        DeliveryFlg = e.DeliveryFlg, SupplierFlg = e.SupplierFlg,
        AccountsPayableFlg = e.AccountsPayableFlg, PaymentScheduleFlg = e.PaymentScheduleFlg,
        PaymentFlg = e.PaymentFlg, CreditMgmtFlg = e.CreditMgmtFlg,
        MakerFlg = e.MakerFlg, PaidSupplyFlg = e.PaidSupplyFlg, RebuyObligationFlg = e.RebuyObligationFlg,
        BpClass01 = e.BpClass01, BpClass02 = e.BpClass02, BpClass03 = e.BpClass03, BpClass04 = e.BpClass04,
        BpClass05 = e.BpClass05, BpClass06 = e.BpClass06, BpClass07 = e.BpClass07, BpClass08 = e.BpClass08,
        BpClass09 = e.BpClass09, BpClass10 = e.BpClass10,
        SalesAnalysis1 = e.SalesAnalysis1, SalesAnalysis2 = e.SalesAnalysis2, SalesAnalysis3 = e.SalesAnalysis3,
        ParentCustomerCd = e.ParentCustomerCd, UserConverterDiv = e.UserConverterDiv,
        AccountsReceivableCd = e.AccountsReceivableCd, CreditMgmtCd = e.CreditMgmtCd,
        CustomerDept = e.CustomerDept, CustomerContact = e.CustomerContact,
        CustomerContactTitle = e.CustomerContactTitle, RecyclingTarget = e.RecyclingTarget,
        SalesPostingDiv = e.SalesPostingDiv, SheetSalesCalcMethod = e.SheetSalesCalcMethod,
        SalesCalcDivM2 = e.SalesCalcDivM2, SalesCalcDivPiece = e.SalesCalcDivPiece,
        FractionCalcDiv = e.FractionCalcDiv, FullSheetSalesDiv = e.FullSheetSalesDiv,
        SlitterBillingDiv = e.SlitterBillingDiv,
        SlitterBillingUnitPrice = e.SlitterBillingUnitPrice, SlitterMaxFlow = e.SlitterMaxFlow,
        PrintMinBilling = e.PrintMinBilling, PrintMinBillingBelow = e.PrintMinBillingBelow,
        PrintMinBillingUnit = e.PrintMinBillingUnit,
        LaminateMinBilling = e.LaminateMinBilling, LaminateMinBillingBelow = e.LaminateMinBillingBelow,
        LaminateMinBillingUnit = e.LaminateMinBillingUnit,
        LaminateAddRate = e.LaminateAddRate, LaminateAddDisplay = e.LaminateAddDisplay,
        ProcessingMinEstimate = e.ProcessingMinEstimate, NewSheetUnitPriceBase = e.NewSheetUnitPriceBase,
        DeliverySlipOutDiv = e.DeliverySlipOutDiv, DeliverySlipIssueDiv = e.DeliverySlipIssueDiv,
        SpecialSlipDiv = e.SpecialSlipDiv, NightLoadDiv = e.NightLoadDiv, SizePrintDiv = e.SizePrintDiv,
        DeliveryCalcOutDiv = e.DeliveryCalcOutDiv, DeliveryCalcIssueDiv = e.DeliveryCalcIssueDiv,
        DeliveryCalcOutDiv2 = e.DeliveryCalcOutDiv2,
        DeliveryCalcAddressee = e.DeliveryCalcAddressee, DeliveryCalcSender = e.DeliveryCalcSender,
        DeliveryCalcZipCd = e.DeliveryCalcZipCd,
        DeliveryCalcAddr1 = e.DeliveryCalcAddr1, DeliveryCalcAddr2 = e.DeliveryCalcAddr2,
        DeliveryCalcAddr3 = e.DeliveryCalcAddr3, DeliveryCalcAddr4 = e.DeliveryCalcAddr4,
        BillingCd = e.BillingCd, BillingName = e.BillingName,
        ReceiptCd = e.ReceiptCd, ReceiptName = e.ReceiptName, CreditMgmtArCd = e.CreditMgmtArCd,
        BillingClosingDay1 = e.BillingClosingDay1,
        BillingPrintDiv = e.BillingPrintDiv, BillingSealDiv = e.BillingSealDiv,
        ElectronicBilling = e.ElectronicBilling,
        BillingAddressee = e.BillingAddressee, BillingSender = e.BillingSender,
        BillingZipCd = e.BillingZipCd, BillingAddr1 = e.BillingAddr1,
        BillingAddr2 = e.BillingAddr2, BillingAddr3 = e.BillingAddr3, BillingAddr4 = e.BillingAddr4,
        RemittanceName = e.RemittanceName, BankCd = e.BankCd, BankBranchCd = e.BankBranchCd,
        RemittanceAccount = e.RemittanceAccount, TempAccountDiv = e.TempAccountDiv,
        MainAccountRegDate = e.MainAccountRegDate, Drawer = e.Drawer,
        CollectionDiv = e.CollectionDiv, CollectionPlannedDay = e.CollectionPlannedDay,
        BillRotationDiv = e.BillRotationDiv,
        ReceiptAddressee = e.ReceiptAddressee, ReceiptSender = e.ReceiptSender,
        ReceiptZipCd = e.ReceiptZipCd, ReceiptAddr1 = e.ReceiptAddr1,
        ReceiptAddr2 = e.ReceiptAddr2, ReceiptAddr3 = e.ReceiptAddr3, ReceiptAddr4 = e.ReceiptAddr4,
        CollectionNote = e.CollectionNote,
        LogisticsGroupCd = e.LogisticsGroupCd, LogisticsGroupName = e.LogisticsGroupName,
        DeliveryDept = e.DeliveryDept, DeliveryContact = e.DeliveryContact,
        DeliveryContactTitle = e.DeliveryContactTitle,
        TruckLengthLimit = e.TruckLengthLimit, DeliveryTimeFrom = e.DeliveryTimeFrom,
        DeliveryTimeTo = e.DeliveryTimeTo, PlannedShipTime = e.PlannedShipTime,
        SupplierPattern = e.SupplierPattern,
        SubcontractTargetFlg = e.SubcontractTargetFlg, SubcontractPriceDiv = e.SubcontractPriceDiv,
        SupplyPostingDiv = e.SupplyPostingDiv, SupplyPriceChangeAllowFlg = e.SupplyPriceChangeAllowFlg,
        DeliveryConfirmFlg = e.DeliveryConfirmFlg,
        PurchaseFractionDiv = e.PurchaseFractionDiv, PurchaseTaxFractionDiv = e.PurchaseTaxFractionDiv,
        PurchaseTaxCd = e.PurchaseTaxCd, PurchaseTaxPriorityFlg = e.PurchaseTaxPriorityFlg,
        SupplierCalendarCd = e.SupplierCalendarCd, SupplyConsignDiv = e.SupplyConsignDiv,
        PurchaseLotSplitDiv = e.PurchaseLotSplitDiv, PurchasePostingDiv = e.PurchasePostingDiv,
        PaidSupplyFractionDiv = e.PaidSupplyFractionDiv, PaidSupplyTaxCd = e.PaidSupplyTaxCd,
        PaidSupplyTaxPriorityFlg = e.PaidSupplyTaxPriorityFlg,
        PaidSupplyAmountFractionDiv = e.PaidSupplyAmountFractionDiv,
        PaidSupplyTaxFractionDiv = e.PaidSupplyTaxFractionDiv, PaidSupplyTaxCalcDiv = e.PaidSupplyTaxCalcDiv,
        FscCertificationDiv = e.FscCertificationDiv,
        PaymentScheduleCd = e.PaymentScheduleCd, PaymentCd = e.PaymentCd,
        PaymentScheduleDeptCd = e.PaymentScheduleDeptCd,
        PaidEachTimeFlg = e.PaidEachTimeFlg, PaymentClosingDay1 = e.PaymentClosingDay1,
        PaymentTaxCalcFlg = e.PaymentTaxCalcFlg, PaymentTaxFractionDiv = e.PaymentTaxFractionDiv,
        BatchPaymentScheduleFlg = e.BatchPaymentScheduleFlg, PaymentScheduleDelayDays = e.PaymentScheduleDelayDays,
        GifuInterfaceFlg = e.GifuInterfaceFlg, McTransferFlg = e.McTransferFlg,
        RowVersion = e.RowVersion,
    };

    private static BusinessPartner FromDto(BusinessPartnerDto d)
    {
        var e = new BusinessPartner();
        ApplyDto(e, d);
        return e;
    }

    private static void ApplyDto(BusinessPartner e, BusinessPartnerDto d)
    {
        e.BpCd = d.BpCd; e.BpName = d.BpName; e.BpAbbrev = d.BpAbbrev; e.BaseCd = d.BaseCd; e.Status = d.Status;
        e.StdCoCd = d.StdCoCd; e.Ein = d.Ein; e.EinType = d.EinType; e.LocalPublicCd = d.LocalPublicCd; e.DenzaiNo = d.DenzaiNo;
        e.ZipCd = d.ZipCd; e.Addr1 = d.Addr1; e.Addr2 = d.Addr2; e.Addr3 = d.Addr3; e.Addr4 = d.Addr4;
        e.Tel = d.Tel; e.Fax = d.Fax; e.AreaCd = d.AreaCd;
        e.SalesStaffCd = d.SalesStaffCd; e.BusinessStaffCd = d.BusinessStaffCd;
        e.CustomerFlg = d.CustomerFlg; e.AccountsReceivableFlg = d.AccountsReceivableFlg;
        e.BillingFlg = d.BillingFlg; e.ReceiptFlg = d.ReceiptFlg;
        e.DeliveryFlg = d.DeliveryFlg; e.SupplierFlg = d.SupplierFlg;
        e.AccountsPayableFlg = d.AccountsPayableFlg; e.PaymentScheduleFlg = d.PaymentScheduleFlg;
        e.PaymentFlg = d.PaymentFlg; e.CreditMgmtFlg = d.CreditMgmtFlg;
        e.MakerFlg = d.MakerFlg; e.PaidSupplyFlg = d.PaidSupplyFlg; e.RebuyObligationFlg = d.RebuyObligationFlg;
        e.BpClass01 = d.BpClass01; e.BpClass02 = d.BpClass02; e.BpClass03 = d.BpClass03; e.BpClass04 = d.BpClass04;
        e.BpClass05 = d.BpClass05; e.BpClass06 = d.BpClass06; e.BpClass07 = d.BpClass07; e.BpClass08 = d.BpClass08;
        e.BpClass09 = d.BpClass09; e.BpClass10 = d.BpClass10;
        e.SalesAnalysis1 = d.SalesAnalysis1; e.SalesAnalysis2 = d.SalesAnalysis2; e.SalesAnalysis3 = d.SalesAnalysis3;
        e.ParentCustomerCd = d.ParentCustomerCd; e.UserConverterDiv = d.UserConverterDiv;
        e.AccountsReceivableCd = d.AccountsReceivableCd; e.CreditMgmtCd = d.CreditMgmtCd;
        e.CustomerDept = d.CustomerDept; e.CustomerContact = d.CustomerContact;
        e.CustomerContactTitle = d.CustomerContactTitle; e.RecyclingTarget = d.RecyclingTarget;
        e.SalesPostingDiv = d.SalesPostingDiv; e.SheetSalesCalcMethod = d.SheetSalesCalcMethod;
        e.SalesCalcDivM2 = d.SalesCalcDivM2; e.SalesCalcDivPiece = d.SalesCalcDivPiece;
        e.FractionCalcDiv = d.FractionCalcDiv; e.FullSheetSalesDiv = d.FullSheetSalesDiv;
        e.SlitterBillingDiv = d.SlitterBillingDiv;
        e.SlitterBillingUnitPrice = d.SlitterBillingUnitPrice; e.SlitterMaxFlow = d.SlitterMaxFlow;
        e.PrintMinBilling = d.PrintMinBilling; e.PrintMinBillingBelow = d.PrintMinBillingBelow;
        e.PrintMinBillingUnit = d.PrintMinBillingUnit;
        e.LaminateMinBilling = d.LaminateMinBilling; e.LaminateMinBillingBelow = d.LaminateMinBillingBelow;
        e.LaminateMinBillingUnit = d.LaminateMinBillingUnit;
        e.LaminateAddRate = d.LaminateAddRate; e.LaminateAddDisplay = d.LaminateAddDisplay;
        e.ProcessingMinEstimate = d.ProcessingMinEstimate; e.NewSheetUnitPriceBase = d.NewSheetUnitPriceBase;
        e.DeliverySlipOutDiv = d.DeliverySlipOutDiv; e.DeliverySlipIssueDiv = d.DeliverySlipIssueDiv;
        e.SpecialSlipDiv = d.SpecialSlipDiv; e.NightLoadDiv = d.NightLoadDiv; e.SizePrintDiv = d.SizePrintDiv;
        e.DeliveryCalcOutDiv = d.DeliveryCalcOutDiv; e.DeliveryCalcIssueDiv = d.DeliveryCalcIssueDiv;
        e.DeliveryCalcOutDiv2 = d.DeliveryCalcOutDiv2;
        e.DeliveryCalcAddressee = d.DeliveryCalcAddressee; e.DeliveryCalcSender = d.DeliveryCalcSender;
        e.DeliveryCalcZipCd = d.DeliveryCalcZipCd;
        e.DeliveryCalcAddr1 = d.DeliveryCalcAddr1; e.DeliveryCalcAddr2 = d.DeliveryCalcAddr2;
        e.DeliveryCalcAddr3 = d.DeliveryCalcAddr3; e.DeliveryCalcAddr4 = d.DeliveryCalcAddr4;
        e.BillingCd = d.BillingCd; e.BillingName = d.BillingName;
        e.ReceiptCd = d.ReceiptCd; e.ReceiptName = d.ReceiptName; e.CreditMgmtArCd = d.CreditMgmtArCd;
        e.BillingClosingDay1 = d.BillingClosingDay1;
        e.BillingPrintDiv = d.BillingPrintDiv; e.BillingSealDiv = d.BillingSealDiv;
        e.ElectronicBilling = d.ElectronicBilling;
        e.BillingAddressee = d.BillingAddressee; e.BillingSender = d.BillingSender;
        e.BillingZipCd = d.BillingZipCd; e.BillingAddr1 = d.BillingAddr1;
        e.BillingAddr2 = d.BillingAddr2; e.BillingAddr3 = d.BillingAddr3; e.BillingAddr4 = d.BillingAddr4;
        e.RemittanceName = d.RemittanceName; e.BankCd = d.BankCd; e.BankBranchCd = d.BankBranchCd;
        e.RemittanceAccount = d.RemittanceAccount; e.TempAccountDiv = d.TempAccountDiv;
        e.MainAccountRegDate = d.MainAccountRegDate; e.Drawer = d.Drawer;
        e.CollectionDiv = d.CollectionDiv; e.CollectionPlannedDay = d.CollectionPlannedDay;
        e.BillRotationDiv = d.BillRotationDiv;
        e.ReceiptAddressee = d.ReceiptAddressee; e.ReceiptSender = d.ReceiptSender;
        e.ReceiptZipCd = d.ReceiptZipCd; e.ReceiptAddr1 = d.ReceiptAddr1;
        e.ReceiptAddr2 = d.ReceiptAddr2; e.ReceiptAddr3 = d.ReceiptAddr3; e.ReceiptAddr4 = d.ReceiptAddr4;
        e.CollectionNote = d.CollectionNote;
        e.LogisticsGroupCd = d.LogisticsGroupCd; e.LogisticsGroupName = d.LogisticsGroupName;
        e.DeliveryDept = d.DeliveryDept; e.DeliveryContact = d.DeliveryContact;
        e.DeliveryContactTitle = d.DeliveryContactTitle;
        e.TruckLengthLimit = d.TruckLengthLimit; e.DeliveryTimeFrom = d.DeliveryTimeFrom;
        e.DeliveryTimeTo = d.DeliveryTimeTo; e.PlannedShipTime = d.PlannedShipTime;
        e.SupplierPattern = d.SupplierPattern;
        e.SubcontractTargetFlg = d.SubcontractTargetFlg; e.SubcontractPriceDiv = d.SubcontractPriceDiv;
        e.SupplyPostingDiv = d.SupplyPostingDiv; e.SupplyPriceChangeAllowFlg = d.SupplyPriceChangeAllowFlg;
        e.DeliveryConfirmFlg = d.DeliveryConfirmFlg;
        e.PurchaseFractionDiv = d.PurchaseFractionDiv; e.PurchaseTaxFractionDiv = d.PurchaseTaxFractionDiv;
        e.PurchaseTaxCd = d.PurchaseTaxCd; e.PurchaseTaxPriorityFlg = d.PurchaseTaxPriorityFlg;
        e.SupplierCalendarCd = d.SupplierCalendarCd; e.SupplyConsignDiv = d.SupplyConsignDiv;
        e.PurchaseLotSplitDiv = d.PurchaseLotSplitDiv; e.PurchasePostingDiv = d.PurchasePostingDiv;
        e.PaidSupplyFractionDiv = d.PaidSupplyFractionDiv; e.PaidSupplyTaxCd = d.PaidSupplyTaxCd;
        e.PaidSupplyTaxPriorityFlg = d.PaidSupplyTaxPriorityFlg;
        e.PaidSupplyAmountFractionDiv = d.PaidSupplyAmountFractionDiv;
        e.PaidSupplyTaxFractionDiv = d.PaidSupplyTaxFractionDiv; e.PaidSupplyTaxCalcDiv = d.PaidSupplyTaxCalcDiv;
        e.FscCertificationDiv = d.FscCertificationDiv;
        e.PaymentScheduleCd = d.PaymentScheduleCd; e.PaymentCd = d.PaymentCd;
        e.PaymentScheduleDeptCd = d.PaymentScheduleDeptCd;
        e.PaidEachTimeFlg = d.PaidEachTimeFlg; e.PaymentClosingDay1 = d.PaymentClosingDay1;
        e.PaymentTaxCalcFlg = d.PaymentTaxCalcFlg; e.PaymentTaxFractionDiv = d.PaymentTaxFractionDiv;
        e.BatchPaymentScheduleFlg = d.BatchPaymentScheduleFlg; e.PaymentScheduleDelayDays = d.PaymentScheduleDelayDays;
        e.GifuInterfaceFlg = d.GifuInterfaceFlg; e.McTransferFlg = d.McTransferFlg;
    }
}
