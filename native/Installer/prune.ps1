# Deletes the parts of the self-contained Windows App SDK output that MovieShelf never loads (on-device AI, search,
# widgets, debugger helpers, translations of the framework strings), so the installer stays small.
# Usage: prune.ps1 <publish folder>
param([Parameter(Mandatory)][string]$Dir)
$ErrorActionPreference = 'Stop'

$remove = @(
    '*.pdb',
    'onnxruntime.dll', 'DirectML.dll', 'Microsoft.ML.OnnxRuntime.dll', 'NPUDetect.dll', 'PerceptiveStreaming.dll',
    'Microsoft.Asg.*', 'Microsoft.Windows.AI.*', 'Microsoft.Windows.Internal.*', 'Microsoft.Windows.Workloads.*', 'workloads*.json',
    'Microsoft.Windows.Search.*', 'Microsoft.Windows.SemanticSearch*', 'Microsoft.Windows.Widgets.*',
    'Microsoft.Windows.Vision*', 'Microsoft.Windows.ImageCreationInternal.winmd', 'Microsoft.Windows.Private*',
    'Microsoft.Graphics.Imaging*', 'Microsoft.Graphics.Internal.Imaging.winmd', 'Microsoft.Graphics.ImagingInternal*',
    'mscordaccore*', 'mscordbi.dll', 'createdump.exe', 'clrgcexp.dll', 'RestartAgent.exe', 'msquic.dll',
    'Microsoft.VisualBasic*.dll', 'Microsoft.Web.WebView2.Core*.dll', 'WebView2Loader.dll',
    # .NET libraries nothing in MovieShelf (or the libraries it uses) calls: XML, data contracts, QUIC, tensors, mail, tar
    'System.Private.Xml*.dll', 'System.Xml*.dll', 'System.Private.DataContractSerialization.dll', 'System.Runtime.Serialization*.dll',
    'System.Net.Quic.dll', 'System.Numerics.Tensors.dll', 'System.Net.Mail.dll', 'System.Net.HttpListener.dll',
    'System.Formats.Tar.dll', 'System.Net.ServerSentEvents.dll', 'System.ServiceModel.Web.dll'
)
foreach ($pattern in $remove) {
    Get-ChildItem -LiteralPath $Dir -File -Filter $pattern | Remove-Item -Force
}

# The framework's own UI strings: keep English and Persian, drop the other ~90 languages.
$keep = 'Assets', 'Microsoft.UI.Xaml', 'en-us', 'fa-IR'
Get-ChildItem -LiteralPath $Dir -Directory | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Recurse -Force
