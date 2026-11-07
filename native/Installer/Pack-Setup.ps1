# Glues the setup file together: setup program + tools.zip + payload.7z + a 64-byte footer (see SetupFile in src\Core.cs):
# tools length (8), payload length (8), unpacked size (8), SHA-256 of tools and payload (32), "MDBSETU2" (8).
param(
    [Parameter(Mandatory)][string]$Stub, [Parameter(Mandatory)][string]$Tools, [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$App, [Parameter(Mandatory)][string]$Out
)
$ErrorActionPreference = 'Stop'

$sha = [Security.Cryptography.SHA256]::Create()
$target = [IO.File]::Create($Out)
try {
    $in = [IO.File]::OpenRead((Resolve-Path $Stub).Path)
    try { $in.CopyTo($target) } finally { $in.Dispose() }

    $buffer = New-Object byte[] (1MB)
    foreach ($f in $Tools, $Payload) {
        $in = [IO.File]::OpenRead((Resolve-Path $f).Path)
        try {
            while (($n = $in.Read($buffer, 0, $buffer.Length)) -gt 0) {
                [void]$sha.TransformBlock($buffer, 0, $n, $null, 0)
                $target.Write($buffer, 0, $n)
            }
        } finally { $in.Dispose() }
    }
    [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)

    $unpacked = (Get-ChildItem -LiteralPath $App -Recurse -File | Measure-Object Length -Sum).Sum
    $w = New-Object IO.BinaryWriter($target)
    $w.Write([int64](Get-Item $Tools).Length)
    $w.Write([int64](Get-Item $Payload).Length)
    $w.Write([int64]$unpacked)
    $w.Write($sha.Hash)
    $w.Write([Text.Encoding]::ASCII.GetBytes('MDBSETU2'))
    $w.Flush()
} finally { $target.Dispose() }
