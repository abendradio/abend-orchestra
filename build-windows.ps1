# Builds abendc.exe, AbendComposer.exe (editor window) and tests.exe into .\bin with the C# compiler that
# comes with Windows (.NET Framework 4.8). No SDK needed.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "C# compiler not found: $csc" }
$bin = Join-Path $root "bin"
New-Item -ItemType Directory -Force $bin | Out-Null

$core = @(Get-ChildItem "$root\src\abendc\*.cs") + @(Get-ChildItem "$root\src\engine\*.cs") | ForEach-Object { $_.FullName }
$editor = Get-ChildItem "$root\src\editor\*.cs" | ForEach-Object { $_.FullName }
$common = @("-nologo", "-warn:0", "-optimize")

& $csc @common -target:exe -main:Abend.Cli "-out:$bin\abendc.exe" @core
if ($LASTEXITCODE -ne 0) { throw "abendc build failed" }
& $csc @common -r:System.Windows.Forms.dll -r:System.Drawing.dll -target:winexe -main:Abend.App "-out:$bin\AbendComposer.exe" @core @editor
if ($LASTEXITCODE -ne 0) { throw "editor build failed" }
& $csc @common -r:System.Drawing.dll -target:exe -main:Abend.Tests "-out:$bin\tests.exe" @core "$root\tests\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw "tests build failed" }

Write-Host "Built into $bin. Run the tests from the repo folder: .\bin\tests.exe"
