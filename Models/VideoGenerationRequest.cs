using System.Text.Json.Serialization;

namespace DotNETSora.Models
{
    public class VideoGenerationRequest
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "A cat playing piano in a jazz bar.";

        [JsonPropertyName("width")]
        public int Width { get; set; } = 480;

        [JsonPropertyName("height")]
        public int Height { get; set; } = 480;

        [JsonPropertyName("n_seconds")]
        public int NSeconds { get; set; } = 5;

        [JsonPropertyName("model")]
        public string Model { get; set; } = "sora";
    }
}
