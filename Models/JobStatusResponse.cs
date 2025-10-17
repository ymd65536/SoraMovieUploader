using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DotNETSora.Models
{
    public class JobStatusResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("generations")]
        public List<Generation> Generations { get; set; } = new List<Generation>();
    }
}
