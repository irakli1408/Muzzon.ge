namespace jagajugi.ge.Data.Entity
{
    public class DownloadLog
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public DateTime DownloadedAt { get; set; }
        public string? Country { get; set; }
        public string? Region { get; set; }
    }
}
