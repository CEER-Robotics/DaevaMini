using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DaevaMini.Config;
using DaevaMini.Services;

namespace DaevaMini.Views.Max;

/// <summary>
/// The one place where recipes are written: pick a drink from the menu to retune it, or
/// start a new one from whatever the lines are carrying.
/// </summary>
/// <remarks>
/// This replaces the separate "dosi" and "crea" pages. They were the same job split in
/// two - both ended up writing <see cref="CocktailConfig.Ingredients"/> - and the split
/// forced you out to the settings grid and back just to copy an existing drink into a
/// new one. Here the picker on the left and the ingredient list in the middle feed the
/// same editor, so tuning and inventing are the same three taps.
///
/// Unlike the old doses page, a change is not written the instant you press "+".
/// A half-built new drink has nothing to write to, so the editor holds the recipe and
/// <see cref="OnSave"/> commits it; <see cref="IsDirty"/> keeps the pending state visible.
/// </remarks>
public partial class CocktailWorkshopPage : UserControl, INotifyPropertyChanged
{
    private const int StepMl = 5;
    private const int MinMl = 5;
    private const int MaxMl = 500;
    private const int DefaultBaseMl = 40;
    private const int DefaultMixerMl = 130;
    private const int MaxNameLength = 24;

    /// <summary>The stored recipe being edited, or null while building a new one.</summary>
    private CocktailConfig? _editing;

    private string _name = string.Empty;
    private bool _dirty;

    /// <summary>Artwork chosen for the drink: the bare asset name, empty for none.</summary>
    private string _image = string.Empty;

    /// <summary>The category the machine files its own creations under.</summary>
    private const string CreationsCategory = "Creazioni";

    public CocktailWorkshopPage()
    {
        InitializeComponent();

        LoadMenu();
        LoadAvailableLiquids();
        LoadArtworks();

        Recipe.CollectionChanged += (_, _) => { MarkDirty(); RecipeChanged(); };
        RecipeChanged();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AppConfigService.Instance.ConfigChanged += OnConfigChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        AppConfigService.Instance.ConfigChanged -= OnConfigChanged;
        base.OnUnloaded(e);
    }

    private void OnConfigChanged(object? sender, AppConfigChangedEventArgs e)
        => Dispatcher.UIThread.Post(LoadAvailableLiquids);

    // ------------------------------------------------------------ the menu, on the left

    public ObservableCollection<MenuCocktail> Menu { get; } = new();

    private void LoadMenu()
    {
        Menu.Clear();
        foreach (var cocktail in AppConfigService.Instance.Config.Cocktails)
            Menu.Add(new MenuCocktail(cocktail));
    }

    private void OnSelectCocktail(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: MenuCocktail entry }) return;

        _editing = entry.Config;
        _name = entry.Config.Name;
        _image = entry.Config.ShowImage ? entry.Config.Image : string.Empty;

        foreach (var other in Menu)
            other.IsSelected = ReferenceEquals(other, entry);

        // Copied, not referenced: an abandoned edit must leave the stored recipe alone.
        Recipe.Clear();
        foreach (var ingredient in entry.Config.Ingredients)
            Recipe.Add(new WorkshopIngredient(
                ingredient.Name,
                ingredient.Milliliters,
                LiquidKinds.IsAlcoholic(ingredient.Name, AppConfigService.Instance.Config)));

        _dirty = false;
        StatusLine.Text = string.Empty;
        RecipeChanged();
    }

    private void OnNewCocktail(object? sender, RoutedEventArgs e)
    {
        _editing = null;
        _name = string.Empty;
        _image = string.Empty;

        foreach (var entry in Menu)
            entry.IsSelected = false;

        Recipe.Clear();
        _dirty = false;
        StatusLine.Text = string.Empty;
        RecipeChanged();
    }

    /// <summary>
    /// Turns a drink on or off. Written straight through, unlike the recipe: it is one
    /// bit with no half-finished state, and this is the switch to reach for when a
    /// bottle runs out mid-service.
    /// </summary>
    private void OnToggleActive(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: MenuCocktail entry }) return;

        entry.IsActive = !entry.IsActive;
        AppConfigService.Instance.SaveConfig("cocktail-active");

        StatusLine.Text = entry.IsActive
            ? $"{entry.Name}: attivo"
            : $"{entry.Name}: disattivato, non ordinabile";
    }

    // ------------------------------------------------------- what the lines are carrying

    public ObservableCollection<WorkshopLiquid> Bases { get; } = new();
    public ObservableCollection<WorkshopLiquid> Mixers { get; } = new();

    public bool HasBases => Bases.Count > 0;
    public bool HasMixers => Mixers.Count > 0;

    /// <summary>
    /// Every liquid the machine knows, not just the ones plumbed in right now: the whole
    /// point of writing a recipe ahead of service is that the bottle is not on the line yet.
    /// </summary>
    /// <remarks>
    /// Which line carries what comes from <see cref="AppConfig.GetActiveLiquidAssignments"/>,
    /// the call that blanks the kegs while "Attiva fusti" is off - so a keg liquid reads as
    /// "non in linea" rather than disappearing, and there is no second rule to keep in step.
    /// A recipe using something unloaded saves fine; the menu simply keeps the drink off
    /// the cards until the liquid arrives, which is <see cref="CocktailAvailability"/>'s job.
    /// </remarks>
    private void LoadAvailableLiquids()
    {
        var config = AppConfigService.Instance.Config;
        var active = config.GetActiveLiquidAssignments();

        // Where each liquid is pouring from, for the ones that are loaded.
        var lines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < active.Length; i++)
        {
            string liquid = active[i]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(liquid) || lines.ContainsKey(liquid)) continue;

            lines[liquid] = i >= AppConfig.PumpChannelCount ? $"fusto {i + 1}" : $"linea {i + 1}";
        }

        var names = new HashSet<string>(CocktailAvailability.KnownLiquids(config), StringComparer.OrdinalIgnoreCase);
        foreach (var custom in config.CustomLiquids)
            if (!string.IsNullOrWhiteSpace(custom.Name))
                names.Add(custom.Name.Trim());

        var bases = new List<WorkshopLiquid>();
        var mixers = new List<WorkshopLiquid>();

        foreach (string name in names)
        {
            lines.TryGetValue(name, out string? line);
            var entry = new WorkshopLiquid(name, line, LiquidKinds.IsAlcoholic(name, config));
            (entry.IsAlcoholic ? bases : mixers).Add(entry);
        }

        // Loaded first: those are the ones you can pour tonight.
        static IEnumerable<WorkshopLiquid> Ordered(IEnumerable<WorkshopLiquid> all)
            => all.OrderByDescending(l => l.IsLoaded)
                  .ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase);

        Bases.Clear();
        foreach (var entry in Ordered(bases)) Bases.Add(entry);

        Mixers.Clear();
        foreach (var entry in Ordered(mixers)) Mixers.Add(entry);

        OnPropertyChanged(nameof(HasBases));
        OnPropertyChanged(nameof(HasMixers));
        RecipeChanged();
    }

    /// <summary>
    /// Files a liquid the bar brought in themselves. Kind comes from which column's
    /// button was pressed rather than from guessing at the name.
    /// </summary>
    private void AddCustomLiquid(string name, bool alcoholic)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        var config = AppConfigService.Instance.Config;

        bool known = config.CustomLiquids.Any(
            c => name.Equals(c.Name?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!known)
        {
            config.CustomLiquids = config.CustomLiquids
                .Append(new CustomLiquidConfig { Name = name, Alcoholic = alcoholic })
                .ToArray();
            AppConfigService.Instance.SaveConfig("custom-liquid");
        }

        LoadAvailableLiquids();
        StatusLine.Text = $"\"{name}\" aggiunto agli ingredienti.";
    }

    // ------------------------------------------------------------ the recipe being built

    public ObservableCollection<WorkshopIngredient> Recipe { get; } = new();

    private void OnAddLiquid(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: WorkshopLiquid liquid }) return;

        var existing = Recipe.FirstOrDefault(
            r => r.Name.Equals(liquid.Name, StringComparison.OrdinalIgnoreCase));

        // A second tap tops up rather than adding a duplicate row, which would have
        // produced two pump runs for one ingredient.
        if (existing != null)
        {
            existing.Milliliters = Math.Min(MaxMl, existing.Milliliters + StepMl);
            MarkDirty();
            RecipeChanged();
            return;
        }

        Recipe.Add(new WorkshopIngredient(
            liquid.Name,
            liquid.IsAlcoholic ? DefaultBaseMl : DefaultMixerMl,
            liquid.IsAlcoholic));
    }

    private void OnRemoveIngredient(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: WorkshopIngredient ingredient }) return;
        Recipe.Remove(ingredient);
    }

    private void OnIncrease(object? sender, RoutedEventArgs e) => Adjust(sender, +StepMl);

    private void OnDecrease(object? sender, RoutedEventArgs e) => Adjust(sender, -StepMl);

    private void Adjust(object? sender, int delta)
    {
        if (sender is not Button { CommandParameter: WorkshopIngredient ingredient }) return;

        ingredient.Milliliters = Math.Clamp(ingredient.Milliliters + delta, MinMl, MaxMl);
        MarkDirty();
        RecipeChanged();
    }

    // ------------------------------------------------------------ names and state

    /// <summary>
    /// What the drink is called. A new one follows the glass - "Gin & Tonica" - so a
    /// recipe can be saved without ever opening the keyboard.
    /// </summary>
    public string DrinkName => string.IsNullOrWhiteSpace(_name) ? SuggestedName : _name.Trim();

    private string SuggestedName => Recipe.Count == 0
        ? "Nuovo cocktail"
        : string.Join(" & ", Recipe.Select(r => r.Name));

    public string EditorSubtitle => _editing == null
        ? "Nuova creazione"
        : $"Ricetta in menu · {_editing.Category}";

    public string TotalLabel => $"{Recipe.Sum(r => r.Milliliters)} ml in totale";

    public bool CanSave => Recipe.Count > 0;
    public bool IsRecipeEmpty => Recipe.Count == 0;
    public bool IsDirty => _dirty;

    /// <summary>
    /// Ingredients no line is carrying. The recipe saves either way - the menu just holds
    /// the drink back until they arrive - so this explains the absence rather than blocking.
    /// </summary>
    private IReadOnlyList<string> MissingFromLines
    {
        get
        {
            var loaded = CocktailAvailability.LoadedLiquids(AppConfigService.Instance.Config);
            return Recipe.Where(r => !loaded.Contains(r.Name)).Select(r => r.Name).ToList();
        }
    }

    public bool HasMissing => MissingFromLines.Count > 0;

    public string MissingLabel
    {
        get
        {
            var missing = MissingFromLines;
            return missing.Count == 0
                ? string.Empty
                : $"Fuori menu finché non carichi: {string.Join(", ", missing)}";
        }
    }

    public string SaveLabel => _editing == null ? "SALVA NEL MENU" : "SALVA MODIFICHE";

    private void MarkDirty()
    {
        if (_dirty) return;
        _dirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    private void RecipeChanged()
    {
        OnPropertyChanged(nameof(DrinkName));
        OnPropertyChanged(nameof(EditorSubtitle));
        OnPropertyChanged(nameof(TotalLabel));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(IsRecipeEmpty));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(HasMissing));
        OnPropertyChanged(nameof(MissingLabel));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(ImageLabel));
        OnPropertyChanged(nameof(ImagePreview));
        OnPropertyChanged(nameof(HasImage));
        DisarmDelete();
    }

    // ------------------------------------------------------------ saving

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Recipe.Count == 0) return;

        var config = AppConfigService.Instance.Config;
        string name = DrinkName;
        var ingredients = Recipe
            .Select(r => new IngredientConfig { Name = r.Name, Milliliters = r.Milliliters })
            .ToArray();

        if (_editing != null)
        {
            _editing.Name = name;
            _editing.Subtitle = string.Join(", ", Recipe.Select(r => r.Name));
            _editing.Ingredients = ingredients;
            _editing.LargeEventOnly = CocktailAvailability.RequiresKegs(_editing, config);
            _editing.Image = _image;
            _editing.ShowImage = !string.IsNullOrEmpty(_image);

            AppConfigService.Instance.SaveConfig("cocktail-recipe");
            StatusLine.Text = $"\"{name}\" aggiornato.";
        }
        else
        {
            var cocktail = new CocktailConfig
            {
                Id = UniqueId(config, name),
                Name = name,
                Subtitle = string.Join(", ", Recipe.Select(r => r.Name)),
                Description = $"Creato sulla macchina: {string.Join(", ", Recipe.Select(r => $"{r.Name} {r.Milliliters} ml"))}.",
                // Empty unless one was picked, in which case the card wears it; ShowImage
                // false is what keeps a drink with no artwork from hunting for a missing png.
                Image = _image,
                ShowImage = !string.IsNullOrEmpty(_image),
                Theme = Recipe.Any(r => r.IsAlcoholic) ? "Burgundy" : "Teal",
                Category = "Creazioni",
                IsActive = true,
                Ingredients = ingredients,
            };

            cocktail.LargeEventOnly = CocktailAvailability.RequiresKegs(cocktail, config);

            config.Cocktails = config.Cocktails.Append(cocktail).ToArray();
            AppConfigService.Instance.SaveConfig("cocktail-new");

            _editing = cocktail;
            StatusLine.Text = $"\"{name}\" salvato: lo trovi nel menu.";
        }

        _dirty = false;
        LoadMenu();
        foreach (var entry in Menu)
            entry.IsSelected = ReferenceEquals(entry.Config, _editing);

        RecipeChanged();
    }

    /// <summary>
    /// Ids key the configuration, so a second "Gin &amp; Tonica" must not overwrite the
    /// first. Slugs the name and counts up until nothing answers to it.
    /// </summary>
    private static string UniqueId(AppConfig config, string name)
    {
        var slug = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        string baseId = slug.ToString().Trim('-');
        if (string.IsNullOrEmpty(baseId)) baseId = "cocktail";

        var taken = config.Cocktails.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseId)) return baseId;

        for (int n = 2; ; n++)
            if (!taken.Contains($"{baseId}-{n}"))
                return $"{baseId}-{n}";
    }

    // ------------------------------------------------------------ artwork

    /// <summary>
    /// Artwork the card can wear. Read out of the compiled resources rather than kept as
    /// a hand-written list, so a png added to the project shows up here on its own.
    /// </summary>
    public ObservableCollection<ArtworkOption> Artworks { get; } = new();

    /// <summary>Logos and glyphs live in the same folder and are no use as a drink card.</summary>
    private static readonly HashSet<string> NotArtwork = new(StringComparer.OrdinalIgnoreCase)
    {
        "DaevaDark", "DaevaWhite", "DaevaMaxFinal", "DaevaMiniLogo", "logo_white",
    };

    private void LoadArtworks()
    {
        Artworks.Clear();
        Artworks.Add(new ArtworkOption(string.Empty));   // "no picture": the plain card

        var names = new List<string>();
        try
        {
            foreach (var uri in AssetLoader.GetAssets(new Uri("avares://Daeva/Assets"), null))
            {
                if (!uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

                string name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                if (NotArtwork.Contains(name)) continue;

                names.Add(name);
            }
        }
        catch
        {
            // Enumeration is a convenience; without it the picker still offers "none".
        }

        foreach (string name in names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            Artworks.Add(new ArtworkOption(name));
    }

    public bool IsImagePickerOpen { get; private set; }

    public string ImageLabel => string.IsNullOrEmpty(_image) ? "Nessuna immagine" : _image;

    /// <summary>Preview path for the editor, empty when the drink has no artwork.</summary>
    public string ImagePreview => string.IsNullOrEmpty(_image) ? string.Empty : $"{_image}.png";

    public bool HasImage => !string.IsNullOrEmpty(_image);

    private void OnOpenImagePicker(object? sender, RoutedEventArgs e)
    {
        IsImagePickerOpen = true;
        OnPropertyChanged(nameof(IsImagePickerOpen));
    }

    private void OnPickArtwork(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: ArtworkOption option }) return;

        _image = option.Name;
        IsImagePickerOpen = false;

        MarkDirty();
        OnPropertyChanged(nameof(IsImagePickerOpen));
        OnPropertyChanged(nameof(ImageLabel));
        OnPropertyChanged(nameof(ImagePreview));
        OnPropertyChanged(nameof(HasImage));
        RecipeChanged();
    }

    private void OnCloseImagePicker(object? sender, RoutedEventArgs e)
    {
        IsImagePickerOpen = false;
        OnPropertyChanged(nameof(IsImagePickerOpen));
    }

    // ------------------------------------------------------------ deleting a creation

    /// <summary>
    /// Only drinks invented on the machine can be deleted. The recipes that ship with it
    /// are switched off instead - the ON/OFF pill already takes them off the menu - so a
    /// mistap here cannot cost you the standard card that the whole bar orders from.
    /// </summary>
    public bool CanDelete => _editing != null
        && CreationsCategory.Equals(_editing.Category, StringComparison.OrdinalIgnoreCase);

    /// <summary>Armed by the first tap: deleting takes two, since there is no dialog to confirm in.</summary>
    private bool _deleteArmed;

    public string DeleteLabel => _deleteArmed ? "TOCCA ANCORA PER ELIMINARE" : "ELIMINA";

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_editing == null || !CanDelete) return;

        if (!_deleteArmed)
        {
            _deleteArmed = true;
            OnPropertyChanged(nameof(DeleteLabel));
            StatusLine.Text = "Tocca di nuovo ELIMINA per cancellare il cocktail.";
            return;
        }

        var config = AppConfigService.Instance.Config;
        string name = _editing.Name;

        config.Cocktails = config.Cocktails.Where(c => !ReferenceEquals(c, _editing)).ToArray();
        AppConfigService.Instance.SaveConfig("cocktail-delete");

        _deleteArmed = false;
        OnNewCocktail(null, e);
        LoadMenu();

        StatusLine.Text = $"\"{name}\" eliminato.";
    }

    /// <summary>Any other move disarms the delete, so it cannot fire on a stale tap.</summary>
    private void DisarmDelete()
    {
        if (!_deleteArmed) return;
        _deleteArmed = false;
        OnPropertyChanged(nameof(DeleteLabel));
    }

    // ------------------------------------------------------------ on-screen keyboard

    /// <summary>Letters in layout order: the three rows of a QWERTY board, left to right.</summary>
    private const string Layout = "QWERTYUIOPASDFGHJKLZXCVBNM";

    private bool _shift = true;
    private bool _keyboardOpen;

    /// <summary>What the keyboard is currently naming.</summary>
    private enum Naming { Drink, AlcoholicLiquid, NonAlcoholicLiquid }

    private Naming _naming = Naming.Drink;

    /// <summary>Held while the keyboard names a liquid, so the drink name survives.</summary>
    private string _liquidDraft = string.Empty;

    public bool IsKeyboardOpen => _keyboardOpen;

    public string KeyboardTitle => _naming switch
    {
        Naming.AlcoholicLiquid => "NUOVA BASE ALCOLICA",
        Naming.NonAlcoholicLiquid => "NUOVO ANALCOLICO",
        _ => "NOME DEL COCKTAIL",
    };

    /// <summary>
    /// Key captions, cased to match what pressing them types. Indexed so the XAML can
    /// bind one property per key without twenty-six of them in this class.
    /// </summary>
    public string[] Keys { get; private set; } = Cased(true);

    private static string[] Cased(bool upper)
        => Layout.Select(c => upper ? c.ToString() : char.ToLowerInvariant(c).ToString()).ToArray();

    public IBrush ShiftBrush => _shift ? ShiftOnBrush : ShiftOffBrush;
    private static readonly IBrush ShiftOnBrush = new SolidColorBrush(Color.Parse("#CAF0F8"));
    private static readonly IBrush ShiftOffBrush = new SolidColorBrush(Color.Parse("#8A8F80"));

    private void OnOpenKeyboard(object? sender, RoutedEventArgs e)
    {
        _naming = Naming.Drink;

        // Starts from the current name so renaming is an edit, not a retype.
        if (string.IsNullOrWhiteSpace(_name))
            _name = Recipe.Count > 0 || _editing != null ? DrinkName : string.Empty;

        OpenKeyboard(_name);
    }

    private void OnAddBase(object? sender, RoutedEventArgs e)
    {
        _naming = Naming.AlcoholicLiquid;
        _liquidDraft = string.Empty;
        OpenKeyboard(string.Empty);
    }

    private void OnAddMixer(object? sender, RoutedEventArgs e)
    {
        _naming = Naming.NonAlcoholicLiquid;
        _liquidDraft = string.Empty;
        OpenKeyboard(string.Empty);
    }

    private void OpenKeyboard(string startingText)
    {
        // A fresh name starts capitalised, the way a keyboard does after a full stop.
        _shift = startingText.Length == 0;
        _keyboardOpen = true;

        Keys = Cased(_shift);
        OnPropertyChanged(nameof(IsKeyboardOpen));
        OnPropertyChanged(nameof(KeyboardTitle));
        OnPropertyChanged(nameof(NameDraft));
        OnPropertyChanged(nameof(Keys));
        OnPropertyChanged(nameof(ShiftBrush));
    }

    /// <summary>The buffer the keys are typing into: the drink's name, or a new liquid's.</summary>
    private string Draft
    {
        get => _naming == Naming.Drink ? _name : _liquidDraft;
        set
        {
            if (_naming == Naming.Drink) _name = value;
            else _liquidDraft = value;
        }
    }

    public string NameDraft => string.IsNullOrEmpty(Draft) ? " " : Draft;

    private void OnShift(object? sender, RoutedEventArgs e)
    {
        _shift = !_shift;
        Keys = Cased(_shift);
        OnPropertyChanged(nameof(Keys));
        OnPropertyChanged(nameof(ShiftBrush));
    }

    private void OnKeyPress(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        if (Draft.Length >= MaxNameLength) return;

        Draft += _shift ? key.ToUpperInvariant() : key.ToLowerInvariant();

        // Shift is a one-shot, like a real keyboard: it lifts after the letter it capitalised.
        if (_shift)
        {
            _shift = false;
            Keys = Cased(false);
            OnPropertyChanged(nameof(Keys));
            OnPropertyChanged(nameof(ShiftBrush));
        }

        if (_naming == Naming.Drink) MarkDirty();
        OnPropertyChanged(nameof(NameDraft));
    }

    private void OnKeySpace(object? sender, RoutedEventArgs e)
    {
        if (Draft.Length >= MaxNameLength) return;
        Draft += " ";
        if (_naming == Naming.Drink) MarkDirty();
        OnPropertyChanged(nameof(NameDraft));
    }

    private void OnKeyBackspace(object? sender, RoutedEventArgs e)
    {
        if (Draft.Length == 0) return;
        Draft = Draft[..^1];
        if (_naming == Naming.Drink) MarkDirty();
        OnPropertyChanged(nameof(NameDraft));
    }

    private void OnKeyClear(object? sender, RoutedEventArgs e)
    {
        Draft = string.Empty;
        if (_naming == Naming.Drink) MarkDirty();
        OnPropertyChanged(nameof(NameDraft));
    }

    private void OnCloseKeyboard(object? sender, RoutedEventArgs e)
    {
        if (_naming != Naming.Drink)
            AddCustomLiquid(_liquidDraft, _naming == Naming.AlcoholicLiquid);

        _naming = Naming.Drink;
        _keyboardOpen = false;
        OnPropertyChanged(nameof(IsKeyboardOpen));
        RecipeChanged();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A drink already in the menu, in the picker on the left.</summary>
public sealed class MenuCocktail : INotifyPropertyChanged
{
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#CAF0F8"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#E7E6DC"));
    private static readonly IBrush OnBrush = new SolidColorBrush(Color.Parse("#7FE0A8"));
    private static readonly IBrush OffBrush = new SolidColorBrush(Color.Parse("#8A8F80"));

    private bool _isSelected;

    public MenuCocktail(CocktailConfig config) => Config = config;

    public CocktailConfig Config { get; }
    public string Name => Config.Name;
    public string TotalLabel => $"{Config.Ingredients.Sum(i => i.Milliliters)} ml";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(NameBrush));
        }
    }

    public IBrush NameBrush => _isSelected ? SelectedBrush : IdleBrush;

    public bool IsActive
    {
        get => Config.IsActive;
        set
        {
            if (Config.IsActive == value) return;
            Config.IsActive = value;
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(ActiveLabel));
            OnPropertyChanged(nameof(ActiveBrush));
            OnPropertyChanged(nameof(RowOpacity));
        }
    }

    public string ActiveLabel => Config.IsActive ? "ON" : "OFF";
    public IBrush ActiveBrush => Config.IsActive ? OnBrush : OffBrush;

    /// <summary>Dims the row when the drink is off, so the state reads at a glance.</summary>
    public double RowOpacity => Config.IsActive ? 1.0 : 0.45;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One choice in the artwork picker. An empty <paramref name="Name"/> is the plain card,
/// which is what a creation gets until someone picks a picture for it.
/// </summary>
public sealed record ArtworkOption(string Name)
{
    public bool IsNone => string.IsNullOrEmpty(Name);

    public string Label => IsNone ? "Nessuna" : Name;

    /// <summary>Asset file for the thumbnail; empty for the "none" tile.</summary>
    public string Source => IsNone ? string.Empty : $"{Name}.png";
}

/// <summary>
/// An ingredient the machine knows about. <paramref name="Line"/> is null when nothing
/// is carrying it right now: still offered, just marked, since a recipe can be written
/// before the bottle is plugged in.
/// </summary>
public sealed record WorkshopLiquid(string Name, string? Line, bool IsAlcoholic)
{
    public bool IsLoaded => Line != null;

    public string Detail => Line ?? "non in linea";

    /// <summary>Dims the unloaded ones so the pourable list reads first.</summary>
    public double RowOpacity => IsLoaded ? 1.0 : 0.5;
}

/// <summary>One line of the recipe in the editor. Held apart from the stored config
/// until save, so an abandoned edit changes nothing.</summary>
public sealed class WorkshopIngredient : INotifyPropertyChanged
{
    private int _milliliters;

    public WorkshopIngredient(string name, int milliliters, bool isAlcoholic)
    {
        Name = name;
        _milliliters = milliliters;
        IsAlcoholic = isAlcoholic;
    }

    public string Name { get; }
    public bool IsAlcoholic { get; }

    public int Milliliters
    {
        get => _milliliters;
        set
        {
            if (_milliliters == value) return;
            _milliliters = value;
            OnPropertyChanged(nameof(Milliliters));
            OnPropertyChanged(nameof(MlLabel));
        }
    }

    public string MlLabel => $"{_milliliters} ml";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
