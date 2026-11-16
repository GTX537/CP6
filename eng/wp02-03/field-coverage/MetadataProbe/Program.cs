using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args.Length != 3) throw new ArgumentException("需要指定已采用程序集、独立内容根目录和输出目录。");
var package = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
    AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(package, name.Name + ".dll"));
Assembly.GetExecutingAssembly().GetType("MetadataExtractor")!.GetMethod("Run")!.Invoke(null, [package, args[1], args[2]]);

public static class MetadataExtractor
{
    public static void Run(string package, string contentRoot, string output)
    {
        contentRoot = Path.GetFullPath(contentRoot);
        output = Path.GetFullPath(output);
        if (!Directory.Exists(contentRoot) || Directory.EnumerateFileSystemEntries(contentRoot).Any())
            throw new ArgumentException("请使用空的独立内容根目录，不能加载既有环境配置。");
        Directory.CreateDirectory(output);
        using var observer = new BuildObserver(output);
        using var subscription = DiagnosticListener.AllListeners.Subscribe(observer);
        var entry = Assembly.LoadFrom(Path.Combine(package, "CP6.WebApi.dll")).EntryPoint!;
        string[] arguments = ["--contentRoot=" + contentRoot, "--applicationName=CP6.WebApi", "--environment=Development",
            "--Database:Provider=PostgreSql", "--ConnectionStrings:DefaultConnection=Host=127.0.0.1;Port=1;Database=metadata_probe;Username=probe",
            "--ConnectionStrings:Redis=", "--Space:Files:RootPath=" + Path.Combine(contentRoot, "space-files"),
            "--Startup:SkipHostedServices=true", "--Startup:InitializeDatabase=false", "--CrmOidc:Enabled=false"];
        try
        {
            if (entry.Invoke(null, [arguments]) is Task task) task.GetAwaiter().GetResult();
            throw new InvalidOperationException("应用未在预期的 HostBuilt 边界停止。");
        }
        catch (TargetInvocationException error) when (error.InnerException is HostCaptured) { }
        catch (HostCaptured) { }
        if (!observer.Captured) throw new InvalidOperationException("未采集到封存目录。");
    }

    private sealed class HostCaptured : Exception { }

    private sealed class BuildObserver(string output) : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> subscriptions = [];
        private IHost? host;
        public bool Captured { get; private set; }
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.Extensions.Hosting") subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != "HostBuilt") return;
            host = (IHost)value.Value!;
            var catalog = AuthorizationCatalogV1.Capture();
            if (!catalog.IsSealed) throw new InvalidOperationException("Main 授权目录尚未封存。");
            var resources = catalog.Fields.Keys.Union(catalog.Scopes.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(resource => new
                {
                    resource,
                    fields = catalog.Fields.TryGetValue(resource, out var fields) ? fields.Select(field => field.Name).ToArray() : null,
                    scope = catalog.Scopes.GetValueOrDefault(resource)
                }).ToArray();
            var actions = host.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
                .OfType<ControllerActionDescriptor>()
                .Select(action => new
                {
                    controller = action.ControllerTypeInfo.FullName,
                    method = action.MethodInfo.Name,
                    route = action.AttributeRouteInfo?.Template,
                    verbs = action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(rule => rule.HttpMethods).Distinct().Order().ToArray() ?? [],
                    attributes = action.MethodInfo.GetCustomAttributesData().Concat(action.ControllerTypeInfo.GetCustomAttributesData())
                        .Where(attribute => attribute.AttributeType.Namespace?.StartsWith("CP6", StringComparison.Ordinal) == true
                            || attribute.AttributeType.Name is "AuthorizeAttribute" or "AllowAnonymousAttribute")
                        .Select(attribute => new { type = attribute.AttributeType.FullName,
                            arguments = attribute.ConstructorArguments.Select(argument => argument.Value?.ToString()).ToArray() }).ToArray(),
                    result = action.MethodInfo.ReturnType.ToString()
                }).OrderBy(action => action.controller).ThenBy(action => action.method).ThenBy(action => action.route).ToArray();
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(output, "catalog.json"), JsonSerializer.Serialize(new
            {
                kind = "main-sealed-catalog-at-host-built", host_started = false, database_started = false, resources
            }, options));
            File.WriteAllText(Path.Combine(output, "routes.json"), JsonSerializer.Serialize(actions, options));
            Captured = true;
            // 在 builder.Build() 内停止，早于 Main 初始化数据库、启动后台任务或监听端口。
            throw new HostCaptured();
        }
        public void OnError(Exception error) => throw error;
        public void OnCompleted() { }
        public void Dispose()
        {
            foreach (var subscription in subscriptions) subscription.Dispose();
            host?.Dispose();
        }
    }
}
