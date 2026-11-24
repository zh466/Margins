param([string]$Output = (Join-Path $PSScriptRoot 'Polya.exe'), [string]$WebViewSdk = '')

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler was not found.'
}

$source = @('Art.cs', 'BoardFonts.cs', 'BoardGrid.cs', 'CanvasGestures.cs', 'CanvasPanels.cs', 'PenInput.cs', 'Catalog.cs', 'Circuits.cs', 'Core.cs', 'DiagramEdit.cs', 'Dialogs.cs', 'FileSheets.cs', 'Lesson.cs', 'Main.cs', 'Program.cs', 'RasterBlitter.cs', 'SceneTiles.cs', 'Shell.cs', 'Surface.cs', 'Toolbar.cs', 'WorkspaceLayout.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$iconFile = Join-Path $PSScriptRoot 'polya.ico'
$resources = @('Assistant-Regular.ttf','Assistant-SemiBold.ttf','SourceSans3-Regular.ttf','SourceSans3-Semibold.ttf','Assistant-OFL.txt','SourceSans3-OFL.txt') |
    ForEach-Object { '/resource:' + (Join-Path $PSScriptRoot $_) + ',Polya.Font.' + $_ }
if (-not $WebViewSdk) {
    $WebViewSdk = Join-Path $PSScriptRoot '.webview2'
    if (-not (Test-Path (Join-Path $WebViewSdk 'lib/net462/Microsoft.Web.WebView2.WinForms.dll'))) {
        $packageFile = Join-Path $PSScriptRoot '.webview2.zip'
        $sdkVersion = '1.0.3405.78'
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg" -OutFile $packageFile
        Expand-Archive -LiteralPath $packageFile -DestinationPath $WebViewSdk -Force
        Remove-Item -LiteralPath $packageFile
    }
}
$webAssemblies = @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll') |
    ForEach-Object { Join-Path $WebViewSdk "lib/net462/$_" }
$webReferences = $webAssemblies | ForEach-Object { "/r:$_" }
& $compiler /platform:x64 @webReferences /nologo /target:winexe /main:Polya.Program /optimize+ /codepage:65001 "/out:$Output" "/win32icon:$iconFile" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll @resources @source
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

$destination = Split-Path -Parent ([IO.Path]::GetFullPath($Output))
Copy-Item -LiteralPath $webAssemblies -Destination $destination
Copy-Item -LiteralPath (Join-Path $WebViewSdk 'runtimes/win-x64/native/WebView2Loader.dll') -Destination $destination
Copy-Item -LiteralPath (Join-Path $WebViewSdk 'LICENSE.txt') -Destination (Join-Path $destination 'WebView2-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $WebViewSdk 'NOTICE.txt') -Destination (Join-Path $destination 'WebView2-NOTICE.txt')
