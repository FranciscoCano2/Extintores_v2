using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Genera el APK para Meta Quest (sideload).
    ///
    /// Dos formas de usarlo:
    /// <list type="bullet">
    ///   <item>Menú <b>Extintores ▸ Build APK (Quest)</b> desde el editor abierto.</item>
    ///   <item>Sin abrir el editor: <c>tools/build-quest.ps1</c>, que invoca Unity en batchmode con
    ///         <c>-executeMethod Extintores.EditorTools.QuestBuild.BuildFromCommandLine</c>.</item>
    /// </list>
    ///
    /// Antes de compilar fuerza la configuración que el Quest necesita (ARM64, IL2CPP, APK y no
    /// AAB, un applicationIdentifier propio). Son los ajustes que, mal puestos, dan un build que
    /// termina "OK" pero que el visor rechaza al instalar o que arranca en negro.
    ///
    /// No se firma con keystore propio: sin uno, Unity usa el debug keystore, que es suficiente
    /// para <c>adb install</c>. Para publicar en la Store haría falta uno real.
    /// </summary>
    public static class QuestBuild
    {
        private const string DefaultOutput = "Build/Android/Extintores_v2.apk";

        /// <summary>El del template URP con el que nació el proyecto; no sirve como id propio.</summary>
        private const string TemplateIdFragment = "unity.template";
        private const string AppId = "com.rv.extintores";

        [MenuItem("Extintores/Build APK (Quest)", priority = 300)]
        public static void BuildFromMenu()
        {
            var report = Build(DefaultOutput, development: false);
            if (report != null && report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(Path.GetFullPath(DefaultOutput));
        }

        /// <summary>
        /// Punto de entrada para batchmode. Argumentos opcionales de la línea de comandos:
        /// <c>-outputPath &lt;ruta&gt;</c> y <c>-development</c>. Sale con código 1 si el build falla,
        /// para que el script de PowerShell lo detecte.
        /// </summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string output = ArgValue(args, "-outputPath") ?? DefaultOutput;
            bool development = args.Contains("-development");

            BuildReport report = null;
            try
            {
                report = Build(output, development);
            }
            catch (Exception e)
            {
                Debug.LogError($"[QuestBuild] Excepción durante el build: {e}");
            }

            bool ok = report != null && report.summary.result == BuildResult.Succeeded;
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static BuildReport Build(string outputPath, bool development)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[QuestBuild] No hay escenas habilitadas en Build Settings.");
                return null;
            }

            ConfigurePlayerSettings();

            string full = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = full,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            Debug.Log($"[QuestBuild] Compilando {scenes.Length} escena(s) → {full}" +
                      (development ? " (development build)" : ""));

            // versionCode = fecha y hora del build (yyMMddHHmm/10 → entra en un int): identifica qué
            // APK quedó instalado (adb shell dumpsys package com.rv.extintores | findstr version) y
            // nunca es menor que el anterior, así que se instala encima sin desinstalar. Se restaura
            // al terminar para no ensuciar ProjectSettings con un cambio por build.
            int previousCode = PlayerSettings.Android.bundleVersionCode;
            int code = (int)(long.Parse(DateTime.Now.ToString("yyMMddHHmm")) / 10); // 2610011015 no entra en int
            PlayerSettings.Android.bundleVersionCode = code;
            Debug.Log($"[QuestBuild] Versión {PlayerSettings.bundleVersion} (versionCode {code}).");

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                PlayerSettings.Android.bundleVersionCode = previousCode;
            }
            var s = report.summary;

            if (s.result == BuildResult.Succeeded)
            {
                // summary.totalSize cuenta también los intermedios (da ~1 GB): para el peso que
                // importa —el que se copia al visor— hay que mirar el archivo.
                float mb = File.Exists(full) ? new FileInfo(full).Length / (1024f * 1024f) : 0f;
                Debug.Log($"[QuestBuild] ✅ APK listo: {full} " +
                          $"({mb:0.0} MB, {s.totalTime.TotalMinutes:0.0} min)");
            }
            else
                Debug.LogError($"[QuestBuild] ❌ Build {s.result}: {s.totalErrors} error(es).");

            return report;
        }

        /// <summary>Ajustes sin los cuales el APK no corre (o no instala) en el visor.</summary>
        private static void ConfigurePlayerSettings()
        {
            var changes = new List<string>();

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                changes.Add("build target → Android");
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }

            // APK, no AAB: el .aab es para la Store, no se puede instalar con adb.
            if (EditorUserBuildSettings.buildAppBundle)
            {
                changes.Add("buildAppBundle → false (APK)");
                EditorUserBuildSettings.buildAppBundle = false;
            }

            // El Quest es ARM64; un build ARMv7 instala pero no arranca.
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                changes.Add("targetArchitectures → ARM64");
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            }

            var android = UnityEditor.Build.NamedBuildTarget.Android;
            if (PlayerSettings.GetScriptingBackend(android) != ScriptingImplementation.IL2CPP)
            {
                changes.Add("scripting backend → IL2CPP (obligatorio para ARM64)");
                PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            }

            string id = PlayerSettings.GetApplicationIdentifier(android);
            if (string.IsNullOrEmpty(id) || id.Contains(TemplateIdFragment))
            {
                changes.Add($"applicationIdentifier '{id}' → '{AppId}'");
                PlayerSettings.SetApplicationIdentifier(android, AppId);
            }

            if (changes.Count > 0)
            {
                Debug.Log("[QuestBuild] Ajustes aplicados:\n  · " + string.Join("\n  · ", changes));
                AssetDatabase.SaveAssets();
            }
        }

        private static string ArgValue(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
