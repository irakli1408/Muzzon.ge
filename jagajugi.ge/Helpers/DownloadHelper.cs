using jagajugi.ge.Services.Logger.Interface;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;

namespace jagajugi.ge.Helpers
{
    public static class DownloadHelper
    {
        public static async Task<IResult> HandleProcessErrorAsync(HttpContext context, string url, StringBuilder errorOutputBuilder, IAppLogger logger)
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
        public static async Task LogDownloadSuccessAsync(HttpContext context, string url, string outputDirectory, string outputTemplate, IAppLogger logger)
        {
            var fileName = GetActualFileName(outputDirectory);
            await logger.LogDownloadAsync(
                url: url,
                fileName: fileName,
                country: context.Request.Headers["X-Country"].FirstOrDefault(),
                region: context.Request.Headers["X-Region"].FirstOrDefault()
            );
        }
        public static async Task HandleExceptionAsync(HttpContext context, Exception exception, IAppLogger logger)
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
        public static bool IsValidYouTubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var pattern = @"https?:\/\/(?:www\.)?(?:youtube\.com\/watch\?v=[\w\-]{11}|youtu\.be\/[\w\-]{11})";

            var matches = Regex.Matches(url, pattern);
            return matches.Count == 1;
        }
        public static string GetDownloadDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        public static ProcessStartInfo CreateProcessStartInfo(string url, string outputTemplate)
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
        public static void AttachErrorHandler(Process process, StringBuilder errorOutputBuilder)
        {
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    errorOutputBuilder.AppendLine(e.Data);
            };
        }
        public static async Task StartProcessAndWaitForExitAsync(Process process, string url, CancellationToken cancellationToken)
        {
            var videoDuration = await GetVideoDurationAsync(url);

            if (videoDuration > 900)
                throw new InvalidOperationException("Video duration exceeds the 15-minute limit.");

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            await process.WaitForExitAsync(cancellationToken);
        }
        public static async Task<int> GetVideoDurationAsync(string url)
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

                if (durationParts.Length == 2)
                    return int.Parse(durationParts[0]) * 60 + int.Parse(durationParts[1]);
                if (durationParts.Length == 3)
                    return int.Parse(durationParts[0]) * 3600 + int.Parse(durationParts[1]) * 60 + int.Parse(durationParts[2]);
            }

            return 0;
        }
        public static string GetActualFileName(string outputDirectory)
        {
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
        public static async Task HandleTimeoutAsync(HttpContext context, Exception exception, IAppLogger logger)
        {
            var fullUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
            var country = context.Request.Headers["X-Country"].FirstOrDefault() ?? "unknown";
            var region = context.Request.Headers["X-Region"].FirstOrDefault() ?? "unknown";

            await logger.LogErrorAsync(
                url: fullUrl,
                errorMessage: "⏱️ Download timed out.",
                stackTrace: exception.StackTrace ?? "",
                errorType: "Timeout",
                country: country,
                region: region
            );

            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            context.Response.ContentType = "application/json";

            var errorResponse = new { error = "ჩატვირთვის დრო ამოიწურა. ოპერაცია ძალიან დიდხანს გაგრძელდა და გაუქმდა." };
            var json = System.Text.Json.JsonSerializer.Serialize(errorResponse);

            await context.Response.WriteAsync(json);
        }

    }
}
