# UABEANext Winlator

Fork of [UABEANext](https://github.com/nesrak1/UABEANext) tuned for running under Wine/Winlator on Android.

## Changes in this fork
- Fixes the startup hang on .NET 7+ under Wine (relaunches once with `DOTNET_EnableWriteXorExecute=0`)
- Main window opens maximized and in dark theme by default
- **Export All / Import All** buttons for assets, so a whole file (e.g. `globalgamemanagers`) can be exported, edited and imported back in one go
- Status-bar progress indicator while loading and saving, with an optional percentage (Tools > Options)
- Saving and repacking large bundles no longer freezes the window
- New **Parallel File Loading** setting (Tools > Options) to avoid running out of RAM on big files

See the [Releases](../../releases) page for detailed changes.

Changes were written with [Claude](https://claude.ai).

## Download
Grab the latest build from the [Releases](../../releases) page.
