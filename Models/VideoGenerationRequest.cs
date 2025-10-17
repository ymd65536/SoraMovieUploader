using System.Text.Json.Serialization;

namespace DotNETSora.Models
{
    public class VideoGenerationRequest
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "A young woman walks through a rainy street in Tokyo. Shallow depth of field. Neon lights reflect off the wet pavement. Ambient sound: rain and city noise.She stops under a red awning, takes out her phone, and says, dialogue: “I hope he’s still awake.” Close-up. Warm side light from a vending machine.";

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
