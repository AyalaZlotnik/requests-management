using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Requests.Api.Common.Caching;
using Requests.Api.Common.Errors;
using Requests.Api.Data;
using Requests.Api.Features.Requests;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        // Don't echo internal type names from deserialization errors back to the client.
        o.AllowInputFormatterExceptionMessages = false;
    });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
    o.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml")));

builder.Services.AddDbContext<RequestsDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("RequestsDb")));

builder.Services.AddMemoryCache();
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection("Cache"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SummaryCache>();

builder.Services.AddScoped<IRequestQueryService, RequestQueryService>();
builder.Services.AddScoped<IRequestCommandService, RequestCommandService>();
builder.Services.AddScoped<IRequestSummaryService, RequestSummaryService>();
builder.Services.AddScoped<DataSeeder>();

var app = builder.Build();

// "dotnet run -- seed [count]" re-creates the test data and exits.
if (args.Length > 0 && args[0] == "seed")
{
    var count = args.Length > 1 && int.TryParse(args[1], out var n) ? n : DataSeeder.DefaultCount;
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(count);
    return;
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<RequestsDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory in the integration tests.
public partial class Program;
