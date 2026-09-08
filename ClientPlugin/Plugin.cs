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
using System.Collections.Generic;
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
    private static Type? s_myInventoryType;
    private static Type? s_myEntityExtensionsType;

    // Cached reflection data
    private static MethodInfo? s_getInventoryMethod; // MyEntityExtensions.GetInventory(MyEntity, int)
    private static PropertyInfo? s_inventoryMaxVolume;
    private static PropertyInfo? s_inventoryCurrentVolume;

    // Cached field info for GetOwner lookups (avoid repeated AccessTools.Field calls)
    private static FieldInfo? s_instanceField;
    private static FieldInfo? s_controllerField;
    private static FieldInfo? s_interactedOwnerField;
    private static FieldInfo? s_userOwnerField;
    private static FieldInfo? s_rightFilterTypeField;
    private static FieldInfo? s_rightOwnersControlField;
    private static FieldInfo? s_interactedGridOwnersField;
    private static PropertyInfo? s_rightFilterTypeIndexProperty;
    private static MethodInfo? s_createInventoryControlsInListMethod;

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
        s_myInventoryType = AccessTools.TypeByName("Sandbox.Game.MyInventory"); // Note: Sandbox.Game, not Sandbox.Game.Entities
        s_myEntityExtensionsType = AccessTools.TypeByName("Sandbox.Game.Entities.MyEntityExtensions");

        // GetInventory extension method: MyEntityExtensions.GetInventory(MyEntity thisEntity, int index = 0)
        if (s_myEntityExtensionsType != null)
        {
            s_getInventoryMethod = AccessTools.Method(s_myEntityExtensionsType, "GetInventory", new[] { typeof(MyEntity), typeof(int) });
        }

        // MaxVolume and CurrentVolume are on MyInventory
        if (s_myInventoryType != null)
        {
            s_inventoryMaxVolume = AccessTools.Property(s_myInventoryType, "MaxVolume");
            s_inventoryCurrentVolume = AccessTools.Property(s_myInventoryType, "CurrentVolume");
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
            s_rightOwnersControlField = AccessTools.Field(s_terminalInventoryControllerType, "m_rightOwnersControl");
            s_interactedGridOwnersField = AccessTools.Field(s_terminalInventoryControllerType, "m_interactedGridOwners");
            s_rightFilterTypeIndexProperty = AccessTools.Property(s_terminalInventoryControllerType, "RightFilterTypeIndex");
            s_createInventoryControlsInListMethod = AccessTools.Method(s_terminalInventoryControllerType, "CreateInventoryControlsInList", new[] { typeof(List<MyEntity>), typeof(MyGuiControlList), typeof(MyInventoryOwnerTypeEnum?) });
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

            // Find the search bar and Hide Empty controls
            var searchBox = page.Controls.GetControlByName("BlockSearchRight") as MyGuiControlSearchBox;
            var hideEmptyCheckbox = page.Controls.GetControlByName("CheckboxHideEmptyRight") as MyGuiControlCheckbox;
            var hideEmptyLabel = page.Controls.GetControlByName("LabelHideEmptyRight") as MyGuiControlLabel;

            if (searchBox == null || hideEmptyCheckbox == null || hideEmptyLabel == null)
                return;

            // ===== Smart layout =====
            // Original positions:
            // Hide Empty Label: X=0.415f, RIGHT-aligned
            // Hide Empty Checkbox: X=0.463f, RIGHT-aligned
            // Search Box: X=0.0185f, width=0.361f-labelSize.X, ends around 0.38f
            
            // We need space for: Sort Label + Sort Checkbox + padding (~0.12f total)
            // Strategy: Move Hide Empty right, shrink search box, place Sort in middle

            const float sortTotalWidth = 0.12f; // Label + checkbox + padding
            
            // Get the hide empty label size (it's RIGHT-aligned, so text ends before X)
            float hideLabelX = hideEmptyLabel.Position.X; // 0.415f
            
            // Shrink search box to make room - new width = original - sortTotalWidth
            float originalSearchWidth = searchBox.Size.X;
            searchBox.Size = new Vector2(originalSearchWidth - sortTotalWidth, searchBox.Size.Y);

            // Calculate new positions
            // Search box ends at: 0.0185f + (originalSearchWidth - 0.12f)
            // Hide Empty Checkbox at: 0.463f + 0.12f = 0.583f (moved right)
            // Sort checkbox at: Hide Label X - 0.02f (just to the left of Hide Label)
            // Sort label at: Sort checkbox X - checkbox_width - 0.01f

            float newHideLabelX = hideLabelX + sortTotalWidth;
            float newHideCheckboxX = hideEmptyCheckbox.Position.X + sortTotalWidth;

            // Move Hide Empty right
            hideEmptyLabel.Position = new Vector2(newHideLabelX, hideEmptyLabel.Position.Y);
            hideEmptyCheckbox.Position = new Vector2(newHideCheckboxX, hideEmptyCheckbox.Position.Y);

            // Place Sort elements between search box and Hide Empty
            // Search box ends at: 0.0185f + newWidth
            float searchBoxEndX = 0.0185f + searchBox.Size.X;
            
            // Sort checkbox: right after search box, LEFT-aligned so its LEFT edge is at searchBoxEndX
            float sortCheckboxX = searchBoxEndX + 0.01f; // small gap
            var sortCheckbox = new MyGuiControlCheckbox
            {
                Position = new Vector2(sortCheckboxX, hideEmptyCheckbox.Position.Y),
                Name = "SortBySpaceRight",
                OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
                IsChecked = Config.Current.SortByAvailableSpace
            };

            // Sort label: to the LEFT of checkbox, LEFT-aligned
            var sortLabel = new MyGuiControlLabel
            {
                Position = new Vector2(sortCheckboxX, hideEmptyLabel.Position.Y),
                Name = "SortBySpaceRightLabel",
                OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
                Text = "Sort"
            };

            sortCheckbox.IsCheckedChanged += OnSortCheckboxChanged;
            page.Controls.Add(sortLabel);
            page.Controls.Add(sortCheckbox);
        }
    }

    private static void OnSortCheckboxChanged(MyGuiControlCheckbox checkbox)
    {
        Config.Current.SortByAvailableSpace = checkbox.IsChecked;
        ConfigStorage.Save(Config.Current);
        RefreshInventoryList();
    }

    /// <summary>
    /// Triggers a refresh of the inventory list by directly calling CreateInventoryControlsInList.
    /// </summary>
    private static void RefreshInventoryList()
    {
        try
        {
            // Check all required cached members
            if (s_instanceField == null || s_controllerField == null) return;
            if (s_rightOwnersControlField == null || s_interactedGridOwnersField == null) return;
            if (s_rightFilterTypeIndexProperty == null || s_rightFilterTypeField == null) return;
            if (s_createInventoryControlsInListMethod == null) return;

            // Get the screen instance
            var instance = s_instanceField.GetValue(null) as MyGuiScreenTerminal;
            if (instance == null) return;

            // Get the controller
            var controller = s_controllerField.GetValue(instance);
            if (controller == null) return;

            // Get the owners list and filter type
            var owners = s_interactedGridOwnersField.GetValue(controller) as List<MyEntity>;
            var rightOwnersControl = s_rightOwnersControlField.GetValue(controller) as MyGuiControlList;
            var filterType = s_rightFilterTypeField.GetValue(controller) as MyInventoryOwnerTypeEnum?;
            var filterTypeIndex = (int?)s_rightFilterTypeIndexProperty.GetValue(controller);

            if (owners == null || rightOwnersControl == null) return;

            // Cache owner values before rebuilding the list
            CacheOwnerValues();

            // Directly call CreateInventoryControlsInList
            s_createInventoryControlsInListMethod.Invoke(controller, new object[] { owners, rightOwnersControl, filterType });

            // Clear cache after list is built
            ClearOwnerCache();
        }
        catch (Exception ex)
        {
            // Log but don't crash
            try { MyLog.Default?.WriteLine($"[InventorySort] RefreshInventoryList failed: {ex.Message}"); } catch {}
        }
    }

    /// <summary>
    /// Patches CreateInventoryControlsInList to set owner cache before sorting begins.
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
            CacheOwnerValues();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ClearOwnerCache();
        }
    }

    /// <summary>
    /// Patches the CompareGuiControlInventoryOwners method to support sorting by available space.
    /// When SortByAvailableSpace is enabled, inventories are sorted by remaining capacity (emptiest first).
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
            var sortEnabled = Config.Current.SortByAvailableSpace;

            // If sorting by space is disabled, use original logic
            if (!sortEnabled)
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

            // Use cached owner values
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

            // Sort by available space (most empty first)
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
    /// Caches owner values before sorting begins.
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

    /// <summary>
    /// Gets total available space (MaxVolume - CurrentVolume) for all inventories.
    /// Uses MyEntityExtensions.GetInventory extension method.
    /// </summary>
    private static float GetTotalAvailableSpace(MyGuiControlInventoryOwner? owner)
    {
        float totalAvailable = 0f;
        if (owner?.InventoryOwner == null || !owner.InventoryOwner.HasInventory)
            return 0f;

        if (s_getInventoryMethod == null || s_inventoryMaxVolume == null || s_inventoryCurrentVolume == null)
            return 0f;

        // Sum all inventories (multi-inventory containers like refineries/assemblers)
        int inventoryCount = owner.InventoryOwner.InventoryCount;
        for (int i = 0; i < inventoryCount; i++)
        {
            // Call MyEntityExtensions.GetInventory(owner, index)
            var inv = s_getInventoryMethod.Invoke(null, new object[] { owner.InventoryOwner, i });
            if (inv == null)
                break;

            var maxVol = s_inventoryMaxVolume.GetValue(inv);
            var curVol = s_inventoryCurrentVolume.GetValue(inv);

            if (maxVol != null && curVol != null)
            {
                // MyFixedPoint can be cast directly to float
                float maxF = (float)maxVol;
                float curF = (float)curVol;
                totalAvailable += maxF - curF;
            }
        }
        return totalAvailable;
    }
}