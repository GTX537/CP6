using System.Security.Claims;
using CP6.Core.Auth;
using CP6.Core.Services.ErpIntegration;
using CP6.Entity.DTOs.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>ERP staff maintains trading authority; independent internal CRM APIs remain read-only.</summary>
[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ErpCommerceController(ErpCommerceAuthority authority) : ControllerBase
{
    [HttpPut("/api/business-partners/{key}/commerce-profile")]
    [RequirePermission("erp-business-partner", "edit")]
    public Task<IActionResult> SetBusinessPartnerProfile(string key, [FromBody] ErpBusinessPartnerProfile input)
        => Execute(actor => authority.SetBusinessPartnerProfileAsync(key, input, actor, HttpContext.RequestAborted));

    [HttpPut("/api/quotations/{key}/commerce-terms")]
    [RequirePermission("erp-quotation", "edit")]
    public Task<IActionResult> SetQuotationTerms(string key, [FromBody] ErpQuotationTerms input)
        => Execute(actor => authority.SetQuotationTermsAsync(key, input, actor, HttpContext.RequestAborted));

    [HttpPost("/api/quotations/{key}/customer-acceptance")]
    [RequirePermission("erp-quotation", "confirm")]
    public Task<IActionResult> AcceptQuotation(string key, [FromBody] ErpQuotationAccept input)
        => Execute(actor => authority.AcceptQuotationAsync(key, input, actor, HttpContext.RequestAborted));

    [HttpDelete("/api/quotations/{key}/customer-acceptance")]
    [RequirePermission("erp-quotation", "confirm")]
    public Task<IActionResult> WithdrawQuotation(string key, [FromBody] ErpQuotationWithdraw input)
        => Execute(actor => authority.RevokeQuotationAcceptanceAsync(key, input, actor, HttpContext.RequestAborted));

    private async Task<IActionResult> Execute(Func<string, Task> action)
    {
        if (User.HasClaim(c => c.Type == "client_id" ||
                (c.Type is "sub" or ClaimTypes.NameIdentifier && c.Value.StartsWith("service:", StringComparison.Ordinal))))
            return Forbid();
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(actor)) return Forbid();
        try
        {
            await action(actor);
            return NoContent();
        }
        catch (ErpCommerceException ex)
        {
            var status = ex.Code switch
            {
                "C03_BUSINESS_PARTNER_NOT_FOUND" or "C03_QUOTATION_NOT_FOUND" => 404,
                "C03_PRECONDITION_REQUIRED" => 428,
                "C03_CONCURRENCY_CONFLICT" or "C03_ACCOUNT_ALREADY_BOUND" or "C03_ACCOUNT_BINDING_IMMUTABLE" => 409,
                _ => 400
            };
            return StatusCode(status, new { code = ex.Code });
        }
        catch (DbUpdateConcurrencyException)
        { return Conflict(new { code = "C03_CONCURRENCY_CONFLICT" }); }
    }
}
