using jagajugi.ge.Data.Entity;
using Microsoft.EntityFrameworkCore;

namespace jagajugi.ge.Data
{
    public class JuzzonDbContext : DbContext
    {
        public JuzzonDbContext(DbContextOptions<JuzzonDbContext> options) : base(options) { }

        public DbSet<DownloadLog> DownloadLogs { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }  
    }
}
