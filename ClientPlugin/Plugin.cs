using System.Reflection;
using System.Runtime.CompilerServices;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRageMath;
using VRage.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.Gui;
using System;
using VRage.Game.Entity;

// Define assembly version when compiled by Pulsar
#if !DEV_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#endif
    
namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "InventorySort";
    public static Plugin Instance { get; private set; }
    private SettingsGenerator settingsGenerator;

    // Cached types resolved at runtime
    private static Type? s_terminalInventoryControllerType;
    private static Type? s_screenTerminalType;

    // Cached reflection data
    private static PropertyInfo? s_inventoryMaxVolume;
    private static PropertyInfo? s_inventoryCurrentVolume;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        Instance = this;
        Instance.settingsGenerator = new SettingsGenerator();

        // Resolve internal types at runtime
        s_terminalInventoryControllerType = AccessTools.TypeByName("Sandbox.Game.Gui.MyTerminalInventoryController");
        s_screenTerminalType = AccessTools.TypeByName("Sandbox.Game.Gui.MyGuiScreenTerminal");
        var myInventoryType = AccessTools.TypeByName("Sandbox.Game.Game.Entities.MyInventory");

        if (myInventoryType != null)
        {
            s_inventoryMaxVolume = AccessTools.Property(myInventoryType, "MaxVolume");
            s_inventoryCurrentVolume = AccessTools.Property(myInventoryType, "CurrentVolume");
        }

        var harmony = new Harmony(Name);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    public void Dispose()
    {
        Instance = null;
    }

    public void Update()
    {
        // No per-frame updates needed
    }

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        Instance.settingsGenerator.SetLayout<Simple>();
        MyGuiSandbox.AddScreen(Instance.settingsGenerator.Dialog);
    }

    // ===== HARMONY PATCHES =====

    /// <summary>
    /// Patches CreateInventoryPageRightSection to add our Sort checkbox next to Hide Empty.
    /// Following Hide Empty behavior, Sort checkbox is only visible on the RIGHT panel.
    /// </summary>
    [HarmonyPatch(typeof(MyGuiScreenTerminal), "CreateInventoryPageRightSection")]
    public static class CreateInventoryPageRightSection_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MyGuiControlTabPage page)
        {
            // Find the Hide Empty label and checkbox by name
            var hideEmptyLabel = page.Controls.GetControlByName("LabelHideEmptyRight") as MyGuiControlLabel;
            var hideEmptyCheckbox = page.Controls.GetControlByName("CheckboxHideEmptyRight") as MyGuiControlCheckbox;

            if (hideEmptyLabel != null)
            {
                // Place Sort label to the left of Hide Empty label
                var sortLabel = new MyGuiControlLabel
                {
                    Position = new Vector2(hideEmptyLabel.Position.X - 0.048f, hideEmptyLabel.Position.Y),
                    Name = "SortBySpaceRightLabel",
                    OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER,
                    Text = "Sort"
                };

                // Place Sort checkbox to the left of Sort label
                var sortCheckbox = new MyGuiControlCheckbox
                {
                    Position = new Vector2(sortLabel.Position.X - 0.048f, hideEmptyCheckbox?.Position.Y ?? hideEmptyLabel.Position.Y),
                    Name = "SortBySpaceRight",
                    OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER,
                    IsChecked = Config.Current.SortByAvailableSpace
                };
                sortCheckbox.IsCheckedChanged += OnSortCheckboxChanged;

                page.Controls.Add(sortLabel);
                page.Controls.Add(sortCheckbox);
            }
        }
    }

    private static void OnSortCheckboxChanged(MyGuiControlCheckbox checkbox)
    {
        Config.Current.SortByAvailableSpace = checkbox.IsChecked;
        ConfigStorage.Save(Config.Current);
        
        // Trigger a rebuild of the right inventory list to apply the new sort order
        RefreshInventoryList();
    }
    
    /// <summary>
    /// Triggers a refresh of the inventory list by calling SetRightFilter.
    /// This causes CreateInventoryControlsInList to be called, which will use our
    /// CompareInventoryOwners_Patch to sort by available space.
    /// </summary>
    private static void RefreshInventoryList()
    {
        if (s_screenTerminalType == null) return;
        
        // Get the screen instance
        var instanceField = AccessTools.Field(s_screenTerminalType, "m_instance");
        var instance = instanceField?.GetValue(null) as MyGuiScreenTerminal;
        if (instance == null) return;
        
        // Get the controller
        var controllerField = AccessTools.Field(s_screenTerminalType, "m_controllerInventory");
        var controller = controllerField?.GetValue(instance);
        if (controller == null) return;
        
        // Call SetRightFilter via reflection to trigger list rebuild
        // This method is called when filter changes and it rebuilds the inventory list
        var setRightFilterMethod = AccessTools.Method(s_terminalInventoryControllerType, "SetRightFilter");
        var getRightFilterMethod = AccessTools.Property(s_terminalInventoryControllerType, "RightFilter");
        
        if (setRightFilterMethod != null && getRightFilterMethod != null)
        {
            var currentFilter = getRightFilterMethod.GetValue(controller);
            setRightFilterMethod.Invoke(controller, new[] { currentFilter });
        }
    }

    /// <summary>
    /// Patches the CompareGuiControlInventoryOwners method to support sorting by available space.
    /// When SortByAvailableSpace is enabled, inventories are sorted by remaining capacity (emptiest first).
    /// This patch affects both left and right inventory lists.
    /// </summary>
    [HarmonyPatch]
    public static class CompareInventoryOwners_Patch
    {
        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(s_terminalInventoryControllerType, "CompareGuiControlInventoryOwners");
        }

        [HarmonyPrefix]
        public static bool Prefix(MyGuiControlBase x, MyGuiControlBase y, ref int __result)
        {
            // If sorting by space is disabled, use original logic
            if (!Config.Current.SortByAvailableSpace)
                return true;

            var ownerX = x as MyGuiControlInventoryOwner;
            var ownerY = y as MyGuiControlInventoryOwner;

            if (ownerX == null)
            {
                __result = ownerY == null ? 0 : -1;
                return false;
            }
            if (ownerY == null)
            {
                __result = 1;
                return false;
            }

            // Get interacted/user owners to prioritize them
            var interactedOwner = GetOwner("m_interactedAsOwner");
            var userOwner = GetOwner("m_userAsOwner");

            // Keep interacted/user owner at the top
            if (ownerX.InventoryOwner == interactedOwner || ownerX.InventoryOwner == userOwner)
            {
                __result = -1;
                return false;
            }
            if (ownerY.InventoryOwner == interactedOwner || ownerY.InventoryOwner == userOwner)
            {
                __result = 1;
                return false;
            }

            // Sort by available space (biggest first = most empty containers first)
            float spaceX = GetTotalAvailableSpace(ownerX);
            float spaceY = GetTotalAvailableSpace(ownerY);

            // Descending order - bigger available space first
            // If equal, fall back to alphabetical
            if (Math.Abs(spaceX - spaceY) > 0.0001f)
            {
                __result = spaceX > spaceY ? -1 : 1;
                return false;
            }

            __result = string.Compare(ownerX.InventoryOwner?.DisplayNameText, ownerY.InventoryOwner?.DisplayNameText);
            return false;
        }
    }

    private static MyEntity? GetOwner(string fieldName)
    {
        if (s_terminalInventoryControllerType == null) return null;
        if (s_screenTerminalType == null) return null;
        
        var instanceField = AccessTools.Field(s_screenTerminalType, "m_instance");
        var instance = instanceField?.GetValue(null) as MyGuiScreenTerminal;
        if (instance == null) return null;
        
        var controllerField = AccessTools.Field(s_screenTerminalType, "m_controllerInventory");
        var controller = controllerField?.GetValue(instance);
        if (controller == null) return null;
        
        var ownerField = AccessTools.Field(s_terminalInventoryControllerType, fieldName);
        return ownerField?.GetValue(controller) as MyEntity;
    }

    private static float GetTotalAvailableSpace(MyGuiControlInventoryOwner? owner)
    {
        float totalAvailable = 0f;
        if (owner?.InventoryOwner != null && owner.InventoryOwner.HasInventory)
        {
            // Use GetInventory method to get each inventory
            var getInventoryMethod = AccessTools.Method(typeof(MyEntity), "GetInventory");
            for (int i = 0; i < 10; i++) // Max 10 inventories
            {
                var inv = getInventoryMethod?.Invoke(owner.InventoryOwner, new object[] { i });
                if (inv == null) break;
                
                // Use reflection to get MaxVolume and CurrentVolume
                if (s_inventoryMaxVolume != null && s_inventoryCurrentVolume != null)
                {
                    var maxVol = s_inventoryMaxVolume.GetValue(inv);
                    var curVol = s_inventoryCurrentVolume.GetValue(inv);
                    if (maxVol != null && curVol != null)
                    {
                        totalAvailable += (float)maxVol - (float)curVol;
                    }
                }
            }
        }
        return totalAvailable;
    }
}
