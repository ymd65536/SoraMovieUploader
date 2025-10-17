using System.Text.Json.Serialization;

namespace DotNETSora.Models
{
    public class Generation
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}
