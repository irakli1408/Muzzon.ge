namespace Muzzon.ge.Services.Logger.Interface
{
    public interface IAppLogger
    {
        Task LogDownloadAsync(string url, string fileName, string? country = null, string? region = null);
        Task LogErrorAsync(string url, string errorMessage, string stackTrace, string? errorType = null, string? country = null, string? region = null);
    }
}
