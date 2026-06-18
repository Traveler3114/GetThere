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
        if (ex is TransitInfoAPI.Exceptions.AppException appEx)
        {
            context.Response.StatusCode = appEx.StatusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = appEx.StatusCode,
                Title = appEx.ErrorCode ?? "Error",
                Detail = appEx.Message
            });
        }
    });
});
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
