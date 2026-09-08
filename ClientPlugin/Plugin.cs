using System.Reflection;
using System.Runtime.CompilerServices;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRageMath;
using Sandbox.Game.Entities;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.Gui;
using System.Collections.Generic;
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
            // Check if space-based sorting is enabled
            if (!Config.Current.SortByAvailableSpace)
                return true; // Run original method

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

            // Get interacted/user owners via reflection to prioritize them
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
        
        var screenType = AccessTools.TypeByName("Sandbox.Game.Gui.MyGuiScreenTerminal");
        if (screenType == null) return null;
        
        var instanceField = AccessTools.Field(screenType, "m_instance");
        var instance = instanceField?.GetValue(null) as MyGuiScreenTerminal;
        if (instance == null) return null;
        
        var controllerField = AccessTools.Field(screenType, "m_controllerInventory");
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
