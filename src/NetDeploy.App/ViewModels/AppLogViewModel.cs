using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Logs;

namespace NetDeploy.App.ViewModels;

/// <summary>One log in the list, with how many matches a search of every log found in it.</summary>
public sealed partial class AppLogViewModel : ObservableObject
{
    public AppLogViewModel(AppLog log) => Log = log;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(IsLive), nameof(Icon))]
    public partial AppLog Log { get; private set; }

    public string Name => Log.Name;
    public bool IsLive => Log.IsLive;
    public bool IsZipped => Log.IsZipped;

    /// <summary>The zip a zipped log is in, or null.</summary>
    public string? Zip => Log.IsZipped ? Path.GetFileName(Log.Path) : null;

    /// <summary>Where it is, for the tooltip: the file, or the zip and the file in it.</summary>
    public string Where => Log.IsZipped ? $"{Log.Path} › {Log.Entry}" : Log.Path;

    public PackIconKind Icon => IsZipped ? PackIconKind.FolderZipOutline : IsLive ? PackIconKind.FileDocumentEditOutline : PackIconKind.FileDocumentOutline;

    /// <summary>Such as "12 KB · 24 Oct, 10:15", and for a zipped log, the zip it is in.</summary>
    public string Detail => string.Join(" · ", new[]
    {
        Words.Size(Log.Size),
        Log.Modified.ToString("d MMM, HH:mm", System.Globalization.CultureInfo.CurrentCulture),
        Zip is { } zip ? "in " + zip : null,
    }.OfType<string>());

    /// <summary>The matches the last search of every log found in it; null when it wasn't searched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHits), nameof(IsMiss), nameof(HitsText), nameof(AutomationName))]
    public partial int? Hits { get; set; }

    public bool HasHits => Hits > 0;

    /// <summary>Searched, with no matches: the row fades, so the logs with some stand out.</summary>
    public bool IsMiss => Hits == 0;

    public string HitsText => Hits is { } hits ? hits.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : "";

    /// <summary>The log open on the right.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>What a screen reader says for the row, such as "app.3.log in app.3.zip, 2 matches".</summary>
    public string AutomationName => Name + (Zip is { } zip ? " in " + zip : "") + Hits switch
    {
        null => "",
        0 => ", no matches",
        { } hits => ", " + Words.Count(hits, "match", "matches"),
    };

    internal void Update(AppLog log)
    {
        Log = log;
        OnPropertyChanged(nameof(Name));
    }
}
