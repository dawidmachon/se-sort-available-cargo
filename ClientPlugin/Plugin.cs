using System.Reflection;
using System.Runtime.CompilerServices;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRageMath;
using Sandbox.Game.Gui;
using Sandbox.Game.Entities;
using Sandbox.Game.Screens.Helpers;
using System.Collections.Generic;
using System;

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

    // Static state for UI buttons (must be static to persist across refreshes)
    private static MyGuiControlCheckbox? s_sortBySpaceLeftCheckbox;
    private static MyGuiControlCheckbox? s_sortBySpaceRightCheckbox;
    private static MyGuiControlLabel? s_sortBySpaceLeftLabel;
    private static MyGuiControlLabel? s_sortBySpaceRightLabel;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        Instance = this;
        Instance.settingsGenerator = new SettingsGenerator();

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

    // Hook called when left checkbox is clicked
    private static void OnLeftSortCheckboxChanged(MyGuiControlCheckbox checkbox)
    {
        Config.Current.SortByAvailableSpace = checkbox.IsChecked;
        ConfigStorage.Save(Config.Current);
        SyncCheckboxes(checkbox.IsChecked);
    }

    // Hook called when right checkbox is clicked
    private static void OnRightSortCheckboxChanged(MyGuiControlCheckbox checkbox)
    {
        Config.Current.SortByAvailableSpace = checkbox.IsChecked;
        ConfigStorage.Save(Config.Current);
        SyncCheckboxes(checkbox.IsChecked);
    }

    // Keep both checkboxes in sync
    private static void SyncCheckboxes(bool value)
    {
        if (s_sortBySpaceLeftCheckbox != null && s_sortBySpaceLeftCheckbox.IsChecked != value)
            s_sortBySpaceLeftCheckbox.IsChecked = value;
        if (s_sortBySpaceRightCheckbox != null && s_sortBySpaceRightCheckbox.IsChecked != value)
            s_sortBySpaceRightCheckbox.IsChecked = value;
    }

    // ===== HARMONY PATCHES =====

    /// <summary>
    /// Patches CreateInventoryPageLeftSection to add our sort checkbox.
    /// </summary>
    [HarmonyPatch(typeof(MyGuiScreenTerminal), "CreateInventoryPageLeftSection")]
    public static class CreateInventoryPageLeftSection_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MyGuiControlTabPage page)
        {
            float num = -0.008f;
            
            // Create label for the checkbox (appears to the left)
            s_sortBySpaceLeftLabel = new MyGuiControlLabel
            {
                Position = new Vector2(-0.06f + num, -0.225f),
                Name = "SortBySpaceLeftLabel",
                OriginAlign = MyGuiDrawAlignEnum.HORIZONTAL_RIGHT_AND_VERTICAL_CENTER,
                Text = "Sort"
            };

            // Create the checkbox for left panel
            s_sortBySpaceLeftCheckbox = new MyGuiControlCheckbox
            {
                Position = new Vector2(-0.0075f + num, -0.225f),
                Name = "SortBySpaceLeft",
                OriginAlign = MyGuiDrawAlignEnum.HORIZONTAL_RIGHT_AND_VERTICAL_CENTER,
                IsChecked = Config.Current.SortByAvailableSpace
            };
            s_sortBySpaceLeftCheckbox.IsCheckedChanged += OnLeftSortCheckboxChanged;

            page.Controls.Add(s_sortBySpaceLeftLabel);
            page.Controls.Add(s_sortBySpaceLeftCheckbox);
        }
    }

    /// <summary>
    /// Patches CreateInventoryPageRightSection to add our sort checkbox.
    /// </summary>
    [HarmonyPatch(typeof(MyGuiScreenTerminal), "CreateInventoryPageRightSection")]
    public static class CreateInventoryPageRightSection_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MyGuiControlTabPage page)
        {
            float num = 0.004f;

            // Create label for the checkbox (appears to the left)
            s_sortBySpaceRightLabel = new MyGuiControlLabel
            {
                Position = new Vector2(0.41f + num, -0.225f),
                Name = "SortBySpaceRightLabel",
                OriginAlign = MyGuiDrawAlignEnum.HORIZONTAL_RIGHT_AND_VERTICAL_CENTER,
                Text = "Sort"
            };

            // Create the checkbox for right panel
            s_sortBySpaceRightCheckbox = new MyGuiControlCheckbox
            {
                Position = new Vector2(0.463f + num, -0.225f),
                Name = "SortBySpaceRight",
                OriginAlign = MyGuiDrawAlignEnum.HORIZONTAL_RIGHT_AND_VERTICAL_CENTER,
                IsChecked = Config.Current.SortByAvailableSpace
            };
            s_sortBySpaceRightCheckbox.IsCheckedChanged += OnRightSortCheckboxChanged;

            page.Controls.Add(s_sortBySpaceRightLabel);
            page.Controls.Add(s_sortBySpaceRightCheckbox);
        }
    }

    /// <summary>
    /// Patches the inventory sorting. When sort by available space is enabled,
    /// we sort by remaining capacity (biggest first = most empty containers first).
    /// </summary>
    [HarmonyPatch(typeof(MyTerminalInventoryController), "CreateInventoryControlsInList")]
    public static class CreateInventoryControlsInList_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(List<MyEntity> owners, MyGuiControlList listControl, MyInventoryOwnerTypeEnum? filterType, MyTerminalInventoryController __instance)
        {
            // Store the current sort preference for the Compare method
            s_currentSortBySpace = Config.Current.SortByAvailableSpace;
            s_currentController = __instance;
        }

        [HarmonyPostfix]
        public static void Postfix(List<MyEntity> owners, MyGuiControlList listControl, MyInventoryOwnerTypeEnum? filterType, MyTerminalInventoryController __instance)
        {
            // Reset after sorting
            s_currentSortBySpace = false;
            s_currentController = null;
        }
        
        // Static fields to pass state to the comparison method
        private static bool s_currentSortBySpace;
        private static MyTerminalInventoryController? s_currentController;
        
        // Public accessor for the comparison
        public static int CompareInventoryOwnersBySpace(MyGuiControlBase x, MyGuiControlBase y)
        {
            var ownerX = x as MyGuiControlInventoryOwner;
            var ownerY = y as MyGuiControlInventoryOwner;

            if (ownerX == null && ownerY == null) return 0;
            if (ownerX == null) return -1;
            if (ownerY == null) return 1;

            // Get the private fields using reflection
            var interactedOwner = GetField<MyEntity>("m_interactedAsOwner");
            var userOwner = GetField<MyEntity>("m_userAsOwner");

            // Keep interacted/user owner at the top
            if (ownerX.InventoryOwner == interactedOwner || ownerX.InventoryOwner == userOwner)
                return -1;
            if (ownerY.InventoryOwner == interactedOwner || ownerY.InventoryOwner == userOwner)
                return 1;

            // If not sorting by space, use default alphabetical sort
            if (!s_currentSortBySpace)
            {
                return string.Compare(ownerX.InventoryOwner?.DisplayNameText, ownerY.InventoryOwner?.DisplayNameText);
            }

            // Sort by available space (biggest first = most empty containers first)
            float spaceX = GetTotalAvailableSpace(ownerX);
            float spaceY = GetTotalAvailableSpace(ownerY);

            // Descending order - bigger available space first
            // If equal, fall back to alphabetical
            if (Math.Abs(spaceX - spaceY) > 0.0001f)
            {
                return spaceX > spaceY ? -1 : 1;
            }

            return string.Compare(ownerX.InventoryOwner?.DisplayNameText, ownerY.InventoryOwner?.DisplayNameText);
        }

        private static T? GetField<T>(string fieldName) where T : class
        {
            if (s_currentController == null) return null;
            var field = typeof(MyTerminalInventoryController).GetField(fieldName, 
                BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(s_currentController) as T;
        }

        private static float GetTotalAvailableSpace(MyGuiControlInventoryOwner owner)
        {
            float totalAvailable = 0f;
            if (owner?.InventoryOwner != null && owner.InventoryOwner.HasInventory)
            {
                foreach (var inv in owner.InventoryOwner.GetInventories())
                {
                    if (inv != null)
                    {
                        totalAvailable += (float)(inv.MaxVolume - inv.CurrentVolume);
                    }
                }
            }
            return totalAvailable;
        }
    }

    /// <summary>
    /// Patches the CompareGuiControlInventoryOwners to use our custom sort when enabled.
    /// </summary>
    [HarmonyPatch(typeof(MyTerminalInventoryController), "CompareGuiControlInventoryOwners")]
    public static class CompareGuiControlInventoryOwners_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MyGuiControlBase x, MyGuiControlBase y, MyTerminalInventoryController __instance, ref int __result)
        {
            // If sorting by space is disabled, use original logic
            if (!Config.Current.SortByAvailableSpace)
                return true; // Run original

            // Use our custom comparison
            __result = CreateInventoryControlsInList_Patch.CompareInventoryOwnersBySpace(x, y);
            return false; // Skip original
        }
    }
}
