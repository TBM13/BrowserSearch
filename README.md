# BrowserSearch
This is a plugin for PowerToys Run.
It reads your default browser's history, allowing you to search its entries and open their URL.

## Supported browsers
Only Chrome is supported right now.

## Install instructions
* Exit PowerToys
* Download latest version from [releases](https://github.com/TBM13/BrowserSearch/releases)
* Extract zip
* Move extracted folder `BrowserSearch` to `%ProgramFiles%\PowerToys\modules\launcher\`
* Start PowerToys

## Build instructions
* Clone this repo
* Inside it, create a folder called `libs`
* Copy the following files from `%ProgramFiles%\PowerToys\modules\launcher\` to `libs`
    * Wox.Plugin.dll
    * Wox.Infrastructure.dll
    * Microsoft.Data.Sqlite.dll
* Open the project in Visual Studio and build it in release mode
* Copy the output folder `net7.0-windows` to `%ProgramFiles%\PowerToys\modules\launcher\`
* (Optional) Rename the copied folder to BrowserSearch