using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Services;
using TransitInfoAPI.Workers;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders().AddConsole().AddDebug();

builder.Services.AddControllers();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

builder.Services.AddDbContext<TransitDbContext>(options =>
    options.UseSqlServer(connectionString, x => x.UseNetTopologySuite()));

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("gtfsrt", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<GtfsParserService>();
builder.Services.AddSingleton<OnestopIdService>();
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddScoped<ScheduleService>();
builder.Services.AddScoped<PlaceMatchingService>();
builder.Services.AddScoped<MobilityService>();
builder.Services.AddScoped<StationService>();
builder.Services.AddScoped<RouteService>();
builder.Services.AddScoped<OperatorService>();
builder.Services.AddScoped<FeedService>();
builder.Services.AddSingleton<ImportLogStore>();
builder.Services.AddSingleton<RealtimeService>();
builder.Services.AddHostedService<RealtimePollingWorker>();
builder.Services.AddHostedService<FeedPollingWorker>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
    var stuck = await db.FeedVersions
        .Where(fv => fv.ImportStatus == FeedImportStatus.Importing)
        .ToListAsync();
    foreach (var version in stuck)
    {
        version.ImportStatus = FeedImportStatus.Failed;
        version.ImportError = "Import interrupted by application restart";
    }
    await db.SaveChangesAsync();
}

await app.RunAsync();
