using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using BmapTextStyleEditor;

internal static class Program
{
    static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static int IndexOfTag(ComboBox box, string tag) => box.Items.Cast<ComboBoxItem>().ToList().FindIndex(x => x.Tag?.ToString() == tag);

    static double RequiredTextHeight(TextBox preview)
    {
        var horizontalInsets = preview.Padding.Left + preview.Padding.Right + 4;
        var verticalInsets = preview.Padding.Top + preview.Padding.Bottom + 4;
        var probe = new TextBlock
        {
            Text = preview.Text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = preview.FontFamily,
            FontSize = preview.FontSize,
            FontWeight = preview.FontWeight
        };
        probe.Measure(new Size(Math.Max(1, preview.Width - horizontalInsets), double.PositiveInfinity));
        return probe.DesiredSize.Height + verticalInsets;
    }

    static void CheckModeMigration()
    {
        foreach (var legacy in new[] { "auto", "uniform", "fixed", "unknown", "" })
        {
            var migrated = new TextBoxStyle { TextSizing = new TextSizing { Mode = legacy } };
            ModelSafety.Normalize(migrated);
            Expect(migrated.TextSizing.Mode == "uniform", $"legacy mode '{legacy}' did not migrate to uniform");
        }
        var stretch = new TextBoxStyle { TextSizing = new TextSizing { Mode = "stretch" } };
        ModelSafety.Normalize(stretch);
        Expect(stretch.TextSizing.Mode == "stretch", "stretch mode did not survive migration");
    }

    static void CheckRuleEngine()
    {
        Expect(InputRuleValidator.IsAllowed("012345", new InputRules { Type = "number" }), "number rejected digits");
        Expect(!InputRuleValidator.IsAllowed("-1", new InputRules { Type = "number" }), "number accepted sign");
        Expect(!InputRuleValidator.IsAllowed("1.2", new InputRules { Type = "number" }), "number accepted decimal point");
        Expect(InputRuleValidator.IsAllowed("AbZ", new InputRules { Type = "letter" }), "letter rejected ASCII letters");
        Expect(!InputRuleValidator.IsAllowed("A1", new InputRules { Type = "letter" }), "letter accepted digit");
        Expect(InputRuleValidator.IsAllowed("中文汉字", new InputRules { Type = "chinese" }), "chinese rejected Han characters");
        Expect(!InputRuleValidator.IsAllowed("中文 A", new InputRules { Type = "chinese" }), "chinese accepted whitespace/ASCII");
        Expect(InputRuleValidator.IsAllowed("Az09", new InputRules { Type = "alnum" }), "alnum rejected valid text");
        Expect(!InputRuleValidator.IsAllowed("Az-09", new InputRules { Type = "alnum" }), "alnum accepted punctuation");
        Expect(InputRuleValidator.IsAllowed("123", new InputRules { Type = "regex", Pattern = "[0-9]+" }), "regex rejected whole match");
        Expect(!InputRuleValidator.IsAllowed("123x", new InputRules { Type = "regex", Pattern = "[0-9]+" }), "regex accepted partial match");
        Expect(!InputRuleValidator.IsAllowed("1234", new InputRules { Type = "number", MaxLength = 3 }), "maximum length not enforced");
        Expect(!InputRuleValidator.IsCompleteValueValid("", new InputRules { Required = true }), "required accepted empty value");
        Expect(!InputRuleValidator.IsCompleteValueValid("   ", new InputRules { Required = true }), "required accepted whitespace-only value");
        Expect(!InputRuleValidator.IsCompleteValueValid("", new InputRules { Type = "regex", Pattern = "[0-9]+" }), "regex completion accepted a non-matching empty value");
    }

    [STAThread]
    static void Main()
    {
        CheckRuleEngine();
        CheckModeMigration();
        _ = new Application();
        var window = new MainWindow();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        var style = (TextBoxStyle)(type.GetField("style", flags)!.GetValue(window) ?? throw new Exception("style missing"));
        var refresh = type.GetMethod("RefreshPreview", flags) ?? throw new Exception("RefreshPreview missing");
        var paste = type.GetMethod("PreviewPaste", flags) ?? throw new Exception("PreviewPaste missing");
        var applyLanguage = type.GetMethod("ApplyLanguage", flags) ?? throw new Exception("ApplyLanguage missing");
        var readControls = type.GetMethod("ReadControls", flags) ?? throw new Exception("ReadControls missing");
        var snapshot = type.GetMethod("Snapshot", flags) ?? throw new Exception("Snapshot missing");
        var updateSyncState = type.GetMethod("UpdateSyncState", flags) ?? throw new Exception("UpdateSyncState missing");
        var updateSaveButton = type.GetMethod("UpdateSaveButton", flags) ?? throw new Exception("UpdateSaveButton missing");
        var measurePreviewText = type.GetMethod("MeasurePreviewText", flags) ?? throw new Exception("MeasurePreviewText missing");
        var lastSyncedSnapshot = type.GetField("lastSyncedSnapshot", flags) ?? throw new Exception("lastSyncedSnapshot missing");
        var hasStandaloneSave = type.GetField("hasStandaloneSave", flags) ?? throw new Exception("hasStandaloneSave missing");
        var inputType = (ComboBox)window.FindName("InputTypeBox");
        var maxLength = (TextBox)window.FindName("MaxLengthBox");
        var pattern = (TextBox)window.FindName("PatternBox");
        var required = (CheckBox)window.FindName("RequiredBox");
        var sizeMode = (ComboBox)window.FindName("SizeModeBox");
        var aspect = (TextBox)window.FindName("AspectBox");
        var fontSize = (TextBox)window.FindName("FontSizeBox");
        var syncButton = (Button)window.FindName("SyncBtn");
        var saveButton = (Button)window.FindName("SaveBtn");
        var preview = (TextBox)window.FindName("PreviewText");
        var card = (Border)window.FindName("PreviewCard");
        var layers = (Canvas)window.FindName("PreviewLayers");
        var status = (TextBlock)window.FindName("StatusText");

        Expect(card.MinHeight == 0, "preview card still forces a height different from the shared layer coordinate system");
        Expect(sizeMode.Items.Count == 2, $"sizing selector still has {sizeMode.Items.Count} options");
        Expect(IndexOfTag(sizeMode, "uniform") == 0 && IndexOfTag(sizeMode, "stretch") == 1, "sizing selector tags are incorrect");
        Expect((sizeMode.Items[0] as ComboBoxItem)?.Content?.ToString() == "固定长宽比", "Chinese uniform label is incorrect");
        Expect((sizeMode.Items[1] as ComboBoxItem)?.Content?.ToString() == "自由拉伸", "Chinese stretch label is incorrect");
        applyLanguage.Invoke(window, [true]);
        Expect((sizeMode.Items[0] as ComboBoxItem)?.Content?.ToString() == "Fixed aspect ratio", "English uniform label is incorrect");
        Expect((sizeMode.Items[1] as ComboBoxItem)?.Content?.ToString() == "Free stretch", "English stretch label is incorrect");
        applyLanguage.Invoke(window, [false]);

        readControls.Invoke(window, null);
        lastSyncedSnapshot.SetValue(window, snapshot.Invoke(window, null));
        updateSyncState.Invoke(window, [null]);
        Expect(syncButton.Content?.ToString()?.Contains("已同步") == true, "sync button did not enter synced state");
        hasStandaloneSave.SetValue(window, true); updateSaveButton.Invoke(window, null);
        sizeMode.SelectedIndex = IndexOfTag(sizeMode, "stretch");
        Expect(syncButton.Content?.ToString() == "保存并同步", "sizing change did not mark the synchronized style dirty");
        Expect(saveButton.Content?.ToString()?.Contains("已保存") == true, "standalone saved button changed with sync dirtiness");
        sizeMode.SelectedIndex = IndexOfTag(sizeMode, "uniform");
        Expect(syncButton.Content?.ToString()?.Contains("已同步") == true, "exactly reverting a change did not restore synced state");
        fontSize.Text = "15";
        Expect(syncButton.Content?.ToString() == "保存并同步", "font-size change did not mark the style dirty");
        fontSize.Text = "14";
        Expect(syncButton.Content?.ToString()?.Contains("已同步") == true, "reverting font size did not restore synced state");

        inputType.SelectedIndex = IndexOfTag(inputType, "number");
        maxLength.Text = "3";
        pattern.Text = "[0-9]+";
        required.IsChecked = true;
        Expect(style.TextRules.Type == "number" && style.TextRules.MaxLength == 3 && style.TextRules.Pattern == "[0-9]+" && style.TextRules.Required, "rule controls did not update model immediately");
        Expect(preview.MaxLength == 3, "maximum length did not update preview immediately");

        preview.Text = "";
        Expect(card.Width > 0 && preview.BorderThickness.Left > 0, "required did not mark empty preview immediately");
        preview.Text = "1";
        Expect(preview.BorderThickness.Left == 0, "valid required preview stayed invalid");

        maxLength.Text = "0"; preview.Text = "12"; preview.SelectionStart = preview.Text.Length;
        status.Text = "unchanged";
        var invalidData = new DataObject(); invalidData.SetData(DataFormats.UnicodeText, "3a");
        paste.Invoke(window, [preview, new DataObjectPastingEventArgs(invalidData, false, DataFormats.UnicodeText)]);
        Expect(status.Text != "unchanged", "invalid paste was not rejected");
        status.Text = "unchanged";
        var validData = new DataObject(); validData.SetData(DataFormats.UnicodeText, "34");
        paste.Invoke(window, [preview, new DataObjectPastingEventArgs(validData, false, DataFormats.UnicodeText)]);
        Expect(status.Text == "unchanged", "valid paste was rejected");

        inputType.SelectedIndex = IndexOfTag(inputType, "any"); required.IsChecked = false;
        style.TextRegion = new TextRegion { X = 10, Y = 10, W = 80, H = 80 };
        style.Layers.Clear(); style.Layers.Add(new StyleLayer { Type = "image", X = 0, Y = 0, W = 100, H = 100, ImageData = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" });
        style.TextSizing.Width = 999; style.TextSizing.Height = 777; style.TextSizing.Aspect = 1.8;
        refresh.Invoke(window, null);
        Expect(layers.Children.OfType<Image>().Any(), "embedded image layer was not rendered in the independent preview");

        var measuredNaN = (PreviewDimensions)(measurePreviewText.Invoke(window, [double.NaN]) ?? throw new Exception("NaN measurement missing"));
        Expect(double.IsFinite(measuredNaN.Width) && double.IsFinite(measuredNaN.Height), "NaN width reached WPF measurement");
        style.TextRegion = new TextRegion { X = double.NaN, Y = double.NaN, W = double.NaN, H = double.NaN };
        refresh.Invoke(window, null);
        Expect(double.IsFinite(card.Width) && double.IsFinite(card.Height), "corrupted text region produced a non-finite preview");
        style.TextRegion = new TextRegion { X = 10, Y = 10, W = 80, H = 80 };

        sizeMode.SelectedIndex = IndexOfTag(sizeMode, "uniform");
        preview.Text = ""; refresh.Invoke(window, null); var emptyUniformWidth = card.Width; var emptyUniformHeight = card.Height;
        preview.Text = "字";
        Expect(Math.Abs(card.Width - emptyUniformWidth) < .05 && Math.Abs(card.Height - emptyUniformHeight) < .05, "empty text and one character do not share the minimum uniform size");
        preview.Text = "字字";
        Expect(Math.Abs(card.Width - emptyUniformWidth) < .05 && Math.Abs(card.Height - emptyUniformHeight) < .05, "uniform grew before the drawn text region was full");
        Expect(preview.VerticalContentAlignment == VerticalAlignment.Center, "preview content is not vertically centered");
        Expect(Math.Abs(card.Width / card.Height - 1.8) < .001, "uniform preview changed aspect ratio");
        Expect(RequiredTextHeight(preview) <= preview.Height + 1, "short uniform preview clips text");

        sizeMode.SelectedIndex = IndexOfTag(sizeMode, "stretch");
        Expect(!aspect.IsEnabled, "aspect input stayed enabled in free-stretch mode");
        preview.Text = ""; refresh.Invoke(window, null); var emptyStretchWidth = card.Width; var emptyStretchHeight = card.Height;
        preview.Text = "字";
        Expect(Math.Abs(card.Width - emptyStretchWidth) < .05 && Math.Abs(card.Height - emptyStretchHeight) < .05, "empty text and one character do not share the minimum stretch size");
        preview.Text = "字字";
        Expect(Math.Abs(card.Width - emptyStretchWidth) < .05 && Math.Abs(card.Height - emptyStretchHeight) < .05, "stretch grew before the drawn text region was full");
        Expect(Math.Abs(card.Width / card.Height - 1.8) < .001, "free-stretch minimum frame flipped the authored orientation");

        double previousWidth = 0, previousHeight = 0;
        foreach (var length in new[] { 20, 100, 500, 2_000, 5_000 })
        {
            preview.Text = new string('中', length);
            Expect(card.Width + .01 >= previousWidth && card.Height + .01 >= previousHeight, $"stretch size shrank at {length} characters");
            var requiredHeight = RequiredTextHeight(preview);
            Expect(requiredHeight <= preview.Height + 1, $"stretch clips at {length} characters: required={requiredHeight:0.##}, region={preview.Height:0.##}");
            previousWidth = card.Width; previousHeight = card.Height;
        }
        Expect(card.Height > 260, $"stretch did not grow for long text: {card.Width:0.##}x{card.Height:0.##}");
        Expect(card.Width > emptyStretchWidth && card.Height > emptyStretchHeight, $"stretch did not grow both axes for long text: {card.Width:0.##}x{card.Height:0.##}");
        Expect(card.Width <= 10_000 && card.Height <= 10_000, "stretch exceeded safe dimensions");
        Expect(Math.Abs(preview.Width - card.Width * .8) < .01 && Math.Abs(preview.Height - card.Height * .8) < .01, "text region did not scale with card");
        Expect(Math.Abs(layers.Width - card.Width) < .01 && Math.Abs(layers.Height - card.Height) < .01, "layers did not scale with card");
        var stretchWidth = card.Width; var stretchHeight = card.Height;

        sizeMode.SelectedIndex = IndexOfTag(sizeMode, "uniform");
        Expect(aspect.IsEnabled, "aspect input stayed disabled in fixed-aspect mode");
        previousWidth = previousHeight = 0;
        foreach (var length in new[] { 20, 100, 500, 2_000, 5_000 })
        {
            preview.Text = new string('A', length);
            Expect(card.Width + .01 >= previousWidth && card.Height + .01 >= previousHeight, $"uniform size shrank at {length} characters");
            Expect(Math.Abs(card.Width / card.Height - 1.8) < .001, $"uniform changed aspect ratio at {length} characters");
            var requiredHeight = RequiredTextHeight(preview);
            Expect(requiredHeight <= preview.Height + 1, $"uniform clips at {length} characters: required={requiredHeight:0.##}, region={preview.Height:0.##}");
            previousWidth = card.Width; previousHeight = card.Height;
        }
        Expect(card.Width > 360 && card.Height > 260, $"uniform remained capped at {card.Width:0.##}x{card.Height:0.##}");
        Expect(Math.Abs(stretchWidth / stretchHeight - card.Width / card.Height) > .1, "free stretch behaves like fixed-aspect sizing");

        Console.WriteLine($"PREVIEW_RULES_SIZING_SMOKE_OK stretch={stretchWidth:0.##}x{stretchHeight:0.##} uniform={card.Width:0.##}x{card.Height:0.##}");
        window.Close();
    }
}
