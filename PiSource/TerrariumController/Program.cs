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
builder.Services.AddScoped<ISchedulerService, SchedulerService>();
builder.Services.AddScoped<IHumidityService, HumidityService>();

// Register background services
builder.Services.AddHostedService<SensorPollingService>();
builder.Services.AddHostedService<ScheduledTaskService>();

// Add SignalR for real-time updates
builder.Services.AddSignalR();

var app = builder.Build();

// Initialize database and GPIO
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();

    // Initialize GPIO for relay control
    var relayService = scope.ServiceProvider.GetRequiredService<IRelayService>();
    await relayService.InitializeGpioAsync();
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
