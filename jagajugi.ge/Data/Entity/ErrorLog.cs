namespace jagajugi.ge.Data.Entity
{
    public class ErrorLog
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string ErrorMessage { get; set; }
        public string StackTrace { get; set; }
        public DateTime ErrorOccurredAt { get; set; }
        public string? Country { get; set; }
        public string? Region { get; set; }
        public string? ErrorType { get; set; }
    }
}
