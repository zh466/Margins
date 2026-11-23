param([string]$Output = (Join-Path $PSScriptRoot 'Polya.exe'))

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler was not found.'
}

$source = @('Art.cs', 'BoardFonts.cs', 'BoardGrid.cs', 'Catalog.cs', 'Circuits.cs', 'Core.cs', 'DiagramEdit.cs', 'Dialogs.cs', 'FileSheets.cs', 'Lesson.cs', 'Main.cs', 'Program.cs', 'RasterBlitter.cs', 'SceneTiles.cs', 'Shell.cs', 'Surface.cs', 'Toolbar.cs', 'WorkspaceLayout.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$iconFile = Join-Path $PSScriptRoot 'polya.ico'
$resources = @('Assistant-Regular.ttf','Assistant-SemiBold.ttf','SourceSans3-Regular.ttf','SourceSans3-Semibold.ttf','Assistant-OFL.txt','SourceSans3-OFL.txt') |
    ForEach-Object { '/resource:' + (Join-Path $PSScriptRoot $_) + ',Polya.Font.' + $_ }
& $compiler /nologo /target:winexe /main:Polya.Program /optimize+ /codepage:65001 "/out:$Output" "/win32icon:$iconFile" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll @resources @source
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}
