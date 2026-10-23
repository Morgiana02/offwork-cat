param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$outputDir = Join-Path $projectRoot 'dist'
$target = '/target:winexe'
$outputName = 'OffworkCat.exe'
if ($SelfTest) { $target = '/target:exe'; $outputName = 'OffworkCat.Tests.exe' }
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$outputPath = Join-Path $outputDir $outputName
$compilerArgs = @('/nologo', $target, '/optimize+', '/codepage:65001', "/out:$outputPath", '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll')
foreach ($catKey in @('leisure','dance','thinking','fishing','happy','feet','reluctant','done','speed-off','angry')) {
    $assetPath = Join-Path $projectRoot "assets/frames/$catKey.bin"
    $compilerArgs += "/resource:$assetPath,cat.$catKey"
}
$iconPath = Join-Path $projectRoot 'assets/icon.ico'
$compilerArgs += "/win32icon:$iconPath"
$compilerArgs += (Join-Path $projectRoot 'src/DesktopCat.cs')
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
if ($SelfTest) {
    & $outputPath --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Self-test failed.' }
} else { Write-Output $outputPath }
