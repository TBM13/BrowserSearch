@echo off

dotnet build %cd%\BrowserSearch.sln /target:BrowserSearch /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary /p:Configuration=Release /p:Platform="x64"

set "source=%cd%\BrowserSearch\bin\x64\Release\net9.0-windows"
set "destination=%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\BrowserSearch"

if exist "%destination%" (
  echo Removing existing directory...
  rmdir /s /q "%destination%"
)

echo Moving directory...
move "%source%" "%destination%"

echo Move completed.
pause