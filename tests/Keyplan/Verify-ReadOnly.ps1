$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$production = Join-Path $repoRoot 'TTSK Dim Plates/TTSK Dim Plates'
$core = [IO.File]::ReadAllText((Join-Path $production 'PHU_Keyplan.cs'))
function Read-KeyplanSource([string]$name) {
    if ($name -match '^PHU_Keyplan(Geometry|Shape|Reader|Properties|Apply|Preferences)\.cs$') {
        $section = [regex]::Match($core, '(?s)#region ' + $Matches[1] + '\r?\n(.*?)#endregion')
        if (!$section.Success) {throw "Missing consolidated section: $name"}
        return $section.Groups[1].Value
    }
    return [IO.File]::ReadAllText((Join-Path $production $name))
}
$forbidden = '\.(Modify|Insert|Delete|CommitChanges|SetCurrentTransformationPlane|SaveActiveDrawing|CloseActiveDrawing|SetActiveDrawing|SetUserProperty|SetAdvancedOption)\s*\('
foreach ($name in @('PHU_KeyplanReader.cs','PHU_KeyplanShape.cs','PHU_KeyplanProperties.cs','PHU_KeyplanGeometry.cs','MainForm.Keyplan.cs','PHU_MainPartResolver.cs')) {
    $source = Read-KeyplanSource $name
    if ($source -match $forbidden) {throw "Model/drawing mutation in slot 09 read path: $name"}
}
$properties = Read-KeyplanSource 'PHU_KeyplanProperties.cs'
if ($properties -match 'Button\([^\r\n]*"(Get|Apply|Modify|Save|OK)"' -or $properties -match 'File\.(Read|Write)|ValueChange|LoadAttributeFile') {throw 'Unsafe original-property read path'}
$project = [IO.File]::ReadAllText((Join-Path $production 'TTSK Dim Plates.csproj'))
if ($project -match 'TeklaExperiment|tests[/\\]Keyplan') {throw 'Mutation harness included in production project'}
$writer = Read-KeyplanSource 'PHU_KeyplanApply.cs'
if ($writer -match $forbidden -or $writer -match 'Tekla\.Structures\.Model|new\s+M\.|File\.(Write|Delete)|LoadAttributeFile|SaveAttributeFile|ValueChange') {throw 'Unexpected API/model/preset mutation in keyplan writer'}
if ($properties -notmatch 'return WithOwnedDialog\(null\)' -or $writer -notmatch 'if\(previewOnly\)return;' -or $writer -notmatch 'identityGuard\(\);') {throw 'Read/preview/identity boundary missing'}
if ($writer.IndexOf('if(requiresRecreate&&!authorized)') -lt 0 -or $writer.IndexOf('if(requiresRecreate&&!authorized)') -gt $writer.IndexOf('submitted();')) {throw 'Mandatory recreation confirmation is missing or occurs after Modify'}
if ($writer -notmatch 'NotifyControl\(h,9\)' -or $writer -notmatch 'ReloadFromDrawing\(process,dialog,identityGuard\)') {throw 'Committed native combo value or authoritative same-dialog reload missing'}
$entry = [IO.File]::ReadAllText((Join-Path $production 'MainForm.Keyplan.cs'))
foreach ($sectionName in @('Geometry','Shape','Reader','Properties','Apply')) {
    $sectionSource = Read-KeyplanSource ('PHU_Keyplan' + $sectionName + '.cs')
    if ($sectionSource -match 'File\.(Write|Append|Create|Copy|Move)|Directory\.Create|RunMacro') {throw "Filesystem/macro output in algorithm: $sectionName"}
}
if ($entry -notmatch 'new KeyplanPreferences\(KeyplanPreferences.DefaultPath\)' -or (Read-KeyplanSource 'PHU_KeyplanPreferences.cs') -notmatch 'SpecialFolder.LocalApplicationData') {throw 'Preference writes must use user LocalAppData'}
if ($entry -match 'Reader\.Capture\(' -or $entry -notmatch 'KeyplanApply\.Run\(request\)') {throw 'MainForm must use one unified run, not open a separate reader dialog'}
Write-Output 'PASS slot 09 read path has no drawing/model mutation; separate native writer has no Model API writes, direct object mutation or preset writes; forced native recreation requires confirmation before Modify; test harness excluded.'
