using System.Collections.Generic;
using UnityEditor;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Verifica (y opcionalmente prende) las features de OpenXR sin las que el juego no anda.
    ///
    /// Existe porque el <c>OpenXRPackageSettings.asset</c> se regeneró solo (10/2026: los features de
    /// PC con fileIDs nuevos) y quedó <b>Meta XR Feature apagado en Standalone</b>. Sin él OVRPlugin no
    /// se engancha a la sesión OpenXR por Link: el rig no recibe la pose de la cabeza, la cámara se
    /// queda en el origen (ojos al ras del piso) y la escena "se mueve con el visor". Ningún error en
    /// consola. En Android faltan los perfiles Detached → los anchors detached no reciben pose.
    ///
    /// Sólo PRENDE lo que falta; nunca apaga nada. Las features se buscan por nombre de tipo para no
    /// referenciar el assembly del SDK de Meta desde acá.
    /// </summary>
    internal static class OpenXrFeatureCheck
    {
        private static readonly (BuildTargetGroup group, string platform, string[] features)[] Required =
        {
            (BuildTargetGroup.Standalone, "PC (Link)", new[] { "MetaXRFeature" }),
            (BuildTargetGroup.Android, "Android", new[]
            {
                "MetaXRFeature",
                "DetachedOculusTouchControllerProfile",
                "DetachedMetaQuestTouchPlusControllerProfile",
                "DetachedMetaQuestTouchProControllerProfile",
            }),
        };

        /// <summary>Una línea por feature revisada; <c>ok == false</c> = problema sin resolver.</summary>
        internal static List<(bool ok, bool fixedNow, string msg)> Ensure(bool apply)
        {
            var result = new List<(bool, bool, string)>();
            foreach (var (group, platform, names) in Required)
            {
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (settings == null)
                {
                    result.Add((false, false, $"OpenXR no está configurado para {platform} " +
                                              "(Project Settings ▸ XR Plug-in Management ▸ OpenXR)."));
                    continue;
                }

                foreach (var name in names)
                {
                    OpenXRFeature feature = null;
                    foreach (var f in settings.GetFeatures())
                        if (f != null && f.GetType().Name == name) { feature = f; break; }

                    if (feature == null)
                        result.Add((false, false, $"OpenXR {platform}: no existe la feature '{name}' (¿cambió el SDK?)."));
                    else if (feature.enabled)
                        result.Add((true, false, $"OpenXR {platform}: '{name}' habilitado."));
                    else if (!apply)
                        result.Add((false, false, $"OpenXR {platform}: '{name}' está APAGADO."));
                    else
                    {
                        Undo.RecordObject(feature, "Enable OpenXR feature");
                        feature.enabled = true;
                        EditorUtility.SetDirty(feature);
                        EditorUtility.SetDirty(settings);
                        result.Add((true, true, $"OpenXR {platform}: '{name}' habilitado."));
                    }
                }
            }

            if (apply) AssetDatabase.SaveAssets();
            return result;
        }
    }
}
