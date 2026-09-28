$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GoogleDrive-ReleaseApi.ps1')
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ttsk-auth-test-' + [guid]::NewGuid().ToString('N'))
$testCredential = Join-Path $testDirectory 'fake-oauth.xml'
$previousOverride = $env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN
try {
    $env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN = $null
    Save-DriveCredential 'fake-client' 'fake-secret' 'fake-refresh' $testCredential
    function Get-DriveCredentialPath { return $testCredential }
    $script:testMode = 'success'
    $script:requests = 0
    function Invoke-RestMethod {
        $script:requests++
        if ($script:testMode -eq 'success') { return [pscustomobject]@{access_token='fake-access';expires_in=3600} }
        $record = New-Object Management.Automation.ErrorRecord ([InvalidOperationException]::new('Fake request failed')), 'OAuthTest', 'InvalidOperation', $null
        $record.ErrorDetails = New-Object Management.Automation.ErrorDetails ('{"error":"' + $script:testMode + '"}')
        throw $record
    }
    if ((Get-DriveAccessToken) -ne 'fake-access') { throw 'Token refresh failed.' }
    $null = Get-DriveAccessToken
    if ($script:requests -ne 1) { throw 'Token cache failed.' }
    foreach ($mode in @('invalid_grant','invalid_client','network_error')) {
        $script:TtskDriveToken = $null
        $script:testMode = $mode
        $caught = $false
        try { Connect-DriveForSourceBackup -NonInteractive }
        catch {
            $caught = $true
            if (($_.Exception.Data['DriveOAuthError'] -eq 'invalid_grant') -ne ($mode -eq 'invalid_grant')) { throw 'Wrong OAuth classification.' }
            if ($_.Exception.Message -match 'fake-secret|fake-refresh|fake-access') { throw 'Sensitive data exposed.' }
        }
        if (!$caught) { throw "Expected failure for $mode." }
    }
    foreach ($file in @('GoogleDrive-ReleaseApi.ps1','Connect-GoogleDrivePublisher.ps1','Backup-Source.ps1')) {
        $tokens=$null; $errors=$null
        $null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors)
        if ($errors.Count) { throw "Syntax errors in $file." }
    }
    Write-Host 'PASS: refresh, cache, invalid_grant, invalid_client, network error, noninteractive behavior, safe diagnostics and syntax.'
} finally {
    $env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN = $previousOverride
    if (Test-Path -LiteralPath $testCredential) { Remove-Item -LiteralPath $testCredential -Force }
    if (Test-Path -LiteralPath $testDirectory) { Remove-Item -LiteralPath $testDirectory }
}
