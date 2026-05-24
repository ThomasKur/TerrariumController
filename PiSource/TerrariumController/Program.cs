using TerrariumController.Components;
using TerrariumController.Data;
using TerrariumController.Services;
using TerrariumController.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure SQLite database
var dbPath = Path.Combine(AppContext.BaseDirectory, "terrarium.db");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Register application services
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<ILoggingService, LoggingService>();
builder.Services.AddScoped<ISensorService, SensorService>();
builder.Services.AddScoped<IRelayService, RelayService>();
builder.Services.AddScoped<IHumidityService, HumidityService>();
builder.Services.AddSingleton<IControlLoopSignal, ControlLoopSignal>();
builder.Services.AddSingleton<IControlDiagnosticsService, ControlDiagnosticsService>();
builder.Services.AddSingleton<IRuntimeHealthService, RuntimeHealthService>();
builder.Services.AddSingleton(new RelayCommandCoordinatorOptions());
builder.Services.AddSingleton<RelayCommandCoordinator>();
builder.Services.AddSingleton<IRelayCommandCoordinator>(sp => sp.GetRequiredService<RelayCommandCoordinator>());

// Register background services
builder.Services.AddHostedService(sp => sp.GetRequiredService<RelayCommandCoordinator>());
builder.Services.AddHostedService<ControlOrchestratorService>();

// Add SignalR for real-time updates
builder.Services.AddSignalR();

var app = builder.Build();

// Initialize database and GPIO
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var runtimeHealth = scope.ServiceProvider.GetRequiredService<IRuntimeHealthService>();
    await dbContext.Database.MigrateAsync();
    runtimeHealth.MarkDatabaseReady();

    // Initialize GPIO for relay control
    var relayService = scope.ServiceProvider.GetRequiredService<IRelayService>();
    await relayService.InitializeGpioAsync();
    runtimeHealth.MarkGpioReady();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

// Map SignalR hub
app.MapHub<SensorHub>("/sensorHub");

app.MapGet("/healthz", (IRuntimeHealthService runtimeHealth) =>
{
    return Results.Ok(runtimeHealth.GetSnapshot());
});

app.MapGet("/readyz", (IRuntimeHealthService runtimeHealth) =>
{
    var snapshot = runtimeHealth.GetSnapshot();
    return snapshot.IsReady
        ? Results.Ok(snapshot)
        : Results.Json(snapshot, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/diagnostics/control", (IControlDiagnosticsService diagnostics) =>
{
    return Results.Ok(diagnostics.GetSnapshot());
});

app.MapPost("/api/kiosk/exit", () =>
{
    var exitRequestPath = Path.Combine(Path.GetTempPath(), "terrarium-kiosk.exit");
    File.WriteAllText(exitRequestPath, DateTime.UtcNow.ToString("O"));

    return Results.NoContent();
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Graceful shutdown: cleanup GPIO
app.Lifetime.ApplicationStopping.Register(async () =>
{
    using (var scope = app.Services.CreateScope())
    {
        var relayService = scope.ServiceProvider.GetRequiredService<IRelayService>();
        await relayService.CleanupGpioAsync();
    }
});

app.Run();
