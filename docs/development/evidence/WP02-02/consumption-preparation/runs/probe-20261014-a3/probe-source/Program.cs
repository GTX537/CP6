using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("Usage: Probe <inspect|runtime> <DLL-directory>");
var directory = Path.GetFullPath(args[1]);
if (args[0] == "inspect")
{
    var types = Directory.GetFiles(directory, "*.dll").Order(StringComparer.Ordinal).SelectMany(path =>
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return reader.TypeDefinitions.Select(handle => reader.GetTypeDefinition(handle))
            .Where(type => reader.GetString(type.Namespace).EndsWith(".Context", StringComparison.Ordinal))
            .Select(type => new
            {
                assembly = Path.GetFileName(path),
                name = reader.GetString(type.Namespace) + "." + reader.GetString(type.Name),
                visibility = (type.Attributes & TypeAttributes.VisibilityMask).ToString()
            }).ToArray();
    }).ToArray();
    var missing = RequiredTypes.Names.Except(types.Select(t => t.name), StringComparer.Ordinal).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        scope = "ARCHIVE_METADATA_ONLY", contextTypes = types, missingRequiredTypes = missing,
        contextSurface = missing.Length == 0 ? "PRESENT_REQUIRES_SIGNATURE_AND_RUNTIME_CHECK" : "MISSING_REQUIRED_TYPES"
    }, JsonOptions.Value));
}
else if (args[0] == "runtime")
{
    var signatures = SignatureInventory.Read(directory);
    var cases = await SyntheticRuntime.Run();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        scope = "SYNTHETIC_TASK_PRIVATE_ONLY",
        mainHostAdoption = "NOT_RUN", formalAdmission = "BLOCKED", businessAcceptance = "NOT_RUN",
        framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        signatures, cases
    }, JsonOptions.Value));
}
else throw new ArgumentException("Unknown probe mode.");

static class JsonOptions
{
    public static readonly JsonSerializerOptions Value = new() { WriteIndented = true };
}

static class RequiredTypes
{
    public static readonly string[] Names =
    [
        "CP6.Platform.Contracts.Context.ContextActorV1",
        "CP6.Platform.Contracts.Context.ContextActorKindV1",
        "CP6.Platform.Contracts.Context.ContextEntryKindV1",
        "CP6.Platform.Contracts.Context.ContextOperationV1",
        "CP6.Platform.Contracts.Context.AuthenticatedContextSourceV1",
        "CP6.Platform.Contracts.Context.ContextResolutionInputV1",
        "CP6.Platform.Contracts.Context.ContextBindingRegistrationV1",
        "CP6.Platform.Contracts.Context.ContextResolutionEvidenceV1",
        "CP6.Platform.Abstractions.Context.IContextSourceVerifierV1`1",
        "CP6.Platform.Abstractions.Context.IContextBindingAuthorityV1",
        "CP6.Platform.Abstractions.Context.ITrustedContextAccessorV1",
        "CP6.Platform.Abstractions.Context.TrustedRequestContextV1",
        "CP6.Platform.Abstractions.Context.ContextResolutionFailureV1",
        "CP6.Platform.Abstractions.Context.ContextResolutionExceptionV1",
        "CP6.Platform.Abstractions.Context.ContextBindingValidatorV1",
        "CP6.Platform.Abstractions.Context.TrustedContextExecutorV1",
        "CP6.Platform.Abstractions.Context.ContextScopeAccessorV1",
        "CP6.Platform.AspNetCore.Context.TrustedRequestContextExtensionsV1",
        "CP6.Platform.AspNetCore.Context.TrustedRequestContextMiddlewareV1",
        "CP6.Platform.Messaging.Context.MessageContextExecutorV1`1"
    ];
}

static class SignatureInventory
{
    private const BindingFlags PublicDeclared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly NullabilityInfoContext Nullability = new();
    private static string Name(Type t) => t.IsByRef ? Name(t.GetElementType()!) + "&"
        : t.IsArray ? Name(t.GetElementType()!) + "[" + new string(',', t.GetArrayRank() - 1) + "]"
        : t.IsGenericParameter ? t.Name
        : t.IsGenericType ? (t.GetGenericTypeDefinition().FullName ?? t.Name).Split('`')[0]
            + "<" + string.Join(",", t.GetGenericArguments().Select(Name)) + ">"
        : t.FullName ?? t.Name;
    private static object Generic(Type t) => new
    {
        name = t.Name, attributes = t.GenericParameterAttributes.ToString(),
        constraints = t.GetGenericParameterConstraints().Select(Name).Order(StringComparer.Ordinal).ToArray()
    };
    private static object NullInfo(NullabilityInfo n) => new
    {
        read = n.ReadState.ToString(), write = n.WriteState.ToString(),
        arguments = n.GenericTypeArguments.Select(NullInfo).ToArray(),
        element = n.ElementType is null ? null : NullInfo(n.ElementType)
    };
    private static object Parameter(ParameterInfo p) => new
    {
        name = p.Name, type = Name(p.ParameterType), attributes = p.Attributes.ToString(),
        requiredModifiers = p.GetRequiredCustomModifiers().Select(Name).ToArray(),
        optionalModifiers = p.GetOptionalCustomModifiers().Select(Name).ToArray(),
        nullability = NullInfo(Nullability.Create(p)), hasDefault = p.HasDefaultValue,
        defaultValue = p.HasDefaultValue ? p.DefaultValue?.ToString() : null
    };
    public static object[] Read(string directory) => Directory.GetFiles(directory, "*.dll")
        .Order(StringComparer.Ordinal).SelectMany(path =>
        {
            // PE filtering avoids resolving unrelated package types/dependencies.
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            var names = reader.TypeDefinitions.Select(h => reader.GetTypeDefinition(h))
                .Where(t => reader.GetString(t.Namespace).EndsWith(".Context", StringComparison.Ordinal)
                    && (t.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public)
                .Select(t => reader.GetString(t.Namespace) + "." + reader.GetString(t.Name)).Order(StringComparer.Ordinal).ToArray();
            var assembly = Assembly.LoadFrom(path);
            var archiveDllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            var loadedDllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant();
            if (loadedDllSha256 != archiveDllSha256) throw new InvalidOperationException("Loaded DLL differs from approved archive DLL.");
            return names.Select(name =>
            {
                var t = assembly.GetType(name, throwOnError: true)!;
                return (object)new
                {
                    assembly = assembly.FullName, loadedDllSha256, name = Name(t), attributes = t.Attributes.ToString(),
                    baseType = t.BaseType is null ? null : Name(t.BaseType),
                    interfaces = t.GetInterfaces().Select(Name).Order(StringComparer.Ordinal).ToArray(),
                    genericParameters = t.GetGenericArguments().Select(Generic).ToArray(),
                    constructors = t.GetConstructors(PublicDeclared).OrderBy(c => c.ToString(), StringComparer.Ordinal)
                        .Select(c => new { attributes = c.Attributes.ToString(), parameters = c.GetParameters().Select(Parameter).ToArray() }).ToArray(),
                    methods = t.GetMethods(PublicDeclared).OrderBy(m => m.ToString(), StringComparer.Ordinal).Select(m => new
                    {
                        name = m.Name, attributes = m.Attributes.ToString(), callingConvention = m.CallingConvention.ToString(),
                        returns = Parameter(m.ReturnParameter), parameters = m.GetParameters().Select(Parameter).ToArray(),
                        genericParameters = m.GetGenericArguments().Select(Generic).ToArray()
                    }).ToArray(),
                    properties = t.GetProperties(PublicDeclared).OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new
                    {
                        name = p.Name, type = Name(p.PropertyType), nullability = NullInfo(Nullability.Create(p)),
                        getter = p.GetMethod?.Attributes.ToString(), setter = p.SetMethod?.Attributes.ToString(),
                        indexParameters = p.GetIndexParameters().Select(Parameter).ToArray()
                    }).ToArray(),
                    fields = t.GetFields(PublicDeclared).OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => new
                    {
                        name = f.Name, type = Name(f.FieldType), attributes = f.Attributes.ToString(),
                        constant = f.IsLiteral ? f.GetRawConstantValue()?.ToString() : null
                    }).ToArray(),
                    events = t.GetEvents(PublicDeclared).OrderBy(e => e.Name, StringComparer.Ordinal)
                        .Select(e => new { name = e.Name, type = Name(e.EventHandlerType!) }).ToArray()
                };
            }).ToArray();
        }).ToArray();
}
