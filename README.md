# BrowserSearch
This is a plugin for PowerToys Run.
It reads your default browser's history, allowing you to search its entries and open their URL.

<p align="center">
    <img src="./Screenshots/1.png" width="500"/>
</p>

## Supported browsers
* Google Chrome
* Microsoft Edge (Chromium version)

## Install instructions
* Exit PowerToys
* Download latest version from [releases](https://github.com/TBM13/BrowserSearch/releases)
* Extract zip
* Move extracted folder `BrowserSearch` to `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\`
* Start PowerToys

## Build instructions
* Clone this repo
* Inside the `BrowserSearch` folder, create another one called `libs`
* Copy the following files from `%ProgramFiles%\PowerToys\` to `libs`
    * Wox.Plugin.dll
    * Wox.Infrastructure.dll
    * Microsoft.Data.Sqlite.dll
    * PowerToys.Settings.UI.Lib.dll
* Open the project in Visual Studio and build it in release mode
* Copy the output folder `net7.0-windows` to `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\`
* (Optional) Rename the copied folder to BrowserSearch