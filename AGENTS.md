# se-sort-available-cargo

Status: PUBLISHED
Category: public

Client plugin that adds a sort checkbox to the terminal inventory panel, allowing users to sort inventory containers by available cargo space (most empty/largest first).

## Features

- **Sort Checkbox**: Adds toggle to right inventory panel (next to "Hide Empty")
- **Cargo-Space Sorting**: Sorts by available capacity (most empty first)
- **Persistent Settings**: Preference saved between sessions
- **Clean Integration**: Only visible on grid inventories (not character)
- **Right Panel Only**: Left panel (production) keeps alphabetical order by default

## Key Files

- `ClientPlugin/Plugin.cs` - Main plugin with Harmony patches
- `ClientPlugin/Config.cs` - Configuration with property change notification
- `ClientPlugin/Settings/` - Settings dialog infrastructure
- `README.md` - User documentation
- `SortAvailableCargo.xml` - Pulsar plugin descriptor

## Harmony Patches

| Method | Purpose |
|--------|---------|
| `CreateInventoryPageRightSection` | Adds Sort checkbox UI |
| `CreateInventoryControlsInList` | Sets owner cache before sorting |
| `CompareGuiControlInventoryOwners` | Custom sort by available cargo space |
| `RightTypeGroup_SelectedChanged` | Updates checkbox visibility |

## Plugin Interop (v1.1.0)

- List rebuilds go through the controller's **public `Refresh()`** (never the private
  `CreateInventoryControlsInList`), so plugins that prefix `Refresh()` to own the
  inventory panel (Unified Storage) intercept our rebuilds like the game's own.
- The Sort checkbox hides itself when any other plugin has a Harmony prefix on
  `Refresh()` — conservative, but guarantees no dead control in any plugin combo.
- Sort label/checkbox positions are measured from the actual localized label sizes
  (anchored to the Hide Empty label's left edge); the search box shrinks to fit.

## Performance Considerations

- **Reflection caching**: All type/method/field lookups cached at Init
- **Owner cache**: Set once per list build, not per comparison
- **Space cache**: Dictionary keyed by MyEntity, cleared between sorts — each owner's space is computed at most once per sort
- **Sort threshold**: ~0.0001f float comparison tolerance to avoid jitter
- **O(N log N)**: Standard sort complexity, ~300 comparisons for 50 items

## Config Options

| Setting | Default | Description |
|---------|---------|-------------|
| SortByAvailableSpace | true | Enable space-based sorting |
| SortLeftPanelToo | false | Also sort left (production) panel |
| KeepActiveContainerFirst | false | ON = vanilla: pin active/opened container to the top. OFF (default) = all containers sorted purely by space. Only effective when SortByAvailableSpace is on. |

Config file: `%AppData%\Roaming\SpaceEngineers\Storage\SortAvailableCargo.cfg`

## Game Code References

- `Sandbox.Game.Gui.MyGuiScreenTerminal.CreateInventoryPageRightSection` - UI creation
- `Sandbox.Game.Gui.MyTerminalInventoryController.CreateInventoryControlsInList` - List building
- `Sandbox.Game.Gui.MyTerminalInventoryController.CompareGuiControlInventoryOwners` - Sort comparison
- `VRage.Game.Entity.MyEntity.GetInventoryBase(int)` - Inventory access
- `Sandbox.Game.MyInventory.MaxVolume/CurrentVolume` - Volume properties

## Building

```bash
dotnet build SortAvailableCargo.sln
```

DLL auto-deploys to Pulsar Local directories when game is closed.
