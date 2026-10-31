using ArnoA2.App.Map;
using ArnoA2.Tracker;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArnoA2.App;

/// <summary>The Pet Tracker's maps: spawns of the pets you still need, in blue.</summary>
public sealed class PetLayer : MapLayer
{
    readonly AppState app;
    Dictionary<int, double> weights = new();

    public PetLayer(AppState app, MapHub hub) : base(hub) => this.app = app;

    public override ToolColors Colors => ToolTheme.PetColors;
    public override int Ring => 0;
    public override MapLook Look => Hub.Preferences;
    public override string Unit => "spawn";
    public override string PickerTitle => "Farm for";
    public override string Help => "Spots rank pet spawns you still need by density and distance. Paws are approximate static spawns, white-edged ones were learned from play; bright ringed dots are pet mobs seen live.";
    protected override string Nothing => weights.Count == 0 ? "No pets match this target" : "No spawn data for these pets here";
    public override bool ShowsMobs => true;
    public override void Save() => Hub.Save();
    public override Control Picker() => new PetPicker(this);
    public override bool WantsMob(int petId) => weights.ContainsKey(petId);

    public void SetTarget(string target, int? petId = null)
    {
        Hub.Preferences.Target = target;
        Hub.Preferences.PetId = petId;
        Hub.Save();
        TargetSpot = 0;
        Refresh(force: true);
    }

    /// <summary>The collection or settings changed: re-rank farming spots.</summary>
    public void CollectionChanged() => Refresh(force: false);

    protected override string Wanted()
    {
        var preferences = Hub.Preferences;
        var collection = app.Engine.Collection;
        var pins = app.Engine.Settings.Pins;
        weights = new Dictionary<int, double>();
        foreach (var entry in GameData.Instance.Pets.Values)
        {
            var pet = collection.Pets.GetValueOrDefault(entry.Id);
            bool wanted = preferences.Target switch
            {
                "Pinned" => pins.Contains(entry.Id),
                "Pet" => preferences.PetId == entry.Id,
                "Unfinished" => true,
                string family => entry.Family.ToString() == family
            };
            double weight = wanted ? FarmingHotspots.Weight(pet) : 0;
            if (weight > 0)
                weights[entry.Id] = weight;
        }

        return Hub.Learned.Count + "|" + string.Join(",", weights.OrderBy(w => w.Key).Select(w => w.Key + ":" + w.Value.ToString("F2")));
    }

    protected override IReadOnlyList<MapDot> Plot()
    {
        var brushes = weights.Keys.ToDictionary(id => id, MapCanvas.FamilyBrush);
        return Hub.Spawns.Points.Where(p => weights.ContainsKey(p.PetId)).Select(p => new MapDot(p.X, p.Y, brushes[p.PetId], MarkerShape.Paw))
            .Concat(Hub.Learned.Region(Hub.Region).Where(p => weights.ContainsKey(p.PetId)).Select(p => new MapDot(p.X, p.Y, brushes[p.PetId], MarkerShape.Paw, Learned: true))).ToArray();
    }

    protected override IReadOnlyList<Hotspot> Rank() => FarmingHotspots.Find(Hub.Spawns.Points.Concat(Hub.Learned.Region(Hub.Region)).Select(p => new MapMarker(p.X, p.Y, p.PetId)), Hub.Map, weights, Hub.Player);

    public override string Names(IEnumerable<int> ids, int limit = 3)
    {
        var names = ids.Select(id => GameData.Instance.Pets.GetValueOrDefault(id)?.Name).Where(n => n != null).ToArray();
        return names.Length <= limit ? string.Join(", ", names) : string.Join(", ", names.Take(limit)) + $" +{names.Length - limit}";
    }
}

/// <summary>Chooses which pets the map looks for.</summary>
public sealed class PetPicker : UserControl
{
    static readonly (string Key, string Label)[] Targets =
    {
        ("Unfinished", "All unfinished pets"), ("Pinned", "Pinned pets"), ("Cogni", "Family: Cogni"), ("Fera", "Family: Fera"),
        ("Natura", "Family: Natura"), ("Varian", "Family: Varian"), ("Special", "Family: Special"), ("Pet", "One pet")
    };

    public PetPicker(PetLayer layer)
    {
        var preferences = layer.Hub.Preferences;
        var target = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Targets.Select(t => t.Label).ToArray() };
        target.SelectedIndex = Math.Max(0, Array.FindIndex(Targets, t => t.Key == preferences.Target));
        var pet = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a pet…", IsVisible = preferences.Target == "Pet" };
        pet.ItemsSource = GameData.Instance.Pets.Values.OrderBy(e => e.Name).Select(e => e.Name).ToArray();
        if (preferences.PetId is int chosen)
            pet.SelectedItem = GameData.Instance.Pets.GetValueOrDefault(chosen)?.Name;

        void Change()
        {
            string key = Targets[Math.Max(0, target.SelectedIndex)].Key;
            int? petId = GameData.Instance.Pets.Values.FirstOrDefault(e => e.Name == pet.SelectedItem as string)?.Id;
            pet.IsVisible = key == "Pet";
            layer.SetTarget(key, petId);
        }

        target.SelectionChanged += (_, _) => Change();
        pet.SelectionChanged += (_, _) => Change();
        Content = new StackPanel { Spacing = 10, Children = { target, pet } };
    }
}
