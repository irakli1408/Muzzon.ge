using AspNetCoreRateLimit;
using jagajugi.ge.Data;
using jagajugi.ge.Services.Logger;
using jagajugi.ge.Services.Logger.Interface;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

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
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<IAppLogger>();
        await HandleExceptionAsync(context, ex, logger); 
    }
});

app.MapPost("/download-video", async (HttpContext context, string url) =>
{
    if (!IsValidYouTubeUrl(url))
        return Results.BadRequest("Only valid YouTube links are allowed.");

    var logger = context.RequestServices.GetRequiredService<IAppLogger>();

    var outputDirectory = GetDownloadDirectory();
    var outputTemplate = Path.Combine(outputDirectory, "%(title).128s.%(ext)s");

    var processStartInfo = CreateProcessStartInfo(url, outputTemplate);
    var process = new Process { StartInfo = processStartInfo };

    var errorOutputBuilder = new StringBuilder();
    AttachErrorHandler(process, errorOutputBuilder);

    await StartProcessAndWaitForExitAsync(process, url);

    if (process.ExitCode != 0)
        return await HandleProcessErrorAsync(context, url, errorOutputBuilder, logger);
    
    await LogDownloadSuccessAsync(context, url, outputDirectory, outputTemplate, logger);

    return Results.Ok("✅ Done! Check the 'downloads' folder.");
});

#region Helper
async Task<IResult> HandleProcessErrorAsync(HttpContext context, string url, StringBuilder errorOutputBuilder, IAppLogger logger)
{
    await logger.LogErrorAsync(
        url: url,
        errorMessage: "yt-dlp exited with non-zero exit code.",
        stackTrace: errorOutputBuilder.ToString(),
        errorType: "YtDlpProcessError",
        country: context.Request.Headers["X-Country"].FirstOrDefault() ?? "unknown",
        region: context.Request.Headers["X-Region"].FirstOrDefault() ?? "unknown"
    );

    return Results.StatusCode(500);
}
async Task LogDownloadSuccessAsync(HttpContext context, string url, string outputDirectory, string outputTemplate, IAppLogger logger)
{
    var fileName = GetActualFileName(outputDirectory, outputTemplate);
    await logger.LogDownloadAsync(
        url: url,
        fileName: fileName,
        country: context.Request.Headers["X-Country"].FirstOrDefault(),
        region: context.Request.Headers["X-Region"].FirstOrDefault()
    );
}
static bool IsValidYouTubeUrl(string url)
{
    if (string.IsNullOrWhiteSpace(url))
        return false;

    var pattern = @"https?:\/\/(?:www\.)?(?:youtube\.com\/watch\?v=[\w\-]{11}|youtu\.be\/[\w\-]{11})";

    var matches = Regex.Matches(url, pattern);

    return matches.Count == 1;
}
async Task HandleExceptionAsync(HttpContext context, Exception exception, IAppLogger logger)
{
    var fullUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
    var country = context.Request.Headers["X-Country"].FirstOrDefault() ?? "unknown";
    var region = context.Request.Headers["X-Region"].FirstOrDefault() ?? "unknown";

    await logger.LogErrorAsync(
        url: fullUrl,
        errorMessage: exception.Message,
        stackTrace: exception.StackTrace ?? "",
        errorType: "UnhandledException",
        country: country,
        region: region
    );

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json";

    var errorResponse = new { error = "An unexpected error occurred." };
    var json = System.Text.Json.JsonSerializer.Serialize(errorResponse);

    await context.Response.WriteAsync(json);
}
string GetDownloadDirectory()
{
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}
ProcessStartInfo CreateProcessStartInfo(string url, string outputTemplate)
{
    return new ProcessStartInfo
    {
        FileName = "yt-dlp.exe",
        Arguments = $"-x --audio-format mp3 --no-mtime --no-cache-dir --force-overwrites --no-post-overwrites -o \"{outputTemplate}\" \"{url}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
}
void AttachErrorHandler(Process process, StringBuilder errorOutputBuilder)
{
    process.ErrorDataReceived += (sender, e) =>
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
            errorOutputBuilder.AppendLine(e.Data);
    };
}
async Task StartProcessAndWaitForExitAsync(Process process, string url)
{
    var videoDuration = await GetVideoDurationAsync(url);
 
    if (videoDuration > 900) 
       throw new InvalidOperationException("Video duration exceeds the 15-minute limit.");
    
    process.Start();
    process.BeginErrorReadLine();
    process.BeginOutputReadLine();
    await process.WaitForExitAsync();
}
async Task<int> GetVideoDurationAsync(string url)
{
    var processStartInfo = new ProcessStartInfo
    {
        FileName = "yt-dlp.exe",
        Arguments = $"--get-duration \"{url}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    var process = new Process { StartInfo = processStartInfo };
    var durationOutput = new StringBuilder();

    process.OutputDataReceived += (sender, e) =>
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
            durationOutput.AppendLine(e.Data);
    };

    process.Start();
    process.BeginOutputReadLine();
    await process.WaitForExitAsync();

    if (process.ExitCode == 0)
    {
        var durationString = durationOutput.ToString().Trim();
        var durationParts = durationString.Split(':');

        int durationInSeconds = 0;

        if (durationParts.Length == 2) 
        {
            durationInSeconds = int.Parse(durationParts[0]) * 60 + int.Parse(durationParts[1]);
        }
        else if (durationParts.Length == 3) 
        {
            durationInSeconds = int.Parse(durationParts[0]) * 3600 + int.Parse(durationParts[1]) * 60 + int.Parse(durationParts[2]);
        }

        return durationInSeconds;
    }

    return 0; 
}
string GetActualFileName(string outputDirectory, string outputTemplate)
{
    var extension = ".mp3";
    var files = Directory.GetFiles(outputDirectory, "*.mp3");

    if (files.Length > 0)
    {
        var latestFile = files
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime) 
            .First();

        return latestFile.Name;
    }

    return "unknown.mp3";
}
#endregion

app.Run();
