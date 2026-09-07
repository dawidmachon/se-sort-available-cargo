# Inventory Sort

A Space Engineers client plugin that enhances the inventory UI by adding a sort feature.

## Features

- **Sort by Available Space**: When enabled, inventory containers are sorted by their remaining capacity (most empty/largest first)
- **Toggle On/Off**: Use the "Sort" checkbox in the inventory UI to enable/disable sorting
- **Persistent Settings**: Your preference is saved between sessions
- **Enabled by Default**: Sorting is ON by default

## How It Works

When you open the inventory of a ship or station, you'll see a "Sort" checkbox near the top-right corner of each inventory panel. When enabled:

1. The inventory list is sorted so that containers with the most available space appear first
2. This helps you quickly find where to store items
3. The interacted-with entity (like the ship you're currently accessing) stays at the top

## Requirements

- [Space Engineers](https://store.steampowered.com/app/244850/Space_Engineers/)
- [Pulsar](https://github.com/SpaceGT/Pulsar)

## Installation

This is a Pulsar client plugin. Place the compiled DLL in your Pulsar plugins folder or let Pulsar compile from source.

## Building

```bash
dotnet build InventorySort.sln
```

## Configuration

Access the plugin settings through the Pulsar settings menu to toggle the sort feature.

## License

MIT
