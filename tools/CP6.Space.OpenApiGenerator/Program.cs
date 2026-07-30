using CP6.WebApi.Controllers.Space;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

var output = args.Length == 1
    ? Path.GetFullPath(args[0])
    : throw new ArgumentException("Pass exactly one OpenAPI output path.");

var builder = WebApplication.CreateBuilder();
builder.Services
    .AddControllers()
    .AddApplicationPart(typeof(SpaceDesignV1Controller).Assembly);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(SpaceDesignV1OpenApi.Configure);

await using var app = builder.Build();
app.MapControllers();
var swagger = app.Services
    .GetRequiredService<ISwaggerProvider>()
    .GetSwagger(SpaceDesignV1OpenApi.DocumentName);

Directory.CreateDirectory(
    Path.GetDirectoryName(output)
    ?? throw new InvalidOperationException("The output directory is invalid."));
await using var stream = File.Create(output);
await using var text = new StreamWriter(stream);
var writer = new OpenApiJsonWriter(text);
swagger.SerializeAsV3(writer);
await text.FlushAsync();
