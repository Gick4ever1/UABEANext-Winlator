# UABEANext Winlator

Fork of [UABEANext](https://github.com/nesrak1/UABEANext) tuned for running under Wine/Winlator on Android.

## Changes in this fork
- Fixes the .NET 7+ startup hang under Wine by relaunching once with `DOTNET_EnableWriteXorExecute=0`
- Main window opens maximized by default.

## Download
Grab the latest build from the [Releases](../../releases) page.
