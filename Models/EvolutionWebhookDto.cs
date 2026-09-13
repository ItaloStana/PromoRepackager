using System.Text.Json.Serialization;

namespace PromoRepackager.Api.Models;

public record EvolutionWebhookDto(
    [property: JsonPropertyName("event")] string? Event,
    [property: JsonPropertyName("instance")] string? Instance,
    [property: JsonPropertyName("data")] EvolutionData? Data
);

public record EvolutionData(
    [property: JsonPropertyName("key")] EvolutionKey? Key,
    [property: JsonPropertyName("message")] EvolutionMessage? Message
);

public record EvolutionKey(
    [property: JsonPropertyName("remoteJid")] string? RemoteJid,
    [property: JsonPropertyName("fromMe")] bool FromMe,
    [property: JsonPropertyName("id")] string? Id
);

public record EvolutionMessage(
    [property: JsonPropertyName("conversation")] string? Conversation,
    [property: JsonPropertyName("extendedTextMessage")] EvolutionExtendedTextMessage? ExtendedTextMessage,
    [property: JsonPropertyName("imageMessage")] EvolutionImageMessage? ImageMessage
)
{
    public string ExtractText() =>
        ExtendedTextMessage?.Text
        ?? Conversation
        ?? ImageMessage?.Caption
        ?? string.Empty;

    public byte[]? ExtractThumbnailBytes()
    {
        if (!string.IsNullOrWhiteSpace(ImageMessage?.JpegThumbnail))
        {
            try
            {
                return Convert.FromBase64String(ImageMessage.JpegThumbnail);
            }
            catch { }
        }
        return null;
    }
}

public record EvolutionExtendedTextMessage(
    [property: JsonPropertyName("text")] string? Text
);

public record EvolutionImageMessage(
    [property: JsonPropertyName("caption")] string? Caption,
    [property: JsonPropertyName("jpegThumbnail")] string? JpegThumbnail,
    [property: JsonPropertyName("url")] string? Url
);