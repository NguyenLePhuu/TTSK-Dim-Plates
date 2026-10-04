# Kiểm offline bằng Git thật và Drive giả; không chạm repository/model đang làm việc.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ttsk-backup-test-' + [guid]::NewGuid().ToString('N'))
# Dùng mã Unicode để cả Windows PowerShell đọc script không BOM vẫn tạo đúng tên.
$repo = Join-Path $testRoot ('D' + [char]0x1ef1 + ' ' + [char]0xe0 + 'n source')
$remote = Join-Path $testRoot 'remote.git'
$originalLocation = Get-Location
$originalConfig = $env:GIT_CONFIG_GLOBAL
$originalNoSystem = $env:GIT_CONFIG_NOSYSTEM
function Git-Test([string[]]$Arguments) {
    $result = & git @Arguments
    if ($LASTEXITCODE) { throw "Git test failed: $Arguments" }
    return $result
}
function Run-Backup([string[]]$Arguments, [int]$Expected = 0) {
    # Windows PowerShell gói stderr tiến trình con thành ErrorRecord, kể cả git push thành công.
    $ErrorActionPreference = 'Continue'
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'scripts/Run-TestBackup.ps1') @Arguments 2>&1
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne $Expected) { throw "Unexpected exit $LASTEXITCODE (expected $Expected): $output" }
    return ($output -join "`n")
}
function Assert-Test([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}
try {
    New-Item -ItemType Directory -Path (Join-Path $repo 'scripts'),(Join-Path $repo 'portable'),(Join-Path $repo 'update') -Force | Out-Null
    # Không dùng hook, signing hay cấu hình remote của người dùng.
    $env:GIT_CONFIG_GLOBAL = Join-Path $testRoot 'empty.gitconfig'
    $env:GIT_CONFIG_NOSYSTEM = '1'
    Set-Content -LiteralPath $env:GIT_CONFIG_GLOBAL -Value ''
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Backup-Source.ps1') -Destination (Join-Path $repo 'scripts')
    # Mô phỏng console BAT không dùng UTF-8 để bắt lỗi đường dẫn tiếng Việt.
    Set-Content (Join-Path $repo 'scripts/Run-TestBackup.ps1') @'
[Console]::OutputEncoding = [Text.Encoding]::GetEncoding(437)
& (Join-Path $PSScriptRoot 'Backup-Source.ps1') @args
exit $LASTEXITCODE
'@
    Set-Content (Join-Path $repo 'scripts/GoogleDrive-ReleaseApi.ps1') @'
function Assert-DriveId($Id) { if (!$Id) { throw 'Missing ID' } }
function Connect-DriveForSourceBackup([switch]$NonInteractive) { }
function Send-DriveFile($Path,$FolderId,$Name) { $script:uploadedPath=$Path; return 'fake-id' }
function Invoke-DriveApi($Uri) {
    $hash = (Get-FileHash -LiteralPath $script:uploadedPath -Algorithm MD5).Hash
    if (Test-Path (Join-Path $PSScriptRoot '../bad-checksum')) { $hash='bad' }
    return [pscustomobject]@{size=(Get-Item -LiteralPath $script:uploadedPath).Length;md5Checksum=$hash}
}
'@
    Set-Content (Join-Path $repo 'scripts/Test-SourceDriveFolder.ps1') 'param($DriveFolderId)'
    Set-Content (Join-Path $repo '.gitignore') '.codex-artifacts/'
    Set-Content (Join-Path $repo 'source.txt') 'initial'
    Set-Content (Join-Path $repo 'portable/runtime.txt') 'old runtime'
    Set-Content (Join-Path $repo 'update/release-request.json') '{}'
    Set-Location -LiteralPath $repo
    Git-Test @('init','-b','main') | Out-Null
    Git-Test @('config','user.name','Backup Test') | Out-Null
    Git-Test @('config','user.email','backup@example.invalid') | Out-Null
    Git-Test @('add','.') | Out-Null
    Git-Test @('commit','-m','initial') | Out-Null
    Git-Test @('init','--bare','-b','main',$remote) | Out-Null
    Git-Test @('remote','add','origin',$remote) | Out-Null
    $initial = Git-Test @('rev-parse','HEAD')
    $null = Run-Backup @('-NoPause') 1
    Assert-Test ((Git-Test @('rev-parse','HEAD')) -eq $initial) 'Missing destination changed HEAD.'
    foreach ($state in @('REVERT_HEAD','sequencer')) {
        $statePath = Join-Path $repo ('.git/' + $state)
        Set-Content -LiteralPath $statePath 'pending'
        $null = Run-Backup @('-Destination','GitHub','-NoPause') 1
        Remove-Item -LiteralPath $statePath
    }
    Set-Content (Join-Path $repo 'source.txt') 'new source'
    Set-Content (Join-Path $repo 'portable/runtime.txt') 'new runtime'
    Git-Test @('add','portable') | Out-Null
    $null = Run-Backup @('-Destination','GitHub','-NoPause') 1
    Assert-Test ((Git-Test @('rev-parse','HEAD')) -eq $initial) 'Excluded staged file was committed.'
    Git-Test @('reset','--','portable') | Out-Null
    $null = Run-Backup @('-Destination','GitHub','-NoPause')
    $head = Git-Test @('rev-parse','HEAD')
    Assert-Test ($head -ne $initial) 'Source was not committed.'
    Assert-Test ((Git-Test @('show','HEAD:portable/runtime.txt')) -eq 'old runtime') 'Runtime was committed.'
    Assert-Test ((Git-Test @('--git-dir',$remote,'rev-parse','refs/heads/backup/main')) -eq $head) 'Backup branch missing.'
    $null = Run-Backup @('-Destination','GitHub','-NoPause')
    Assert-Test ((Git-Test @('rev-parse','HEAD')) -eq $head) 'Repeat run created empty commit.'
    $heldLock = [IO.File]::Open((Join-Path $repo '.codex-artifacts/portable-update/update.lock'),'Open','ReadWrite','None')
    try { $null = Run-Backup @('-Destination','GitHub','-NoPause') 1 }
    finally { $heldLock.Dispose() }
    Git-Test @('remote','set-url','origin',(Join-Path $testRoot 'missing.git')) | Out-Null
    $pushFailure = Run-Backup @('-Destination','GitHub','-NoPause') 1
    Assert-Test ($pushFailure -match 'Commit local') 'Push failure lost recovery diagnostics.'
    Git-Test @('remote','set-url','origin',$remote) | Out-Null
    foreach ($badName in @('../escape','CON','bad.')) {
        $null = Run-Backup @('-Destination','GoogleDrive','-ArchiveName',$badName,'-NoPause') 1
    }
    $driveArgs = @('-Destination','GoogleDrive','-ArchiveName','Ban luu [01]','-NoPause')
    $null = Run-Backup $driveArgs
    $null = Run-Backup $driveArgs
    $zips = @(Get-ChildItem -LiteralPath (Join-Path $repo '.codex-artifacts/portable-update') -Filter '*.zip' -Recurse)
    Assert-Test ($zips.Count -eq 2) 'Repeat archive name overwrote a backup.'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zips[0].FullName)
    try { Assert-Test ($null -ne $archive.GetEntry('source.txt')) 'ZIP missing source.' }
    finally { $archive.Dispose() }
    Set-Content (Join-Path $repo 'bad-checksum') 'simulate failure'
    $failure = Run-Backup $driveArgs 1
    Assert-Test ($failure -match 'ZIP local' -and $failure -match 'Commit local') 'Recovery diagnostics missing.'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../update/Luu_Tru_Code.bat') -Destination (Join-Path $repo 'update')
    & (Join-Path $repo 'update/Luu_Tru_Code.bat') -Destination GitHub -NoPause | Out-Null
    Assert-Test ($LASTEXITCODE -eq 0) 'BAT success exit failed.'
    & (Join-Path $repo 'update/Luu_Tru_Code.bat') -NoPause | Out-Null
    Assert-Test ($LASTEXITCODE -eq 1) 'BAT failure exit failed.'
    # Thư mục script nằm trong repo cha phải bị chặn trước git add/commit.
    $nested = Join-Path $repo 'nested/scripts'
    New-Item -ItemType Directory -Path $nested -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'scripts/Backup-Source.ps1') -Destination $nested
    $before = Git-Test @('rev-parse','HEAD')
    & powershell.exe -NoProfile -File (Join-Path $nested 'Backup-Source.ps1') -Destination GitHub -NoPause | Out-Null
    Assert-Test ($LASTEXITCODE -eq 1) 'Nested source was accepted.'
    Assert-Test ((Git-Test @('rev-parse','HEAD')) -eq $before) 'Nested source changed parent HEAD.'
    Write-Host 'PASS: noninteractive, revert/sequencer, staged exclusions, source commit, local bare push, repeat, lock, failed push, invalid names, ZIP/checksum, recovery, BAT exit and repository boundary.'
} finally {
    Set-Location -LiteralPath $originalLocation
    $env:GIT_CONFIG_GLOBAL = $originalConfig
    $env:GIT_CONFIG_NOSYSTEM = $originalNoSystem
    # Chỉ xóa fixture GUID do test vừa tạo trong TEMP.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notmatch '^ttsk-backup-test-[a-f0-9]{32}$') { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
