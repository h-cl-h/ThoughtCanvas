using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BmapTextStyleEditor;

internal static class Program
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;

    static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static TextBoxStyle CurrentStyle(MainWindow window, FieldInfo styleField) =>
        (TextBoxStyle)(styleField.GetValue(window) ?? throw new Exception("style missing"));

    static void CheckNonFiniteHistorySafety()
    {
        var window = new MainWindow();
        var styleField = typeof(MainWindow).GetField("style", Flags) ?? throw new Exception("style field missing");
        var commit = typeof(MainWindow).GetMethod("CommitHistory", Flags) ?? throw new Exception("CommitHistory missing");
        var historyField = typeof(MainWindow).GetField("history", Flags) ?? throw new Exception("history field missing");
        var style = CurrentStyle(window, styleField);
        style.Layers.Add(new StyleLayer { X = double.PositiveInfinity, Y = double.NaN, W = double.NegativeInfinity, H = double.PositiveInfinity });
        style.TextRegion = new TextRegion { X = double.NegativeInfinity, Y = double.NaN, W = double.PositiveInfinity, H = double.NegativeInfinity };
        for (var i = 0; i < 2_000; i++)
        {
            style.Layers[0].X = i % 2 == 0 ? double.PositiveInfinity : double.NaN;
            commit.Invoke(window, null);
        }
        var history = (System.Collections.ICollection)(historyField.GetValue(window) ?? throw new Exception("history missing"));
        Expect(history.Count > 0, "history stayed empty after non-finite safety cycle");
        window.Close();
    }

    static void CheckShortcutHistory()
    {
        var window = new MainWindow();
        var type = typeof(MainWindow);
        var styleField = type.GetField("style", Flags) ?? throw new Exception("style field missing");
        var selectedField = type.GetField("selected", Flags) ?? throw new Exception("selected field missing");
        var historyIndexField = type.GetField("historyIndex", Flags) ?? throw new Exception("historyIndex missing");
        var commit = type.GetMethod("CommitHistory", Flags) ?? throw new Exception("CommitHistory missing");
        var handle = type.GetMethod("HandleHistoryShortcut", Flags) ?? throw new Exception("HandleHistoryShortcut missing");
        var isTextFocus = type.GetMethod("IsTextEditingFocus", Flags) ?? throw new Exception("IsTextEditingFocus missing");
        var readControls = type.GetMethod("ReadControls", Flags) ?? throw new Exception("ReadControls missing");
        var applyLayerFields = type.GetMethod("ApplyLayerFields", Flags) ?? throw new Exception("ApplyLayerFields missing");

        bool Shortcut(Key key, ModifierKeys modifiers, bool textInputFocused = false) =>
            (bool)(handle.Invoke(window, [key, modifiers, textInputFocused]) ?? false);

        // Drawing state.
        var style = CurrentStyle(window, styleField);
        style.Layers.Add(new StyleLayer { Type = "rect", X = 10, Y = 12, W = 30, H = 20, Fill = "#DDE9FF" });
        commit.Invoke(window, null);

        // Move state.
        style.Layers[0].X = 35; style.Layers[0].Y = 22;
        commit.Invoke(window, null);

        // Resize state.
        style.Layers[0].W = 48; style.Layers[0].H = 42;
        commit.Invoke(window, null);

        // Actual property-control path: read general controls, apply selected-layer fields,
        // then commit exactly as the Apply button does.
        selectedField.SetValue(window, 0);
        ((TextBox)window.FindName("NameBox")).Text = "属性版本";
        ((TextBox)window.FindName("FontSizeBox")).Text = "27";
        ((TextBox)window.FindName("LayerFillBox")).Text = "#112233";
        ((TextBox)window.FindName("LayerStrokeBox")).Text = "#445566";
        ((TextBox)window.FindName("XBox")).Text = "35";
        ((TextBox)window.FindName("YBox")).Text = "22";
        ((TextBox)window.FindName("WBox")).Text = "48";
        ((TextBox)window.FindName("HBox")).Text = "42";
        readControls.Invoke(window, null); applyLayerFields.Invoke(window, null); commit.Invoke(window, null);
        style = CurrentStyle(window, styleField);
        Expect(style.Name == "属性版本" && style.FontSize == 27 && style.Layers[0].Fill == "#112233", "property-control state was not committed");

        // When a text input owns focus, editor history must not consume its Ctrl+Z/Y.
        var beforeTextShortcut = (int)(historyIndexField.GetValue(window) ?? -1);
        Expect(!Shortcut(Key.Z, ModifierKeys.Control, true), "Ctrl+Z was stolen from a text input");
        Expect(!Shortcut(Key.Y, ModifierKeys.Control, true), "Ctrl+Y was stolen from a text input");
        Expect(!Shortcut(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, true), "Ctrl+Shift+Z was stolen from a text input");
        Expect((int)(historyIndexField.GetValue(window) ?? -1) == beforeTextShortcut, "text-input shortcut changed editor history");
        Expect((bool)(isTextFocus.Invoke(null, [new TextBox()]) ?? false), "TextBox was not recognized as text editing focus");
        Expect((bool)(isTextFocus.Invoke(null, [new PasswordBox()]) ?? false), "PasswordBox was not recognized as text editing focus");
        Expect((bool)(isTextFocus.Invoke(null, [new ComboBox { IsEditable = true }]) ?? false), "editable ComboBox was not recognized as text editing focus");
        Expect(!(bool)(isTextFocus.Invoke(null, [new ComboBox { IsEditable = false }]) ?? true), "ordinary ComboBox was incorrectly treated as text editing focus");

        // Undo property, resize, move, and drawing in order.
        Expect(Shortcut(Key.Z, ModifierKeys.Control), "Ctrl+Z was not handled");
        style = CurrentStyle(window, styleField);
        Expect(style.Name != "属性版本" && style.Layers[0].Fill == "#DDE9FF" && style.Layers[0].W == 48, "Ctrl+Z did not undo property edit only");

        Expect(Shortcut(Key.Z, ModifierKeys.Control), "second Ctrl+Z was not handled");
        style = CurrentStyle(window, styleField);
        Expect(style.Layers[0].W == 30 && style.Layers[0].H == 20 && style.Layers[0].X == 35, "Ctrl+Z did not undo resize only");

        Expect(Shortcut(Key.Z, ModifierKeys.Control), "third Ctrl+Z was not handled");
        style = CurrentStyle(window, styleField);
        Expect(style.Layers[0].X == 10 && style.Layers[0].Y == 12, "Ctrl+Z did not undo move");

        Expect(Shortcut(Key.Z, ModifierKeys.Control), "fourth Ctrl+Z was not handled");
        Expect(CurrentStyle(window, styleField).Layers.Count == 0, "Ctrl+Z did not undo drawing");

        // Redo every state using both supported redo shortcuts.
        Expect(Shortcut(Key.Y, ModifierKeys.Control), "Ctrl+Y was not handled");
        style = CurrentStyle(window, styleField);
        Expect(style.Layers.Count == 1 && style.Layers[0].X == 10, "Ctrl+Y did not redo drawing");

        Expect(Shortcut(Key.Z, ModifierKeys.Control | ModifierKeys.Shift), "Ctrl+Shift+Z was not handled");
        Expect(CurrentStyle(window, styleField).Layers[0].X == 35, "Ctrl+Shift+Z did not redo move");

        Expect(Shortcut(Key.Y, ModifierKeys.Control), "second Ctrl+Y was not handled");
        Expect(CurrentStyle(window, styleField).Layers[0].W == 48, "Ctrl+Y did not redo resize");

        Expect(Shortcut(Key.Z, ModifierKeys.Control | ModifierKeys.Shift), "second Ctrl+Shift+Z was not handled");
        style = CurrentStyle(window, styleField);
        Expect(style.Name == "属性版本" && style.Layers[0].Fill == "#112233", "Ctrl+Shift+Z did not redo property edit");

        Expect(!Shortcut(Key.A, ModifierKeys.Control), "unrelated shortcut was consumed as history command");
        Console.WriteLine("EDITOR_SHORTCUT_HISTORY_OK draw move resize properties Ctrl+Z Ctrl+Y Ctrl+Shift+Z text-focus");
        window.Close();
    }

    [STAThread]
    static void Main()
    {
        _ = new Application();
        CheckNonFiniteHistorySafety();
        CheckShortcutHistory();
    }
}
