# ============================================================================
# New-DesktopShortcut.ps1
# Tao shortcut (.lnk) tren Desktop tro den TTSK Dim Plates.exe trong portable.
# Su dung Windows API IShellLinkW (ho tro Unicode hoan toan) thay vi
# WScript.Shell COM (bi loi voi duong dan chua ky tu tieng Viet).
# Dam bao hoat dong tren moi may, moi tai khoan Windows.
# ============================================================================
[CmdletBinding()]
param([switch]$NoPause)

$ErrorActionPreference = 'Stop'

# --- Xac dinh duong dan goc cua project ---
$repo     = Split-Path -Parent $PSScriptRoot
$portable = Join-Path $repo 'portable'
$exe      = Join-Path $portable 'TTSK Dim Plates.exe'

# --- Ten shortcut se tao ---
$shortcutName = 'TTSK Dim Plates.lnk'

# ============================================================================
# Khai bao Windows API IShellLinkW bang C# (ho tro Unicode hoan toan)
# Cach nay KHONG bi loi Unicode nhu WScript.Shell COM
# ============================================================================
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

// CLSID cua ShellLink: 00021401-0000-0000-C000-000000000046
[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
public class ShellLinkObject { }

// IShellLinkW interface - dung Unicode (wchar_t*) cho moi duong dan
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
public interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                 int cch, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out ushort pwHotkey);
    void SetHotkey(ushort wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                         int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

// Lop tien ich de tao shortcut
public static class ShortcutCreator
{
    public static void CreateShortcut(
        string lnkPath,
        string targetExe,
        string workingDir,
        string description,
        string iconPath,
        int iconIndex)
    {
        // Tao COM object ShellLink
        var shellLink = (IShellLinkW)new ShellLinkObject();
        // Thiet lap cac thuoc tinh cua shortcut
        shellLink.SetPath(targetExe);
        shellLink.SetWorkingDirectory(workingDir);
        shellLink.SetArguments("");
        shellLink.SetDescription(description);
        shellLink.SetIconLocation(iconPath, iconIndex);
        shellLink.SetShowCmd(1); // SW_SHOWNORMAL

        // Luu file .lnk bang IPersistFile interface
        var persistFile = (IPersistFile)shellLink;
        persistFile.Save(lnkPath, true);

        // Giai phong COM object
        Marshal.FinalReleaseComObject(shellLink);
    }
}
"@ -Language CSharp

# ============================================================================
# Ham: Tim duong dan Desktop cua user hien tai (tuong thich moi may)
# ============================================================================
function Get-UserDesktopPath {
    # Cach 1: Doc tu Registry - chinh xac nhat vi day la noi Windows luu cau hinh
    try {
        $regPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
        $regValue = (Get-ItemProperty -Path $regPath -Name 'Desktop' -ErrorAction Stop).Desktop
        $expanded = [Environment]::ExpandEnvironmentVariables($regValue)
        if ($expanded -and (Test-Path -LiteralPath $expanded -PathType Container)) {
            return $expanded
        }
    } catch { }

    # Cach 2: Dung Shell Folders (da mo rong san, khong can expand)
    try {
        $regPath2 = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders'
        $regValue2 = (Get-ItemProperty -Path $regPath2 -Name 'Desktop' -ErrorAction Stop).Desktop
        if ($regValue2 -and (Test-Path -LiteralPath $regValue2 -PathType Container)) {
            return $regValue2
        }
    } catch { }

    # Cach 3: Dung .NET SpecialFolder
    $dotnetDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    if ($dotnetDesktop -and (Test-Path -LiteralPath $dotnetDesktop -PathType Container)) {
        return $dotnetDesktop
    }

    # Cach 4: Dung bien moi truong USERPROFILE (luon co tren moi Windows)
    $userProfile = $env:USERPROFILE
    if ($userProfile) {
        $desktop = Join-Path $userProfile 'Desktop'
        if (Test-Path -LiteralPath $desktop -PathType Container) {
            return $desktop
        }
    }

    return $null
}

# ============================================================================
# CHAY CHINH
# ============================================================================
try {
    Write-Host ''
    Write-Host '============================================' -ForegroundColor Cyan
    Write-Host '  TAO SHORTCUT TTSK DIM PLATES TREN DESKTOP' -ForegroundColor Cyan
    Write-Host '============================================' -ForegroundColor Cyan
    Write-Host ''

    # Kiem tra file EXE portable co ton tai khong
    if (!(Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "Khong tim thay file EXE portable:`n  $exe`nHay chay Cap_Nhat_Portable.bat truoc."
    }
    Write-Host "[OK] Tim thay EXE: $exe" -ForegroundColor Green

    # Tim Desktop cua user hien tai
    $desktopPath = Get-UserDesktopPath
    if (!$desktopPath) {
        throw "Khong the xac dinh thu muc Desktop.`nBien USERPROFILE = '$($env:USERPROFILE)'"
    }
    Write-Host "[OK] Thu muc Desktop: $desktopPath" -ForegroundColor Green

    # Duong dan file shortcut se tao
    $lnkFullPath = Join-Path $desktopPath $shortcutName
    Write-Host ''
    Write-Host "Dang tao shortcut: $lnkFullPath ..."

    # Tao shortcut bang Windows API IShellLinkW (ho tro Unicode)
    [ShortcutCreator]::CreateShortcut(
        $lnkFullPath,         # Duong dan file .lnk
        $exe,                 # File EXE can chay
        $portable,            # Thu muc lam viec
        'TTSK Auto Dimension cho Tekla Structures',  # Mo ta
        $exe,                 # Icon lay tu file EXE
        0                     # Icon index
    )

    # Kiem tra file .lnk da duoc tao thanh cong
    if (!(Test-Path -LiteralPath $lnkFullPath -PathType Leaf)) {
        throw "Khong tao duoc file shortcut tai: $lnkFullPath"
    }

    Write-Host ''
    Write-Host '[THANH CONG] Da tao shortcut thanh cong!' -ForegroundColor Green
    Write-Host "  Vi tri: $lnkFullPath" -ForegroundColor Green
    Write-Host ''
    Write-Host 'Click vao shortcut "TTSK Dim Plates" tren Desktop de chay phan mem.' -ForegroundColor White
    Write-Host ''
    exit 0

} catch {
    Write-Host ''
    Write-Host "[LOI] $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ''
    exit 1
}
