using System.Text.Json.Serialization;
using Requests.Api.ErrorHandling;
using Requests.Application;
using Requests.Infrastructure;

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

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// "dotnet run -- seed [count]" re-creates the test data and exits.
if (args.Length > 0 && args[0] == "seed")
{
    int? count = args.Length > 1 && int.TryParse(args[1], out var n) ? n : null;
    await app.Services.ReseedDatabaseAsync(count);
    return;
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
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
