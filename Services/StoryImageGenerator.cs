using SkiaSharp;
using PromoRepackager.Api.Models;

namespace PromoRepackager.Api.Services;

public interface IStoryImageGenerator
{
    byte[] GenerateStoryImage(byte[]? rawProductImage, OfferCuratedResult curatedOffer);
}

public class StoryImageGenerator : IStoryImageGenerator
{
    private const int Width = 1080;
    private const int Height = 1920;

    public byte[] GenerateStoryImage(byte[]? rawProductImage, OfferCuratedResult curatedOffer)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        using (var bgPaint = new SKPaint())
        {
            bgPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, Height),
                new[] { new SKColor(245, 246, 250), new SKColor(225, 229, 238) },
                null,
                SKShaderTileMode.Clamp
            );
            canvas.DrawRect(0, 0, Width, Height, bgPaint);
        }

        using (var tagPaint = new SKPaint { Color = new SKColor(255, 71, 87), IsAntialias = true })
        using (var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 36,
            IsAntialias = true,
            FakeBoldText = true,
            TextAlign = SKTextAlign.Center
        })
        {
            var tagRect = new SKRoundRect(new SKRect(340, 260, 740, 325), 32, 32);
            canvas.DrawRoundRect(tagRect, tagPaint);
            canvas.DrawText("ACHADINHO EXCLUSIVO", 540, 305, textPaint);
        }

        var cardRect = new SKRoundRect(new SKRect(90, 370, 990, 1170), 36, 36);
        using (var cardShadow = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 20),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 20),
            IsAntialias = true
        })
        using (var cardPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawRoundRect(cardRect, cardShadow);
            canvas.DrawRoundRect(cardRect, cardPaint);
        }

        if (rawProductImage != null && rawProductImage.Length > 0)
        {
            using var originalBitmap = SKBitmap.Decode(rawProductImage);
            if (originalBitmap != null)
            {
                var targetBox = new SKRect(140, 420, 940, 1120);
                float scale = Math.Min(targetBox.Width / originalBitmap.Width, targetBox.Height / originalBitmap.Height);
                float destWidth = originalBitmap.Width * scale;
                float destHeight = originalBitmap.Height * scale;
                float left = targetBox.Left + (targetBox.Width - destWidth) / 2;
                float top = targetBox.Top + (targetBox.Height - destHeight) / 2;

                var destRect = new SKRect(left, top, left + destWidth, top + destHeight);
                using var imagePaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
                canvas.DrawBitmap(originalBitmap, destRect, imagePaint);
            }
        }

        using (var titlePaint = new SKPaint
        {
            Color = new SKColor(33, 37, 41),
            TextSize = 50,
            IsAntialias = true,
            FakeBoldText = true,
            TextAlign = SKTextAlign.Center
        })
        {
            var title = curatedOffer.Produto.TituloCurto;
            if (title.Length > 36) title = string.Concat(title.AsSpan(0, 33), "...");
            canvas.DrawText(title, 540, 1260, titlePaint);
        }

        using (var pillPaint = new SKPaint { Color = new SKColor(16, 185, 129), IsAntialias = true })
        using (var pillTextPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 54,
            IsAntialias = true,
            FakeBoldText = true,
            TextAlign = SKTextAlign.Center
        })
        {
            var pillRect = new SKRoundRect(new SKRect(240, 1310, 840, 1420), 55, 55);
            canvas.DrawRoundRect(pillRect, pillPaint);
            canvas.DrawText(curatedOffer.InstagramStories.TextoPillPreco, 540, 1385, pillTextPaint);
        }

        if (!string.IsNullOrWhiteSpace(curatedOffer.InstagramStories.TextoPillCupom))
        {
            using var cupomBg = new SKPaint { Color = new SKColor(254, 243, 199), IsAntialias = true };
            using var cupomBorder = new SKPaint
            {
                Color = new SKColor(245, 158, 11),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3
            };
            using var cupomText = new SKPaint
            {
                Color = new SKColor(180, 83, 9),
                TextSize = 36,
                IsAntialias = true,
                FakeBoldText = true,
                TextAlign = SKTextAlign.Center
            };

            var cupomRect = new SKRoundRect(new SKRect(290, 1445, 790, 1520), 30, 30);
            canvas.DrawRoundRect(cupomRect, cupomBg);
            canvas.DrawRoundRect(cupomRect, cupomBorder);
            canvas.DrawText(curatedOffer.InstagramStories.TextoPillCupom, 540, 1495, cupomText);
        }

        using (var stickerAreaPaint = new SKPaint
        {
            Color = new SKColor(100, 116, 139, 120),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            PathEffect = SKPathEffect.CreateDash(new float[] { 14, 10 }, 0)
        })
        using (var stickerLabelPaint = new SKPaint
        {
            Color = new SKColor(100, 116, 139),
            TextSize = 32,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center
        })
        {
            var stickerSlot = new SKRoundRect(new SKRect(270, 1550, 810, 1640), 24, 24);
            canvas.DrawRoundRect(stickerSlot, stickerAreaPaint);
            canvas.DrawText($"[ FIXAR STICKER: {curatedOffer.InstagramStories.StickerLinkLabel} ]", 540, 1605, stickerLabelPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }
}