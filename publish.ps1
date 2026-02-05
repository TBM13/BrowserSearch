$out = ".\PublishOutput"
$pluginDir = "$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run\Plugins\BrowserSearch"

dotnet publish BrowserSearch -c Release -o $out
if ($?) {
    Remove-Item "$out\Microsoft.Windows.SDK.NET.dll"
    Remove-Item "$out\WinRT.Runtime.dll"

    if (Test-Path $pluginDir) {
        Write-Host "Removing existing plugin dir..."
        Remove-Item $pluginDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $pluginDir > $null
    Copy-Item "$out\*" $pluginDir -Recurse
    Write-Host "Plugin copied to $pluginDir"
}