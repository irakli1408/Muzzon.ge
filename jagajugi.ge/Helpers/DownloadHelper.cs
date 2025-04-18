using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Logging;
using Muzzon.ge.Model;
using Muzzon.ge.Services.Logger.Interface;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using static System.Net.WebRequestMethods;

namespace Muzzon.ge.Helpers
{
    public static class DownloadHelper
    {
        public static async Task HandleExceptionAsync(HttpContext context, Exception exception, IAppLogger logger)
        {
            var fullUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";

            var ipData = await ResolveClientGeoAsync(context, logger, context.RequestAborted);

            await logger.LogErrorAsync(
                url: fullUrl,
                errorMessage: exception.Message,
                stackTrace: exception.StackTrace ?? "",
                errorType: "UnhandledException",
                country: ipData.Country,
                region: ipData.Region,
                ipData.Ip
            );

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var errorResponse = new
            {
                error = "⚠️ მოხდა გაუთვალისწინებელი შეცდომა."
            };

            var json = System.Text.Json.JsonSerializer.Serialize(errorResponse);
            await context.Response.WriteAsync(json, context.RequestAborted);
        }
        public static bool IsValidYouTubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var pattern = @"https?:\/\/(?:www\.)?(?:youtube\.com\/watch\?v=[\w\-]{11}|youtu\.be\/[\w\-]{11})";

            var matches = Regex.Matches(url, pattern);
            return matches.Count == 1;
        }
        public static ProcessStartInfo CreateProcessStartInfo(string url)
        {
            return new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"-f bestaudio -x --audio-format mp3 -o - {url}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        public static async Task<(string Title, int DurationInSeconds)> GetVideoTitleAndDurationAsync(string url)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp.exe",
                Arguments = $"--get-title --get-duration \"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = processStartInfo };
            var outputLines = new List<string>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    outputLines.Add(e.Data.Trim());
            };

            process.Start();
            process.BeginOutputReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || outputLines.Count < 2)
                return ("unknown", 0);

            string title = SanitizeFileName(outputLines[0]);
            string durationString = outputLines[1];

            var durationParts = durationString.Split(':');
            int durationInSeconds = 0;

            if (durationParts.Length == 2)
            {
                durationInSeconds = int.Parse(durationParts[0]) * 60 + int.Parse(durationParts[1]);
            }
            else if (durationParts.Length == 3)
            {
                durationInSeconds = int.Parse(durationParts[0]) * 3600 +
                                    int.Parse(durationParts[1]) * 60 +
                                    int.Parse(durationParts[2]);
            }

            return (title, durationInSeconds);
        }
        public static async Task HandleTimeoutAsync(HttpContext context, OperationCanceledException ex, IAppLogger logger)
        {
            var fullUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";

            var ipData = await ResolveClientGeoAsync(context, logger, context.RequestAborted);

            await logger.LogErrorAsync(
                url: fullUrl,
                errorMessage: "⏱️ Download timed out.",
                stackTrace: ex.StackTrace ?? "",
                errorType: "Timeout",
                country: ipData.Country,
                region: ipData.Region,
                ipData.Ip
            );

            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            context.Response.ContentType = "application/json";

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                error = "⏱️ ჩატვირთვის დრო ამოიწურა. ოპერაცია ძალიან დიდხანს გაგრძელდა და გაუქმდა."
            });

            await context.Response.WriteAsync(json, context.RequestAborted);
        }





        //public static async Task StreamAudioToBrowserAsync(HttpContext context, string url, IAppLogger logger, IConfiguration config, CancellationToken cancellationToken)
        //{

        //    var ipData = await ResolveClientGeoAsync(context, logger, cancellationToken);

        //    // ✅ Шаг 1: Получаем заголовок
        //    string title = "unknown";

        //    //try
        //    //{
        //    //    var titleStartInfo = new ProcessStartInfo
        //    //    {
        //    //        FileName = "yt-dlp",
        //    //        Arguments = $"--print title \"{url}\"",
        //    //        RedirectStandardOutput = true,
        //    //        RedirectStandardError = true,
        //    //        UseShellExecute = false,
        //    //        CreateNoWindow = true
        //    //    };

        //    //    using var titleProcess = Process.Start(titleStartInfo);
        //    //    title = (await titleProcess!.StandardOutput.ReadLineAsync())?.Trim() ?? "audio";
        //    //    await titleProcess.WaitForExitAsync(cancellationToken);
        //    //}
        //    //catch
        //    //{
        //    //    title = "audio";
        //    //}

        //    // ✅ Шаг 2: Запускаем основной процесс для стриминга
        //    var psi = new ProcessStartInfo
        //    {
        //        FileName = "yt-dlp",
        //        Arguments = $"-f bestaudio -x --audio-format mp3 --no-playlist -o - \"{url}\"",
        //        RedirectStandardOutput = true,
        //        RedirectStandardError = true,
        //        UseShellExecute = false,
        //        CreateNoWindow = true
        //    };

        //    using var process = new Process { StartInfo = psi };
        //    process.Start();

        //    context.Response.StatusCode = 200;
        //    context.Response.ContentType = "audio/mpeg";
        //    //var safeTitle = SanitizeFileName(title);
        //    context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{"safeTitle"}.mp3\"";

        //    // ✅ Таймер стриминга
        //    var streamWatch = Stopwatch.StartNew();

        //    var copyTask = process.StandardOutput.BaseStream.CopyToAsync(context.Response.Body, cancellationToken);
        //    var waitTask = process.WaitForExitAsync(cancellationToken);
        //    var completed = await Task.WhenAny(copyTask, waitTask);

        //    if (completed != copyTask && !process.HasExited)
        //    {
        //        try { process.Kill(true); } catch { }
        //        throw new OperationCanceledException("Download timed out.");
        //    }

        //    cancellationToken.ThrowIfCancellationRequested();


        //    //if (process.ExitCode != 0)
        //    //{
        //    //    //await logger.LogErrorAsync(
        //    //    //    url: url,
        //    //    //    errorMessage: $"yt-dlp exited with code {process.ExitCode}.",
        //    //    //    stackTrace: "",
        //    //    //    errorType: "YTDLPProcessError",
        //    //    //    country: ipData.Country,
        //    //    //    region: ipData.Region,
        //    //    //    ipData.Ip
        //    //    );
        //    //}
        //    //else
        //    //{
        //    //    //await logger.LogDownloadAsync(
        //    //    //    url: url,
        //    //    //    fileName: title,
        //    //    //    country: ipData.Country,
        //    //    //    region: ipData.Region,
        //    //    //    ipAddress: ipData.Ip

        //    //    );
        //    //}
        //}






        public static async Task StreamAudioToBrowserAsync(HttpContext context, string url, IAppLogger logger, IConfiguration config, CancellationToken cancellationToken)
        {


            var processStartInfo = CreateProcessStartInfo(url);

            var process = new Process { StartInfo = processStartInfo };
            process.Start();

            context.Response.StatusCode = 200;
            context.Response.ContentType = "audio/mpeg";
            context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{SanitizeFileName("sssss")}.mp3\"";
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
            context.Response.Headers["Accept-Ranges"] = "none";
            context.Response.Headers["Connection"] = "close";

            var copyTask = process.StandardOutput.BaseStream.CopyToAsync(context.Response.Body, cancellationToken);
            var waitTask = process.WaitForExitAsync(cancellationToken);

            var timeoutTask = Task.Delay(Timeout.Infinite, cancellationToken);
            var completed = await Task.WhenAny(copyTask, waitTask, timeoutTask);

            if (completed != copyTask && !process.HasExited)
            {
                try { process.Kill(true); } catch { }

                throw new OperationCanceledException();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var ipData = await ResolveClientGeoAsync(context, logger, context.RequestAborted);

            //if (process.ExitCode != 0)
            //{
            //    await logger.LogErrorAsync(
            //        url: url,
            //        errorMessage: $"yt-dlp exited with code {process.ExitCode}.",
            //        stackTrace: "",
            //        errorType: "YTDLPProcessError",
            //        country: ipData.Country,
            //        region: ipData.Region,
            //        ipData.Ip
            //    );
            //}
            //else
            //{
            //    await logger.LogDownloadAsync(
            //        url: url,
            //        fileName: "ssss" ?? "unknown",
            //        country: ipData.Country,
            //        region: ipData.Region,
            //        ipData.Ip
            //    );
            //}
        }





        public static Task LogProcessErrorStreamAsync(
        Process process,
        HttpContext context,
        string url,
        IAppLogger logger,
        CancellationToken token)
        {
            return Task.Run(async () =>
            {
                string country = context.Request.Headers["X-Country"].FirstOrDefault() ?? "unknown";
                string region = context.Request.Headers["X-Region"].FirstOrDefault() ?? "unknown";

                string? line;
                while (!token.IsCancellationRequested &&
                       (line = await process.StandardError.ReadLineAsync()) != null)
                {
                    await logger.LogErrorAsync(
                        url: url,
                        errorMessage: "[yt-dlp stderr] " + line,
                        stackTrace: "", // здесь нет stack trace — можно оставить пустым
                        errorType: "YTDLP-Stderr",
                        country: country,
                        region: region
                    );
                }
            }, token);
        }
        public static string SanitizeFileName(string input)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                input = input.Replace(c, '_');

            return input;
        }
        public static async Task<(string Ip, string Country, string Region)> ResolveClientGeoAsync(HttpContext context, IAppLogger logger, CancellationToken cancellationToken = default)
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            string country = "unknown";
            string region = "unknown";

            if (ip != "unknown")
            {
                try
                {
                    using var httpClient = new HttpClient();
                    var ipData = await httpClient.GetFromJsonAsync<IpDataModel>(
                        $"http://ip-api.com/json/{ip}?fields=country,regionName",
                        cancellationToken
                    );

                    if (ipData is not null)
                    {
                        country = ipData.Country ?? "unknown";
                        region = ipData.RegionName ?? "unknown";
                    }
                }
                catch (Exception ex)
                {
                    await logger.LogErrorAsync(
                    url: $"http://ip-api.com/json/{ip}",
                    errorMessage: ex.Message,
                    stackTrace: ex.StackTrace ?? "",
                    errorType: "IpLocationError",
                    country: "unknown",
                    region: "unknown",
                    ip
                    );
                }
            }

            return (ip, country, region);
        }
    }
}
