# Inventory Sort Plugin

Adds a "Sort" checkbox to the terminal inventory panel, allowing users to sort inventory containers by available space (most empty/largest first).

## Features

- **Sort Checkbox**: Adds a "Sort" toggle to the right inventory panel (next to "Hide Empty")
- **Space-Based Sorting**: Sorts containers by available space (most empty first), making it easy to find storage with room
- **Persistent Settings**: Remembers your sort preference between sessions
- **Clean Integration**: Follows existing game UI patterns - only visible when viewing grid inventories (not character inventory)
- **No Performance Impact**: Reflection-based access is cached; sorting only happens when panel is rebuilt

## How It Works

When enabled, inventories are sorted by available capacity:
- **Most available space first** (emptiest containers)
- **Interacted/user inventory stays at top** for easy access
- **Original alphabetical order preserved** as tiebreaker

The sorting applies only to the **right inventory panel** (cargo/storage blocks). The left panel (production blocks like assemblers/refineries) keeps the game's original alphabetical order.

### Left Panel Sorting

By default, the left panel (production blocks) is not sorted. You can enable sorting for the left panel via the settings dialog or by editing the config file at:
```
%AppData%\Roaming\SpaceEngineers\Storage\InventorySort.cfg
```

Set `SortLeftPanelToo = True` in the config file.

## UI Placement

The Sort checkbox appears in the right inventory panel's toolbar:
```
[Search Box] [Sort □] [Hide Empty □]
```

Visibility follows Hide Empty behavior - only shown when viewing grid inventories, hidden when viewing character inventory.

## Requirements

- Space Engineers with [Pulsar](https://pulsarplugin.dev/) installed
- Both Legacy (.NET Framework 4.8) and Interim (.NET 10.0) editions supported

## Building

```bash
dotnet build ClientPlugin/ClientPlugin.csproj -c Release
```

DLL auto-deploys to Pulsar Local directories on Windows when game is closed.

## Configuration

Config file: `%AppData%\Roaming\SpaceEngineers\Storage\InventorySort.cfg`

```xml
<?xml version="1.0"?>
<Config xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <SortByAvailableSpace>true</SortByAvailableSpace>
  <SortLeftPanelToo>false</SortLeftPanelToo>
</Config>
```

| Setting | Default | Description |
|---------|---------|-------------|
| SortByAvailableSpace | true | Enable/disable space-based sorting |
| SortLeftPanelToo | false | Also sort left panel (production blocks) |

## Technical Notes

- Uses Harmony for runtime patching
- Caches reflection data on init for performance
- Only affects sorting comparison - no other inventory behavior modified
- Safe for multiplayer: client-side only, no server impact
