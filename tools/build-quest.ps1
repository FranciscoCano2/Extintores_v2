<#
.SYNOPSIS
    Compila el APK para Meta Quest y, opcionalmente, lo instala en el visor por USB.

.DESCRIPTION
    Corre Unity en batchmode con Extintores.EditorTools.QuestBuild.BuildFromCommandLine.
    No hace falta tener el editor abierto — de hecho NO puede estarlo: Unity bloquea el proyecto
    (Temp/UnityLockfile) y el batchmode fallaría.

    El APK se firma con el debug keystore de Unity, suficiente para sideload por adb.

.PARAMETER Install
    Tras compilar, instala el APK en el Quest conectado (adb install -r).

.PARAMETER Run
    Implica -Install: además lanza la app en el visor.

.PARAMETER Development
    Development build (profiler + debugging remoto). Anda más lento; no usar para la entrega.

.PARAMETER Output
    Ruta del APK, relativa a la raíz del proyecto. Por defecto Build/Android/Extintores_v2.apk

.EXAMPLE
    .\tools\build-quest.ps1
.EXAMPLE
    .\tools\build-quest.ps1 -Run
#>
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Run,
    [switch]$Development,
    [string]$Output = "Build/Android/Extintores_v2.apk"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$appId = "com.rv.extintores"

# --- Unity: la versión exacta del proyecto, no "la que haya" -------------------------------
$versionLine = Get-Content (Join-Path $projectRoot "ProjectSettings/ProjectVersion.txt") |
    Where-Object { $_ -match "^m_EditorVersion:" } | Select-Object -First 1
$unityVersion = ($versionLine -split ":\s*")[1].Trim()
$unityExe = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"

if (-not (Test-Path $unityExe)) {
    throw "No encontré Unity $unityVersion en '$unityExe'. Instalalo desde Unity Hub."
}

# Un editor abierto sobre el proyecto deja el batchmode esperando el lock y muriendo por timeout.
$lockfile = Join-Path $projectRoot "Temp/UnityLockfile"
if (Test-Path $lockfile) {
    try {
        $fs = [System.IO.File]::Open($lockfile, 'Open', 'ReadWrite', 'None')
        $fs.Close()
    } catch {
        throw "El proyecto está abierto en el editor de Unity. Cerralo y volvé a correr este script."
    }
}

$logPath = Join-Path $projectRoot "Logs/build-quest.log"
New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null

# Ojo: Start-Process une los elementos de -ArgumentList con espacios SIN citarlos. La ruta del
# proyecto tiene espacios ("10mo semestre", "Proyecto Final"), así que Unity la recibía partida en
# pedazos y abortaba con "Couldn't set project path". Hay que citar a mano lo que lleve espacios.
$q = { param($s) '"' + $s + '"' }
$unityArgs = @(
    "-quit", "-batchmode", "-nographics",
    "-projectPath", (& $q $projectRoot),
    "-buildTarget", "Android",
    "-executeMethod", "Extintores.EditorTools.QuestBuild.BuildFromCommandLine",
    "-outputPath", (& $q $Output),
    "-logFile", (& $q $logPath)
)
if ($Development) { $unityArgs += "-development" }

Write-Host "Compilando con Unity $unityVersion (la primera build compila shaders + IL2CPP: puede tardar 15-30 min)..."
Write-Host "Log: $logPath"

$proc = Start-Process -FilePath $unityExe -ArgumentList $unityArgs -PassThru -Wait -NoNewWindow
if ($proc.ExitCode -ne 0) {
    Write-Host "`n--- últimas líneas del log ---"
    Get-Content $logPath -Tail 40
    throw "El build falló (exit $($proc.ExitCode)). Log completo en $logPath"
}

$apk = Join-Path $projectRoot $Output
if (-not (Test-Path $apk)) { throw "Unity terminó OK pero no encuentro el APK en '$apk'." }
$sizeMb = [math]::Round((Get-Item $apk).Length / 1MB, 1)
Write-Host "`nAPK listo: $apk ($sizeMb MB)"

if (-not ($Install -or $Run)) {
    Write-Host "Para instalarlo en el visor: .\tools\install-quest.ps1 -Run   (sirve también en otra PC, sin Unity)"
    exit 0
}

# La instalación vive en install-quest.ps1, que también se usa solo para llevar el APK a otra PC.
& (Join-Path $PSScriptRoot "install-quest.ps1") -Apk $apk -Run:$Run
