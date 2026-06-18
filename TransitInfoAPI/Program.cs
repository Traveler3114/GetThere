using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Workers;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders().AddConsole().AddDebug();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

builder.Services.AddDbContext<TransitDbContext>(options =>
    options.UseSqlServer(connectionString, x => x.UseNetTopologySuite()));

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("gtfsrt", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<GtfsParserManager>();
builder.Services.AddSingleton<OnestopIdManager>();
builder.Services.AddScoped<ReconciliationManager>();
builder.Services.AddScoped<ScheduleManager>();
builder.Services.AddScoped<PlaceMatchingManager>();
builder.Services.AddScoped<MobilityManager>();
builder.Services.AddScoped<StationManager>();
builder.Services.AddScoped<RouteManager>();
builder.Services.AddScoped<OperatorManager>();
builder.Services.AddScoped<FeedManager>();
builder.Services.AddSingleton<ImportLogStore>();
builder.Services.AddSingleton<RealtimeManager>();
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
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        app.Logger.LogError(ex, "Unhandled exception");
        var pd = new Microsoft.AspNetCore.Mvc.ProblemDetails { Detail = ex?.InnerException?.Message ?? ex?.Message };
        if (ex is TransitInfoAPI.Exceptions.AppException appEx)
        {
            pd.Status = appEx.StatusCode;
            pd.Title = appEx.ErrorCode ?? "Error";
        }
        else
        {
            pd.Status = 500;
            pd.Title = "Internal Server Error";
        }
        context.Response.StatusCode = pd.Status ?? 500;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(pd);
    });
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
    await db.Database.MigrateAsync();

    var stuck = await db.FeedVersions
        .Where(fv => fv.ImportStatus == FeedImportStatus.Importing)
        .ToListAsync();
    var stuckIds = stuck.Select(v => v.Id).ToList();
    foreach (var version in stuck)
    {
        version.ImportStatus = FeedImportStatus.Failed;
        version.ImportError = "Import interrupted by application restart";
    }
    await db.SaveChangesAsync();

    if (stuckIds.Count > 0)
    {
        await db.StopTimes.Where(st => stuckIds.Contains(st.Trip.FeedVersionId)).ExecuteDeleteAsync();
        await db.RawStops.Where(rs => stuckIds.Contains(rs.FeedVersionId)).ExecuteDeleteAsync();
        await db.Trips.Where(t => stuckIds.Contains(t.FeedVersionId)).ExecuteDeleteAsync();
    }

    await db.Database.ExecuteSqlRawAsync(
        "UPDATE cs SET IsActive = 1 FROM CanonicalStations cs WHERE cs.IsActive = 0 AND cs.StationType = 'Stop' AND EXISTS (SELECT 1 FROM RawStops rs INNER JOIN FeedVersions fv ON fv.Id = rs.FeedVersionId WHERE rs.CanonicalStationId = cs.Id AND rs.IsActive = 1 AND fv.IsActive = 1)");
}

await app.RunAsync();
