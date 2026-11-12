# Builds Macro Kit.exe with the Roslyn C# compiler against .NET Framework 4.8 (inbox on every Windows 10/11 PC).
#   powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1                          app into this folder
#   powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -OutputDirectory build\out
#   powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -OutputDirectory build\out -Test
# -Test also compiles tests\*.cs with every source folder except src\App (the WPF shell) into MacroKit.Tests.exe
# (in -OutputDirectory, or build\ when building into this folder). Set MK_STAGE_ROOT to compile in a private folder
# (parallel development builds); leftovers of other builds are then left alone and the project root is not rebuilt.
# Installers are never built here: the 4:00 AM Daily App Installer Queue does that.
param([string]$OutputDirectory = $PSScriptRoot, [switch]$Test)
$ErrorActionPreference = 'Stop'
# Compile in the background without competing with interactive apps (csc inherits the class).
try { [Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal' } catch {}

# Shared sources: a staged _shared copy (installer queue) wins over the live App Maintenance folder.
$sharedRoot = @((Join-Path $PSScriptRoot '_shared'), 'C:\dev\App Maintenance\shared') | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'Build.Common.ps1') } | Select-Object -First 1
if (-not $sharedRoot) { throw 'Shared app source is missing: C:\dev\App Maintenance\shared' }
. (Join-Path $sharedRoot 'Build.Common.ps1')
$cleanupName = 'Macro Kit'
$exeName = 'Macro Kit.exe'

# The inbox csc is C# 5 only. Macro Kit uses the Roslyn compiler from the VS2022 build tools (build-time only);
# the program it produces needs nothing but .NET Framework 4.8.
function Find-Roslyn {
    $candidates = @($env:MK_ROSLYN_CSC, 'C:\dev\Toolchains\VS2022\MSBuild\Current\Bin\Roslyn\csc.exe')
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($root) { $candidates += @(Get-ChildItem -Path (Join-Path $root 'Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe') -ErrorAction SilentlyContinue | ForEach-Object FullName) }
    }
    $found = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if (-not $found) { throw 'The Roslyn C# compiler was not found. Expected C:\dev\Toolchains\VS2022\MSBuild\Current\Bin\Roslyn\csc.exe (or set MK_ROSLYN_CSC to a Roslyn csc.exe, for example from Visual Studio 2022 Build Tools).' }
    return $found
}

$compiler = Find-Roslyn
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll',
    'System.Xml.dll', 'System.Xaml.dll', 'Microsoft.CSharp.dll',
    'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'WPF\WindowsBase.dll'
) | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$common = @('-noconfig', '-nologo', '-langversion:latest', '-nullable-', '-platform:x64', '-optimize+', '-debug-', '-codepage:65001', '-utf8output', '-nowarn:1591')

function Invoke-Roslyn([string]$What, [string[]]$Arguments) {
    & $compiler @common @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$What compilation failed." }
}

function Install-Binary([string]$Candidate, [string]$Target, [string]$Stage) {
    # The replaced file stays in the staging folder; Move-BuildLeftovers sends it to C:\dev\_cleanup next build.
    if (Test-Path -LiteralPath $Target) { [IO.File]::Replace($Candidate, $Target, (Join-Path $Stage 'previous.exe')) } else { [IO.File]::Move($Candidate, $Target) }
    if (-not (Get-ChildItem -LiteralPath $Stage -Force)) { [IO.Directory]::Delete($Stage) }
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
# Earlier staging folders (each holds that build's replaced exe) and *-previous-*.exe files are moved
# to C:\dev\_cleanup\Macro Kit\build-leftovers (never deleted); files still in use wait for the next build.
# MK_STAGE_ROOT (parallel development builds) compiles in a private folder and leaves other builds' folders alone.
if (-not $env:MK_STAGE_ROOT) { Move-BuildLeftovers -AppRoot $PSScriptRoot -App $cleanupName -Folders @($OutputDirectory) }

$src = Join-Path $PSScriptRoot 'src'
function New-Stage {
    $base = if ($env:MK_STAGE_ROOT) { $env:MK_STAGE_ROOT } else { Join-Path $PSScriptRoot 'build' }
    $s = Join-Path $base ('compile-' + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $s -Force | Out-Null; return $s
}

# --- the app ---------------------------------------------------------------------------------------
$stage = New-Stage
$candidate = Join-Path $stage $exeName
$sources = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' -File -Recurse | ForEach-Object FullName)
$sources += @(Get-SharedSource @('AppRestart.cs', 'SingleInstance.cs', 'ConfigurationTransfer.cs', 'ReleaseNotes.cs', 'ReleaseAttributes.cs'))
$resources = @(('/resource:' + (Join-Path $PSScriptRoot 'CHANGELOG.md') + ',App.Changelog.md'), ('/resource:' + (Join-Path $src 'App\Styles.xaml') + ',App.Styles.xaml'))
$png = Join-Path $PSScriptRoot 'assets\MK.png'
if (Test-Path -LiteralPath $png) { $resources += ('/resource:' + $png + ',App.Icon.png') }
$icon = Join-Path $PSScriptRoot 'assets\MK.ico'
$iconArgument = @(); if (Test-Path -LiteralPath $icon) { $iconArgument = @('-win32icon:' + $icon) }
Invoke-Roslyn 'Macro Kit' (@('-target:winexe', ('-win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('-out:' + $candidate)) + $iconArgument + $resources + $references + $sources)
$target = Join-Path $OutputDirectory $exeName
Install-Binary $candidate $target $stage

if ($OutputDirectory -ne $PSScriptRoot) {
    foreach ($item in @('assets', 'README.md', 'CHANGELOG.md', 'Macro Kit.exe.config')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $item) -Destination $OutputDirectory -Recurse -Force }
}
Copy-TransferHelper -Destination $OutputDirectory -App $cleanupName
Write-Output "Built app: $target. Installer files were not changed."
# Project rule (2026-11-11): the project-root exe is always the latest build, so a build into any other
# folder also rebuilds the root "Macro Kit.exe". Queue stages are skipped by the helper itself. MK_STAGE_ROOT builds
# (several agents compiling in parallel) skip it; whoever finishes such a batch builds the root once with plain Build.ps1.
if (-not $env:MK_STAGE_ROOT) { Update-ProjectRoot -AppRoot $PSScriptRoot -OutputDirectory $OutputDirectory -BuildScript $PSCommandPath }

# --- tests -----------------------------------------------------------------------------------------
if ($Test) {
    $stage = New-Stage
    $candidate = Join-Path $stage 'MacroKit.Tests.exe'
    # Every source folder except the WPF app shell is under test; new layers need no Build.ps1 change.
    $sources = @(Get-ChildItem -LiteralPath $src -Directory | Where-Object Name -ne 'App' | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.cs' -File -Recurse | ForEach-Object FullName })
    $sources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' -File | ForEach-Object FullName)
    Invoke-Roslyn 'Tests' (@('-target:exe', ('-win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('-out:' + $candidate)) + $references + $sources)
    $testTarget = if ($OutputDirectory -eq $PSScriptRoot) { Join-Path $PSScriptRoot 'build\MacroKit.Tests.exe' } else { Join-Path $OutputDirectory 'MacroKit.Tests.exe' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $testTarget) -Force | Out-Null
    Install-Binary $candidate $testTarget $stage
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Macro Kit.exe.config') -Destination ($testTarget + '.config') -Force
    Write-Output "Built tests: $testTarget"
}
