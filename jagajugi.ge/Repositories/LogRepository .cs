using jagajugi.ge.Data;
using jagajugi.ge.Data.Entity;
using jagajugi.ge.Repositories.Interface;

namespace jagajugi.ge.Repositories
{
    public class LogRepository : ILogRepository
    {
        private readonly JuzzonDbContext _context;

        public LogRepository(JuzzonDbContext context) => _context = context;

        public async Task AddDownloadLogAsync(DownloadLog log)
        {
            _context.DownloadLogs.Add(log);  
            await _context.SaveChangesAsync(); 
        }

        public async Task AddErrorLogAsync(ErrorLog log)
        {
            _context.ErrorLogs.Add(log);  
            await _context.SaveChangesAsync();  
        }
    }
}
