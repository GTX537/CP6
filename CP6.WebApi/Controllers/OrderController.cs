using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers;

/// <summary>
/// MSBBPA070 / 080 / 090 — Web 受注 / 受注一覧 / 単価訂正 Web API
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public class OrderController : ControllerBase
{
    private readonly IOrderService _service;

    public OrderController(IOrderService service)
    {
        _service = service;
    }

    private string? CurrentUser => User?.Identity?.Name;

    // ═══════════════════════════════════════════════════════════
    //  PA070 受注入力
    // ═══════════════════════════════════════════════════════════

    /// <summary>採番 — GET /api/orders/next-seq</summary>
    [HttpGet("next-seq")]
    public async Task<IActionResult> GetNextSeq()
    {
        var seq = await _service.NextSequenceAsync();
        return Ok(new { code = 0, message = "OK", data = new { sequence = seq } });
    }

    /// <summary>手配NO検索（既存受注を引入）— GET /api/orders/by-haibai-no</summary>
    [HttpGet("by-haibai-no")]
    public async Task<IActionResult> GetByHaibaiNo([FromQuery] string? no1, [FromQuery] string? no2, [FromQuery] string? no3)
    {
        if (string.IsNullOrWhiteSpace(no1) && string.IsNullOrWhiteSpace(no2) && string.IsNullOrWhiteSpace(no3))
            return BadRequest(new { code = 400, message = "手配NO 1〜3 のいずれかを指定してください。" });

        var dto = await _service.LookupByHaibaiNoAsync(no1, no2, no3);
        if (dto == null)
            return NotFound(new { code = 404, message = "E10008: 検索結果がありません" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>セット製品CD部材引入 — GET /api/orders/by-set-product/{cd}</summary>
    [HttpGet("by-set-product/{cd}")]
    public async Task<IActionResult> GetBySetProduct(string cd)
    {
        var rows = await _service.LookupBySetProductCdAsync(cd);
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    /// <summary>製品基本マスタから第2画面 引入 — GET /api/orders/lookup-product-master/{cd}</summary>
    [HttpGet("lookup-product-master/{cd}")]
    public async Task<IActionResult> LookupProductMaster(string cd)
    {
        var dto = await _service.LookupProductMasterForDetailAsync(cd);
        if (dto == null)
            return NotFound(new { code = 404, message = "E10008: 製品マスタが見つかりません" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>製品加工工程マスタ 引入 — GET /api/orders/lookup-product-processes/{cd}</summary>
    [HttpGet("lookup-product-processes/{cd}")]
    public async Task<IActionResult> LookupProductProcesses(string cd)
    {
        var rows = await _service.LookupProductProcessesAsync(cd);
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    /// <summary>製品加工材料マスタ 引入 — GET /api/orders/lookup-product-materials/{cd}</summary>
    [HttpGet("lookup-product-materials/{cd}")]
    public async Task<IActionResult> LookupProductMaterials(string cd)
    {
        var rows = await _service.LookupProductMaterialsAsync(cd);
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    /// <summary>isEditable 判定 — GET /api/orders/calc-is-editable</summary>
    [HttpGet("calc-is-editable")]
    public async Task<IActionResult> CalcIsEditable(
        [FromQuery] string orderType,
        [FromQuery] string? catBig,
        [FromQuery] string? productCd)
    {
        var r = await _service.CheckIsEditableAsync(orderType, catBig, productCd);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>仕掛チェック（L2/L3）— GET /api/orders/check-wip</summary>
    [HttpGet("check-wip")]
    public async Task<IActionResult> CheckWip([FromQuery] string webOrderNo, [FromQuery] int detailNo)
    {
        var r = await _service.CheckWipAsync(webOrderNo, detailNo);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>与信チェック — GET /api/orders/credit-check</summary>
    [HttpGet("credit-check")]
    public async Task<IActionResult> CreditCheck([FromQuery] string customerCd, [FromQuery] decimal amount)
    {
        var r = await _service.CheckCreditAsync(customerCd, amount);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>預り売上数チェック — GET /api/orders/consigned-check</summary>
    [HttpGet("consigned-check")]
    public async Task<IActionResult> ConsignedCheck(
        [FromQuery] string webOrderNo, [FromQuery] int detailNo, [FromQuery] decimal qty)
    {
        var ok = await _service.CheckConsignedSalesQtyAsync(webOrderNo, detailNo, qty);
        return Ok(new { code = 0, message = "OK", data = new { ok } });
    }

    /// <summary>加工予定日計算 — POST /api/orders/lead-time</summary>
    [HttpPost("lead-time")]
    public async Task<IActionResult> CalcLeadTime([FromBody] LeadTimeRequestDto req)
    {
        var dates = await _service.CalcLeadTimeAsync(req.ShipDateTime ?? "", req.LeadTimeDays ?? new List<int>(), req.WgCd);
        return Ok(new { code = 0, message = "OK", data = dates });
    }

    /// <summary>製品区分 自動設定 — POST /api/orders/calc-product-category</summary>
    [HttpPost("calc-product-category")]
    public async Task<IActionResult> CalcProductCategory([FromBody] CalcProductCategoryRequest req)
    {
        var (big, mid, sml) = await _service.CalcProductCategoryAsync(req.ProductCd ?? "", req.Processes ?? new List<OrderProcessDto>());
        return Ok(new { code = 0, message = "OK", data = new { catBig = big, catMid = mid, catSml = sml } });
    }

    /// <summary>材料設定計算（BOM 展開）— POST /api/orders/calc-materials</summary>
    [HttpPost("calc-materials")]
    public async Task<IActionResult> CalcMaterials([FromBody] CalcMaterialsRequest req)
    {
        var rows = await _service.CalcMaterialsAsync(req.ProductCd ?? "", req.Processes ?? new List<OrderProcessDto>());
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    /// <summary>客先納期 vs 合計LT チェック — GET /api/orders/check-delivery-lt</summary>
    [HttpGet("check-delivery-lt")]
    public async Task<IActionResult> CheckDeliveryLt(
        [FromQuery] string productCd, [FromQuery] DateTime? orderDate, [FromQuery] DateTime? deliveryDate)
    {
        var (ok, ltSum, msg) = await _service.CheckDeliveryLeadTimeAsync(productCd, orderDate, deliveryDate);
        return Ok(new { code = 0, message = "OK", data = new { ok, ltSumDays = ltSum, message = msg } });
    }

    // ═══════════════════════════════════════════════════════════
    //  PA080 一覧照会
    // ═══════════════════════════════════════════════════════════

    /// <summary>受注一覧 検索 — GET /api/orders/list</summary>
    [HttpGet("list")]
    public async Task<IActionResult> SearchList([FromQuery] OrderQueryDto query)
    {
        // FROM≦TO バリデーション（E10036）
        if (!ValidateFromTo(query.OrderDateFrom, query.OrderDateTo, out var msg)) return BadRequest(new { code = 400, message = msg });
        if (!ValidateFromTo(query.DeliveryDateFrom, query.DeliveryDateTo, out msg)) return BadRequest(new { code = 400, message = msg });

        var (rows, total) = await _service.SearchOrdersAsync(query);
        return Ok(new { code = 0, message = "OK", data = new { rows, total } });
    }

    /// <summary>受注一覧 CSV 出力 — GET /api/orders/list/export.csv</summary>
    [HttpGet("list/export.csv")]
    public async Task<IActionResult> ExportListCsv([FromQuery] OrderQueryDto query)
    {
        var bytes = await _service.ExportListCsvAsync(query);
        var filename = $"orders_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", filename);
    }

    /// <summary>受注伝票 PDF 発行 — POST /api/orders/report</summary>
    [HttpPost("report")]
    public async Task<IActionResult> ExportReport([FromBody] ReportRequest req)
    {
        if (req?.Keys == null || req.Keys.Count == 0)
            return BadRequest(new { code = 400, message = "E10010: 発行対象がありません" });
        var bytes = await _service.ExportOrderReportPdfAsync(req.Keys);
        var filename = $"order_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt"; // 簡易版は txt として返す
        return File(bytes, "text/plain; charset=utf-8", filename);
    }

    // ═══════════════════════════════════════════════════════════
    //  PA090 単価訂正
    // ═══════════════════════════════════════════════════════════

    /// <summary>単価訂正対象一覧 — GET /api/orders/price-correction/list</summary>
    [HttpGet("price-correction/list")]
    public async Task<IActionResult> SearchPriceCorrection([FromQuery] OrderPriceCorrectionQueryDto query)
    {
        if (!ValidateFromTo(query.OrderDateFrom, query.OrderDateTo, out var msg)) return BadRequest(new { code = 400, message = msg });
        if (!ValidateFromTo(query.DeliveryDateFrom, query.DeliveryDateTo, out msg)) return BadRequest(new { code = 400, message = msg });
        if (query.QtyFrom.HasValue && query.QtyTo.HasValue && query.QtyFrom > query.QtyTo)
            return BadRequest(new { code = 400, message = "E10036: 数量 FROM≦TO の関係で指定してください" });
        if (query.AmountFrom.HasValue && query.AmountTo.HasValue && query.AmountFrom > query.AmountTo)
            return BadRequest(new { code = 400, message = "E10036: 金額 FROM≦TO の関係で指定してください" });

        try
        {
            var (rows, total) = await _service.SearchPriceCorrectionsAsync(query);
            return Ok(new { code = 0, message = "OK", data = new { rows, total } });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>金額リアルタイム計算 — GET /api/orders/price-correction/{webOrderNo}/{detailNo}/amount</summary>
    [HttpGet("price-correction/{webOrderNo}/{detailNo}/amount")]
    public async Task<IActionResult> CalcAmount(
        string webOrderNo, int detailNo,
        [FromQuery] decimal? indPrice, [FromQuery] decimal? setPrice)
    {
        var amount = await _service.CalcAmountAsync(webOrderNo, detailNo, indPrice, setPrice);
        return Ok(new { code = 0, message = "OK", data = new { amount } });
    }

    /// <summary>単価訂正バッチ更新 — PUT /api/orders/price-correction/batch</summary>
    [HttpPut("price-correction/batch")]
    public async Task<IActionResult> BatchUpdatePrice([FromBody] OrderPriceCorrectionBatchUpdateDto req)
    {
        try
        {
            var result = await _service.BatchUpdatePriceAsync(req, CurrentUser);
            return Ok(new { code = 0, message = "OK", data = result });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new
            {
                code = 409,
                message = "W10002: 更新対象が他の処理によって更新されています",
                msgId = "MSG-W10002"
            });
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  PA070 CRUD（{webOrderNo} ルートは最後に置く）
    // ═══════════════════════════════════════════════════════════

    /// <summary>受注詳細取得 — GET /api/orders/{webOrderNo}</summary>
    [HttpGet("{webOrderNo}")]
    public async Task<IActionResult> Get(string webOrderNo, [FromQuery] bool includeDeleted = false)
    {
        var dto = await _service.GetByWebOrderNoAsync(webOrderNo, includeDeleted);
        if (dto == null)
            return NotFound(new { code = 404, message = "受注が見つかりません" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>受注登録 — POST /api/orders</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] OrderDto dto)
    {
        try
        {
            var no = await _service.CreateAsync(dto, CurrentUser);
            var fresh = await _service.GetByWebOrderNoAsync(no);
            return Ok(new { code = 0, message = "OK", data = fresh });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>受注訂正 — PUT /api/orders/{webOrderNo}</summary>
    [HttpPut("{webOrderNo}")]
    public async Task<IActionResult> Update(string webOrderNo, [FromBody] OrderDto dto)
    {
        try
        {
            await _service.UpdateAsync(webOrderNo, dto, CurrentUser);
            var fresh = await _service.GetByWebOrderNoAsync(webOrderNo);
            return Ok(new { code = 0, message = "OK", data = fresh });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new
            {
                code = 409,
                message = "W10002: 更新対象が他の処理によって更新されています",
                msgId = "MSG-W10002"
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>受注削除（軟削除）— DELETE /api/orders/{webOrderNo}</summary>
    [HttpDelete("{webOrderNo}")]
    public async Task<IActionResult> Delete(string webOrderNo, [FromQuery] string? rowVersion)
    {
        try
        {
            byte[]? rv = null;
            if (!string.IsNullOrEmpty(rowVersion))
            {
                try { rv = Convert.FromBase64String(rowVersion); }
                catch { rv = null; }
            }
            await _service.DeleteAsync(webOrderNo, rv, CurrentUser);
            return Ok(new { code = 0, message = "OK" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new
            {
                code = 409,
                message = "W10002: 更新対象が他の処理によって更新されています",
                msgId = "MSG-W10002"
            });
        }
    }

    // ───── helpers ─────

    private static bool ValidateFromTo(DateTime? from, DateTime? to, out string msg)
    {
        msg = "";
        if (from.HasValue && to.HasValue && from > to)
        {
            msg = "E10036: FROM≦TO の関係で指定してください";
            return false;
        }
        return true;
    }

    // ═══════════════════════════════════════════════════════════
    //  Phase 6 — 受注取消（反向級联）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 受注取消 — POST /api/orders/{webOrderNo}/cancel
    /// </summary>
    /// <remarks>
    /// force=false（既定）: 探査モード — 関連 WO/Outbound の現状を返すのみ、DB 未変更。
    ///   半路状態あれば Outcome=NeedsDecision、画面で確認後 force=true で再呼出。
    /// force=true: 強制実施 — Bridge Hook 経由で全自動取消可能な WO/Outbound を取消。
    /// </remarks>
    [HttpPost("{webOrderNo}/cancel")]
    public async Task<IActionResult> Cancel(string webOrderNo, [FromBody] OrderCancelRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Reason))
        {
            return BadRequest(new { code = 400, message = "取消理由（reason）は必須です" });
        }

        var result = await _service.CancelAsync(webOrderNo, req.Reason, req.Force, CurrentUser);
        return Ok(new { code = 0, message = "OK", data = result });
    }
}

/// <summary>受注取消リクエスト（Phase 6）</summary>
public class OrderCancelRequest
{
    /// <summary>取消理由（必須、監査用）</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// false=探査モード（DB 未変更、半路状態あれば NeedsDecision を返す）/
    /// true=強制実施（半路状態でも自動取消可能な項目は全部取消）
    /// </summary>
    public bool Force { get; set; } = false;
}

/// <summary>加工予定日計算リクエスト</summary>
public class LeadTimeRequestDto
{
    public string? ShipDateTime { get; set; }
    public List<int>? LeadTimeDays { get; set; }
    public string? WgCd { get; set; }
}

/// <summary>製品区分自動設定リクエスト</summary>
public class CalcProductCategoryRequest
{
    public string? ProductCd { get; set; }
    public List<OrderProcessDto>? Processes { get; set; }
}

/// <summary>材料設定計算リクエスト</summary>
public class CalcMaterialsRequest
{
    public string? ProductCd { get; set; }
    public List<OrderProcessDto>? Processes { get; set; }
}

/// <summary>受注伝票 PDF 発行リクエスト</summary>
public class ReportRequest
{
    /// <summary>各キーは "{WebOrderNo}-{DetailNo}" 形式</summary>
    public List<string> Keys { get; set; } = new();
}
