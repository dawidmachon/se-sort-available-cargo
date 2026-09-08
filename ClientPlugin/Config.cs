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

    // Default OFF - also sort the left inventory panel (production blocks)
    // Most users only want right panel sorted (cargo storage), since production
    // blocks (assemblers, refineries) usually have items in process.
    private bool sortLeftPanelToo = false;

    #endregion

    #region User interface

    public readonly string Title = "Sort by Available Cargo";

    [Checkbox(description: "Sort inventory containers by available space (most empty first). Default: ON")]
    public bool SortByAvailableSpace
    {
        get => sortByAvailableSpace;
        set => SetField(ref sortByAvailableSpace, value);
    }

    [Checkbox(description: "Also sort the LEFT inventory panel (production blocks). Default: OFF - left panel keeps original alphabetical order, since production blocks usually have items.")]
    public bool SortLeftPanelToo
    {
        get => sortLeftPanelToo;
        set => SetField(ref sortLeftPanelToo, value);
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
