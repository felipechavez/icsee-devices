using System.Text.Json.Serialization;
using CamGuard.Api.Common;
using CamGuard.Api.Common.Auth;
using CamGuard.Api.Common.Data;
using CamGuard.Api.Common.Endpoints;
using CamGuard.Api.Common.Events;
using CamGuard.Api.Common.Notifications;
using CamGuard.Api.Common.Realtime;
using CamGuard.Api.Common.Security;
using CamGuard.Api.Common.Storage;
using CamGuard.Api.Features.Capture;
using CamGuard.Api.Features.Devices;
using CamGuard.Api.Features.Live;
using CamGuard.Api.Features.Monitoring;
using CamGuard.Api.Features.Retention;
using CamGuard.Api.Features.Streams;
using CamGuard.Api.Features.Tuya;
using CamGuard.Api.Features.Upload;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Compartido
builder.Services.Configure<AppOptions>(config.GetSection(AppOptions.Section));
builder.Services.Configure<Go2rtcOptions>(config.GetSection(Go2rtcOptions.Section));
builder.Services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
builder.Services.Configure<NtfyOptions>(config.GetSection(NtfyOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(config.GetConnectionString("Default")));
builder.Services.AddSingleton<CameraEventChannel>();
builder.Services.AddSingleton<CameraAwakeChannel>();
builder.Services.AddDataProtection()
    .SetApplicationName("CamGuard")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(config[$"{AppOptions.Section}:DataDirectory"] ?? "data", "keys")));
builder.Services.AddSingleton<CameraSecretProtector>();
builder.Services.AddSingleton<IVideoStorage, S3VideoStorage>();
builder.Services.AddHttpClient<INotifier, NtfyNotifier>();
builder.Services.AddJwtAuth(config);
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// Slices
builder.Services.AddEndpoints(typeof(Program).Assembly);
builder.Services.AddMonitoringFeature(config);
builder.Services.AddCaptureFeature(config);
builder.Services.AddUploadFeature();
builder.Services.AddLiveFeature(config);
builder.Services.AddStreamsFeature(config);
builder.Services.AddDevicesFeature(config);
builder.Services.AddTuyaFeature(config);
builder.Services.AddHostedService<RetentionService>();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapHub<EventsHub>(EventsHub.Path);
app.MapEndpoints();

app.Run();

public partial class Program;
