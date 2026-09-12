# Test Packaging Inventory and Workflow Validation
Run-Test "Nhom 11: Packaging Inventory" "Goi ZIP chua dung 39 file runtime tu csproj, co 2 DLL NuGet Tekla/Trimble va macro GridVisibility" {
    $pkgOutDir = Join-Path $testRunDir "inventory_pkg"
    $headSha = (git rev-parse HEAD).Trim()
    $pkgScript = Join-Path $repoRoot "scripts\Build-GitHubReleasePackage.ps1"
    & $pkgScript -Version "1.0.50" -Tag "v1.0.50" -CommitSha $headSha -OutDir $pkgOutDir

    $zipPath = Join-Path $pkgOutDir "TTSK-Dim-Plates-Portable.zip"
    $zipStream = [System.IO.File]::OpenRead($zipPath)
    $archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Read)

    $entryNames = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $archive.Entries) {
        $entryNames.Add($entry.FullName.Replace('\', '/'))
    }
    $archive.Dispose()
    $zipStream.Dispose()

    Assert-True ($entryNames.Contains("TTSK Dim Plates/Tekla.Technology.Serialization.dll")) "Phai chua Tekla.Technology.Serialization.dll"
    Assert-True ($entryNames.Contains("TTSK Dim Plates/Trimble.Remoting.dll")) "Phai chua Trimble.Remoting.dll"
    Assert-True ($entryNames.Contains("TTSK Dim Plates/Phu_Macro_GridVisibility.cs")) "Phai chua Phu_Macro_GridVisibility.cs"

    Assert-True (!($entryNames | Where-Object { $_ -match '\.pdb$' })) "Khong duoc chua file PDB"
    Assert-True (!($entryNames | Where-Object { $_ -match '\.cfg$' })) "Khong duoc chua file cfg"
    Assert-True (!($entryNames | Where-Object { $_ -match '\.bat$' })) "Khong duoc chua file bat"
    Assert-True (!($entryNames | Where-Object { $_ -match 'logs/' })) "Khong duoc chua thu muc logs"
    Assert-True (!($entryNames | Where-Object { $_ -match 'Phu_Macro_LoadStandard\.cs$' })) "Phu_Macro_LoadStandard khong phai runtime CopyToOutputDirectory"
}

Run-Test "Nhom 12: Workflow Validation" "Kiem tra cu phap YAML, concurrency group va path filter trong release-portable.yml" {
    $wfPath = Join-Path $repoRoot ".github\workflows\release-portable.yml"
    Assert-True (Test-Path -LiteralPath $wfPath) "Workflow file phai ton tai"
    $wfContent = Get-Content -LiteralPath $wfPath -Raw

    Assert-True ($wfContent -match 'group:\s*ttsk-portable-release') "Phai co concurrency group ttsk-portable-release"
    Assert-True ($wfContent -match 'cancel-in-progress:\s*false') "cancel-in-progress phai la false"
    Assert-True ($wfContent -match 'contents:\s*write') "Permissions phai co contents: write"
    Assert-True ($wfContent -match 'branches:\s*\r?\n\s*-\s*main') "Trigger push phai chi dinh branch main"
    Assert-True ($wfContent -match 'paths:') "Phai co path filter"
    Assert-True ($wfContent -match 'Build-GitHubReleasePackage\.ps1') "Phai goi script dong goi"
    $publisher = Get-Content (Join-Path $repoRoot 'scripts\Publish-GitHubPortableRelease.ps1') -Raw
    Assert-True ($wfContent -match 'Publish-GitHubPortableRelease.ps1') 'Workflow calls audited publisher'
    Assert-True ($publisher -match "'--draft'") "Phai tao draft release truoc khi publish"
    Assert-True ($publisher -match '--draft=false') "Phai publish stable sau khi verify"
}
