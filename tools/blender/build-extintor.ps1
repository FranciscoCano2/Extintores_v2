# Genera el extintor de 1 kg (FBX para Unity + STL para imprimir) con Blender en background.
# Uso, desde la raíz del repo:
#   powershell -ExecutionPolicy Bypass -File tools\blender\build-extintor.ps1 [-Render] [-Blender <ruta>]
# -Render deja vistas frente/costado/atrás/arriba en Logs\blender-render para compararlas con las fotos.
param(
    [switch]$Render,
    [string]$Blender = 'C:\Program Files\Blender Foundation\Blender 4.5\blender.exe'
)
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'extintor_1kg.py'
if (-not (Test-Path $Blender)) { throw "No encuentro Blender en '$Blender' (pasalo con -Blender)." }
$extra = @()
if ($Render) { $extra = @('--', '--render', (Join-Path $PSScriptRoot '..\..\Logs\blender-render')) }
& $Blender -b --factory-startup --python $script @extra 2>&1 | ForEach-Object {
    if ($_ -match '\[extintor_1kg\]|Error|Traceback') { $_ }
}
if ($LASTEXITCODE -ne 0) { throw "Blender terminó con código $LASTEXITCODE" }
