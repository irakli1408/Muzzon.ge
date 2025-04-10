using AspNetCoreRateLimit;
using jagajugi.ge.Data;
using jagajugi.ge.Helpers;
using jagajugi.ge.Services.Logger;
using jagajugi.ge.Services.Logger.Interface;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<JuzzonDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("JuzzonConnection")));

builder.Services.AddScoped<IAppLogger, AppLogger>();

// Rate limiting
builder.Services.AddOptions();
builder.Services.AddMemoryCache();

builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Use forwarded headers (for reverse proxy support)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = null,
    KnownNetworks = { },
    KnownProxies = { }
});

// Require valid IP for all requests (excluding swagger)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";

    if (path.StartsWith("/swagger") || path.StartsWith("/favicon"))
    {
        await next();
        return;
    }

    var ip = context.Request.Headers["X-Real-IP"].FirstOrDefault()
             ?? context.Connection.RemoteIpAddress?.ToString();

    if (string.IsNullOrWhiteSpace(ip))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\": \"Missing IP address.\"}");
        return;
    }

    await next();
});

// Rate limiting
app.UseIpRateLimiting();

// Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (InvalidOperationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest; 
        context.Response.ContentType = "application/json";

        var errorResponse = new { error = ex.Message }; 
        var json = System.Text.Json.JsonSerializer.Serialize(errorResponse);

        await context.Response.WriteAsync(json);
    }
    catch (OperationCanceledException ex)
    {
        var logger = context.RequestServices.GetRequiredService<IAppLogger>();
        await DownloadHelper.HandleTimeoutAsync(context, ex, logger);
    }

    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<IAppLogger>();
        await DownloadHelper.HandleExceptionAsync(context, ex, logger); 
    }
});

app.MapGet("/download-video", async (HttpContext context, string url) =>
{
    if (!DownloadHelper.IsValidYouTubeUrl(url))
        return Results.BadRequest("Only valid YouTube links are allowed.");

    var logger = context.RequestServices.GetRequiredService<IAppLogger>();
    var config = context.RequestServices.GetRequiredService<IConfiguration>();

    var timeoutMinutes = config.GetSection("DownloadSettings:DownloadTimeoutMinutes").Get<int>();

    var outputDirectory = DownloadHelper.GetDownloadDirectory();
    var outputTemplate = Path.Combine(outputDirectory, "%(title).128s.%(ext)s");

    var processStartInfo = DownloadHelper.CreateProcessStartInfo(url, outputTemplate);
    var process = new Process { StartInfo = processStartInfo };

    var errorOutputBuilder = new StringBuilder();
    DownloadHelper.AttachErrorHandler(process, errorOutputBuilder);

    await DownloadLimiter.Semaphore.WaitAsync();

    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));

    try
    {
        await DownloadHelper.StartProcessAndWaitForExitAsync(process, url, cts.Token);

        if (process.ExitCode != 0)
            return await DownloadHelper.HandleProcessErrorAsync(context, url, errorOutputBuilder, logger);

        await DownloadHelper.LogDownloadSuccessAsync(context, url, outputDirectory, outputTemplate, logger);

        return Results.Ok("✅ Done! Check the 'downloads' folder.");
    }
    finally
    {
        DownloadLimiter.Semaphore.Release();
    }
});

app.Run();
