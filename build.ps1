# Compila LHInstaller usando il compilatore C# che Windows ha gia' dentro di se',
# in C:\Windows\Microsoft.NET. Non servono Visual Studio ne' l'SDK di .NET:
# lo stesso PC appena formattato che LHInstaller serve a ripopolare e' anche in
# grado di ricompilarlo.
#
#   .\build.ps1            compila in dist\
#   .\build.ps1 -Run       compila e avvia
#   .\build.ps1 -Clean     svuota dist\ e ricompila

param(
    [switch]$Run,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$exe  = Join-Path $dist 'LHInstaller.exe'
$ico  = Join-Path $root 'app.ico'

# --- l'icona dell'eseguibile ----------------------------------------------------
# Lo stesso disegno di Icons.DrawAppBitmap in UI\Theme.cs: rettangolo in colore
# accento con la freccia di scaricamento. Generata qui, cosi' non c'e' un file
# binario da tenere allineato a mano.
function New-AppIcon([string]$path) {
    Add-Type -AssemblyName System.Drawing
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $blobs = @()
    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        $r = $s * 0.22
        $d = $r * 2
        $path2 = New-Object System.Drawing.Drawing2D.GraphicsPath
        $rect = New-Object System.Drawing.RectangleF 0, 0, ($s - 1), ($s - 1)
        $path2.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $path2.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $path2.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $path2.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $path2.CloseFigure()
        $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 103, 192))
        $g.FillPath($bg, $path2)

        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, $s * 0.1))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $w = [float]$s
        $g.DrawLine($pen, $w * 0.5, $w * 0.22, $w * 0.5, $w * 0.6)
        $g.DrawLine($pen, $w * 0.32, $w * 0.44, $w * 0.5, $w * 0.62)
        $g.DrawLine($pen, $w * 0.68, $w * 0.44, $w * 0.5, $w * 0.62)
        $g.DrawLine($pen, $w * 0.24, $w * 0.66, $w * 0.24, $w * 0.78)
        $g.DrawLine($pen, $w * 0.24, $w * 0.78, $w * 0.76, $w * 0.78)
        $g.DrawLine($pen, $w * 0.76, $w * 0.78, $w * 0.76, $w * 0.66)

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $blobs += ,@{ Size = $s; Bytes = $ms.ToArray() }
        $ms.Dispose(); $g.Dispose(); $bmp.Dispose(); $pen.Dispose(); $bg.Dispose(); $path2.Dispose()
    }

    # Formato ICO: intestazione, una voce per immagine, poi i PNG uno dietro l'altro.
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$blobs.Count)
    $offset = 6 + 16 * $blobs.Count
    foreach ($b in $blobs) {
        $dim = if ($b.Size -ge 256) { 0 } else { $b.Size }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$b.Bytes.Length); $bw.Write([UInt32]$offset)
        $offset += $b.Bytes.Length
    }
    foreach ($b in $blobs) { $bw.Write($b.Bytes) }
    $bw.Flush(); $bw.Dispose(); $fs.Dispose()
}

# --- il compilatore ------------------------------------------------------------
$fwDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $fwDir)) {
    $fwDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$csc = Join-Path $fwDir 'csc.exe'
if (-not (Test-Path $csc)) {
    throw "csc.exe non trovato. Manca .NET Framework 4.x, cosa insolita su Windows 10/11."
}

# --- le librerie di riferimento ------------------------------------------------
$refDirs = @(
    "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
    "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2",
    $fwDir
) | Where-Object { $_ -and (Test-Path $_) }

$needed = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Web.Extensions.dll'
)

$refs = @()
foreach ($dll in $needed) {
    $found = $null
    foreach ($dir in $refDirs) {
        $candidate = Join-Path $dir $dll
        if (Test-Path $candidate) { $found = $candidate; break }
    }
    if (-not $found) { throw "Libreria di riferimento non trovata: $dll" }
    $refs += "/reference:$found"
}

# --- i sorgenti ----------------------------------------------------------------
$sources = @()
$sources += Get-ChildItem -Path $root -Filter '*.cs' -File | ForEach-Object { $_.FullName }
foreach ($sub in @('Core', 'UI')) {
    $dir = Join-Path $root $sub
    if (Test-Path $dir) {
        $sources += Get-ChildItem -Path $dir -Filter '*.cs' -File -Recurse | ForEach-Object { $_.FullName }
    }
}
if ($sources.Count -eq 0) { throw "Nessun sorgente .cs trovato in $root" }

# I sorgenti devono restare in puro ASCII: il compilatore in dotazione legge con la
# codepage di sistema, e un carattere accentato in un posto sbagliato compila male
# senza dirlo. Meglio fermarsi subito.
foreach ($src in $sources) {
    $bytes = [System.IO.File]::ReadAllBytes($src)
    $bad = 0
    foreach ($b in $bytes) { if ($b -gt 127) { $bad++ } }
    if ($bad -gt 0) { throw "Caratteri non ASCII in $src ($bad byte): scrivili come \uXXXX." }
}

# --- preparo dist\ -------------------------------------------------------------
if ($Clean -and (Test-Path $dist)) { Remove-Item $dist -Recurse -Force }
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

if (-not (Test-Path $ico)) {
    Write-Host "Genero l'icona..."
    New-AppIcon $ico
}

Write-Host "Compilatore : $csc"
Write-Host "Sorgenti    : $($sources.Count) file"
Write-Host "Uscita      : $exe"
Write-Host ""

$manifest = Join-Path $root 'app.manifest'
$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:3',
    '/codepage:65001',
    "/out:$exe",
    "/win32manifest:$manifest",
    "/win32icon:$ico"
) + $refs + $sources

& $csc $cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compilazione fallita (codice $LASTEXITCODE)."
}

# Le istruzioni viaggiano con l'eseguibile, in entrambe le lingue: su una chiavetta
# servono piu' che qui.
foreach ($doc in @('LEGGIMI.txt', 'README.txt')) {
    $src = Join-Path $root $doc
    if (Test-Path $src) { Copy-Item $src $dist -Force }
}

$size = [math]::Round((Get-Item $exe).Length / 1KB, 1)
Write-Host ""
Write-Host "Fatto: $exe  ($size KB)" -ForegroundColor Green
Write-Host "Per usarlo su un PC formattato basta copiare la cartella dist\ su una chiavetta."

if ($Run) { Start-Process $exe }
