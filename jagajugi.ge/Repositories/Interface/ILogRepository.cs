using jagajugi.ge.Data.Entity;

namespace jagajugi.ge.Repositories.Interface
{
    public interface ILogRepository
    {
        Task AddDownloadLogAsync(DownloadLog log);
        Task AddErrorLogAsync(ErrorLog log);     
    }
}
