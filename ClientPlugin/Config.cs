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

    // Default ON - keep the active/opened container pinned to the top of the sorted list
    // (vanilla behavior). Turn OFF to let sorting apply to all containers including the
    // one you currently have open — useful if the active container is already full and
    // you want to jump straight to the next emptiest one.
    private bool keepActiveContainerFirst = true;

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

    [Checkbox(description: "Keep the active (opened) container pinned to the top of the sorted list. Default: ON. Turn OFF to apply sorting to every container including the active one — then the active container will be ordered purely by remaining space, exactly like the others. Only takes effect when 'Sort by available space' is ON.")]
    public bool KeepActiveContainerFirst
    {
        get => keepActiveContainerFirst;
        set => SetField(ref keepActiveContainerFirst, value);
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
