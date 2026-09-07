using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace ClientPlugin;

public class Config : INotifyPropertyChanged
{
    #region Options

    // Default ON - sort inventories by available space (biggest/emptiest first)
    private bool sortByAvailableSpace = true;

    #endregion

    #region User interface

    public readonly string Title = "Inventory Sort";

    [Checkbox(description: "Sort inventory containers by available space (most empty first). Default: ON")]
    public bool SortByAvailableSpace
    {
        get => sortByAvailableSpace;
        set => SetField(ref sortByAvailableSpace, value);
    }

    #endregion

    #region Property change notification boilerplate

    public static readonly Config Default = new Config();
    public static readonly Config Current = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
