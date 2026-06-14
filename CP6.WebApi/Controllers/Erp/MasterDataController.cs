using CP6.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>
/// 主数据查询 API（MSBBPA010 第1页下拉依赖）
/// </summary>
[ApiController]
[Route("api/master")]
[Authorize]
public class MasterDataController : LocalizedControllerBase
{
    private readonly IMasterDataService _service;

    public MasterDataController(IMasterDataService service)
    {
        _service = service;
    }

    /// <summary>GET /api/master/bases  据点下拉</summary>
    [HttpGet("bases")]
    public async Task<IActionResult> GetBases()
    {
        var list = await _service.GetBasesAsync();
        return Ok(new { code = 0, message = "OK", data = list });
    }

    /// <summary>GET /api/master/staffs?baseCd=01  按据点过滤担当者</summary>
    [HttpGet("staffs")]
    public async Task<IActionResult> GetStaffs([FromQuery] string? baseCd)
    {
        var list = await _service.GetStaffsAsync(baseCd);
        return Ok(new { code = 0, message = "OK", data = list });
    }

    /// <summary>
    /// GET /api/master/generic-codes?groupCode=OrderType  通用代码组查询
    /// 常用 groupCode：OrderType（受注区分）/ ParentChildDiv（親子区分）/ SheetFlute（シート段）/
    ///              FscDiv（FSC 区分）/ ProductCategory / ProductShape1 / DistDiv / AdShape / Unit /
    ///              M014（印刷）/ M038（工程）/ M044（据点阈值）/ M067（段成率）
    /// </summary>
    [HttpGet("generic-codes")]
    public async Task<IActionResult> GetGenericCodes([FromQuery] string groupCode)
    {
        if (string.IsNullOrWhiteSpace(groupCode))
            return BadRequest(new { code = 400, message = Localizer["groupCode 不能为空"] });

        var list = await _service.GetGenericCodesAsync(groupCode);
        return Ok(new { code = 0, message = "OK", data = list });
    }

    /// <summary>
    /// GET /api/master/lookup/customer  得意先ルックアップ（Master Popup 共通）
    /// </summary>
    /// <remarks>MSBBPACOM 共通設計書 §項目定義 「テ:取引先マスタ」 パターン</remarks>
    [HttpGet("lookup/customer")]
    public async Task<IActionResult> LookupCustomer(
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.SearchCustomersAsync(keyword, page, pageSize);
        return Ok(new { code = 0, message = "OK", data = result });
    }

    /// <summary>
    /// GET /api/master/lookup/product  製品マスタルックアップ（Master Popup 共通）
    /// </summary>
    /// <remarks>セット品検索 / 既存製品参照に共用</remarks>
    [HttpGet("lookup/product")]
    public async Task<IActionResult> LookupProduct(
        [FromQuery] string? keyword,
        [FromQuery] string? customerCd,
        [FromQuery] bool onlyApproved = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.SearchProductsAsync(keyword, customerCd, onlyApproved, page, pageSize);
        return Ok(new { code = 0, message = "OK", data = result });
    }
}
