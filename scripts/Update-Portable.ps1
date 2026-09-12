[CmdletBinding()]
param([switch]$NoPause, [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo
$work = Join-Path $repo '.codex-artifacts\portable-update'
New-Item -ItemType Directory -Path $work -Force | Out-Null
$run = Join-Path $work ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $run | Out-Null
$log = Join-Path $run 'update.log'
$lock = $null
$transcript = $false
$exitCode = 1

function Invoke-Git([string[]]$Arguments, [int]$Attempts = 1) {
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        # Avoid OneDrive locks during automatic GC. Never delete Git locks/objects.
        $ErrorActionPreference = 'Continue'
        $output = & git -c gc.auto=0 -c maintenance.auto=false -c core.safecrlf=false @Arguments 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($code -eq 0) { return ($output | Out-String).Trim() }
        Write-Host ($output | Out-String)
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds 2 }
    }
    throw "Git $($Arguments[0]) that bai (exit $code). Kiem tra mang/quyen GitHub va thong bao ben tren; chay lai BAT de thu lai."
}

function Copy-Verified([string]$Source, [string]$Destination) {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
            Copy-Item -LiteralPath $Source -Destination $Destination -Force
            if ((Get-FileHash -LiteralPath $Source).Hash -ne (Get-FileHash -LiteralPath $Destination).Hash) { throw "SHA256 khong khop: $Destination" }
            return
        } catch {
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds 2
        }
    }
}

try {
    try { $lock = [IO.File]::Open((Join-Path $work 'update.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Mot tien trinh cap nhat khac dang chay. Hay cho no ket thuc.' }
    Start-Transcript -LiteralPath $log | Out-Null
    $transcript = $true
    Write-Host '[1/4] Kiem tra Git va cong cu build...'
    $branch = ''
    $syncError = $null
    if (!$BuildOnly) {
        try {
            Get-Command git -ErrorAction Stop | Out-Null
            $branch = Invoke-Git @('symbolic-ref', '--quiet', '--short', 'HEAD')
            if (Invoke-Git @('ls-files', '--unmerged')) { throw 'Git dang co conflict chua giai quyet.' }
            $gitDir = Invoke-Git @('rev-parse', '--absolute-git-dir')
            foreach ($state in @('MERGE_HEAD', 'rebase-merge', 'rebase-apply', 'CHERRY_PICK_HEAD')) {
                if (Test-Path -LiteralPath (Join-Path $gitDir $state)) { throw 'Hay hoan tat merge/rebase/cherry-pick truoc.' }
            }
            Invoke-Git @('fetch', '--no-auto-maintenance', 'origin') 3 | Write-Host
            $remoteRef = 'refs/remotes/origin/' + $branch
            if (Invoke-Git @('for-each-ref', '--format=%(refname)', $remoteRef)) {
                $behind = [int](Invoke-Git @('rev-list', '--count', "HEAD..$remoteRef"))
                if ($behind -gt 0) {
                    $ahead = [int](Invoke-Git @('rev-list', '--count', "$remoteRef..HEAD"))
                    if ($ahead -gt 0 -or (Invoke-Git @('status', '--porcelain'))) { throw 'GitHub va local co thay doi rieng. Can merge/rebase va xu ly conflict truoc khi push; khong ghi de lich su.' }
                    Invoke-Git @('merge', '--ff-only', $remoteRef) | Write-Host
                    throw 'Da cap nhat source tu GitHub. Chay lai BAT de nap ca script moi.'
                }
            }
        } catch { $syncError = $_.Exception.Message }
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = $null
    if (Test-Path -LiteralPath $vswhere) {
        $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
        if (!$msbuild) { $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1 }
    }
    if (!$msbuild) { throw 'Khong tim thay MSBuild. Can Visual Studio voi .NET Desktop Development.' }
    $project = Join-Path $repo 'TTSK Dim Plates\TTSK Dim Plates\TTSK Dim Plates.csproj'
    $stage = Join-Path $run 'build'
    New-Item -ItemType Directory -Path $stage | Out-Null
    Write-Host '[2/4] Rebuild Release x64 vao thu muc rieng...'
    # MSBuild receives Windows command-line arguments. A single trailing slash
    # before a closing quote escapes that quote, so use a double trailing slash.
    $outDirArgument = '/p:OutDir="' + $stage + '\\"'
    & $msbuild $project /t:Rebuild /p:Configuration=Release /p:Platform=x64 $outDirArgument /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build loi. Portable cu chua bi thay doi.' }
    # Only actual project runtime files; no local settings or Tekla product DLLs.
    [xml]$definition = Get-Content -LiteralPath $project -Raw
    $ns = New-Object Xml.XmlNamespaceManager($definition.NameTable)
    $ns.AddNamespace('m', $definition.DocumentElement.NamespaceURI)
    $files = @('TTSK Dim Plates.exe', 'TTSK Dim Plates.exe.config')
    foreach ($reference in $definition.SelectNodes('//m:Reference[m:HintPath]', $ns)) {
        $privateNode = $reference.SelectSingleNode('m:Private', $ns)
        if ($privateNode -and $privateNode.InnerText -eq 'False') { continue }
        $files += [IO.Path]::GetFileName($reference.SelectSingleNode('m:HintPath', $ns).InnerText)
    }
    foreach ($content in $definition.SelectNodes('//m:Content[m:CopyToOutputDirectory="PreserveNewest" or m:CopyToOutputDirectory="Always"]', $ns)) { $files += $content.GetAttribute('Include') }
    $files = @($files | Sort-Object -Unique)
    foreach ($relative in $files) {
        if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)' -or $relative -match '\.(cfg|log|pdb)$') { throw "Runtime path khong hop le: $relative" }
        if (!(Test-Path -LiteralPath (Join-Path $stage $relative) -PathType Leaf)) { throw "Build thieu runtime: $relative" }
    }
    Write-Host '[3/4] Cap nhat portable va doi chieu SHA256 tung file...'
    $portable = Join-Path $repo 'portable'
    $backup = Join-Path $run 'backup'
    $updated = New-Object 'System.Collections.Generic.List[string]'
    foreach ($relative in $files) {
        $target = Join-Path $portable $relative
        if (Test-Path -LiteralPath $target) {
            try { $handle = [IO.File]::Open($target, 'Open', 'ReadWrite', 'None'); $handle.Dispose() }
            catch { throw "File bi khoa: $target. Dong ung dung TTSK dang chay roi thu lai. Portable chua bi thay doi." }
            Copy-Verified $target (Join-Path $backup $relative)
        }
    }
    try {
        foreach ($relative in $files) {
            $updated.Add($relative)
            Copy-Verified (Join-Path $stage $relative) (Join-Path $portable $relative)
        }
        foreach ($relative in $files) {
            if ((Get-FileHash -LiteralPath (Join-Path $stage $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $portable $relative)).Hash) { throw "Portable da thay doi trong luc copy: $relative" }
        }
    } catch {
        $copyError = $_.Exception.Message
        foreach ($relative in $updated) {
            try {
                $saved = Join-Path $backup $relative
                if (Test-Path -LiteralPath $saved) { Copy-Verified $saved (Join-Path $portable $relative) }
                else { Remove-Item -LiteralPath (Join-Path $portable $relative) -Force -ErrorAction SilentlyContinue }
            } catch { Write-Warning "Khong khoi phuc duoc $relative. Backup: $backup" }
        }
        throw "Copy loi: $copyError. Da thu khoi phuc portable. Dong TTSK neu file bi khoa, roi chay lai."
    }
    Write-Host "Portable da xac minh: $($files.Count) file runtime."
    if ($BuildOnly) {
        Write-Host '[HOAN TAT] BuildOnly: portable moi tu source local; khong push GitHub.'
    } else {
        if ($syncError) { throw "Portable da cap nhat, nhung GitHub CHUA dong bo: $syncError" }
        Write-Host "[4/4] Commit va push nhanh $branch len origin..."
        Invoke-Git @('add', '--all') | Write-Host
        if (Invoke-Git @('diff', '--cached', '--name-only')) { Invoke-Git @('commit', '-m', ('Cap nhat source va portable ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))) | Write-Host }
        # Retry an earlier unpushed commit even with a clean worktree.
        Invoke-Git @('push', 'origin', "HEAD:refs/heads/$branch") 3 | Write-Host
        $head = Invoke-Git @('rev-parse', 'HEAD')
        $remote = Invoke-Git @('ls-remote', '--exit-code', 'origin', "refs/heads/$branch") 3
        if (($remote -split '\s+')[0] -ne $head) { throw 'Commit tren GitHub khong khop HEAD. Chua xac minh duoc dong bo.' }
        Write-Host "[THANH CONG] Portable da xac minh SHA256; GitHub $branch = $head"
    }
    $exitCode = 0
} catch {
    Write-Host "[THAT BAI] $($_.Exception.Message)" -ForegroundColor Red
} finally {
    Write-Host "Log: $log"
    if ($transcript) { Stop-Transcript | Out-Null }
    if ($lock) { $lock.Dispose() }
}
exit $exitCode
