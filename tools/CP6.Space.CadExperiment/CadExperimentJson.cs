using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Space.CadExperiment;

public static class CadExperimentJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The output directory is invalid."));

        await using var stream = File.Create(fullPath);
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            Options,
            cancellationToken);
    }
}
