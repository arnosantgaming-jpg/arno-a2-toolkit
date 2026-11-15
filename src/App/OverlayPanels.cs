using System.Globalization;
using ArnoA2.Meter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>Small builders for the overlays' settings pages: chips to pick one of a few, and
/// ticks for the ones that can be on together. Every change calls back so the overlay redraws.</summary>
static class OverlayUi
{
    /// <summary>A row of chips; the chosen one is lit.</summary>
    public static Control Chips(string label, string[] options, Func<string> chosen, Action<string> pick, Func<string, IBrush?>? colour = null)
    {
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        void Draw()
        {
            chips.Children.Clear();
            foreach (string option in options)
            {
                bool on = option == chosen();
                var words = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                if (colour?.Invoke(option) is { } swatch)
                    words.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = swatch, VerticalAlignment = VerticalAlignment.Center });
                words.Children.Add(new TextBlock { Text = option, FontSize = 12.5, Foreground = on ? Navy.Background : Navy.Text2, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal });
                var chip = new Border { Padding = new Thickness(11, 4), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Background = on ? Navy.Accent : Navy.Surface2, BorderBrush = on ? Navy.Accent : Navy.LineStrong, Child = words, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
                chip.PointerReleased += (_, _) =>
                {
                    pick(option);
                    Draw();
                };
                chips.Children.Add(chip);
            }
        }

        Draw();
        return Labelled(label, chips);
    }

    /// <summary>A tick for each option, shown when ticked.</summary>
    public static Control Ticks(string label, IEnumerable<string> options, List<string> hidden, Action changed, Func<string, IBrush?>? colour = null)
    {
        var ticks = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (string option in options)
        {
            var box = new CheckBox { IsChecked = !hidden.Contains(option), Margin = new Thickness(0, 0, 16, 0), Content = new TextBlock { Text = option, Foreground = colour?.Invoke(option) ?? Navy.Text } };
            box.IsCheckedChanged += (_, _) =>
            {
                hidden.Remove(option);
                if (box.IsChecked != true)
                    hidden.Add(option);
                changed();
            };
            ticks.Children.Add(box);
        }

        return Labelled(label, ticks);
    }

    public static Control Tick(string label, bool value, Action<bool> changed) => Ui.Toggle(label, value, changed);

    public static Control Labelled(string label, Control input) => new StackPanel { Spacing = 6, Children = { new TextBlock { Text = label, FontSize = 12.5, Foreground = Navy.Text2 }, input } };

    public static Control Section(string title, params Control[] parts)
    {
        var panel = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Navy.Text } } };
        foreach (var part in parts)
            panel.Children.Add(part);
        return new Border { Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(8), Background = Navy.Surface2, BorderBrush = Navy.Line, BorderThickness = new Thickness(1), Child = panel, Classes = { "card" } };
    }
}

/// <summary>An overlay's right-click menu: its settings in the toolkit, a shortcut or two, Close.</summary>
static class OverlayMenu
{
    public static List<MenuItem> Items(Action? settings, Action close, params (string Label, Action Act)[] extra)
    {
        MenuItem Item(string header, Action act)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => act();
            return item;
        }

        var items = new List<MenuItem>();
        if (settings != null)
            items.Add(Item("Settings…", settings));
        items.AddRange(extra.Select(e => Item(e.Label, e.Act)));
        items.Add(Item("Close", close));
        return items;
    }
}

/// <summary>The HP, mana and stamina bars' settings, on the toolkit's HP and Mana page.</summary>
public sealed class BarsSettingsPanel : UserControl
{
    public BarsSettingsPanel(BarSettings bars, Action changed)
    {
        static string[] Names((string Name, string Color)[] colours) => colours.Select(c => c.Name).ToArray();
        static Func<string, IBrush?> Swatch((string Name, string Color)[] colours) => name => Brush.Parse(colours.First(c => c.Name == name).Color);
        Action<string> Set(Action<string> apply) => value =>
        {
            apply(value);
            changed();
        };

        string[] pulses = { "Off", "Below 20 %", "Below 30 %", "Below 50 %" };
        Content = new StackPanel
        {
            Children =
            {
                OverlayUi.Section("Look",
                    OverlayUi.Chips("Size", BarSettings.Sizes, () => bars.Size, Set(v => bars.Size = v)),
                    OverlayUi.Chips("Text on the bars", BarSettings.NumberStyles, () => bars.Numbers, Set(v => bars.Numbers = v))),
                OverlayUi.Section("Colours",
                    OverlayUi.Chips("HP", Names(BarSettings.HpColors), () => bars.HpColor, Set(v => bars.HpColor = v), Swatch(BarSettings.HpColors)),
                    OverlayUi.Chips("Mana", Names(BarSettings.ManaColors), () => bars.ManaColor, Set(v => bars.ManaColor = v), Swatch(BarSettings.ManaColors)),
                    OverlayUi.Chips("Stamina", Names(BarSettings.StaminaColors), () => bars.StaminaColor, Set(v => bars.StaminaColor = v), Swatch(BarSettings.StaminaColors))),
                OverlayUi.Section("Behaviour",
                    OverlayUi.Tick("Stamina bar", bars.Stamina, on => { bars.Stamina = on; changed(); }),
                    OverlayUi.Chips("Pulse the HP bar when low", pulses, () => bars.LowHp == 0 ? "Off" : $"Below {bars.LowHp} %",
                        Set(v => bars.LowHp = v == "Off" ? 0 : int.Parse(v.Split(' ')[1], CultureInfo.InvariantCulture))),
                    OverlayUi.Tick("Fade when HP, mana and stamina are full", bars.Fade, on => { bars.Fade = on; changed(); }),
                    OverlayUi.Tick("Fade when you've done nothing for a moment", bars.IdleFade, on => { bars.IdleFade = on; changed(); }),
                    Ui.Note("Drag the bars to move them. The game sends each value only when it changes, so a bar is blank until its first change. On KDE, System Settings → Shortcuts can give Show or hide HP bars a key."))
            }
        };
    }
}

/// <summary>Session Gains' settings, on the toolkit's Session Gains page.</summary>
public sealed class GainsSettingsPanel : UserControl
{
    public GainsSettingsPanel(SessionLook look, Action changed, Action restart)
    {
        string[] columns = Enumerable.Range(1, SessionGains.Tracked.Length).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray();
        var body = new StackPanel();
        void Build()
        {
            body.Children.Clear();
            body.Children.Add(OverlayUi.Section("Show",
                OverlayUi.Ticks("Currencies", SessionGains.Tracked.Select(t => t.Name), look.Hidden, changed, name => Brush.Parse(SessionWindow.Colors[name])),
                OverlayUi.Tick("Per hour", look.Rates, on => { look.Rates = on; changed(); })));
            body.Children.Add(OverlayUi.Section("Layout",
                OverlayUi.Chips("Size", SessionLook.Sizes, () => look.Size, v => { look.Size = v; changed(); }),
                OverlayUi.Chips("Columns", columns, () => look.Columns.ToString(CultureInfo.InvariantCulture), v => { look.Columns = int.Parse(v, CultureInfo.InvariantCulture); changed(); }),
                Ui.Note("One row puts SESSION and the timer in front."),
                Ui.Button("Reset layout", () =>
                {
                    look.Reset();
                    changed();
                    Build();
                })));
            body.Children.Add(OverlayUi.Section("Session",
                Ui.Note("Counts from when the toolkit found you, or from the last reset, and leaves out what a defeat costs."),
                Ui.Button("Reset session", restart, "Start counting again from now")));
        }

        Build();
        Content = body;
    }
}

/// <summary>The item log's settings, on the toolkit's Item Log page.</summary>
public sealed class ItemLogSettingsPanel : UserControl
{
    public ItemLogSettingsPanel(ItemLogLook look, Action changed, Action clear)
    {
        string[] lines = ItemLogLook.LineChoices.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray();
        var body = new StackPanel();
        void Build()
        {
            body.Children.Clear();
            body.Children.Add(OverlayUi.Section("Show",
                OverlayUi.Ticks("Grades", ItemTable.Grades.Select(g => g.Grade), look.HiddenGrades, changed, grade => Brush.Parse(ItemTable.ColorOf(grade))),
                OverlayUi.Ticks("Types", ItemTable.Types, look.HiddenTypes, changed)));
            body.Children.Add(OverlayUi.Section("Layout",
                OverlayUi.Chips("Lines", lines, () => look.Lines.ToString(CultureInfo.InvariantCulture), v => { look.Lines = int.Parse(v, CultureInfo.InvariantCulture); changed(); }),
                OverlayUi.Chips("Size", SessionLook.Sizes, () => look.Size, v => { look.Size = v; changed(); }),
                OverlayUi.Tick("Show times", look.Times, on => { look.Times = on; changed(); }),
                Ui.Button("Reset layout", () =>
                {
                    look.Reset();
                    changed();
                    Build();
                })));
            body.Children.Add(OverlayUi.Section("Log",
                Ui.Note("Item names, grades and types come from " + ItemTable.SourcePage + "."),
                Ui.Button("Clear log", clear, "Empty the item log")));
        }

        Build();
        Content = body;
    }
}
