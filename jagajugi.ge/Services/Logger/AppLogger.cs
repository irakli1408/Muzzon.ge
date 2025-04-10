using jagajugi.ge.Data;
using jagajugi.ge.Data.Entity;
using jagajugi.ge.Services.Logger.Interface;

namespace jagajugi.ge.Services.Logger
{
    public class AppLogger : IAppLogger
    {
        private readonly JuzzonDbContext _context;

        public AppLogger(JuzzonDbContext context)
        {
            _context = context;
        }

        public async Task LogDownloadAsync(string url, string fileName, string? country = null, string? region = null)
        {
            var log = new DownloadLog
            {
                Url = url,
                FileName = fileName,
                DownloadedAt = DateTime.UtcNow,
                Country = country,
                Region = region
            };

            _context.DownloadLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        public async Task LogErrorAsync(string url, string errorMessage, string stackTrace, string? errorType = null, string? country = null, string? region = null)
        {
            var log = new ErrorLog
            {
                Url = url,
                ErrorMessage = errorMessage,
                StackTrace = stackTrace,
                ErrorOccurredAt = DateTime.UtcNow,
                ErrorType = errorType,
                Country = country,
                Region = region
            };

            _context.ErrorLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}
