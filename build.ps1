param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'), [switch]$RunTests, [switch]$VisualTest, [switch]$NativeReview)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'O compilador do .NET Framework 4.x não foi encontrado.' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $PSScriptRoot 'Memento.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    # A small paper fold, drawn locally; no external image or font dependencies.
    $iconStream = [IO.File]::Create($iconPath)
    $writer = [IO.BinaryWriter]::new($iconStream)
    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $frames = @()
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $g = [Drawing.Graphics]::FromImage($bitmap)
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([Drawing.Color]::Transparent)
        $scale = $size / 64.0
        $g.ScaleTransform($scale, $scale)
        $paper = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F5D96A'))
        $fold = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#D5B442'))
        $ink = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#3C3423'), 3)
        $points = [Drawing.PointF[]]@([Drawing.PointF]::new(9,6),[Drawing.PointF]::new(55,6),[Drawing.PointF]::new(55,44),[Drawing.PointF]::new(41,58),[Drawing.PointF]::new(9,58))
        $g.FillPolygon($paper,$points)
        $g.FillPolygon($fold,[Drawing.PointF[]]@([Drawing.PointF]::new(41,44),[Drawing.PointF]::new(55,44),[Drawing.PointF]::new(41,58)))
        $g.DrawLine($ink,19,21,44,21); $g.DrawLine($ink,19,30,39,30)
        $memory = [IO.MemoryStream]::new()
        $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
        $frames += ,$memory.ToArray()
        $memory.Dispose(); $ink.Dispose(); $fold.Dispose(); $paper.Dispose(); $g.Dispose(); $bitmap.Dispose()
    }
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    $writer.Dispose(); $iconStream.Dispose()
}
$shared = @((Join-Path $PSScriptRoot 'Shared.cs'))
$appSources = @('Shared.cs','Models.cs','GridLayout.cs','App.cs','NoteForm.cs','DesktopSupport.cs','ThemeSettingsForm.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$common = @('/nologo','/optimize+','/platform:anycpu','/codepage:65001',('/win32icon:' + $iconPath),('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),('/resource:' + $iconPath + ',Memento.ico'),'/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll')
$appArguments = $common + @('/target:winexe',('/out:' + (Join-Path $out 'Memento.exe')),'/r:System.Web.Extensions.dll') + $appSources
& $compiler @appArguments
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o aplicativo.' }
foreach ($file in @('Memento.exe.config','Memento.ico','LEIA-ME.txt','LICENSE.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $out $file) -Force }
$resources = @('Memento.exe','Memento.exe.config','Memento.ico','LEIA-ME.txt','LICENSE.txt') | ForEach-Object { '/resource:' + (Join-Path $out $_) + ',Payload.' + $_ }
$installerArguments = $common + @('/target:winexe',('/out:' + (Join-Path $out 'Memento-Instalar.exe')),'/r:System.Web.Extensions.dll') + $resources + $shared + @((Join-Path $PSScriptRoot 'Models.cs'),(Join-Path $PSScriptRoot 'GridLayout.cs'),(Join-Path $PSScriptRoot 'Installer.cs'))
& $compiler @installerArguments
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }
if ($RunTests) {
    $testArguments = $common + @('/target:exe','/main:Memento.Tests',('/out:' + (Join-Path $out 'Memento.Tests.exe')),'/r:System.Web.Extensions.dll') + $appSources + @((Join-Path $PSScriptRoot 'Tests.cs'))
    & $compiler @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar os testes.' }
    & (Join-Path $out 'Memento.Tests.exe') (Join-Path $out 'test-data')
    if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam.' }
}
if ($VisualTest) {
    $visualArguments = $common + @('/target:winexe','/define:VISUALTEST','/main:Memento.VisualTest',('/out:' + (Join-Path $out 'Memento.VisualTest.exe')),'/r:System.Web.Extensions.dll') + $appSources + @((Join-Path $PSScriptRoot 'VisualTest.cs'))
    & $compiler @visualArguments
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a revisão visual.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Memento.exe.config') -Destination (Join-Path $out 'Memento.VisualTest.exe.config') -Force
}
if ($NativeReview) {
    $reviewArguments = $common + @('/target:winexe','/main:Memento.NativeReview',('/out:' + (Join-Path $out 'Memento.NativeReview.exe')),'/r:System.Web.Extensions.dll') + $appSources + @((Join-Path $PSScriptRoot 'NativeReview.cs'))
    & $compiler @reviewArguments
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a conferência nativa.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Memento.exe.config') -Destination (Join-Path $out 'Memento.NativeReview.exe.config') -Force
}
Write-Output ('Pronto: ' + (Join-Path $out 'Memento-Instalar.exe'))
