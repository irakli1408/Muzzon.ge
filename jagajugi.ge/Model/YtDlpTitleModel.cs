using System.Text.Json.Serialization;

namespace Muzzon.ge.Model
{
    public class YtDlpTitleModel
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }
}
