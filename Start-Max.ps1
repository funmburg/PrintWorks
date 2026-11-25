$ErrorActionPreference = 'Stop'
$ids = Read-Host 'Введите числовые MAX ID сотрудников через запятую'
if ($ids -notmatch '^\s*[1-9]\d*(\s*,\s*[1-9]\d*)*\s*$') { throw 'Нужны числовые ID' }
$secret = Read-Host 'Токен бота MAX' -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
try {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $PSScriptRoot 'PrintWorks.exe'
    $info.WorkingDirectory = $PSScriptRoot
    $info.UseShellExecute = $false
    $info.EnvironmentVariables['PRINTWORKS_MAX_TOKEN'] = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    $info.EnvironmentVariables['PRINTWORKS_MAX_USERS'] = $ids
    [void][Diagnostics.Process]::Start($info)
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    $secret.Dispose()
}
