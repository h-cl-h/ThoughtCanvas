using System.Text.Json;
using System.IO;
using BmapTextStyleEditor;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var style = new TextBoxStyle {
    Radius = double.PositiveInfinity, FontSize = double.NaN,
    TextRegion = new TextRegion { X = double.NegativeInfinity, Y = double.NaN, W = double.PositiveInfinity, H = double.NegativeInfinity },
    TextSizing = new TextSizing { Width = double.PositiveInfinity, Height = double.NaN, MaxWidth = double.NegativeInfinity, Aspect = double.PositiveInfinity },
    Layers = [new StyleLayer { X = double.PositiveInfinity, Y = double.NaN, W = double.NegativeInfinity, H = double.PositiveInfinity, Opacity = double.NaN, Rotation = double.PositiveInfinity }]
};
for (var i = 0; i < 10_000; i++) ModelSafety.Normalize(style);
var output = JsonSerializer.Serialize(style);
if (output.Contains("Infinity", StringComparison.OrdinalIgnoreCase) || output.Contains("NaN", StringComparison.OrdinalIgnoreCase)) throw new Exception("非有限数值仍存在");
if (style.Layers.Any(x => !double.IsFinite(x.X) || !double.IsFinite(x.Y) || !double.IsFinite(x.W) || !double.IsFinite(x.H))) throw new Exception("图层坐标未清洗");
var validImage = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
var imageStyle = new TextBoxStyle { Layers = [new StyleLayer { Type = "image", ImageData = validImage }, new StyleLayer { Type = "image", ImageData = "https://example.invalid/a.png" }] };
ModelSafety.Normalize(imageStyle);
if (imageStyle.Layers[0].ImageData != validImage || imageStyle.Layers[1].ImageData.Length != 0) throw new Exception("图片数据安全清洗错误");
var measuredStyle = new TextBoxStyle { TextRegion = new TextRegion { X = 10, Y = 10, W = 50, H = 25 }, TextSizing = new TextSizing { Mode = "uniform", Width = 999, Height = 777, Aspect = 2 } };
var uniform = PreviewSizingCalculator.Calculate(measuredStyle, "", new PreviewDimensions(30, 32));
if (Math.Abs(uniform.Width - 256) > .001 || Math.Abs(uniform.Height - 128) > .001) throw new Exception("固定比例没有按一个字的写字区域换算外框");
measuredStyle.TextSizing.Mode = "stretch";
var stretch = PreviewSizingCalculator.Calculate(measuredStyle, "", new PreviewDimensions(30, 32));
if (Math.Abs(stretch.Width - 256) > .001 || Math.Abs(stretch.Height - 128) > .001) throw new Exception("自由拉伸的最小外框没有保留画出的长宽比");

var largeBmp = Path.Combine(Path.GetTempPath(), "bmap-large-import-" + Guid.NewGuid().ToString("N") + ".bmp");
try
{
    const int width = 2400, height = 2400, stride = width * 4;
    var pixels = new byte[stride * height];
    var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
    var encoder = new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
    using (var stream = File.Create(largeBmp)) encoder.Save(stream);
    if (new FileInfo(largeBmp).Length <= 20L * 1024 * 1024) throw new Exception("测试图片没有超过 20 MB");
    var imported = ImageImportPipeline.Encode(largeBmp);
    if (!ModelSafety.IsSupportedImageData(imported.DataUrl) || imported.PixelWidth <= 0 || imported.PixelHeight <= 0) throw new Exception("超过 20 MB 的图片没有成功转换");
}
finally { if (File.Exists(largeBmp)) File.Delete(largeBmp); }
var lib = new StyleLibrary { Styles = [new TextBoxStyle { Id = "my-text-style", Name = "第一个" }] };
var second = StyleIdentity.UniqueId("my-text-style", [lib]); if (second != "my-text-style-2") throw new Exception("第二个样式 ID 未自动分离");
lib.Styles.Add(new TextBoxStyle { Id = second, Name = "第二个" }); var third = StyleIdentity.UniqueId("my-text-style", [lib]); if (third != "my-text-style-3") throw new Exception("第三个样式 ID 未自动分离");
Console.WriteLine("MODEL_SAFETY_SMOKE_OK IMAGE_DATA_OK LARGE_IMAGE_IMPORT_OK ONE_CHARACTER_BASELINE_OK MULTI_STYLE_IDS_OK");
