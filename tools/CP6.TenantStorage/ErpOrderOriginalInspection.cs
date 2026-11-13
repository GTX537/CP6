using System.Text.Json;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.Messaging;

internal static class ErpOrderOriginalInspection
{
    internal static async Task<int> RunAsync()
    {
        const int maxEnvelope = 1_048_576;
        const int inputLimit = 2 * ((maxEnvelope + 2) / 3) * 4 + 64;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var input = Console.OpenStandardInput();
            var bytes = new byte[inputLimit + 1];
            var length = await input.ReadAtLeastAsync(bytes, bytes.Length, false, deadline.Token);
            if (length > inputLimit) throw new InvalidDataException();

            using var supplied = JsonDocument.Parse(bytes.AsMemory(0, length));
            var request = supplied.RootElement.GetProperty("request").GetBytesFromBase64();
            var result = supplied.RootElement.GetProperty("result").GetBytesFromBase64();
            var validator = new ErpEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/erp")));
            if (!validator.IsValid(request) || !validator.IsValid(result)) throw new InvalidDataException();

            using var requestDocument = JsonDocument.Parse(request);
            using var resultDocument = JsonDocument.Parse(result);
            if (requestDocument.RootElement.GetProperty("type").GetString() != ErpEventContracts.OrderRequested ||
                resultDocument.RootElement.GetProperty("type").GetString() != ErpEventContracts.OrderFailed)
                throw new InvalidDataException();

            var order = requestDocument.RootElement.GetProperty("data").Deserialize<ErpOrderRequested>(ErpEventContracts.Json)!;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                requestSha256 = ErpEventContracts.Hash(request),
                resultSha256 = ErpEventContracts.Hash(result),
                inputSha256 = ErpEventContracts.OrderInputSha256(order)
            }));
            return 0;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or FormatException or
            InvalidOperationException or KeyNotFoundException or IOException or OperationCanceledException)
        {
            Console.Error.WriteLine("ERP order original rejected.");
            return 1;
        }
    }
}
