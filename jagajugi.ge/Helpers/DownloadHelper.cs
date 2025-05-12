using Muzzon.ge.Constants;
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
                Arguments = $"--force-ipv4 --no-part -f bestaudio -x --audio-format mp3 -o - {url}",
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
        public static async Task StreamAudioToBrowserAsync(HttpContext context, string url, IAppLogger logger, IConfiguration config, IWebHostEnvironment env, CancellationToken cancellationToken)
        {
            string fileName = await GetVideoFileNameAsync(url);

            var processStartInfo = CreateProcessStartInfo(url);

            var process = new Process { StartInfo = processStartInfo };
            process.Start();

            if (env.IsDevelopment())
            {
                // _ = LogProcessErrorStreamAsync(process, context, url, logger, cancellationToken);
                _ = StartSilentErrorReadAsync(process);
            }
            else
            {
                _ = StartSilentErrorReadAsync(process);
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "audio/mpeg";
            var asciiFileName = RemoveNonAscii(SanitizeFileName(fileName));
            var utf8FileName = Uri.EscapeDataString(SanitizeFileName(fileName));
            context.Response.Headers["Content-Disposition"] =
                $"attachment; filename=\"{asciiFileName}.mp3\"; filename*=UTF-8''{utf8FileName}.mp3"; context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
            context.Response.Headers["Accept-Ranges"] = "none";
            context.Response.Headers["Connection"] = "close";

            var ipData = await ResolveClientGeoAsync(context, logger, context.RequestAborted);

            try
            {
                await process.StandardOutput.BaseStream.CopyToAsync(context.Response.Body, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            finally
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await process.WaitForExitAsync();
                        if (process.ExitCode != 0)
                        {
                            await logger.LogErrorAsync(
                                url: url,
                                errorMessage: $"yt-dlp exited with code {process.ExitCode}.",
                                stackTrace: "",
                                errorType: "YTDLPProcessError",
                                country: ipData.Country,
                                region: ipData.Region,
                                ipData.Ip
                            );
                        }
                        else
                        {
                            await logger.LogDownloadAsync(
                                url: url,
                                fileName: fileName,
                                country: ipData.Country,
                                region: ipData.Region,
                                ipData.Ip
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        await logger.LogErrorAsync(
                            url: url,
                            errorMessage: "Process monitoring failed: " + ex.Message,
                            stackTrace: ex.StackTrace ?? "",
                            errorType: "YTDLPMonitorError",
                            country: ipData.Country,
                            region: ipData.Region,
                            ipData.Ip
                        );
                    }
                });
            }
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
                        stackTrace: "",
                        errorType: "YTDLP-Stderr",
                        country: country,
                        region: region
                    );
                }
            }, token);
        }
        private static Task StartSilentErrorReadAsync(Process process)
        {
            return Task.Run(async () =>
            {
                try
                {
                    using var reader = process.StandardError;
                    while (await reader.ReadLineAsync() != null) { }
                }
                catch
                {
                }
            });
        }
        public static async Task<string> GetVideoFileNameAsync(string url)
        {
            try
            {
                using var titleProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "yt-dlp",
                        Arguments = $"--print filename -o \"%(title)s.%(ext)s\" {url}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                titleProcess.Start();

                string? fileName = await titleProcess.StandardOutput.ReadLineAsync();
                await titleProcess.WaitForExitAsync();

                return string.IsNullOrWhiteSpace(fileName) ? "audio.mp3" : fileName.Trim();
            }
            catch
            {
                return "audio.mp3";
            }
        }
        public static string SanitizeFileName(string input)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                input = input.Replace(c, '_');

            return input;
        }
        private static string RemoveNonAscii(string input)
        {
            return new string(input.Where(c => c <= 127).ToArray());
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
