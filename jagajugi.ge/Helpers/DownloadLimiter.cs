namespace jagajugi.ge.Helpers
{
    public static class DownloadLimiter
    {
        public static readonly SemaphoreSlim Semaphore = new(40, 40);
    }
}
