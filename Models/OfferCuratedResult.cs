using System.Text.Json.Serialization;

namespace PromoRepackager.Api.Models;

public record ProdutoDto(
    [property: JsonPropertyName("titulo_curto")] string TituloCurto,
    [property: JsonPropertyName("preco")] string Preco,
    [property: JsonPropertyName("condicao")] string Condicao,
    [property: JsonPropertyName("cupom")] string? Cupom,
    [property: JsonPropertyName("destaque")] string Destaque
);

public record WhatsappDto(
    [property: JsonPropertyName("copy_pronta")] string CopyPronta
);

public record InstagramStoriesDto(
    [property: JsonPropertyName("texto_pill_preco")] string TextoPillPreco,
    [property: JsonPropertyName("texto_pill_cupom")] string? TextoPillCupom,
    [property: JsonPropertyName("sticker_link_label")] string StickerLinkLabel
);

public record OfferCuratedResult(
    [property: JsonPropertyName("produto")] ProdutoDto Produto,
    [property: JsonPropertyName("whatsapp")] WhatsappDto Whatsapp,
    [property: JsonPropertyName("instagram_stories")] InstagramStoriesDto InstagramStories
);