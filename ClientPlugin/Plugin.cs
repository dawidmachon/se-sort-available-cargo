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
    private static MethodInfo? s_getInventoryMethod;
    private static MethodInfo? s_setRightFilterMethod;

    // Cached field info for GetOwner lookups (avoid repeated AccessTools.Field calls)
    private static FieldInfo? s_instanceField;
    private static FieldInfo? s_controllerField;
    private static FieldInfo? s_interactedOwnerField;
    private static FieldInfo? s_userOwnerField;
    private static FieldInfo? s_rightFilterTypeField;

    // Cached owner values for current sort operation (set in Prefix, cleared in Postfix)
    private static MyEntity? s_cachedInteractedOwner;
    private static MyEntity? s_cachedUserOwner;

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
            s_getInventoryMethod = AccessTools.Method(myInventoryType, "GetInventory");
        }

        // Cache filter-related methods
        if (s_terminalInventoryControllerType != null)
        {
            s_setRightFilterMethod = AccessTools.Method(s_terminalInventoryControllerType, "SetRightFilter");
        }

        // Cache field info for GetOwner lookups
        if (s_screenTerminalType != null)
        {
            s_instanceField = AccessTools.Field(s_screenTerminalType, "m_instance");
            s_controllerField = AccessTools.Field(s_screenTerminalType, "m_controllerInventory");
        }
        if (s_terminalInventoryControllerType != null)
        {
            s_interactedOwnerField = AccessTools.Field(s_terminalInventoryControllerType, "m_interactedAsOwner");
            s_userOwnerField = AccessTools.Field(s_terminalInventoryControllerType, "m_userAsOwner");
            s_rightFilterTypeField = AccessTools.Field(s_terminalInventoryControllerType, "m_rightFilterType");
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
            // Skip if our controls already exist (page was reused)
            if (page.Controls.GetControlByName("SortBySpaceRight") != null)
                return;

            // Find the RightFilterAllButton to position ourselves consistently
            var filterButton = page.Controls.GetControlByName("RightFilterAllButton") as MyGuiControlRadioButton;

            if (filterButton != null)
            {
                // Place Sort in the filter button row (y aligned with filter buttons)
                // Position X = 0.20f - in the gap between search bar (X~0.018) and filter buttons (X~0.275)
                float yPos = filterButton.Position.Y;

                // Sort checkbox - in the gap between search bar and filter buttons
                var sortCheckbox = new MyGuiControlCheckbox
                {
                    Position = new Vector2(0.20f, yPos),
                    Name = "SortBySpaceRight",
                    OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
                    IsChecked = Config.Current.SortByAvailableSpace
                };

                // Sort label - to the left of checkbox
                var sortLabel = new MyGuiControlLabel
                {
                    Position = new Vector2(sortCheckbox.Position.X - 0.01f, yPos),
                    Name = "SortBySpaceRightLabel",
                    OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER,
                    Text = "Sort"
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
    /// The CreateInventoryControlsInList_Patch will handle cache setup/cleanup.
    /// </summary>
    private static void RefreshInventoryList()
    {
        try
        {
            if (s_instanceField == null || s_controllerField == null) return;
            if (s_setRightFilterMethod == null || s_rightFilterTypeField == null) return;

            // Get the screen instance using cached FieldInfo
            var instance = s_instanceField.GetValue(null) as MyGuiScreenTerminal;
            if (instance == null) return;

            // Get the controller using cached FieldInfo
            var controller = s_controllerField.GetValue(instance);
            if (controller == null) return;

            // Get the actual filter type (m_rightFilterType field), not the UI property (RightFilter)
            // SetRightFilter expects MyInventoryOwnerTypeEnum? but RightFilter property returns MyGuiControlRadioButtonStyleEnum
            var currentFilter = s_rightFilterTypeField.GetValue(controller);
            s_setRightFilterMethod.Invoke(controller, new[] { currentFilter });
        }
        catch (Exception ex)
        {
            // Log but don't crash - the plugin should be resilient
            // MyLog.Default may be null early in startup, so check before logging
            try
            {
                MyLog.Default?.WriteLine($"[InventorySort] RefreshInventoryList failed: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }

    /// <summary>
    /// Patches CreateInventoryControlsInList to set owner cache before sorting begins.
    /// This ensures the cache is populated regardless of how the list is rebuilt.
    /// </summary>
    [HarmonyPatch]
    public static class CreateInventoryControlsInList_Patch
    {
        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(s_terminalInventoryControllerType, "CreateInventoryControlsInList");
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            // Cache owner values before sorting begins
            CacheOwnerValues();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            // Clear cache after sorting completes
            ClearOwnerCache();
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

            // Use cached owner values (set once per sort operation by CreateInventoryControlsInList_Patch)
            var interactedOwner = s_cachedInteractedOwner;
            var userOwner = s_cachedUserOwner;

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

    /// <summary>
    /// Caches owner values before sorting begins. Called from CreateInventoryControlsInList_Patch.Prefix.
    /// </summary>
    private static void CacheOwnerValues()
    {
        s_cachedInteractedOwner = null;
        s_cachedUserOwner = null;

        if (s_instanceField == null || s_controllerField == null ||
            s_interactedOwnerField == null || s_userOwnerField == null)
            return;

        var instance = s_instanceField.GetValue(null) as MyGuiScreenTerminal;
        if (instance == null) return;

        var controller = s_controllerField.GetValue(instance);
        if (controller == null) return;

        s_cachedInteractedOwner = s_interactedOwnerField.GetValue(controller) as MyEntity;
        s_cachedUserOwner = s_userOwnerField.GetValue(controller) as MyEntity;
    }

    /// <summary>
    /// Clears cached owner values. Called after sorting completes.
    /// </summary>
    private static void ClearOwnerCache()
    {
        s_cachedInteractedOwner = null;
        s_cachedUserOwner = null;
    }

    private static float GetTotalAvailableSpace(MyGuiControlInventoryOwner? owner)
    {
        float totalAvailable = 0f;
        if (owner?.InventoryOwner != null && owner.InventoryOwner.HasInventory)
        {
            if (s_getInventoryMethod == null) return 0f;

            // Use InventoryCount like game code instead of hardcoded limit
            int inventoryCount = owner.InventoryOwner.InventoryCount;
            for (int i = 0; i < inventoryCount; i++)
            {
                var inv = s_getInventoryMethod.Invoke(owner.InventoryOwner, new object[] { i });
                if (inv == null) break;
                
                // Use reflection to get MaxVolume and CurrentVolume
                // Use direct cast like game code: (float)MaxVolume - (float)CurrentVolume
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
