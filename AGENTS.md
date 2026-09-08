# se-inventory-sort

Status: LOCAL-ONLY
Category: public

Client plugin that adds a sort button to the inventory UI, allowing users to sort inventory containers by available space (biggest/emptiest first).

## Review Requirements

**IMPORTANT:** For ANY code changes, perform MULTI-PASS review before declaring done:

1. **Pass 1 - Architecture**: Read full code, trace data flow, null safety on reflection
2. **Pass 2 - Performance**: Reflection caching, GC pressure, per-operation costs
3. **Pass 3 - Edge Cases**: Lifecycle, event handlers, page reuse, race conditions
4. **Pass 4 - Integration**: How patches interact with each other and game code

Do NOT say "verification complete" after one pass. Found bugs in passes 1, 2, 3, and 4.

## Features

- Adds a "Sort" checkbox to both left and right inventory panels in the terminal
- By default, inventories are sorted by available space (most empty, largest first)
- User preference persists between sessions
- Can be toggled on/off via the checkbox or settings dialog

## Key Files

- `ClientPlugin/Plugin.cs` - Main plugin entry point with Harmony patches
- `ClientPlugin/Config.cs` - Configuration settings
- `ClientPlugin/Settings/` - Settings dialog infrastructure

## Harmony Patches

- `MyGuiScreenTerminal.CreateInventoryPageLeftSection` - Adds sort checkbox to left panel
- `MyGuiScreenTerminal.CreateInventoryPageRightSection` - Adds sort checkbox to right panel
- `MyTerminalInventoryController.CompareGuiControlInventoryOwners` - Custom sort logic

## Game Code References

- `Sandbox.Game.GUI.MyGuiScreenTerminal` - Terminal screen with inventory tab
- `Sandbox.Game.GUI.MyTerminalInventoryController` - Inventory controller with sorting logic
- `Sandbox.Game.Screens.Helpers.MyGuiControlInventoryOwner` - Individual inventory container control

## Building

```bash
dotnet build InventorySort.sln
```

DLL auto-deploys to Pulsar Local directories when game is closed.
