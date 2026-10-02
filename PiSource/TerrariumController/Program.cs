using Microsoft.EntityFrameworkCore;
using TerrariumController.Components;
using TerrariumController.Data;
using TerrariumController.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Terrarium") ?? "Data Source=terrarium.db";
    options.UseSqlite(connectionString);
});
builder.Services.AddSingleton<ControlStateStore>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<ISensorService, SensorService>();
builder.Services.AddScoped<IRelayService, RelayService>();
builder.Services.AddScoped<ILoggingService, LoggingService>();
builder.Services.AddScoped<ISchedulerService, SchedulerService>();
builder.Services.AddScoped<IHumidityService, HumidityService>();
builder.Services.AddScoped<ICameraService, CameraService>();
builder.Services.AddHostedService<ControlBackgroundService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();