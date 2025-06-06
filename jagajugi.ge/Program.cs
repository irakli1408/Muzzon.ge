﻿using AspNetCoreRateLimit;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Muzzon.ge.Constants;
using Muzzon.ge.Data;
using Muzzon.ge.Helpers;
using Muzzon.ge.Services.Logger;
using Muzzon.ge.Services.Logger.Interface;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<JuzzonDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("JuzzonConnection")));

builder.Services.AddScoped<IAppLogger, AppLogger>();

builder.Services.AddOptions();

builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", x =>
    {
        x.PermitLimit = 5;
        x.Window = TimeSpan.FromMinutes(1);
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseRateLimiter();

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
        await context.Response.WriteAsync($"{{\"error\": \"{ErrorMessages.MissingIp}\"}}");
        return;
    }

    await next();
});

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
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";

            var errorResponse = new { error = ex.Message };
            var json = System.Text.Json.JsonSerializer.Serialize(errorResponse);

            await context.Response.WriteAsync(json, context.RequestAborted);
        }
    }
    catch (OperationCanceledException ex)
    {
        if (!context.Response.HasStarted)
        {
            var logger = context.RequestServices.GetRequiredService<IAppLogger>();
            await DownloadHelper.HandleTimeoutAsync(context, ex, logger);
        }
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<IAppLogger>();
        await DownloadHelper.HandleExceptionAsync(context, ex, logger);
    }
});

app.MapGet("/stream-mp3", async (HttpContext context, string url, IAppLogger logger, IWebHostEnvironment env, IConfiguration config) =>
{
    var error = DownloadHelper.ValidateYouTubeUrl(url);
    if (error != null)
        return Results.BadRequest(error);

    var timeoutMinutes = config.GetValue<int>("DownloadSettings:DownloadTimeoutMinutes");

    await DownloadLimiter.Semaphore.WaitAsync();

    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));

    try
    {
        await DownloadHelper.StreamAudioToBrowserAsync(context, url, logger, config, env, cts.Token);
        return Results.Empty;
    }
    catch (OperationCanceledException ex)
    {
        await DownloadHelper.HandleTimeoutAsync(context, ex, logger);
        return Results.Empty;
    }
    catch (Exception ex)
    {
        await DownloadHelper.HandleExceptionAsync(context, ex, logger);
        return Results.Empty;
    }
    finally
    {
        DownloadLimiter.Semaphore.Release();
    }
}).RequireRateLimiting("fixed");


app.Run();
