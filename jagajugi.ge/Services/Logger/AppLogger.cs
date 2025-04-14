using Muzzon.ge.Data;
using Muzzon.ge.Data.Entity;
using Muzzon.ge.Services.Logger.Interface;
using System.Net;

namespace Muzzon.ge.Services.Logger
{
    public class AppLogger : IAppLogger
    {
        private readonly JuzzonDbContext _context;

        public AppLogger(JuzzonDbContext context)
        {
            _context = context;
        }

        public async Task LogDownloadAsync(string url, string fileName, string? country = null, string? region = null, string ? ipAddress = null)
        {
            var log = new DownloadLog
            {
                Url = url,
                FileName = fileName,
                DownloadedAt = DateTime.UtcNow,
                Country = country,
                Region = region,
                IpAddress = ipAddress
            };

            _context.DownloadLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        public async Task LogErrorAsync(string url, string errorMessage, string stackTrace, string? errorType = null, string? country = null, string? region = null, string? ipAddress = null)
        {
            var log = new ErrorLog
            {
                Url = url,
                ErrorMessage = errorMessage,
                StackTrace = stackTrace,
                ErrorOccurredAt = DateTime.UtcNow,
                ErrorType = errorType,
                Country = country,
                Region = region,
                IpAddress = ipAddress
            };

            _context.ErrorLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}
