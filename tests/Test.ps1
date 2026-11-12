# Builds Macro Kit and its tests into a temporary folder and runs them. Nothing here touches the real PC, real input, hooks, hotkeys,
# the clipboard, the installer queue or the project-root exe.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests\Test.ps1 [test-name-filter]
# 1. MacroKit.Tests.exe: the logic tests (console app, opens no windows).
# 2. When the app supports it: "Macro Kit.exe --self-test-ui <dir> --data <tmp> --no-input", run on the hidden desktop
#    (Toolchains\HiddenDesktop\Invoke-OnHiddenDesktop.ps1) so no window ever appears on a real screen.
# Exit code 0 = everything passed.
param([string]$Filter)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('MacroKit-tests-' + [Guid]::NewGuid().ToString('N'))
$out = Join-Path $root 'out'
$data = Join-Path $root 'data'
$ui = Join-Path $root 'ui'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $projectRoot 'Build.ps1'
$hiddenRunner = 'C:\dev\Toolchains\HiddenDesktop\Invoke-OnHiddenDesktop.ps1'
$oldStage = $env:MK_STAGE_ROOT
$oldData = $env:MK_DATA_ROOT
$failures = 0
try {
    New-Item -ItemType Directory -Path $out, $data, $ui -Force | Out-Null
    $env:MK_STAGE_ROOT = Join-Path $root 'stage'     # private compile folder; the project-root exe is left alone
    & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -OutputDirectory $out -Test
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }

    # Logic tests. MK_DATA_ROOT keeps any stray default path out of the real data folder.
    $env:MK_DATA_ROOT = $data
    $testExe = Join-Path $out 'MacroKit.Tests.exe'
    if ($Filter) { & $testExe $Filter } else { & $testExe }
    $failures += $LASTEXITCODE
    Remove-Item Env:\MK_DATA_ROOT -ErrorAction SilentlyContinue

    # UI self test, only once the app shell implements --self-test-ui (the option text is in the exe's string table).
    $appExe = Join-Path $out 'Macro Kit.exe'
    $needle = [Text.Encoding]::Unicode.GetBytes('--self-test-ui')
    $bytes = [IO.File]::ReadAllBytes($appExe)
    $supported = $false
    for ($i = 0; $i -le $bytes.Length - $needle.Length -and -not $supported; $i++) {
        if ($bytes[$i] -ne $needle[0]) { continue }
        $match = $true
        for ($j = 1; $j -lt $needle.Length; $j++) { if ($bytes[$i + $j] -ne $needle[$j]) { $match = $false; break } }
        if ($match) { $supported = $true }
    }
    if ($supported) {
        if (-not (Test-Path -LiteralPath $hiddenRunner)) { throw "The hidden-desktop runner is missing: $hiddenRunner" }
        $argumentString = '--self-test-ui {q}' + $ui + '{q} --data {q}' + (Join-Path $root 'ui-data') + '{q} --no-input'
        & powershell -NoProfile -ExecutionPolicy Bypass -File $hiddenRunner -FilePath $appExe -ArgumentString $argumentString -WorkingDirectory $out -TimeoutMinutes 5
        $uiExit = $LASTEXITCODE
        $report = Join-Path $ui 'self-test.txt'
        if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report | ForEach-Object { Write-Output $_ } }
        if ($uiExit -ne 0) { Write-Output "UI self test FAILED (exit code $uiExit)."; $failures++ } else { Write-Output 'UI self test passed.' }
    }
    else {
        Write-Output 'UI self test skipped: this build of Macro Kit.exe has no --self-test-ui yet.'
    }
}
catch {
    Write-Output ('ERROR: ' + $_.Exception.Message)
    $failures++
}
finally {
    $env:MK_STAGE_ROOT = $oldStage
    $env:MK_DATA_ROOT = $oldData
    try { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}
if ($failures -gt 0) { Write-Output "FAILED ($failures)."; exit 1 }
Write-Output 'All tests passed.'
exit 0
