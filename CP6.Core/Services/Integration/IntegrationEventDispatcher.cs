using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels;

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
    };

    private readonly IMesBridgeHook _mes;
    private readonly IWmsBridgeHook _wms;
    private readonly IErpBridgeHook _erp;
    private readonly IOrderCancelBridgeHook _cancel;
    private readonly IFinBridgeHook _fin;

    public IntegrationEventDispatcher(
        IMesBridgeHook mes,
        IWmsBridgeHook wms,
        IErpBridgeHook erp,
        IOrderCancelBridgeHook cancel,
        IFinBridgeHook fin)
    {
        _mes = mes;
        _wms = wms;
        _erp = erp;
        _cancel = cancel;
        _fin = fin;
    }

    /// <summary>
    /// Builds the dispatcher route key from source, target, and hook name.
    /// </summary>
    public static string RouteKey(string source, string target, string hook)
        => $"{source}|{target}|{hook}";

    /// <inheritdoc />
    public Task<bool> DispatchAsync(IntegrationEvent evt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var key = RouteKey(evt.SourceModule, evt.TargetModule, evt.HookName);
        if (!Routes.TryGetValue(key, out var route))
        {
            throw new InvalidOperationException(
                $"DISPATCH-404: unknown route {evt.SourceModule}->{evt.TargetModule}.{evt.HookName}");
        }

        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        var context = new DispatchContext(_mes, _wms, _erp, _cancel, _fin, payload);
        return route(context);
    }

    private sealed class DispatchContext
    {
        public DispatchContext(
            IMesBridgeHook mes,
            IWmsBridgeHook wms,
            IErpBridgeHook erp,
            IOrderCancelBridgeHook cancel,
            IFinBridgeHook fin,
            JsonElement payload)
        {
            Mes = mes;
            Wms = wms;
            Erp = erp;
            Cancel = cancel;
            Fin = fin;
            Payload = payload;
        }

        public IMesBridgeHook Mes { get; }
        public IWmsBridgeHook Wms { get; }
        public IErpBridgeHook Erp { get; }
        public IOrderCancelBridgeHook Cancel { get; }
        public IFinBridgeHook Fin { get; }
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
}
