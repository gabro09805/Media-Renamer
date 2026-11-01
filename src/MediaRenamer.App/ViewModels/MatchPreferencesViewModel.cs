using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaRenamer.Model;

namespace MediaRenamer.App.ViewModels;

public sealed record LanguageOption(string Code, string Name);

public sealed partial class OrderChoice : ObservableObject
{
    public OrderChoice(EpisodeOrder value, string label) { Value = value; Label = label; }

    public EpisodeOrder Value { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>View model of the "Match Preferences" dialog (output language, match mode, episode order).</summary>
public sealed record MissingOption(MissingTranslationBehavior Value, string Label);

public sealed partial class MatchPreferencesViewModel : ObservableObject
{
    public static readonly IReadOnlyList<LanguageOption> Languages = new LanguageOption[]
    {
        new("en", "English"), new("it", "Italian"), new("de", "German"), new("fr", "French"),
        new("es", "Spanish"), new("pt-BR", "Brazilian Portuguese"), new("pt", "Portuguese"),
        new("ru", "Russian"),         new("zh-CN", "Simplified Chinese"), new("zh-TW", "Traditional Chinese"),
                new("ja", "Japanese"), new("ja-Latn", "Romanized Japanese"), new("ko", "Korean"), new("ar", "Arabic"), new("bg", "Bulgarian"),
        new("ca", "Catalan"), new("hr", "Croatian"), new("cs", "Czech"), new("da", "Danish"),
        new("nl", "Dutch"), new("fi", "Finnish"), new("el", "Greek"), new("he", "Hebrew"),
        new("hi", "Hindi"), new("hu", "Hungarian"), new("id", "Indonesian"), new("no", "Norwegian"),
        new("pl", "Polish"), new("ro", "Romanian"), new("sk", "Slovak"), new("sv", "Swedish"),
        new("th", "Thai"), new("tr", "Turkish"), new("uk", "Ukrainian"), new("vi", "Vietnamese"),
    };

    public MatchPreferencesViewModel(MatchPreferences current)
    {
        Orders = new ObservableCollection<OrderChoice>
        {
            new(EpisodeOrder.Airdate, "Airdate"), new(EpisodeOrder.Dvd, "DVD"),
            new(EpisodeOrder.Absolute, "Absolute"), new(EpisodeOrder.Digital, "Digital"),
            new(EpisodeOrder.StoryArc, "Story Arc"), new(EpisodeOrder.Production, "Production"),
            new(EpisodeOrder.Official, "Official"), new(EpisodeOrder.DateAndTitle, "Date and Title"),
        };
        Orders.First(o => o.Value == current.Order).IsChecked = true;

        var primary = LanguageTag.Primary(current.OutputLanguage);
        _selectedLanguage = Languages.FirstOrDefault(l => string.Equals(l.Code, current.OutputLanguage, StringComparison.OrdinalIgnoreCase))
                            ?? Languages.FirstOrDefault(l => LanguageTag.Primary(l.Code) == primary)
                            ?? Languages[0];
        _isStrict = current.Mode == MatchMode.Strict;
        _selectedMissing = MissingOptions.First(o => o.Value == current.MissingTranslation);
    }

    public IReadOnlyList<LanguageOption> LanguageList => Languages;

    public IReadOnlyList<MissingOption> MissingOptions { get; } = new MissingOption[]
    {
        new(MissingTranslationBehavior.UseEnglish, "English title"),
        new(MissingTranslationBehavior.UseOriginal, "Original title"),
        new(MissingTranslationBehavior.UseProviderDefault, "Provider's main title"),
    };

    [ObservableProperty]
    private MissingOption _selectedMissing;
    public ObservableCollection<OrderChoice> Orders { get; }

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpportunistic))]
    private bool _isStrict;

    public bool IsOpportunistic
    {
        get => !IsStrict;
        set => IsStrict = !value;
    }

    public MatchPreferences ToPreferences() =>
        new(SelectedLanguage.Code, IsStrict ? MatchMode.Strict : MatchMode.Opportunistic,
            Orders.First(o => o.IsChecked).Value, SelectedMissing.Value);
}
