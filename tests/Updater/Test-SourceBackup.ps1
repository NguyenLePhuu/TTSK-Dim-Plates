# Standalone integration test; only local temporary Git repositories are used.
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '..\..\scripts\Backup-Source.ps1'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$fixture = Join-Path $tempRoot ('ttsk-source-backup-' + [guid]::NewGuid().ToString('N'))
$original = Get-Location
function Git-Test([string[]]$Arguments) {
    & git @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Fixture git command failed.' }
}
try {
    New-Item -ItemType Directory -Path "$fixture\repo\scripts","$fixture\repo\update","$fixture\repo\portable" -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination "$fixture\repo\scripts\Backup-Source.ps1"
    Set-Location "$fixture\repo"
    Git-Test @('init','-b','main')
    Git-Test @('config','user.email','test@example.invalid')
    Git-Test @('config','user.name','Backup Test')
    Set-Content .gitignore '.codex-artifacts/'
    Set-Content update/release-request.json 'original-request'
    Set-Content portable/runtime.txt 'original-runtime'
    Set-Content code.txt 'old'
    Git-Test @('add','.')
    Git-Test @('commit','-m','initial')
    Git-Test @('init','--bare',"$fixture\remote.git")
    Git-Test @('remote','add','origin',"$fixture\remote.git")
    Set-Content code.txt 'new'
    Set-Content update/release-request.json 'pending-request'
    Set-Content portable/runtime.txt 'pending-runtime'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Backup-Source.ps1 -Destination GitHub -NoPause
    if ($LASTEXITCODE -ne 0) { throw 'Backup failed.' }
    $remote = & git ls-remote origin refs/heads/backup/main
    $head = & git rev-parse HEAD
    if (($remote -split '\s+')[0] -ne $head) { throw 'Backup remote mismatch.' }
    if (& git ls-remote origin refs/heads/main) { throw 'Backup pushed main.' }
    if ((& git show HEAD:code.txt) -ne 'new') { throw 'Source not committed.' }
    if ((& git show HEAD:update/release-request.json) -ne 'original-request') { throw 'Release request committed.' }
    if ((Get-Content update/release-request.json) -ne 'pending-request') { throw 'Pending request lost.' }
    if ((& git show HEAD:portable/runtime.txt) -ne 'original-runtime') { throw 'Runtime committed.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Backup-Source.ps1 -Destination GitHub -NoPause
    if ($LASTEXITCODE -ne 0) { throw 'Clean retry failed.' }
    Git-Test @('add','update/release-request.json')
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Backup-Source.ps1 -Destination GitHub -NoPause
    if ($LASTEXITCODE -eq 0) { throw 'Staged release request was not blocked.' }
    Write-Host 'PASS: source backup, isolated remote branch, preserved release/runtime, clean retry, staged request guard.'
} finally {
    Set-Location $original
    $fixtureFullPath = [IO.Path]::GetFullPath($fixture)
    if (!$fixtureFullPath.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean fixture outside the temporary directory: $fixtureFullPath"
    }
    if (Test-Path -LiteralPath $fixtureFullPath) {
        Remove-Item -LiteralPath $fixtureFullPath -Recurse -Force
    }
    Write-Host 'Temporary source-backup fixture cleaned.'
}
