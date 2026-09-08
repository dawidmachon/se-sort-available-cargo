# se-inventory-sort

Status: PUBLISHED
Category: public

Client plugin that adds a sort checkbox to the terminal inventory panel, allowing users to sort inventory containers by available space (most empty/largest first).

## Features

- **Sort Checkbox**: Adds toggle to right inventory panel (next to "Hide Empty")
- **Space-Based Sorting**: Sorts by available capacity (most empty first)
- **Persistent Settings**: Preference saved between sessions
- **Clean Integration**: Only visible on grid inventories (not character)
- **Right Panel Only**: Left panel (production) keeps alphabetical order by default

## Key Files

- `ClientPlugin/Plugin.cs` - Main plugin with Harmony patches
- `ClientPlugin/Config.cs` - Configuration with property change notification
- `ClientPlugin/Settings/` - Settings dialog infrastructure
- `README.md` - User documentation
- `PLUGINHUB.md` - PluginHub listing description

## Harmony Patches

| Method | Purpose |
|--------|---------|
| `CreateInventoryPageRightSection` | Adds Sort checkbox UI |
| `CreateInventoryControlsInList` | Sets owner cache before sorting |
| `CompareGuiControlInventoryOwners` | Custom sort by available space |
| `RightTypeGroup_SelectedChanged` | Updates checkbox visibility |

## Performance Considerations

- **Reflection caching**: All type/method/field lookups cached at Init
- **Owner cache**: Set once per list build, not per comparison
- **Sort threshold**: ~0.0001f float comparison tolerance to avoid jitter
- **O(N log N)**: Standard sort complexity, ~300 comparisons for 50 items

## Config Options

| Setting | Default | Description |
|---------|---------|-------------|
| SortByAvailableSpace | true | Enable space-based sorting |
| SortLeftPanelToo | false | Also sort left (production) panel |

Config file: `%AppData%\Roaming\SpaceEngineers\Storage\InventorySort.cfg`

## Game Code References

- `Sandbox.Game.Gui.MyGuiScreenTerminal.CreateInventoryPageRightSection` - UI creation
- `Sandbox.Game.Gui.MyTerminalInventoryController.CreateInventoryControlsInList` - List building
- `Sandbox.Game.Gui.MyTerminalInventoryController.CompareGuiControlInventoryOwners` - Sort comparison
- `Sandbox.Game.Entities.MyEntity.GetInventoryBase(int)` - Inventory access
- `Sandbox.Game.MyInventory.MaxVolume/CurrentVolume` - Volume properties

## Building

```bash
dotnet build ClientPlugin/ClientPlugin.csproj -c Release
```

DLL auto-deploys to Pulsar Local directories when game is closed.
