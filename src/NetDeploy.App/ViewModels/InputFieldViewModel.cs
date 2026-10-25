using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NetDeploy.Core.Manifest;

namespace NetDeploy.App.ViewModels;

/// <summary>One field on a parameter or input form.</summary>
public sealed partial class InputFieldViewModel : ObservableObject
{
    public InputFieldViewModel(string name, InputField field)
    {
        Name = name;
        Field = field;
        Value = field.Initial;
    }

    public string Name { get; }
    public InputField Field { get; }
    public string Label => Field.Label ?? Name;
    public InputType Type => Field.Type;
    public IReadOnlyList<string> Options => Field.Options ?? [];

    /// <summary>The placeholder in the empty box.</summary>
    public string? Hint => Field.Optional ? "Optional" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChecked))]
    public partial string Value { get; set; }

    /// <summary>Yes/No inputs store "true" or "false", so conditions can compare them with equals.</summary>
    public bool IsChecked
    {
        get => bool.TryParse(Value, out var isChecked) && isChecked;
        set => Value = value ? "true" : "false";
    }

    /// <summary>Returns a message when the value can't be used, otherwise null.</summary>
    public string? Validate()
    {
        if (Value.Length == 0)
            return Field.Optional ? null : $"{Label} is required.";
        if (Type == InputType.Number && !double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return $"{Label} must be a number.";
        return null;
    }

    [RelayCommand]
    private void Browse()
    {
        if (Type == InputType.Folder)
        {
            var dialog = new OpenFolderDialog { Title = Label };
            if (dialog.ShowDialog() == true)
                Value = dialog.FolderName;
        }
        else
        {
            var dialog = new OpenFileDialog { Title = Label };
            if (dialog.ShowDialog() == true)
                Value = dialog.FileName;
        }
    }
}
