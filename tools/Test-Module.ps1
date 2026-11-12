# Compiles one module with its tests into a private temp folder and runs it, so parallel module work never breaks another module's build.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Test-Module.ps1 -Module Core|Macros|Phrases|Recording|Win32 [-Filter name-part]
# Sources: src\Core\*.cs (always) + src\<Module>\*.cs (not for Core) + tests\TestKit.cs + tests\Fakes.cs + tests\<Module>Tests.cs.
# The test exe is a console app that opens no windows, sends no input, installs no hooks and never touches the real clipboard.
# Exit code = number of failed tests (1 when the build fails).
param(
    [Parameter(Mandatory = $true)][ValidateSet('Core', 'Macros', 'Phrases', 'Recording', 'Win32')][string]$Module,
    [string]$Filter
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$compiler = @($env:MK_ROSLYN_CSC, 'C:\dev\Toolchains\VS2022\MSBuild\Current\Bin\Roslyn\csc.exe') | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $compiler) { Write-Output 'ERROR: the Roslyn C# compiler was not found (set MK_ROSLYN_CSC).'; exit 1 }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll',
    'System.Xml.dll', 'System.Xaml.dll', 'Microsoft.CSharp.dll',
    'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'WPF\WindowsBase.dll'
) | ForEach-Object { '/r:' + (Join-Path $framework $_) }

function Get-Sources([string]$Folder, [string]$Pattern = '*.cs') {
    if (-not (Test-Path -LiteralPath $Folder)) { return @() }
    @(Get-ChildItem -LiteralPath $Folder -Filter $Pattern -File -Recurse | ForEach-Object FullName)
}

$sources = @(Get-Sources (Join-Path $root 'src\Core'))
if ($Module -ne 'Core') { $sources += Get-Sources (Join-Path $root "src\$Module") }
$sources += @('TestKit.cs', 'Fakes.cs', "${Module}Tests.cs") | ForEach-Object { Join-Path $root "tests\$_" } | Where-Object { Test-Path -LiteralPath $_ }

$work = Join-Path ([IO.Path]::GetTempPath()) ('MacroKit-module-' + $Module + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
$exitCode = 1
try {
    $exe = Join-Path $work "MacroKit.$Module.Tests.exe"
    $arguments = @('-noconfig', '-nologo', '-langversion:latest', '-nullable-', '-platform:x64', '-debug-', '-codepage:65001', '-utf8output', '-nowarn:1591',
        '-target:exe', "-out:$exe") + $references + $sources
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { Write-Output "ERROR: the $Module tests did not compile."; exit 1 }
    # Tests that touch storage use their own temp folders; this keeps any stray default path out of the real data folder.
    $env:MK_DATA_ROOT = Join-Path $work 'data'
    if ($Filter) { & $exe $Filter } else { & $exe }
    $exitCode = $LASTEXITCODE
}
finally {
    Remove-Item Env:\MK_DATA_ROOT -ErrorAction SilentlyContinue
    try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}
exit $exitCode
