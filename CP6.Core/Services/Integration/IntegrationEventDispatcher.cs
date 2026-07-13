using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Integration;

/// <summary>
/// Reflection-style dispatcher for persisted integration event retry payloads.
/// </summary>
public class IntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Dictionary<string, Func<DispatchContext, Task<bool>>> Routes = new()
    {
        [RouteKey("ERP", "MES", "OnOrderCreatedAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<OnOrderCreatedPayload>();
            var r = await ctx.Mes.OnOrderCreatedAsync(p.WebOrderNo, p.UserName);
            return r.Success;
        },
        [RouteKey("MES", "WMS", "OnWorkOrderIssuedAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<OnWorkOrderIssuedPayload>();
            var r = await ctx.Wms.OnWorkOrderIssuedAsync(p.WorkOrderNo, p.UserName);
            return r.Success;
        },
        [RouteKey("ERP", "WMS", "OnOrderCreatedAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<OnOrderCreatedPayload>();
            var r = await ctx.Wms.OnOrderCreatedAsync(p.WebOrderNo, p.UserName);
            return r.Success;
        },
        [RouteKey("MES", "WMS", "OnProductionCompletedAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<OnProductionCompletedPayload>();
            var r = await ctx.Wms.OnProductionCompletedAsync(p.WorkOrderNo, p.GoodQty, p.UserName);
            return r.Success;
        },
        [RouteKey("WMS", "ERP", "OnShipmentConfirmedAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<OnShipmentConfirmedPayload>();
            var r = await ctx.Erp.OnShipmentConfirmedAsync(p.OutboundNo, p.UserName);
            return r.Success;
        },
        [RouteKey("WMS", "FIN", "OnShipmentConfirmedAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<FinShipmentInvoiceRequest>();
            var r = await ctx.Fin.OnShipmentConfirmedAsync(p, null);
            return r.Success;
        },
        [RouteKey("WMS", "FIN", "OnShipmentCancelledAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<OnShipmentCancelledFinPayload>();
            var r = await ctx.Fin.OnShipmentCancelledAsync(p.ShipmentId, p.UserName);
            return r.Success;
        },
        [RouteKey("MES", "FIN", "OnWorkOrderCompletedAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<OnWorkOrderCompletedFinPayload>();
            var r = await ctx.Fin.OnWorkOrderCompletedAsync(p.WorkOrderNo, p.UserName);
            return r.Success;
        },
        [RouteKey("SPACE", "WMS", "OnLocationPublishedAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<LocationPublishBatch>();
            // 重试路径不重复落事件：Worker 负责更新原 IntegrationEvent 行的 Status/Attempts
            var r = await ctx.Space.OnLocationPublishedAsync(p, Guid.NewGuid(), persistEvent: false);
            return r.Success;
        },
    };

    private readonly IMesBridgeHook _mes;
    private readonly IWmsBridgeHook _wms;
    private readonly IErpBridgeHook _erp;
    private readonly IOrderCancelBridgeHook _cancel;
    private readonly IFinBridgeHook _fin;
    private readonly ISpaceBridgeHook _space;
    private readonly IWfTriggerBridgeHook _wfTrigger;

    public IntegrationEventDispatcher(
        IMesBridgeHook mes,
        IWmsBridgeHook wms,
        IErpBridgeHook erp,
        IOrderCancelBridgeHook cancel,
        IFinBridgeHook fin,
        ISpaceBridgeHook space,
        // 可选：既有 6 参构造点（测试）零改动仍编译；DI 已注册 IWfTriggerBridgeHook 会注入真实 hook。
        IWfTriggerBridgeHook? wfTrigger = null)
    {
        _mes = mes;
        _wms = wms;
        _erp = erp;
        _cancel = cancel;
        _fin = fin;
        _space = space;
        _wfTrigger = wfTrigger ?? new NoOpWfTriggerBridgeHook();
    }

    /// <summary>
    /// Builds the dispatcher route key from source, target, and hook name.
    /// </summary>
    public static string RouteKey(string source, string target, string hook)
        => $"{source}|{target}|{hook}";

    /// <inheritdoc />
    public async Task<bool> DispatchAsync(IntegrationEvent evt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var key = RouteKey(evt.SourceModule, evt.TargetModule, evt.HookName);

        // WF 触发器目标泛化路由（spec §3.3）：target=WF & hook=OnEventAsync 不看 source 直接路由。
        // 走 ReplayEventAsync（重放不再写新 outbox 行，映射表⑦）；DISPATCH-404 语义对其余路由不变。
        if (evt.TargetModule == "WF" && evt.HookName == nameof(IWfTriggerBridgeHook.OnEventAsync))
        {
            var p = JsonSerializer.Deserialize<WfTriggerEventPayload>(evt.PayloadJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("DISPATCH-400: empty WfTrigger payload");
            var r = await _wfTrigger.ReplayEventAsync(p.EventKey, p.EventId, p.PayloadJson, p.UserName);
            return r.Success;
        }

        if (!Routes.TryGetValue(key, out var route))
        {
            throw new InvalidOperationException(
                $"DISPATCH-404: unknown route {evt.SourceModule}->{evt.TargetModule}.{evt.HookName}");
        }

        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        var context = new DispatchContext(_mes, _wms, _erp, _cancel, _fin, _space, payload);
        return await route(context);
    }

    private sealed class DispatchContext
    {
        public DispatchContext(
            IMesBridgeHook mes,
            IWmsBridgeHook wms,
            IErpBridgeHook erp,
            IOrderCancelBridgeHook cancel,
            IFinBridgeHook fin,
            ISpaceBridgeHook space,
            JsonElement payload)
        {
            Mes = mes;
            Wms = wms;
            Erp = erp;
            Cancel = cancel;
            Fin = fin;
            Space = space;
            Payload = payload;
        }

        public IMesBridgeHook Mes { get; }
        public IWmsBridgeHook Wms { get; }
        public IErpBridgeHook Erp { get; }
        public IOrderCancelBridgeHook Cancel { get; }
        public IFinBridgeHook Fin { get; }
        public ISpaceBridgeHook Space { get; }
        public JsonElement Payload { get; }

        public T GetPayload<T>()
        {
            var result = Payload.Deserialize<T>(JsonOptions);
            if (result == null)
            {
                throw new InvalidOperationException("DISPATCH-400: payload deserialization returned null");
            }

            return result;
        }
    }

    private sealed class OnOrderCreatedPayload
    {
        public string WebOrderNo { get; set; } = string.Empty;
        public string? UserName { get; set; }
    }

    private sealed class OnWorkOrderIssuedPayload
    {
        public string WorkOrderNo { get; set; } = string.Empty;
        public string? UserName { get; set; }
    }

    private sealed class OnProductionCompletedPayload
    {
        public string WorkOrderNo { get; set; } = string.Empty;
        public decimal GoodQty { get; set; }
        public string? UserName { get; set; }
    }

    private sealed class OnShipmentConfirmedPayload
    {
        public string OutboundNo { get; set; } = string.Empty;
        public string? UserName { get; set; }
    }

    private sealed class OnShipmentCancelledFinPayload
    {
        public string ShipmentId { get; set; } = string.Empty;
        public string? UserName { get; set; }
    }

    private sealed class OnWorkOrderCompletedFinPayload
    {
        public string WorkOrderNo { get; set; } = string.Empty;
        public string? UserName { get; set; }
    }
}
