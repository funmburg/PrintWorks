$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
$exe = Join-Path $PSScriptRoot 'PrintWorks.exe'
& $compiler /nologo /target:winexe /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.Xml.Linq.dll "/out:$exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
if ($args -contains '-Test') {
    $process = Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Self-test failed ($($process.ExitCode))." }
}