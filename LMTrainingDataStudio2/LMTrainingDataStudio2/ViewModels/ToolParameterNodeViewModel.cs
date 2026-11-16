using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Editable node in a tool parameter schema tree.
/// </summary>
public partial class ToolParameterNodeViewModel : ObservableObject
{
    private readonly ObservableCollection<ToolParameterNodeViewModel> _owner;
    private readonly Action _onChanged;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _type;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private bool _isRequired;

    public ToolParameterNodeViewModel(
        ObservableCollection<ToolParameterNodeViewModel> owner,
        ToolParameterNodeViewModel? parent,
        string name,
        string type,
        string? description,
        bool isRequired,
        Action onChanged)
    {
        _owner = owner;
        _onChanged = onChanged;
        Parent = parent;

        _name = name;
        _type = type;
        _description = description ?? string.Empty;
        _isRequired = isRequired;

        AddChildCommand = new RelayCommand(AddChild);
        RemoveCommand = new RelayCommand(Remove);
    }

    public ToolParameterNodeViewModel? Parent { get; }

    public ObservableCollection<ToolParameterNodeViewModel> Children { get; } = new();

    public IReadOnlyList<string> Types { get; } = new[]
    {
        "string", "number", "integer", "boolean", "object", "array"
    };

    public IRelayCommand AddChildCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        _onChanged();
    }

    partial void OnTypeChanged(string value) => _onChanged();

    partial void OnDescriptionChanged(string value) => _onChanged();

    partial void OnIsRequiredChanged(bool value) => _onChanged();

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(unnamed)" : Name;

    private void AddChild()
    {
        var child = new ToolParameterNodeViewModel(
            _owner,
            this,
            $"field{Children.Count + 1}",
            "string",
            null,
            false,
            _onChanged);
        Children.Add(child);
        _onChanged();
    }

    private void Remove()
    {
        if (Parent is not null)
        {
            Parent.Children.Remove(this);
        }
        else
        {
            _owner.Remove(this);
        }

        _onChanged();
    }
}