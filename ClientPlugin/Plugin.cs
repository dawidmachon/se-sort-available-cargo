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
    public const string Name = "SortAvailableCargo";
    public static Plugin Instance { get; private set; }
    private SettingsGenerator settingsGenerator;

    // Cached types resolved at runtime
    private static Type? s_terminalInventoryControllerType;
    private static Type? s_screenTerminalType;
    private static Type? s_radioButtonStyleEnumType; // MyGuiControlRadioButtonStyleEnum

    // Cached reflection data
    private static MethodInfo? s_getInventoryBaseMethod;
    private static PropertyInfo? s_rightFilterProperty; // Returns current filter VisualStyle

    // Cached field info for GetOwner lookups
    private static FieldInfo? s_instanceField;
    private static FieldInfo? s_controllerField;
    private static FieldInfo? s_interactedOwnerField;
    private static FieldInfo? s_userOwnerField;
    private static FieldInfo? s_rightFilterTypeField;
    private static FieldInfo? s_rightOwnersControlField;
    private static FieldInfo? s_interactedGridOwnersField;
    private static PropertyInfo? s_rightFilterTypeIndexProperty;
    private static MethodInfo? s_createInventoryControlsInListMethod;

    // Cached owner values for current sort operation
    private static MyEntity? s_cachedInteractedOwner;
    private static MyEntity? s_cachedUserOwner;

    // Cached Sort controls (for visibility updates)
    private static MyGuiControlCheckbox? s_sortCheckbox;
    private static MyGuiControlLabel? s_sortLabel;

    // Tracks which panel is currently being sorted (set in CreateInventoryControlsInList_Patch.Prefix)
    private static bool s_isLeftPanel;

    // Cache of available space per inventory owner (cleared between sorts)
    // Avoids re-computing MaxVolume-CurrentVolume for the same owner multiple times during one sort
    private static readonly Dictionary<MyEntity, float> s_spaceCache = new Dictionary<MyEntity, float>();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        Instance = this;
        Instance.settingsGenerator = new SettingsGenerator();

        // Resolve internal types at runtime
        s_terminalInventoryControllerType = AccessTools.TypeByName("Sandbox.Game.Gui.MyTerminalInventoryController");
        s_screenTerminalType = AccessTools.TypeByName("Sandbox.Game.Gui.MyGuiScreenTerminal");
        var myEntityType = AccessTools.TypeByName("VRage.Game.Entity.MyEntity");

        // GetInventoryBase is a public method on MyEntity that returns MyInventoryBase
        if (myEntityType != null)
        {
            s_getInventoryBaseMethod = AccessTools.Method(myEntityType, "GetInventoryBase", new[] { typeof(int) });
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
            s_rightFilterProperty = AccessTools.Property(s_terminalInventoryControllerType, "RightFilter");
            s_radioButtonStyleEnumType = AccessTools.TypeByName("VRage.Game.MyGuiControlRadioButtonStyleEnum");
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
    /// Layout: [Search box] [Sort label Sort □] [Hide Empty label Hide Empty □]
    /// Sort fits in the existing gap between search box end and Hide Empty label.
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

            var searchBox = page.Controls.GetControlByName("BlockSearchRight") as MyGuiControlSearchBox;
            var hideEmptyCheckbox = page.Controls.GetControlByName("CheckboxHideEmptyRight") as MyGuiControlCheckbox;
            var hideEmptyLabel = page.Controls.GetControlByName("LabelHideEmptyRight") as MyGuiControlLabel;

            if (searchBox == null || hideEmptyCheckbox == null || hideEmptyLabel == null)
                return;

            // Use exact Y from Hide Empty checkbox (game uses -0.255f)
            float yPos = hideEmptyCheckbox.Position.Y;

            // ===== Layout (explicit positions, no relative math) =====
            // Game creates these with num=0.004f offset for right section:
            //   Search box:        X = 0.0225f, LEFT-aligned, width = 0.361f - labelSize.X
            //   Hide Empty label:  X = 0.419f, RIGHT-aligned, width ~0.08f, starts at 0.339f
            //   Hide Empty checkbox: X = 0.467f, RIGHT-aligned, width ~0.025f, starts at 0.442f
            //
            // We need:
            //   [Search box (shrunk)] [Sort label Sort □] [Hide Empty label Hide Empty □]
            //
            //   Make search box MUCH shorter (0.20f width). It will end at 0.0225+0.20 = 0.2225f.
            //   Sort label right edge at 0.285f, left edge 0.245f (gap 0.0225f from search)
            //   Sort checkbox right edge at 0.325f, left edge 0.30f (gap 0.04f... tight but OK)
            //   Gap between Sort checkbox right edge and Hide Empty label left edge = 0.339 - 0.325 = 0.014f

            // Shrink search box dramatically
            searchBox.Size = new Vector2(0.20f, searchBox.Size.Y);

            // Sort label - RIGHT-aligned (right edge at 0.285f)
            var sortLabel = new MyGuiControlLabel
            {
                Position = new Vector2(0.285f, yPos),
                Name = "SortBySpaceRightLabel",
                OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER,
                Text = "Sort"
            };

            // Sort checkbox - RIGHT-aligned (right edge at 0.325f)
            var sortCheckbox = new MyGuiControlCheckbox
            {
                Position = new Vector2(0.325f, yPos),
                Name = "SortBySpaceRight",
                OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER,
                IsChecked = Config.Current.SortByAvailableSpace
            };

            // Tooltip on hover
            sortCheckbox.SetToolTip("Sort inventories by available space (most empty first)");

            // Visibility based on filter (like Hide Empty)
            // Visible on all filters EXCEPT FilterCharacter (where there's only 1 inventory)
            bool isCharacterFilter = IsRightFilterCharacter();
            sortCheckbox.Visible = !isCharacterFilter;
            sortLabel.Visible = !isCharacterFilter;

            // Store in static fields for later access
            s_sortCheckbox = sortCheckbox;
            s_sortLabel = sortLabel;

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
            if (s_instanceField == null || s_controllerField == null) return;
            if (s_rightOwnersControlField == null || s_interactedGridOwnersField == null) return;
            if (s_rightFilterTypeIndexProperty == null || s_rightFilterTypeField == null) return;
            if (s_createInventoryControlsInListMethod == null) return;

            var instance = s_instanceField.GetValue(null) as MyGuiScreenTerminal;
            if (instance == null) return;

            var controller = s_controllerField.GetValue(instance);
            if (controller == null) return;

            var owners = s_interactedGridOwnersField.GetValue(controller) as List<MyEntity>;
            var rightOwnersControl = s_rightOwnersControlField.GetValue(controller) as MyGuiControlList;
            var filterType = s_rightFilterTypeField.GetValue(controller) as MyInventoryOwnerTypeEnum?;
            var filterTypeIndex = (int?)s_rightFilterTypeIndexProperty.GetValue(controller);

            if (owners == null || rightOwnersControl == null) return;

            // Use same logic as SetRightFilter
            var ownersToUse = (filterTypeIndex == 2) ? owners : owners;

            // Cache owner values before rebuilding
            CacheOwnerValues();

            // Directly call CreateInventoryControlsInList
            s_createInventoryControlsInListMethod.Invoke(controller, new object[] { ownersToUse, rightOwnersControl, filterType });

            ClearOwnerCache();
        }
        catch
        {
            // Silently ignore refresh failures - the list will still work, just won't re-sort immediately
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
        public static void Prefix(object[] __args)
        {
            // Detect which panel we're sorting by checking the listControl parameter
            // listControl is m_leftOwnersControl or m_rightOwnersControl
            if (s_rightOwnersControlField == null || s_controllerField == null || s_instanceField == null)
            {
                s_isLeftPanel = false;
                return;
            }

            try
            {
                var instance = s_instanceField.GetValue(null) as MyGuiScreenTerminal;
                var controller = s_controllerField.GetValue(instance);
                var rightList = s_rightOwnersControlField.GetValue(controller) as MyGuiControlList;

                // __args[1] is the listControl parameter
                if (__args != null && __args.Length > 1)
                {
                    s_isLeftPanel = !ReferenceEquals(__args[1], rightList);
                }
                else
                {
                    s_isLeftPanel = false;
                }
            }
            catch
            {
                s_isLeftPanel = false;
            }

            // Only cache owner values for RIGHT panel when sorting is enabled
            // Left panel keeps original game sorting (alphabetical)
            if (s_isLeftPanel && !Config.Current.SortLeftPanelToo)
            {
                ClearOwnerCache();
                return;
            }

            CacheOwnerValues();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ClearOwnerCache();
            s_isLeftPanel = false;
        }
    }

    /// <summary>
    /// Patches the right type group selection change to update Sort checkbox visibility.
    /// Mirrors Hide Empty behavior - Sort is hidden on character filter only.
    /// </summary>
    [HarmonyPatch]
    public static class RightTypeGroup_SelectedChanged_Patch
    {
        private static MethodInfo TargetMethod()
        {
            // RightTypeGroup_SelectedChanged(MyGuiControlRadioButtonGroup obj)
            return AccessTools.Method(s_terminalInventoryControllerType, "RightTypeGroup_SelectedChanged");
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            // Update Sort visibility - hide only on FilterCharacter
            bool isCharacterFilter = IsRightFilterCharacter();
            bool visible = !isCharacterFilter;

            if (s_sortCheckbox != null) s_sortCheckbox.Visible = visible;
            if (s_sortLabel != null) s_sortLabel.Visible = visible;
        }
    }

    /// <summary>
    /// Patches CompareGuiControlInventoryOwners to sort by available space when enabled.
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

            if (!sortEnabled)
                return true;

            // Only sort the RIGHT panel by default.
            // Left panel (production blocks) keeps original alphabetical order unless explicitly enabled.
            if (s_isLeftPanel && !Config.Current.SortLeftPanelToo)
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
    /// Clears cached owner values and space cache. Called after sorting completes.
    /// </summary>
    private static void ClearOwnerCache()
    {
        s_cachedInteractedOwner = null;
        s_cachedUserOwner = null;
        s_spaceCache.Clear();
    }

    /// <summary>
    /// Checks if the right inventory panel is currently showing character inventory.
    /// When true, Sort should be hidden (only one inventory).
    /// When false, Sort should be visible (multiple inventories to sort).
    /// </summary>
    private static bool IsRightFilterCharacter()
    {
        if (s_rightFilterProperty == null || s_radioButtonStyleEnumType == null) return false;
        if (s_instanceField == null || s_controllerField == null) return false;

        try
        {
            var instance = s_instanceField.GetValue(null) as MyGuiScreenTerminal;
            if (instance == null) return false;

            var controller = s_controllerField.GetValue(instance);
            if (controller == null) return false;

            // RightFilter returns MyGuiControlRadioButtonStyleEnum
            var filter = s_rightFilterProperty.GetValue(controller);
            if (filter == null) return false;

            // FilterCharacter = 0 in MyGuiControlRadioButtonStyleEnum
            // FilterGrid = 1
            int filterValue = (int)filter;
            return filterValue == 0; // FilterCharacter
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Updates Sort checkbox visibility based on current filter.
    /// </summary>
    private static void UpdateSortVisibility()
    {
        bool isCharacterFilter = IsRightFilterCharacter();
        bool visible = !isCharacterFilter;

        if (s_sortCheckbox != null) s_sortCheckbox.Visible = visible;
        if (s_sortLabel != null) s_sortLabel.Visible = visible;
    }

    /// <summary>
    /// Gets total available space (MaxVolume - CurrentVolume) for all inventories.
    /// Uses GetInventoryBase (public method on MyEntity).
    /// Uses RawValue field of MyFixedPoint for proper float conversion.
    /// Caches results per owner during a single sort to avoid reflection overhead.
    /// </summary>
    private static float GetTotalAvailableSpace(MyGuiControlInventoryOwner? owner)
    {
        if (owner?.InventoryOwner == null || !owner.InventoryOwner.HasInventory)
            return 0f;

        // Cache lookup - same owner is compared multiple times during a sort
        if (s_spaceCache.TryGetValue(owner.InventoryOwner, out float cached))
            return cached;

        float totalAvailable = 0f;
        if (s_getInventoryBaseMethod == null)
            return 0f;

        int inventoryCount = owner.InventoryOwner.InventoryCount;
        for (int i = 0; i < inventoryCount; i++)
        {
            var inv = s_getInventoryBaseMethod.Invoke(owner.InventoryOwner, new object[] { i });
            if (inv == null)
                break;

            // Use late binding to get properties from actual instance type
            var invType = inv.GetType();
            var maxProp = invType.GetProperty("MaxVolume");
            var curProp = invType.GetProperty("CurrentVolume");

            if (maxProp != null && curProp != null)
            {
                var maxVol = maxProp.GetValue(inv);
                var curVol = curProp.GetValue(inv);

                if (maxVol != null && curVol != null)
                {
                    // Convert MyFixedPoint to float via RawValue (long) / 1,000,000
                    // MyFixedPoint is stored as millionths in RawValue
                    var fpType = maxVol.GetType();
                    var rawValueField = fpType.GetField("RawValue");

                    if (rawValueField != null)
                    {
                        long maxRaw = (long)rawValueField.GetValue(maxVol);
                        long curRaw = (long)rawValueField.GetValue(curVol);
                        // Convert from millionths to float
                        totalAvailable += (maxRaw - curRaw) / 1000000f;
                    }
                }
            }
        }

        s_spaceCache[owner.InventoryOwner] = totalAvailable;
        return totalAvailable;
    }
}
