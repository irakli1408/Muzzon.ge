﻿using Muzzon.ge.Constants;
using Muzzon.ge.Model;
using Muzzon.ge.Services.Logger.Interface;
using System.Diagnostics;
using System.Text.RegularExpressions;

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

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                error = ErrorMessages.Unexpected
            });

            await context.Response.WriteAsync(json, context.RequestAborted);
        }
        public static bool IsValidYouTubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var pattern = @"^https?:\/\/(?:www\.)?(?:youtube\.com\/watch\?v=|youtu\.be\/)[\w\-]{11}";
            if (!Regex.IsMatch(url, pattern))
                return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            if (!string.IsNullOrEmpty(query.Get("list")))
                return false;

            return true;
        }

        public static string? ValidateYouTubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return ErrorMessages.EmptyUrl;

            var pattern = @"^https?:\/\/(?:www\.)?(?:youtube\.com\/watch\?v=|youtu\.be\/)[\w\-]{11}";
            if (!Regex.IsMatch(url, pattern))
                return ErrorMessages.InvalidFormat;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return ErrorMessages.InvalidUrl;

            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            if (!string.IsNullOrEmpty(query.Get("list")))
                return ErrorMessages.IsPlaylist;

            return null;
        }

        public static ProcessStartInfo CreateProcessStartInfo(string url)
        {
            return new ProcessStartInfo
            {
                FileName = "yt-dlp",
                //Arguments = $"-f bestaudio -x --audio-format mp3 -o - {url}",
                Arguments = $"-f bestaudio -x --audio-format mp3 -o \"%(title)s.%(ext)s\" {url}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        public static async Task<(string title, int videoDuration)> GetVideoTitleAndDurationAsync(string url, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp.exe",
                Arguments = $"--get-title --get-duration \"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            var outputLines = new List<string>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    outputLines.Add(e.Data.Trim());
            };

            process.Start();
            process.BeginOutputReadLine();

            var timeoutTask = Task.Delay(Timeout.Infinite, cancellationToken);
            var waitTask = process.WaitForExitAsync(cancellationToken);
            var completed = await Task.WhenAny(waitTask, timeoutTask);

            if (completed != waitTask)
            {
                try { process.Kill(true); } catch { }
                throw new OperationCanceledException();
            }

            if (process.ExitCode != 0 || outputLines.Count < 2)
                return ("unknown", 0);

            string title = SanitizeFileName(outputLines[0]);
            string durationString = outputLines[1];

            int durationInSeconds = 0;
            var durationParts = durationString.Split(':');

            if (durationParts.Length == 2)
                durationInSeconds = int.Parse(durationParts[0]) * 60 + int.Parse(durationParts[1]);
            else if (durationParts.Length == 3)
                durationInSeconds = int.Parse(durationParts[0]) * 3600 +
                                    int.Parse(durationParts[1]) * 60 +
                                    int.Parse(durationParts[2]);

            return (title, durationInSeconds);
        }

        public static async Task HandleTimeoutAsync(HttpContext context, OperationCanceledException ex, IAppLogger logger)
        {
            var fullUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";

            //var ipData = await ResolveClientGeoAsync(context, logger, context.RequestAborted);

            //await logger.LogErrorAsync(
            //    url: fullUrl,
            //    errorMessage: "⏱️ Download timed out.",
            //    stackTrace: ex.StackTrace ?? "",
            //    errorType: "Timeout",
            //    country: ipData.Country,
            //    region: ipData.Region,
            //    ipData.Ip
            //);

            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            context.Response.ContentType = "application/json";

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                error = ErrorMessages.Timeout
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
            //var (title, videoDuration) = await GetVideoTitleAndDurationAsync(url);

            //var maxDuration = config.GetValue<int>("DownloadSettings:MaxDurationSeconds");

            //var ipData = await ResolveClientGeoAsync(context, logger, context.RequestAborted);

            //if (videoDuration > maxDuration)
            //{
            //    context.Response.StatusCode = StatusCodes.Status400BadRequest;
            //    context.Response.ContentType = "application/json";

            //    var errorResponse = new { error = "ვიდეოს ხანგრძლივობა აღემატება 15 წუთს." };
            //    var json = System.Text.Json.JsonSerializer.Serialize(errorResponse);
            //    await context.Response.WriteAsync(json, cancellationToken);
            //    return;
            //}

            var processStartInfo = CreateProcessStartInfo(url);

            var process = new Process { StartInfo = processStartInfo };
            process.Start();

            context.Response.StatusCode = 200;
            context.Response.ContentType = "audio/mpeg";
            context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{SanitizeFileName("title111")}.mp3\"";
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
            context.Response.Headers["Accept-Ranges"] = "none";
            context.Response.Headers["Connection"] = "close";

            await process.StandardOutput.BaseStream.CopyToAsync(context.Response.Body, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            //    if (process.ExitCode != 0)
            //    {
            //        await logger.LogErrorAsync(
            //            url: url,
            //            errorMessage: $"yt-dlp exited with code {process.ExitCode}.",
            //            stackTrace: "",
            //            errorType: "YTDLPProcessError",
            //            country: ipData.Country,
            //            region: ipData.Region,
            //            ipData.Ip
            //        );
            //    }
            //    else
            //    {
            //        await logger.LogDownloadAsync(
            //            url: url,
            //            fileName: title ?? "unknown",
            //            country: ipData.Country,
            //            region: ipData.Region,
            //            ipData.Ip
            //        );
            //    }
            //}


        }


        //public static async Task StreamAudioToBrowserAsync(HttpContext context, string url, IAppLogger logger, IConfiguration config, CancellationToken cancellationToken)
        //{
        //    string? detectedTitle = null;

        //    var processStartInfo = CreateProcessStartInfo(url);
        //    var process = new Process { StartInfo = processStartInfo };

        //    process.ErrorDataReceived += (sender, args) =>
        //    {
        //        if (string.IsNullOrWhiteSpace(args.Data))
        //            return;

        //        var data = args.Data.Trim();

        //        // Пример строки: [ExtractAudio] Destination: My Cool Song.mp3
        //        if (data.Contains("Destination:"))
        //        {
        //            var match = Regex.Match(data, @"Destination:\s+(.+)\.mp3", RegexOptions.IgnoreCase);
        //            if (match.Success)
        //            {
        //                detectedTitle = match.Groups[1].Value;
        //            }
        //        }

        //        // Можно логировать stderr при отладке:
        //        // Console.WriteLine("[yt-dlp stderr] " + data);
        //    };

        //    process.Start();
        //    process.BeginErrorReadLine(); 
        //    context.Response.StatusCode = 200;
        //    context.Response.ContentType = "audio/mpeg";

        //    var titleForHeader = SanitizeFileName(detectedTitle ?? "audio");

        //    context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{titleForHeader}.mp3\"";
        //    context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        //    context.Response.Headers["Pragma"] = "no-cache";
        //    context.Response.Headers["Expires"] = "0";
        //    context.Response.Headers["Accept-Ranges"] = "none";
        //    context.Response.Headers["Connection"] = "close";

        //    var copyTask = process.StandardOutput.BaseStream.CopyToAsync(context.Response.Body, cancellationToken);
        //    var waitTask = process.WaitForExitAsync(cancellationToken);
        //    var completed = await Task.WhenAny(copyTask, waitTask);

        //    if (completed != copyTask && !process.HasExited)
        //    {
        //        try { process.Kill(true); } catch { }
        //        throw new OperationCanceledException();
        //    }

        //    cancellationToken.ThrowIfCancellationRequested();

        //    var ipData = await ResolveClientGeoAsync(context, logger, context.RequestAborted);

        //    if (process.ExitCode != 0)
        //    {
        //        await logger.LogErrorAsync(
        //            url: url,
        //            errorMessage: $"yt-dlp exited with code {process.ExitCode}.",
        //            stackTrace: "",
        //            errorType: "YTDLPProcessError",
        //            country: ipData.Country,
        //            region: ipData.Region,
        //            ipData.Ip
        //        );
        //    }
        //    else
        //    {
        //        await logger.LogDownloadAsync(
        //            url: url,
        //            fileName: detectedTitle ?? "unknown",
        //            country: ipData.Country,
        //            region: ipData.Region,
        //            ipData.Ip
        //        );
        //    }
        //}


        //public static async Task StreamAudioToBrowserAsync(HttpContext context, string url, IAppLogger logger, IConfiguration config, CancellationToken cancellationToken)
        //{

        //    var processStartInfo = CreateProcessStartInfo(url);

        //    var process = new Process { StartInfo = processStartInfo };
        //    process.Start();

        //    //_ = DownloadHelper.LogProcessErrorStreamAsync(process, context, url, logger, cancellationToken);

        //    context.Response.StatusCode = 200;
        //    context.Response.ContentType = "audio/mpeg";
        //    context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{SanitizeFileName("title")}.mp3\"";
        //    context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        //    context.Response.Headers["Pragma"] = "no-cache";
        //    context.Response.Headers["Expires"] = "0";
        //    context.Response.Headers["Accept-Ranges"] = "none";
        //    context.Response.Headers["Connection"] = "close";

        //    var copyTask = process.StandardOutput.BaseStream.CopyToAsync(context.Response.Body, cancellationToken);
        //    var waitTask = process.WaitForExitAsync(cancellationToken);

        //    var timeoutTask = Task.Delay(Timeout.Infinite, cancellationToken);
        //    var completed = await Task.WhenAny(copyTask, waitTask, timeoutTask);

        //    if (completed != copyTask && !process.HasExited)
        //    {
        //        try { process.Kill(true); } catch { }

        //        throw new OperationCanceledException();
        //    }

        //    cancellationToken.ThrowIfCancellationRequested();

        //    var ipData = await ResolveClientGeoAsync(context, logger, context.RequestAborted);

        //    if (process.ExitCode != 0)
        //    {
        //        await logger.LogErrorAsync(
        //            url: url,
        //            errorMessage: $"yt-dlp exited with code {process.ExitCode}.",
        //            stackTrace: "",
        //            errorType: "YTDLPProcessError",
        //            country: ipData.Country,
        //            region: ipData.Region,
        //            ipData.Ip
        //        );
        //    }
        //    else
        //    {
        //        await logger.LogDownloadAsync(
        //            url: url,
        //            fileName: "title" ?? "unknown",
        //            country: ipData.Country,
        //            region: ipData.Region,
        //            ipData.Ip
        //        );
        //    }
        //}


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
                catch { }              
            }

            return (ip, country, region);
        }
    }
}
