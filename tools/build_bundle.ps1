param(
    [string]$UnityEditor = $env:UNITY_EDITOR_PATH
)
$ErrorActionPreference = 'Stop'
if (-not $UnityEditor -or -not (Test-Path -LiteralPath $UnityEditor)) {
    throw 'Specify -UnityEditor with the path to Unity 2020.3.35f1/Editor/Unity.exe, or set UNITY_EDITOR_PATH.'
}
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskProject = Join-Path $taskRoot 'build/unity-bundle'
$taskOutput = Join-Path $taskRoot 'build/bundle'
$taskAssets = Join-Path $taskProject 'Assets/RTLC'
# This is generated staging data, never an editable source directory.
if (Test-Path -LiteralPath $taskAssets) {
    $resolvedStaging = [IO.Path]::GetFullPath($taskAssets)
    $expectedStaging = [IO.Path]::GetFullPath((Join-Path $taskRoot 'build/unity-bundle/Assets/RTLC'))
    if ($resolvedStaging -ne $expectedStaging) { throw 'Unexpected bundle staging directory.' }
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}
foreach ($dir in @($taskAssets, "$taskProject/Assets/Editor", "$taskProject/Packages", "$taskProject/ProjectSettings", $taskOutput)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
Copy-Item -LiteralPath "$PSScriptRoot/BundleBuilder.cs" -Destination "$taskProject/Assets/Editor/BundleBuilder.cs"
Copy-Item -LiteralPath "$taskRoot/Translations/translation.txt" -Destination "$taskAssets/translation.txt"
foreach ($font in (Get-Content -LiteralPath "$PSScriptRoot/bundle-fonts.json" -Raw | ConvertFrom-Json)) {
    if (-not (Test-Path -LiteralPath (Join-Path "$taskRoot/Fonts" $font))) {
        throw "Missing font: $font. Prepare game fonts with tools/extract_game_fonts.py before building."
    }
    $fontTarget = Join-Path "$taskAssets/Fonts" $font
    New-Item -ItemType Directory -Path (Split-Path $fontTarget -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path "$taskRoot/Fonts" $font) -Destination $fontTarget
}
New-Item -ItemType Directory -Path "$taskAssets/Textures" -Force | Out-Null
Copy-Item -Path "$taskRoot/Textures/*.png" -Destination "$taskAssets/Textures" -Force
[IO.File]::WriteAllText("$taskProject/Packages/manifest.json", '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}')
[IO.File]::WriteAllText("$taskProject/ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2020.3.35f1`n")
$env:SABLE_BUNDLE_OUTPUT = $taskOutput
$taskLog = Join-Path $taskOutput 'unity-build.log'
$taskArgs = @('-batchmode','-nographics','-projectPath',('"'+$taskProject+'"'),'-executeMethod','BundleBuilder.Build','-logFile',('"'+$taskLog+'"'))
$taskProcess = Start-Process -FilePath $UnityEditor -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
[PSCustomObject]@{ProcessId=$taskProcess.Id; Log=$taskLog; Output=$taskOutput} | ConvertTo-Json
$taskProcess.WaitForExit()
if ($taskProcess.ExitCode -ne 0) { throw "Unity bundle build failed with exit code $($taskProcess.ExitCode). See $taskLog" }
if (-not (Select-String -LiteralPath $taskLog -Pattern 'SABLE_BUNDLE_OK:' -Quiet)) {
    throw "Unity did not confirm successful bundle validation. See $taskLog"
}
Write-Output "Bundle built and validated: $taskOutput/rtlc.bundle"
