using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf.Executors;

/// <summary>
/// 动态租户连接器（WFS infra ④ / spec §5，D5 解析合并）：从 <see cref="Wf_Connector"/> 行 + 解密后的 auth 构造，
/// 经 <see cref="IHttpClientFactory"/> 发出真实 HTTP（baseURL/超时/认证 header 来自租户行，绝不进 SchemaJson，D4）。
/// <para>解密后的明文 auth 仅驻留本实例内存供单次调用，绝不外泄到 OutputVars/日志。构造由
/// <see cref="TenantConnectorResolver"/> 每次解析时临时 new，不进 DI，故不受启动期 <see cref="WfConnectorLeaseGuard"/>
/// 校验——租户连接器的租约安全由保存时 E-WF-028 前置守卫（TimeoutSec &lt; 租约）。</para>
/// </summary>
public sealed class DbWfConnector : IWfConnector
{
    private readonly string _baseUrl;
    private readonly string? _authJson;   // 解密后明文（无认证→null）
    private readonly int _timeoutSec;
    private readonly IHttpClientFactory _httpFactory;

    public string Name { get; }
    public string DisplayName { get; }

    /// <summary>单次调用上界＝TimeoutSec 换算（brief 未另指定值来源，用 TimeoutSec）。保存时 E-WF-028 已保证
    /// &lt; 租约，故与 <see cref="WfConnectorLeaseGuard"/> 口径同向。</summary>
    public TimeSpan? MaxCallDuration => TimeSpan.FromSeconds(_timeoutSec);

    public DbWfConnector(Wf_Connector row, string? decryptedAuthJson, IHttpClientFactory httpFactory)
    {
        Name = row.Name;
        DisplayName = row.DisplayName;
        _baseUrl = row.BaseUrl;
        _timeoutSec = row.TimeoutSec;
        _authJson = decryptedAuthJson;
        _httpFactory = httpFactory;
    }

    public async Task<ServiceTaskResult> CallAsync(string pathTemplate, string? paramsJson, ServiceTaskContext ctx)
    {
        // 模板求值：把 path 中 {var} 替换为 $.var（沿 EchoConnector 口径）
        var tplCtx = new ServiceTemplateCtx(
            varsJson:   ctx.VarsJson,
            actorId:    ctx.ActorId.ToString(),
            jobId:      ctx.JobId.ToString(),
            instanceId: ctx.InstanceId.ToString(),
            nowUtcIso:  ctx.NowUtc.ToString("O"));
        var resolvedPath = Regex.Replace(pathTemplate ?? "", @"\{(\w+)\}", m =>
            ServiceVarsHelper.ResolveValue("{" + m.Groups[1].Value + "}", tplCtx));

        Uri url;
        try
        {
            var baseUri = new Uri(_baseUrl.EndsWith("/") ? _baseUrl : _baseUrl + "/");
            url = new Uri(baseUri, resolvedPath.TrimStart('/'));
        }
        catch (Exception ex)
        {
            return ServiceTaskResult.Fail($"webApiCallFailed|badUrl:{ex.GetType().Name}");
        }

        var hasBody = !string.IsNullOrEmpty(paramsJson) && paramsJson != "{}";
        var method = hasBody ? HttpMethod.Post : HttpMethod.Get;

        using var req = new HttpRequestMessage(method, url);
        // 幂等键（P1-2）：executor at-least-once，崩溃可能重投，下游按此去重
        req.Headers.TryAddWithoutValidation("Idempotency-Key", $"wf-service-job-{ctx.JobId}");
        ApplyAuth(req);
        if (hasBody)
            req.Content = new StringContent(paramsJson!, Encoding.UTF8, "application/json");

        var client = _httpFactory.CreateClient("wf-connector");
        client.Timeout = TimeSpan.FromSeconds(_timeoutSec);

        HttpResponseMessage resp;
        try
        {
            resp = await client.SendAsync(req);
        }
        catch (Exception ex)
        {
            // 超时/网络异常 → 失败（job 层退避/路由处理）。绝不回显 auth。
            return ServiceTaskResult.Fail($"webApiCallFailed|{ex.GetType().Name}");
        }

        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return ServiceTaskResult.Fail($"webApiCallFailed|http{(int)resp.StatusCode}");

        return ServiceTaskResult.Ok(new Dictionary<string, object?>
        {
            ["statusCode"]   = (int)resp.StatusCode,
            ["responseBody"] = body,
        });
    }

    /// <summary>按 auth type 追加认证 header（bearer/basic/apiKey）。畸形 auth → 无 header（不抛，避免泄露）。</summary>
    private void ApplyAuth(HttpRequestMessage req)
    {
        if (string.IsNullOrEmpty(_authJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(_authJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type?.ToLowerInvariant())
            {
                case "bearer":
                    if (root.TryGetProperty("token", out var tok))
                        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {tok.GetString()}");
                    break;
                case "basic":
                    var user = root.TryGetProperty("username", out var u) ? u.GetString() : "";
                    var pass = root.TryGetProperty("password", out var p) ? p.GetString() : "";
                    var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                    req.Headers.TryAddWithoutValidation("Authorization", $"Basic {b64}");
                    break;
                case "apikey":
                    var header = (root.TryGetProperty("header", out var h) ? h.GetString() : null) ?? "X-Api-Key";
                    var key = root.TryGetProperty("key", out var k) ? k.GetString() : "";
                    req.Headers.TryAddWithoutValidation(header, key);
                    break;
            }
        }
        catch (JsonException)
        {
            // 畸形凭证 JSON：静默降级为无认证（不外泄，不阻断）
        }
    }
}
