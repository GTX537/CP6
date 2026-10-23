using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class MaterialShortageNotifier : IMaterialShortageNotifier
{
    private const string HubTypeName = "CP6.WebApi.Hubs.WmsHub, CP6.WebApi";
    private const string HubContextTypeName = "Microsoft.AspNetCore.SignalR.IHubContext`1, Microsoft.AspNetCore.SignalR.Core";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MaterialShortageNotifier> _logger;
    private readonly CP6Context _db;

    public MaterialShortageNotifier(IServiceProvider serviceProvider, ILogger<MaterialShortageNotifier> logger, CP6Context db)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _db = db;
    }

    public async Task NotifyAsync(string workOrderNo, string productCd, decimal requiredQty)
    {
        try
        {
            if (_db.Database.IsNpgsql())
            {
                if (_db.CurrentTenantId == Guid.Empty || _db.Database.CurrentTransaction != null
                    || System.Transactions.Transaction.Current != null)
                    throw new InvalidOperationException("Committed trusted shortage notification context required.");
                // The production IWmsNotifier adapter sends an empty tenant inventory invalidation.
                // Do not pass document, product, quantity or actor from this legacy notification API.
                await _serviceProvider.GetRequiredService<IWmsNotifier>().NotifyStockChangedAsync(new StockChangedEvent());
                return;
            }
            var hubContext = ResolveWmsHubContext();
            if (hubContext == null)
            {
                _logger.LogWarning("WMS HubContext was not available for material shortage {WorkOrderNo}/{ProductCd}", workOrderNo, productCd);
                return;
            }

            var clients = hubContext.GetType().GetProperty("Clients")?.GetValue(hubContext);
            var all = clients?.GetType().GetProperty("All")?.GetValue(clients);
            if (all == null)
            {
                _logger.LogWarning("WMS HubContext clients were not available for material shortage {WorkOrderNo}/{ProductCd}", workOrderNo, productCd);
                return;
            }

            var payload = new
            {
                WorkOrderNo = workOrderNo,
                ProductCd = productCd,
                RequiredQty = requiredQty,
                DetectedAt = DateTime.UtcNow,
            };

            var method = all.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                    m.Name == "SendCoreAsync"
                    && m.GetParameters().Length == 3);
            if (method == null)
            {
                _logger.LogWarning("SendCoreAsync was not found for material shortage {WorkOrderNo}/{ProductCd}", workOrderNo, productCd);
                return;
            }

            var task = method.Invoke(all, new object?[]
            {
                "MaterialShortageDetected",
                new object?[] { payload },
                CancellationToken.None,
            }) as Task;
            if (task != null)
            {
                await task;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR material shortage push failed for {WorkOrderNo}/{ProductCd}", workOrderNo, productCd);
        }
    }

    private object? ResolveWmsHubContext()
    {
        var hubType = Type.GetType(HubTypeName);
        var openHubContextType = Type.GetType(HubContextTypeName);
        if (hubType == null || openHubContextType == null)
        {
            return null;
        }

        var hubContextType = openHubContextType.MakeGenericType(hubType);
        return _serviceProvider.GetService(hubContextType);
    }
}
