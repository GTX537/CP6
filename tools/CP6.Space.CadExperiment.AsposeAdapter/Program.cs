using System.Collections;
using System.Text.Json;
using Aspose.CAD;
using Aspose.CAD.FileFormats.Cad;
using Aspose.CAD.FileFormats.Cad.CadObjects;
using CP6.Space.CadExperiment;

namespace CP6.Space.CadExperiment.AsposeAdapter;

public static class Program
{
    private const string LicensePathEnvironmentVariable = "CP6_SPACE_ASPOSE_LICENSE_PATH";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var commandLine = new CommandLine(args);
            if (commandLine.Command != "inspect")
            {
                throw new ArgumentException("Only the 'inspect' command is supported.");
            }

            var input = Path.GetFullPath(commandLine.Required("--input"));
            var output = commandLine.Required("--output");
            var requestedVersion = commandLine.Required("--candidate-version");
            var sourceHash = await DatasetAuditor.ComputeSha256Async(input);

            ApplyOptionalLicense();
            using var image = Image.Load(input);
            if (image is not CadImage cadImage)
            {
                throw new InvalidDataException(
                    $"Aspose.CAD loaded '{image.GetType().FullName}', not CadImage.");
            }

            var entityTypes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var layers = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long handleCount = 0;
            long duplicateHandleCount = 0;
            var entities = cadImage.Entities.ToArray();
            foreach (CadEntityBase entity in entities)
            {
                Increment(entityTypes, entity.TypeName.ToString());
                Increment(layers, entity.LayerName ?? "<null>");
                var handle = Convert.ToString(entity.Id, System.Globalization.CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(handle))
                {
                    continue;
                }

                handleCount++;
                if (!handles.Add(handle))
                {
                    duplicateHandleCount++;
                }
            }

            var issues = new List<string>();
            var unit = TryReadUnit(cadImage, issues);
            if (handleCount != entities.LongLength)
            {
                issues.Add(
                    $"SPACE_CAD_SOURCE_HANDLE_MISSING:{entities.LongLength - handleCount}");
            }

            if (duplicateHandleCount > 0)
            {
                issues.Add($"SPACE_CAD_SOURCE_HANDLE_DUPLICATE:{duplicateHandleCount}");
            }

            var actualAssemblyVersion = typeof(Image).Assembly
                .GetName()
                .Version?
                .ToString();
            var candidateVersion = string.IsNullOrWhiteSpace(actualAssemblyVersion)
                ? requestedVersion
                : actualAssemblyVersion;
            var observation = new CadAdapterObservation(
                1,
                candidateVersion,
                sourceHash,
                Path.GetExtension(input).TrimStart('.').ToUpperInvariant(),
                cadImage.Header?.AcadVersion.ToString(),
                unit,
                null,
                entities.LongLength,
                handleCount,
                duplicateHandleCount,
                entityTypes,
                layers,
                new Dictionary<string, long>(),
                issues);
            await CadExperimentJson.WriteAsync(output, observation);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ApplyOptionalLicense()
    {
        var licensePath = Environment.GetEnvironmentVariable(LicensePathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(licensePath))
        {
            return;
        }

        var license = new License();
        license.SetLicense(Path.GetFullPath(licensePath));
    }

    private static string? TryReadUnit(CadImage cadImage, ICollection<string> issues)
    {
        var properties = cadImage.Header?.HeaderProperties;
        if (properties is null)
        {
            issues.Add("SPACE_CAD_UNIT_UNKNOWN:header-properties-null");
            return null;
        }

        foreach (var item in (IEnumerable)properties)
        {
            var itemType = item.GetType();
            var key = itemType.GetProperty("Key")?.GetValue(item);
            if (!string.Equals(
                    Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture),
                    "INSUNITS",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = itemType.GetProperty("Value")?.GetValue(item);
            var code = TryUnwrapNumericValue(value);
            if (code is null)
            {
                issues.Add(
                    $"SPACE_CAD_UNIT_UNKNOWN:value-type={value?.GetType().FullName ?? "<null>"}");
                return null;
            }

            return code == 4 ? "Millimeter" : $"CadInsertionUnit:{code.Value}";
        }

        issues.Add(
            $"SPACE_CAD_UNIT_UNKNOWN:header-properties-type={properties.GetType().FullName}");
        return null;
    }

    private static int? TryUnwrapNumericValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is CadCodeValue codeValue)
        {
            return codeValue.GetShortValue();
        }

        if (value is IEnumerable values and not string)
        {
            foreach (var item in values)
            {
                var nested = TryUnwrapNumericValue(item);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                return convertible.ToInt32(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                // Continue with known wrapper properties.
            }
        }

        foreach (var propertyName in new[] { "Value", "IntValue", "ShortValue" })
        {
            var nested = value.GetType().GetProperty(propertyName)?.GetValue(value);
            if (nested is IConvertible nestedConvertible)
            {
                try
                {
                    return nestedConvertible.ToInt32(
                        System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    // Try the next wrapper.
                }
            }
        }

        return null;
    }

    private static void Increment(IDictionary<string, long> values, string key)
    {
        values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
    }
}
