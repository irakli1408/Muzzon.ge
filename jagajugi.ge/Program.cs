using jagajugi.ge.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<JuzzonDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("JuzzonConnection") + ";TrustServerCertificate=True"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\": \"An unexpected error occurred.\"}");
    }
});

app.MapPost("/download-video", (string url) =>
{
    if (!IsValidYouTubeUrl(url))
    {
        return Results.BadRequest("Only valid YouTube links are allowed.");
    }

    string outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    Directory.CreateDirectory(outputDirectory);

    string outputTemplate = Path.Combine(outputDirectory, "%(title).128s.%(ext)s");

    Console.WriteLine($"Output will be saved to: {outputTemplate}");

    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp.exe",
            Arguments = $"-x --audio-format mp3 --no-mtime --no-cache-dir --force-overwrites --no-post-overwrites -o \"{outputTemplate}\" \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };


    //process.OutputDataReceived += (sender, e) =>
    //{
    //    if (e.Data != null)
    //        Console.WriteLine(e.Data);
    //};

    //process.ErrorDataReceived += (sender, e) =>
    //{
    //    if (e.Data != null)
    //        Console.WriteLine("Error: " + e.Data);
    //};

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        return Results.StatusCode(500);
    }

    return Results.Ok("✅ Done! Check the 'downloads' folder.");
});

static bool IsValidYouTubeUrl(string url)
{
    if (string.IsNullOrWhiteSpace(url))
        return false;

    if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
        return false;

    var uri = new Uri(url);
    var allowedHosts = new[]
    {
        "www.youtube.com",
        "youtube.com",
        "youtu.be",
        "m.youtube.com"
    };

    return allowedHosts.Contains(uri.Host);
}

app.Run();
