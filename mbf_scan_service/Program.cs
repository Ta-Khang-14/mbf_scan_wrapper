using mbf_scan_service.Logging;
using mbf_scan_service.Models;
using mbf_scan_service.Services;
using Serilog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using mbf_scan_service.Controllers;
using System.Text.Json;

namespace mbf_scan_service;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        LoggingSetup.Configure();

        try
        {
            Log.Information("Application starting...");

            var builder = WebApplication.CreateBuilder(args);

            builder.Host.UseSerilog();

            // Load appsettings.json
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            AppSettings appSettings = new();
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                appSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                Log.Information("Loaded appsettings.json: MaxPages={MaxPages}, Duplex={Duplex}",
                    appSettings.Scanner.DefaultMaxPages, appSettings.Scanner.DefaultEnableDuplex);
            }
            else
            {
                Log.Warning("appsettings.json not found, using default settings");
            }

            builder.Services.AddSingleton(appSettings);

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new() { Title = "MBF Scan Service API", Version = "v1" });
            });

            builder.Services.AddSingleton<ScannerService>(sp =>
                new ScannerService(appSettings.Scanner));
            builder.Services.AddSingleton<BarcodeService>();
            builder.Services.AddSingleton<OCRService>();
            builder.Services.AddSingleton<PDFService>();
            builder.Services.AddSingleton<FileService>();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseCors("AllowAll");

            MapEndpoints(app);

            ScanController.Initialize(
                app.Services.GetRequiredService<ScannerService>(),
                app.Services.GetRequiredService<BarcodeService>(),
                app.Services.GetRequiredService<OCRService>(),
                app.Services.GetRequiredService<PDFService>(),
                app.Services.GetRequiredService<FileService>()
            );

            ApplicationConfiguration.Initialize();

            var form = new Form1();
            form.ShowInTaskbar = true;
            form.WindowState = FormWindowState.Minimized;
            form.Visible = false;

            var apiThread = new Thread(() =>
            {
                Log.Information("Starting API server on http://localhost:5000");
                app.Run("http://localhost:5000");
            })
            {
                IsBackground = true,
                Name = "API-Server"
            };
            apiThread.Start();

            Log.Information("Starting WinForms application - minimized to system tray");
            Application.Run();

            Log.Information("Application shutting down...");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application crashed");
        }
        finally
        {
            LoggingSetup.Shutdown();
        }
    }

    private static void MapEndpoints(WebApplication app)
    {
        var apiGroup = app.MapGroup("/api");

        apiGroup.MapGet("/scanner/list", Controllers.ScanController.ListScanners)
            .WithName("ListScanners")
            .WithTags("Scanner");

        apiGroup.MapPost("/scanner/scan", Controllers.ScanController.Scan)
            .WithName("Scan")
            .WithTags("Scanner");

        apiGroup.MapGet("/scanner/pages", ScanController.GetPages)
            .WithName("GetPages")
            .WithTags("Scanner");

        apiGroup.MapDelete("/scanner/pages/{index}", ScanController.DeletePage)
            .WithName("DeletePage")
            .WithTags("Scanner");

        apiGroup.MapPost("/scanner/process", ScanController.ProcessScan)
            .WithName("ProcessScan")
            .WithTags("Scanner");

        apiGroup.MapGet("/scanner/files/{id}", Controllers.ScanController.GetFile)
            .WithName("GetFile")
            .WithTags("Scanner");

        apiGroup.MapDelete("/scanner/files/{id}", Controllers.ScanController.DeleteStoredFile)
            .WithName("DeleteFile")
            .WithTags("Scanner");

        apiGroup.MapGet("/scanner/stored-files", ScanController.GetAllStoredFiles)
            .WithName("GetAllStoredFiles")
            .WithTags("Scanner");

        app.MapGet("/api/files/{id}", Controllers.ScanController.DownloadFile)
            .WithName("DownloadFile")
            .WithTags("Files");

        app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.Now }))
            .WithName("HealthCheck")
            .WithTags("System");

        apiGroup.MapGet("/scanner/preview/{index}", ScanController.GetPagePreview)
            .WithName("GetPagePreview")
            .WithTags("Scanner");

        apiGroup.MapGet("/scanner/page-pdf/{index}", ScanController.GetPagePdf)
            .WithName("GetPagePdf")
            .WithTags("Scanner");
    }
}
