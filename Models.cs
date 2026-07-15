using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text;
using System.IO;
using System.Windows.Media.Imaging;
namespace BmapTextStyleEditor;
public static class UiState { public static bool English { get; set; } }

public sealed class StyleLibrary { public string Format { get; set; } = "thoughtcanvas-text-styles"; public int Version { get; set; } = 1; public string DefaultStyleId { get; set; } = "classic"; public Dictionary<string, StyleSetting> StyleSettings { get; set; } = []; public List<TextBoxStyle> Styles { get; set; } = []; }
public sealed class StyleSetting { public string Scope { get; set; } = "all"; }
public sealed class TextBoxStyle {
    public string Id { get; set; } = "custom-style"; public string Name { get; set; } = "自定义样式";
    public string Scope { get; set; } = "all";
    public bool ReplaceFrame { get; set; } = true;
    public string Bg { get; set; } = "#FFFFFF"; public string Border { get; set; } = "#5B8DEF"; public string Color { get; set; } = "#2C3140";
    public double Radius { get; set; } = 12; public double BorderWidth { get; set; } = 1.5; public string Shadow { get; set; } = "0 5px 16px #17203322";
    public string FontFamily { get; set; } = "Microsoft YaHei"; public double FontSize { get; set; } = 14; public double FontWeight { get; set; } = 400; public string Padding { get; set; } = "11px 18px";
    public List<StyleLayer> Layers { get; set; } = [];
    public TextRegion TextRegion { get; set; } = new(); public InputRules TextRules { get; set; } = new(); public TextSizing TextSizing { get; set; } = new();
}
public sealed class StyleLayer { public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string Name { get; set; } = ""; public bool IsVisible { get; set; } = true; public bool IsLocked { get; set; } public string Type { get; set; } = "rect"; public double X { get; set; } public double Y { get; set; } public double W { get; set; } public double H { get; set; } public string Fill { get; set; } = "#DDE9FF"; public string Stroke { get; set; } = "#5B8DEF"; public double StrokeWidth { get; set; } = 1; public double Radius { get; set; } public double Opacity { get; set; } = 1; public double Rotation { get; set; } public string BlendMode { get; set; } = "normal"; public string ClipMode { get; set; } = "none"; public string MaskMode { get; set; } = "none"; public string ImageData { get; set; } = ""; [JsonIgnore] public string DisplayName { get { var type = Type == "rect" ? (UiState.English ? "Rectangle" : "矩形") : Type == "ellipse" ? (UiState.English ? "Ellipse" : "椭圆") : Type == "line" ? (UiState.English ? "Line" : "直线") : Type == "image" ? (UiState.English ? "Image" : "图片") : Type; var name = string.IsNullOrWhiteSpace(Name) ? type : Name; return $"{name}{(ClipMode != "none" ? (UiState.English ? " · Clip" : " · 剪切") : "")}{(MaskMode != "none" ? (UiState.English ? " · Mask" : " · 蒙版") : "")}"; } }
}
public sealed class TextRegion { public double X { get; set; } public double Y { get; set; } public double W { get; set; } public double H { get; set; } }
public sealed class InputRules { public int MaxLength { get; set; } public string Type { get; set; } = "any"; public string Pattern { get; set; } = ""; public bool Required { get; set; } }
public sealed class TextSizing { public string Mode { get; set; } = "uniform"; public double Width { get; set; } = 220; public double Height { get; set; } = 96; public double MaxWidth { get; set; } = 360; public double Aspect { get; set; } = 1.8; }

public static class TextSizingModes
{
    public const string Uniform = "uniform";
    public const string Stretch = "stretch";

    // V0.0.1 早期文件可能包含 auto/fixed。现在只有两种公开策略：
    // stretch 保留为自由拉伸，其余旧值都迁移为固定长宽比。
    public static string Normalize(string? mode) =>
        string.Equals(mode, Stretch, StringComparison.OrdinalIgnoreCase) ? Stretch : Uniform;
}

public static class InputRuleValidator
{
    public static bool IsAllowed(string? value, InputRules? rules)
    {
        value ??= ""; rules ??= new InputRules();
        if (rules.MaxLength > 0 && value.Length > rules.MaxLength) return false;
        if (value.Length == 0) return true;
        return rules.Type switch
        {
            "number" => Regex.IsMatch(value, "^[0-9]*$", RegexOptions.CultureInvariant),
            "letter" => Regex.IsMatch(value, "^[A-Za-z]*$", RegexOptions.CultureInvariant),
            "chinese" => Regex.IsMatch(value, "^[\\u3400-\\u9FFF]*$", RegexOptions.CultureInvariant),
            "alnum" => Regex.IsMatch(value, "^[A-Za-z0-9]*$", RegexOptions.CultureInvariant),
            "regex" when !string.IsNullOrWhiteSpace(rules.Pattern) => MatchesWholeValue(value, rules.Pattern),
            _ => true
        };
    }

    public static bool IsCompleteValueValid(string? value, InputRules? rules)
    {
        value ??= ""; rules ??= new InputRules();
        if (rules.Required && string.IsNullOrWhiteSpace(value)) return false;
        if (value.Length == 0 && rules.Type == "regex" && !string.IsNullOrWhiteSpace(rules.Pattern)) return MatchesWholeValue(value, rules.Pattern);
        return IsAllowed(value, rules);
    }

    static bool MatchesWholeValue(string value, string pattern)
    {
        try
        {
            var match = Regex.Match(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return match.Success && match.Index == 0 && match.Length == value.Length;
        }
        catch (ArgumentException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
    }
}

public readonly record struct PreviewDimensions(double Width, double Height);

public readonly record struct ImportedImageData(string DataUrl, int PixelWidth, int PixelHeight);

public static class ImageImportPipeline
{
    const int MaximumEncodedBytes = 24 * 1024 * 1024;
    static readonly int[] DecodeLimits = [4096, 3072, 2048, 1536, 1024];

    public static ImportedImageData Encode(string file)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp")) throw new InvalidDataException("Unsupported image format");
        var header = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var originalWidth = Math.Max(1, header.PixelWidth); var originalHeight = Math.Max(1, header.PixelHeight);
        var preserveAlpha = extension is ".png" or ".gif";
        byte[]? encoded = null; BitmapSource? finalBitmap = null;
        foreach (var limit in DecodeLimits)
        {
            var scale = Math.Min(1, limit / (double)Math.Max(originalWidth, originalHeight));
            var targetWidth = Math.Max(1, (int)Math.Round(originalWidth * scale));
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat; bitmap.UriSource = new Uri(file); if (targetWidth < originalWidth) bitmap.DecodePixelWidth = targetWidth; bitmap.EndInit(); bitmap.Freeze();
            BitmapEncoder encoder = preserveAlpha ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 92 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream); encoded = stream.ToArray(); finalBitmap = bitmap;
            if (encoded.Length <= MaximumEncodedBytes) break;
        }
        if (encoded == null || finalBitmap == null) throw new InvalidDataException("Image could not be decoded");
        var mime = preserveAlpha ? "image/png" : "image/jpeg";
        return new($"data:{mime};base64,{Convert.ToBase64String(encoded)}", finalBitmap.PixelWidth, finalBitmap.PixelHeight);
    }
}

public static class PreviewSizingCalculator
{
    const double MaximumSafeDimension = 10_000;

    public static PreviewDimensions Calculate(TextBoxStyle style, string? text, PreviewDimensions? singleCharacterRegion = null)
    {
        var sizing = style.TextSizing ?? new TextSizing();
        var fallback = new PreviewDimensions(Math.Clamp(style.FontSize, 1, 500) + 16, Math.Clamp(style.FontSize, 1, 500) * 1.4 + 12);
        var glyph = singleCharacterRegion is { Width: > 0, Height: > 0 } measured && double.IsFinite(measured.Width) && double.IsFinite(measured.Height) ? measured : fallback;
        var targetRegionWidth = Math.Clamp(glyph.Width, 1, MaximumSafeDimension);
        var targetRegionHeight = Math.Clamp(glyph.Height, 1, MaximumSafeDimension);
        var regionWidth = style.TextRegion.W; var regionHeight = style.TextRegion.H;
        var widthFraction = double.IsFinite(regionWidth) && regionWidth > 0 ? Math.Clamp(regionWidth / 100, .001, 1) : 1;
        var heightFraction = double.IsFinite(regionHeight) && regionHeight > 0 ? Math.Clamp(regionHeight / 100, .001, 1) : 1;
        var aspect = Math.Clamp(double.IsFinite(sizing.Aspect) ? sizing.Aspect : 1.8, .01, 100);
        // 两种策略都必须从用户画出的完整构图比例开始。自由拉伸只在文字
        // 确实溢出之后才允许分别扩展两条轴，不能在空文本时把横框翻成竖框。
        var baseWidth = Math.Max(targetRegionWidth / widthFraction, targetRegionHeight * aspect / heightFraction);
        var baseHeight = baseWidth / aspect;
        var safeScale = Math.Min(1, Math.Min(MaximumSafeDimension / baseWidth, MaximumSafeDimension / baseHeight));
        baseWidth *= safeScale; baseHeight *= safeScale;
        // 空文本和一个字共用“一字容量”的最小基准。更多文字由 MainWindow
        // 使用实际 Windows 字体测量后继续放大整个样式。
        return new(baseWidth, baseHeight);
    }
}

public static class ModelSafety
{
    static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
    public static bool IsSupportedImageData(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 36_000_000) return false;
        return value.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("data:image/gif;base64,", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("data:image/bmp;base64,", StringComparison.OrdinalIgnoreCase);
    }
    public static void Normalize(TextBoxStyle s)
    {
        s.Radius = Math.Clamp(Finite(s.Radius, 12), 0, 500); s.BorderWidth = Math.Clamp(Finite(s.BorderWidth, 1.5), 0, 100); s.FontSize = Math.Clamp(Finite(s.FontSize, 14), 1, 500); s.FontWeight = Math.Clamp(Finite(s.FontWeight, 400), 1, 1000);
        foreach (var l in s.Layers)
        {
            l.X = Math.Clamp(Finite(l.X, 0), 0, 100); l.Y = Math.Clamp(Finite(l.Y, 0), 0, 100);
            l.W = Math.Clamp(Finite(l.W, 1), .01, Math.Max(.01, 100 - l.X)); l.H = Math.Clamp(Finite(l.H, 1), .01, Math.Max(.01, 100 - l.Y));
            l.StrokeWidth = Math.Clamp(Finite(l.StrokeWidth, 1.5), 0, 100); l.Radius = Math.Clamp(Finite(l.Radius, 0), 0, 500); l.Opacity = Math.Clamp(Finite(l.Opacity, 1), 0, 1); l.Rotation = Math.Clamp(Finite(l.Rotation, 0), -36000, 36000);
            if (l.Type == "image" && !IsSupportedImageData(l.ImageData)) l.ImageData = "";
        }
        var t = s.TextRegion; t.X = Math.Clamp(Finite(t.X, 0), 0, 100); t.Y = Math.Clamp(Finite(t.Y, 0), 0, 100); t.W = Math.Clamp(Finite(t.W, 0), 0, Math.Max(0, 100 - t.X)); t.H = Math.Clamp(Finite(t.H, 0), 0, Math.Max(0, 100 - t.Y));
        var z = s.TextSizing; z.Mode = TextSizingModes.Normalize(z.Mode); z.Width = Math.Clamp(Finite(z.Width, 220), 1, 10000); z.Height = Math.Clamp(Finite(z.Height, 96), 1, 10000); z.MaxWidth = Math.Clamp(Finite(z.MaxWidth, 360), 1, 10000); z.Aspect = Math.Clamp(Finite(z.Aspect, 1.8), .01, 100);
        s.TextRules.MaxLength = Math.Clamp(s.TextRules.MaxLength, 0, 1_000_000);
    }
}

public static class StyleIdentity
{
    public static string UniqueId(string requested, IEnumerable<StyleLibrary> libraries)
    {
        var baseId = string.IsNullOrWhiteSpace(requested) ? "custom-style" : requested; var candidate = baseId; var suffix = 2;
        bool Exists(string id) => libraries.Any(lib => lib.Styles.Any(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
        while (Exists(candidate)) candidate = baseId + "-" + suffix++;
        return candidate;
    }
}
