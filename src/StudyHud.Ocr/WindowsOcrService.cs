using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRtOcrEngine = Windows.Media.Ocr.OcrEngine;
using WinRtOcrResult = Windows.Media.Ocr.OcrResult;

namespace StudyHud.Ocr;

/// <summary>
/// OCR service using the built-in Windows OCR engine (Windows.Media.Ocr) — local only, no cloud
/// (spec §40, §71). Targets the Windows 10 SDK so the WinRT APIs are called directly. Requires an
/// OCR language pack; falls back gracefully (empty result) when the engine or a language is missing.
/// </summary>
public sealed class WindowsOcrService : IOcrService
{
    private readonly ILogger<WindowsOcrService> _logger;
    private readonly object _gate = new();
    private WinRtOcrEngine? _engine;
    private bool _initialised;

    public WindowsOcrService(ILogger<WindowsOcrService> logger) => _logger = logger;

    public string EngineName => "Windows.Media.Ocr";

    public bool IsAvailable => GetEngine() is not null;

    private WinRtOcrEngine? GetEngine()
    {
        lock (_gate)
        {
            if (_initialised) return _engine;
            _initialised = true;
            try
            {
                // Prefer the user's own languages; fall back to English variants.
                _engine = WinRtOcrEngine.TryCreateFromUserProfileLanguages()
                          ?? TryLanguage("en-US")
                          ?? TryLanguage("en-GB")
                          ?? TryLanguage("en");
                if (_engine is null)
                    _logger.LogWarning(
                        "No Windows OCR language is installed. Install one via Settings → Time & language "
                        + "→ Language & region → your language → Language options → Basic typing / OCR.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create the Windows OCR engine.");
                _engine = null;
            }
            return _engine;
        }
    }

    private static WinRtOcrEngine? TryLanguage(string tag)
    {
        try
        {
            var lang = new Language(tag);
            return WinRtOcrEngine.IsLanguageSupported(lang)
                ? WinRtOcrEngine.TryCreateFromLanguage(lang)
                : null;
        }
        catch { return null; }
    }

    public async Task<OcrResult> RecogniseAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        var engine = GetEngine();
        if (engine is null || imageBytes is not { Length: > 0 })
            return Empty();

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            var writer = new DataWriter(stream);
            writer.WriteBytes(imageBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var decoded = await decoder.GetSoftwareBitmapAsync();

            // OCR wants a Bgra8 bitmap; convert regardless of the source format.
            using var bitmap = SoftwareBitmap.Convert(
                decoded, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            ct.ThrowIfCancellationRequested();
            var ocr = await engine.RecognizeAsync(bitmap);

            var rawText = ocr.Text ?? string.Empty;
            var words = ExtractWords(ocr);
            var normalised = OcrNormaliser.Normalise(rawText);
            // Windows OCR exposes no confidence score; treat any recognised words as usable text.
            float confidence = words.Count > 0 ? 0.9f : 0f;

            _logger.LogDebug("OCR read {Words} words / {Chars} chars.", words.Count, rawText.Length);

            return new OcrResult
            {
                RawText = rawText,
                NormalisedText = normalised,
                Confidence = confidence,
                Words = words,
                EngineName = EngineName
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Windows OCR failed on an image.");
            return Empty();
        }
    }

    private OcrResult Empty() => new()
    {
        RawText = string.Empty,
        NormalisedText = string.Empty,
        Confidence = 0f,
        Words = [],
        EngineName = EngineName
    };

    private static IReadOnlyList<OcrWord> ExtractWords(WinRtOcrResult ocr)
    {
        var words = new List<OcrWord>();
        foreach (var line in ocr.Lines)
        {
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                words.Add(new OcrWord
                {
                    Text = word.Text,
                    Confidence = 0.9f, // Windows OCR does not expose per-word confidence
                    BoundingBox = new ScreenRect(
                        (int)r.X, (int)r.Y, (int)(r.X + r.Width), (int)(r.Y + r.Height))
                });
            }
        }
        return words;
    }
}

/// <summary>
/// Deterministic OCR normalisation for engineering maths (spec §53): lowercases for
/// case-insensitive matching while restoring canonical unit casing. No generative AI.
/// </summary>
public static class OcrNormaliser
{
    public static string Normalise(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        var text = raw.Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
        return RestoreEngineeringUnits(text);
    }

    private static string RestoreEngineeringUnits(string text)
    {
        string[] units = ["kNm", "MPa", "GPa", "kPa", "kN", "kJ", "kg", "mm", "cm", "MN"];
        foreach (var unit in units)
            text = text.Replace(unit.ToLowerInvariant(), unit, StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
