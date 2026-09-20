[CmdletBinding()]
param(
    [ValidateSet('GitHub','GoogleDrive')][string]$Destination,
    [string]$CommitMessage,
    [string]$DriveFolderId = '1PKjvRjjE3yJQo1rbxN-O3XCE9O77fuNF',
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
function Invoke-SourceGit([string[]]$Arguments) {
    $output = & git -c gc.auto=0 -c maintenance.auto=false @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git $($Arguments[0]) that bai ($LASTEXITCODE)." }
    return $output
}
$lock = $null
try {
    if (!$Destination) {
        do { $choice = Read-Host 'Luu tru code: 1 = GitHub, 2 = Google Drive' } while ($choice -notin @('1','2'))
        $Destination = if ($choice -eq '1') { 'GitHub' } else { 'GoogleDrive' }
    }
    $work = Join-Path (Get-Location) '.codex-artifacts\portable-update'
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $lock = [IO.File]::Open((Join-Path $work 'update.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $branch = Invoke-SourceGit @('symbolic-ref','--quiet','--short','HEAD')
    if (Invoke-SourceGit @('ls-files','--unmerged')) { throw 'Hay giai quyet Git conflict truoc.' }
    $gitDir = Invoke-SourceGit @('rev-parse','--absolute-git-dir')
    foreach ($state in @('MERGE_HEAD','rebase-merge','rebase-apply','CHERRY_PICK_HEAD')) {
        if (Test-Path -LiteralPath (Join-Path $gitDir $state)) { throw 'Hay hoan tat merge/rebase/cherry-pick truoc.' }
    }
    if ($Destination -eq 'GoogleDrive') {
        . (Join-Path $PSScriptRoot 'GoogleDrive-ReleaseApi.ps1')
        if (!$DriveFolderId) { $DriveFolderId = Read-Host 'ID thu muc Google Drive RIENG de luu source (tai khoan Google da dang nhap phai co quyen ghi)' }
        Assert-DriveId $DriveFolderId
        Write-Host "Thu muc luu source: https://drive.google.com/drive/folders/$DriveFolderId"
        & (Join-Path $PSScriptRoot 'Test-SourceDriveFolder.ps1') -DriveFolderId $DriveFolderId
    }
    if (!$CommitMessage) { $CommitMessage = 'Luu tru source ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') }
    # Preserve release/runtime edits for the release flow, including already staged edits.
    $excluded = @('portable', 'update/release-request.json')
    if (Invoke-SourceGit (@('diff','--cached','--name-only','--') + $excluded)) {
        throw 'Portable/release-request dang staged. Hay unstage cac file nay truoc khi luu source.'
    }
    Invoke-SourceGit @('add','--all','--','.',':(exclude)portable',':(exclude)update/release-request.json') | Out-Host
    if (Invoke-SourceGit @('diff','--cached','--name-only')) {
        Invoke-SourceGit @('commit','-m',$CommitMessage) | Out-Host
    }
    $head = Invoke-SourceGit @('rev-parse','HEAD')
    if ($Destination -eq 'GitHub') {
        # Separate remote branch prevents releases even before the new workflow reaches main.
        $target = 'refs/heads/backup/' + $branch
        Invoke-SourceGit @('push','origin',"${head}:$target") | Out-Host
        $remote = Invoke-SourceGit @('ls-remote','--exit-code','origin',$target)
        if (($remote -split '\s+')[0] -ne $head) { throw 'Remote khong khop commit local.' }
        Write-Host "[THANH CONG] Da luu code len GitHub backup/$branch ($head). Khong tao release."
    } else {
        $name = 'TTSK-Source-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $head.Substring(0,12) + '.zip'
        $zip = Join-Path $work $name
        Invoke-SourceGit @('archive','--format=zip',"--output=$zip",$head) | Out-Host
        $id = Send-DriveFile -Path $zip -FolderId $DriveFolderId -Name $name
        $metadata = Invoke-DriveApi -Uri "https://www.googleapis.com/drive/v3/files/${id}?fields=id,size,md5Checksum"
        if ([long]$metadata.size -ne (Get-Item -LiteralPath $zip).Length -or $metadata.md5Checksum -ne (Get-FileHash -LiteralPath $zip -Algorithm MD5).Hash) {
            throw "Ban sao Drive khong khop. File ID: $id"
        }
        Write-Host "[THANH CONG] Da luu ZIP commit $head len Drive, ID: $id. Khong cap nhat latest.json."
    }
} catch {
    Write-Host "[THAT BAI] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    if ($lock) { $lock.Dispose() }
}
