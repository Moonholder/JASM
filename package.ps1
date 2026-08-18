# utf-8

#!/usr/bin/env pwsh
$r = @('.\src\*\bin', '.\src\*\obj', '.\output', '.\*.7z', '.\*.exe')
Remove-Item -Path $r -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "🗑️  Clean done"

# Find VS developer context to run Release.py AOT compiler
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsDevCmd = $null
$latestToolset = $null

if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -property installationPath
    if ($vsPath -and (Test-Path $vsPath)) {
        $msvcPath = Join-Path $vsPath "VC\Tools\MSVC"
        if (Test-Path $msvcPath) {
            $validToolsets = Get-ChildItem $msvcPath | Where-Object {
                $linkPath = Join-Path $_.FullName "bin\Hostx64\x64\link.exe"
                Test-Path $linkPath
            } | Sort-Object Name -Descending

            if ($validToolsets.Count -gt 0) {
                $latestToolset = $validToolsets[0].Name
                $cmdFile = Join-Path $vsPath "Common7\Tools\VsDevCmd.bat"
                if (Test-Path $cmdFile) {
                    $vsDevCmd = $cmdFile
                }
            }
        }
    }
}

if ($vsDevCmd -and $latestToolset) {
    Write-Host "Found VS Developer Command Prompt with valid toolset: $latestToolset"
    # Invoke Python under VS Dev Command Prompt environment
    cmd.exe /c "call `"$vsDevCmd`" -arch=amd64 -vcvars_ver=$latestToolset && python .\Build\Release.py SelfContained"
} else {
    Write-Warning "Could not locate a valid Visual Studio C++ toolset. Attempting to build directly..."
    python .\Build\Release.py SelfContained
}