<#
.SYNOPSIS
    Instala (y opcionalmente lanza) un APK YA COMPILADO en el Meta Quest conectado por USB.

.DESCRIPTION
    No compila ni necesita Unity: sirve para llevar el .apk a otra PC (pendrive, Drive, mail) e
    instalarlo ahí. Sólo hace falta adb: usa el del PATH, o el del SDK de Unity si está instalado,
    o el que esté junto a este script (copiá la carpeta platform-tools al lado si la otra PC no
    tiene nada).

    El visor tiene que estar en modo desarrollador y hay que aceptar "Permitir depuración por USB"
    DENTRO del visor al conectarlo.

.PARAMETER Apk
    Ruta del APK. Por defecto Build/Android/Extintores_v2.apk (relativa a la raíz del repo), o el
    único .apk que haya junto al script.

.PARAMETER Run
    Además de instalar, abre la app en el visor.

.EXAMPLE
    .\tools\install-quest.ps1 -Run
.EXAMPLE
    .\install-quest.ps1 -Apk D:\Extintores_v2.apk -Run
#>
[CmdletBinding()]
param(
    [string]$Apk,
    [switch]$Run
)

$ErrorActionPreference = "Stop"
$appId = "com.rv.extintores"
$projectRoot = Split-Path -Parent $PSScriptRoot

# --- APK --------------------------------------------------------------------------------------
if (-not $Apk) {
    $inRepo = Join-Path $projectRoot "Build/Android/Extintores_v2.apk"
    $besideScript = @(Get-ChildItem -Path $PSScriptRoot -Filter *.apk -ErrorAction SilentlyContinue)
    if (Test-Path $inRepo) { $Apk = $inRepo }
    elseif ($besideScript.Count -eq 1) { $Apk = $besideScript[0].FullName }
    else { throw "No encontré el APK. Pasalo con -Apk <ruta>." }
}
if (-not (Test-Path $Apk)) { throw "No existe '$Apk'." }
$Apk = (Resolve-Path $Apk).Path
$sizeMb = [math]::Round((Get-Item $Apk).Length / 1MB, 1)

# --- adb: PATH, junto al script, o el del SDK de Unity -----------------------------------------
$adb = (Get-Command adb -ErrorAction SilentlyContinue).Source
if (-not $adb) {
    $candidates = @(
        (Join-Path $PSScriptRoot "platform-tools/adb.exe"),
        (Join-Path $PSScriptRoot "adb.exe")
    )
    $versionFile = Join-Path $projectRoot "ProjectSettings/ProjectVersion.txt"
    if (Test-Path $versionFile) {
        $line = Get-Content $versionFile | Where-Object { $_ -match "^m_EditorVersion:" } | Select-Object -First 1
        $unityVersion = ($line -split ":\s*")[1].Trim()
        $candidates += "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
    }
    $adb = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $adb) {
    throw "No encontré adb. Bajá 'SDK Platform Tools' de developer.android.com y poné la carpeta platform-tools junto a este script."
}

# --- visor --------------------------------------------------------------------------------------
$lines = & $adb devices | Select-Object -Skip 1 | Where-Object { $_.Trim() }
$devices = $lines | Where-Object { $_ -match "\sdevice$" }
if ($lines | Where-Object { $_ -match "\sunauthorized$" }) {
    throw "El Quest está conectado pero no autorizado: ponete el visor y aceptá 'Permitir depuración por USB'."
}
if (-not $devices) {
    throw "No hay ningún Quest conectado. Conectalo por USB (modo desarrollador activo) y reintentá."
}

Write-Host "Instalando $Apk ($sizeMb MB) en: $($devices -join ', ')"
& $adb install -r $Apk
if ($LASTEXITCODE -ne 0) {
    throw "adb install falló. Si dice INSTALL_FAILED_UPDATE_INCOMPATIBLE: 'adb uninstall $appId' y reintentá (se pierde la configuración guardada)."
}

$version = & $adb shell dumpsys package $appId | Select-String "versionCode|versionName" | ForEach-Object { $_.Line.Trim() }
if ($version) { Write-Host "Instalado: $($version -join ' ')" }

if ($Run) {
    & $adb shell monkey -p $appId -c android.intent.category.LAUNCHER 1 | Out-Null
    Write-Host "Lanzado en el visor. Ya podés desconectar el cable: corre solo en el Quest."
}

Write-Host "Listo. En el Quest: Biblioteca ▸ (filtro) Orígenes desconocidos ▸ Extintores_v2."
