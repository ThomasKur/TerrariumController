using TerrariumController.Components;
using TerrariumController.Data;
using TerrariumController.Services;
using TerrariumController.Hubs;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

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

// Snapshot camera endpoint: returns a single JPEG captured via rpicam-still
app.MapGet("/camera/snapshot.jpg", async (HttpContext ctx) =>
{
    int width = int.TryParse(Environment.GetEnvironmentVariable("CAMERA_WIDTH"), out var w) ? w : 640;
    int height = int.TryParse(Environment.GetEnvironmentVariable("CAMERA_HEIGHT"), out var h) ? h : 480;
    int timeoutMs = 4000;

    if (ctx.Request.Query.ContainsKey("w")) int.TryParse(ctx.Request.Query["w"], out width);
    if (ctx.Request.Query.ContainsKey("h")) int.TryParse(ctx.Request.Query["h"], out height);

    var psi = new ProcessStartInfo
    {
        FileName = "rpicam-still",
        ArgumentList = { "-n", "--width", width.ToString(), "--height", height.ToString(), "-o", "-" },
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    try
    {
        using var proc = Process.Start(psi);
        if (proc == null)
        {
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsync("Failed to start rpicam-still");
            return;
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        // Read JPEG bytes from stdout (rpicam-still writes the image to stdout)
        using var ms = new MemoryStream();
        await proc.StandardOutput.BaseStream.CopyToAsync(ms, cts.Token);
        await proc.WaitForExitAsync(cts.Token);
        var bytes = ms.ToArray();
        ctx.Response.ContentType = "image/jpeg";
        await ctx.Response.Body.WriteAsync(bytes);
    }
    catch (OperationCanceledException)
    {
        ctx.Response.StatusCode = 504;
        await ctx.Response.WriteAsync("Camera snapshot timeout");
    }
    catch (Exception ex)
    {
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsync($"Camera error: {ex.Message}");
    }
});
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
