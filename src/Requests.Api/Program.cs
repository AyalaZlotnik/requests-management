using System.Text.Json.Serialization;
using Requests.Api.ErrorHandling;
using Requests.Application;
using Requests.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Enums travel as names. Controllers and Problem Details (error bodies) use two different JSON
// serializers, so both get the same converter – otherwise 409 bodies would contain "status": 1.
var enumsAsNames = new JsonStringEnumConverter(allowIntegerValues: false);
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(o => o.LogValidationFailures())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(enumsAsNames);
        // Don't echo internal type names from deserialization errors back to the client.
        o.AllowInputFormatterExceptionMessages = false;
    });
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(enumsAsNames));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
    o.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml")));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// "dotnet run -- seed [count]" deletes all requests, re-creates the test data and exits.
if (args.Length > 0 && args[0] == "seed")
{
    if (app.Environment.IsProduction())
    {
        Console.Error.WriteLine("seed deletes all data and is not available in Production.");
        Environment.ExitCode = 1;
        return;
    }

    int? count = null;
    if (args.Length > 1)
    {
        if (!int.TryParse(args[1], out var n) || n < 1 || n > DataSeedLimits.MaxCount)
        {
            Console.Error.WriteLine($"seed count must be a whole number between 1 and {DataSeedLimits.MaxCount:N0}.");
            Environment.ExitCode = 1;
            return;
        }
        count = n;
    }

    await app.Services.ReseedDatabaseAsync(count);
    return;
}

// Development: create/upgrade the database on startup and seed 100,000 requests on the first run.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.InitializeDatabaseAsync(seedIfEmpty: app.Configuration.GetValue<bool>("Database:SeedIfEmpty"));
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
