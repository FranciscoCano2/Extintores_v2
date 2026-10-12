using System;
using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Preferencias del usuario que se ajustan desde el menú de configuración
    /// (<see cref="SettingsMenu"/>) y sobreviven entre sesiones (PlayerPrefs).
    ///
    /// Estático a propósito: lo consumen piezas que no se conocen entre sí (los mounts del tanque y
    /// de la boquilla, cinco lugares que hacen vibrar el mando, el AudioListener) y cablear una
    /// referencia a cada una sería otro punto donde la escena puede fallar en silencio.
    ///
    /// <b>Calibración del prop físico.</b> El mando va atado al extintor real con una abrazadera: su
    /// orientación respecto del tanque es FIJA (la da la abrazadera y queda en el
    /// <c>rotationOffset</c> del <see cref="ExtinguisherMount"/>), pero la abrazadera se puede correr
    /// a lo largo del tanque. Lo que más cambia de un montaje a otro es entonces esa altura, y lo
    /// mismo para la boquilla sobre el mando izquierdo: un deslizamiento sobre su propio eje. Encima
    /// de eso, tanque y boquilla tienen cada uno un desplazamiento transversal y una rotación en los
    /// tres ejes, para corregir lo que la abrazadera no deja donde el modelo supone.
    /// </summary>
    public static class GameSettings
    {
        // Rangos y pasos, en las unidades en las que se muestran.
        public const float VolumeStep = 0.1f;
        public const float HeightMin = -0.25f, HeightMax = 0.25f;   // m
        public const float NozzleMin = -0.15f, NozzleMax = 0.15f;   // m
        public const float LengthStep = 0.01f;                      // 1 cm
        public const float ShiftMax = 0.15f;                        // m, desplazamiento transversal
        public const float RotationStep = 5f;                       // grados
        public const float RotationMax = 90f;                       // grados, a cada lado
        public const float PanelStep = 0.05f;                       // 5 cm
        public const float PanelLateralMax = 0.8f;                  // m, a cada lado
        public const float PanelHeightMax = 0.5f;                   // m, arriba y abajo
        public const float PanelDistanceMax = 0.8f;                 // m, más cerca y más lejos

        // Calibración de fábrica del extintor: la que se midió con el prop real montado (prueba del
        // 01/10/2026). "Restablecer" vuelve acá, y un visor sin preferencias guardadas arranca así.
        public const float DefaultTankHeight = -0.22f;                                   // m
        // Se midió (2, 0, 2) cm con la composición vieja, donde el desplazamiento giraba con la
        // rotación Y; es el mismo vector girado 65° sobre Y, para que el tanque quede donde estaba.
        public static readonly Vector3 DefaultTankShift = new Vector3(0.0266f, 0f, -0.0097f); // m
        public static readonly Vector3 DefaultTankRotation = new Vector3(0f, 65f, 0f);   // grados
        public const float DefaultNozzleOffset = 0f;                                     // m
        public static readonly Vector3 DefaultNozzleShift = Vector3.zero;                // m
        public static readonly Vector3 DefaultNozzleRotation = Vector3.zero;             // grados

        // Versión de las preferencias. v1 → v2: la calibración del extintor se descarta UNA vez (si
        // no, un visor que ya tenía prefs —los viejos ceros— nunca vería los defaults nuevos).
        // v2 → v3: cambió cómo se compone la rotación del tanque (ver SplitTankRotation); la
        // calibración guardada se convierte para que el tanque no se mueva.
        private const int SettingsVersion = 3;
        private const string KeyVersion = "cfg.version";

        private const string KeyVolume = "cfg.volume";
        private const string KeyVibration = "cfg.vibration";
        private const string KeyTankHeight = "cfg.tankControllerHeight";
        private const string KeyNozzleOffset = "cfg.nozzleOffset";
        private const string KeyShiftX = "cfg.tankShiftX";
        private const string KeyShiftZ = "cfg.tankShiftZ";
        private const string KeyRotX = "cfg.tankRotX";
        private const string KeyRotY = "cfg.tankRotY";
        private const string KeyRotZ = "cfg.tankRotZ";
        private const string KeyNozzleShiftX = "cfg.nozzleShiftX";
        private const string KeyNozzleShiftY = "cfg.nozzleShiftY";
        private const string KeyNozzleRotX = "cfg.nozzleRotX";
        private const string KeyNozzleRotY = "cfg.nozzleRotY";
        private const string KeyNozzleRotZ = "cfg.nozzleRotZ";
        private const string KeyPanelX = "cfg.panelLateral";
        private const string KeyPanelY = "cfg.panelHeight";
        private const string KeyPanelZ = "cfg.panelDistance";

        /// <summary>Se dispara cada vez que cambia cualquier valor.</summary>
        public static event Action Changed;

        private static bool _loaded;
        private static float _volume = 1f;
        private static bool _vibration = true;
        private static float _tankHeight;
        private static float _nozzleOffset;
        private static Vector3 _panel;
        private static Vector3 _tankRotation;
        private static Vector3 _tankShift; // sólo X y Z: el eje largo (Y) es TankControllerHeight
        private static Vector3 _nozzleRotation;
        private static Vector3 _nozzleShift; // sólo X e Y: su eje (Z) es NozzleOffset

        /// <summary>Volumen general, 0..1. Se aplica sobre <see cref="AudioListener.volume"/>.</summary>
        public static float Volume
        {
            get { EnsureLoaded(); return _volume; }
            set { EnsureLoaded(); Set(ref _volume, Mathf.Clamp01(Round(value, VolumeStep))); }
        }

        /// <summary>Si es false, <see cref="Haptics"/> no hace vibrar ningún mando.</summary>
        public static bool Vibration
        {
            get { EnsureLoaded(); return _vibration; }
            set
            {
                EnsureLoaded();
                if (_vibration == value) return;
                _vibration = value;
                if (!value) Haptics.StopAll(); // una vibración en curso no debe quedar colgada
                Save();
            }
        }

        /// <summary>
        /// Altura del mando sobre el tanque, en metros, respecto de la calibración de fábrica.
        /// Positivo = la abrazadera está más ARRIBA (hacia la válvula): el tanque virtual baja.
        /// </summary>
        public static float TankControllerHeight
        {
            get { EnsureLoaded(); return _tankHeight; }
            set { EnsureLoaded(); Set(ref _tankHeight, Mathf.Clamp(Round(value, LengthStep), HeightMin, HeightMax)); }
        }

        /// <summary>
        /// Corrimiento de la boquilla sobre su eje (el +Z del mando izquierdo), en metros.
        /// Positivo = la punta queda más ADELANTE.
        /// </summary>
        public static float NozzleOffset
        {
            get { EnsureLoaded(); return _nozzleOffset; }
            set { EnsureLoaded(); Set(ref _nozzleOffset, Mathf.Clamp(Round(value, LengthStep), NozzleMin, NozzleMax)); }
        }

        /// <summary>
        /// Corrimiento del panel de extintores respecto de su anchor en cada puesto, en metros y en el
        /// espacio del anchor: X = lateral (+ a la derecha del usuario), Y = altura, Z = distancia
        /// (+ más lejos). El anchor mira DESDE el usuario HACIA el panel, así que estos ejes son los
        /// que el usuario percibe, y el mismo ajuste vale para los cuatro puestos.
        /// </summary>
        public static Vector3 PanelOffset
        {
            get { EnsureLoaded(); return _panel; }
        }

        public static float PanelLateral
        {
            get => PanelOffset.x;
            set { EnsureLoaded(); SetPanel(0, value, PanelLateralMax); }
        }

        public static float PanelHeight
        {
            get => PanelOffset.y;
            set { EnsureLoaded(); SetPanel(1, value, PanelHeightMax); }
        }

        public static float PanelDistance
        {
            get => PanelOffset.z;
            set { EnsureLoaded(); SetPanel(2, value, PanelDistanceMax); }
        }

        /// <summary>
        /// Desplazamiento transversal del tanque respecto del mando, en metros y en los ejes locales
        /// del tanque (ya girado): X y Z, perpendiculares a su eje largo. Y siempre vale 0 — el
        /// corrimiento a lo largo del tanque es <see cref="TankControllerHeight"/>. Corrige que la
        /// abrazadera no quede centrada donde el modelo supone.
        /// </summary>
        public static Vector3 TankShift
        {
            get { EnsureLoaded(); return _tankShift; }
        }

        public static float TankShiftX
        {
            get => TankShift.x;
            set { EnsureLoaded(); SetAxis(ref _tankShift, 0, value, LengthStep, ShiftMax); }
        }

        public static float TankShiftZ
        {
            get => TankShift.z;
            set { EnsureLoaded(); SetAxis(ref _tankShift, 2, value, LengthStep, ShiftMax); }
        }

        /// <summary>
        /// Rotación del tanque respecto del mando, en grados (ejes locales del tanque: Y es su eje
        /// largo). X y Z lo inclinan alrededor del punto de agarre —la abrazadera—, que es por donde
        /// el tanque real está unido al mando; Y lo gira sobre su PROPIO eje, ya inclinado. NO es un
        /// euler de Unity: ver <see cref="SplitTankRotation"/>. Se suma encima del
        /// <c>rotationOffset</c> de fábrica.
        /// </summary>
        public static Vector3 TankRotation
        {
            get { EnsureLoaded(); return _tankRotation; }
        }

        public static float TankRotationX
        {
            get => TankRotation.x;
            set { EnsureLoaded(); SetAxis(ref _tankRotation, 0, value, RotationStep, RotationMax); }
        }

        public static float TankRotationY
        {
            get => TankRotation.y;
            set { EnsureLoaded(); SetAxis(ref _tankRotation, 1, value, RotationStep, RotationMax); }
        }

        public static float TankRotationZ
        {
            get => TankRotation.z;
            set { EnsureLoaded(); SetAxis(ref _tankRotation, 2, value, RotationStep, RotationMax); }
        }

        /// <summary>
        /// Parte <see cref="TankRotation"/> en la inclinación (X, Z) y el giro sobre el eje largo (Y),
        /// que <see cref="ExtinguisherMount"/> aplica en ese orden con el desplazamiento en el medio.
        /// Con <c>Quaternion.Euler(x, y, z)</c> la Y queda POR FUERA de X/Z: con el tanque inclinado,
        /// tocar Y lo hacía girar alrededor del eje del mando, describiendo un cono.
        /// </summary>
        public static void SplitTankRotation(Vector3 r, out Quaternion tilt, out Quaternion spin)
        {
            tilt = Quaternion.Euler(r.x, 0f, r.z);
            spin = Quaternion.AngleAxis(r.y, Vector3.up);
        }

        /// <summary>
        /// v2 → v3: convierte la calibración del tanque de la composición vieja (un solo
        /// <c>Quaternion.Euler</c>, desplazamiento aplicado después del giro) a la nueva, dejando el
        /// tanque exactamente donde estaba. Sin redondear al paso: el próximo "+"/"−" lo alinea.
        /// </summary>
        private static void MigrateTankRotationToV3()
        {
            Quaternion old = Quaternion.Euler(_tankRotation);

            // Inclinación: la que lleva el eje largo (+Y) a donde lo dejaba la rotación vieja.
            // Euler(x, 0, z) · up = (−sen z, cos z · cos x, cos z · sen x).
            Vector3 axis = old * Vector3.up;
            float z = Mathf.Asin(Mathf.Clamp(-axis.x, -1f, 1f)) * Mathf.Rad2Deg;
            float x = Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
            Quaternion tilt = Quaternion.Euler(x, 0f, z);

            // Giro: lo que falta, que por construcción es sólo sobre Y.
            Vector3 f = (Quaternion.Inverse(tilt) * old) * Vector3.forward;
            float y = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;

            // El desplazamiento viejo vivía en el espacio ya girado; el nuevo, en el inclinado.
            Vector3 shift = Quaternion.AngleAxis(y, Vector3.up) * _tankShift;

            Vector3 before = _tankRotation;
            _tankRotation = new Vector3(Mathf.Clamp(x, -RotationMax, RotationMax),
                                        Mathf.Clamp(y, -RotationMax, RotationMax),
                                        Mathf.Clamp(z, -RotationMax, RotationMax));
            _tankShift = new Vector3(Mathf.Clamp(shift.x, -ShiftMax, ShiftMax), 0f,
                                     Mathf.Clamp(shift.z, -ShiftMax, ShiftMax));
            Debug.Log($"[GameSettings] Rotación del tanque convertida a v3: {before}° → {_tankRotation}°, " +
                      $"desplazamiento → {_tankShift * 100f} cm.");
        }

        /// <summary>
        /// Desplazamiento transversal de la boquilla respecto del mando izquierdo, en metros y en sus
        /// ejes locales (ya girada): X e Y, perpendiculares a su eje. Z siempre vale 0 — el corrimiento
        /// a lo largo de la boquilla es <see cref="NozzleOffset"/>. Mismo papel que
        /// <see cref="TankShift"/> en el tanque.
        /// </summary>
        public static Vector3 NozzleShift
        {
            get { EnsureLoaded(); return _nozzleShift; }
        }

        public static float NozzleShiftX
        {
            get => NozzleShift.x;
            set { EnsureLoaded(); SetAxis(ref _nozzleShift, 0, value, LengthStep, ShiftMax); }
        }

        public static float NozzleShiftY
        {
            get => NozzleShift.y;
            set { EnsureLoaded(); SetAxis(ref _nozzleShift, 1, value, LengthStep, ShiftMax); }
        }

        /// <summary>
        /// Rotación de la boquilla respecto del mando izquierdo, en grados (euler, ejes locales de la
        /// boquilla: Z es su eje, hacia donde sale el chorro). Gira alrededor del mando, que es por
        /// donde la boquilla real está unida. Se suma encima del <c>rotationOffset</c> de fábrica.
        /// </summary>
        public static Vector3 NozzleRotation
        {
            get { EnsureLoaded(); return _nozzleRotation; }
        }

        public static float NozzleRotationX
        {
            get => NozzleRotation.x;
            set { EnsureLoaded(); SetAxis(ref _nozzleRotation, 0, value, RotationStep, RotationMax); }
        }

        public static float NozzleRotationY
        {
            get => NozzleRotation.y;
            set { EnsureLoaded(); SetAxis(ref _nozzleRotation, 1, value, RotationStep, RotationMax); }
        }

        public static float NozzleRotationZ
        {
            get => NozzleRotation.z;
            set { EnsureLoaded(); SetAxis(ref _nozzleRotation, 2, value, RotationStep, RotationMax); }
        }

        /// <summary>Vuelve todo a los valores de fábrica.</summary>
        public static void ResetToDefaults()
        {
            EnsureLoaded();
            _volume = 1f;
            _vibration = true;
            SetTankDefaults();
            SetNozzleDefaults();
            _panel = Vector3.zero;
            Save();
        }

        /// <summary>Vuelve sólo la calibración del tanque (altura, desplazamiento, rotación).</summary>
        public static void ResetExtinguisher()
        {
            EnsureLoaded();
            SetTankDefaults();
            Save();
        }

        /// <summary>Vuelve sólo la calibración de la boquilla (corrimiento, desplazamiento, rotación).</summary>
        public static void ResetNozzle()
        {
            EnsureLoaded();
            SetNozzleDefaults();
            Save();
        }

        private static void SetTankDefaults()
        {
            _tankHeight = DefaultTankHeight;
            _tankShift = DefaultTankShift;
            _tankRotation = DefaultTankRotation;
        }

        private static void SetNozzleDefaults()
        {
            _nozzleOffset = DefaultNozzleOffset;
            _nozzleShift = DefaultNozzleShift;
            _nozzleRotation = DefaultNozzleRotation;
        }

        /// <summary>Vuelve sólo el panel a su posición de fábrica.</summary>
        public static void ResetPanel()
        {
            EnsureLoaded();
            if (_panel == Vector3.zero) return;
            _panel = Vector3.zero;
            Save();
        }

        private static void SetPanel(int axis, float value, float max) =>
            SetAxis(ref _panel, axis, value, PanelStep, max);

        private static void SetAxis(ref Vector3 vector, int axis, float value, float step, float max)
        {
            float v = Mathf.Clamp(Round(value, step), -max, max);
            if (Mathf.Approximately(vector[axis], v)) return;
            vector[axis] = v;
            Save();
        }

        /// <summary>Aplica lo que no depende de nadie más (volumen). Idempotente.</summary>
        public static void Apply()
        {
            EnsureLoaded();
            AudioListener.volume = _volume;
        }

        // Unity 6 no recarga el dominio al entrar a Play si está desactivado en Project Settings:
        // sin esto, los estáticos quedarían con los valores de la corrida anterior del editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loaded = false;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyOnStartup() => Apply();

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _volume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyVolume, 1f));
            _vibration = PlayerPrefs.GetInt(KeyVibration, 1) != 0;
            _tankHeight = Mathf.Clamp(PlayerPrefs.GetFloat(KeyTankHeight, DefaultTankHeight), HeightMin, HeightMax);
            _nozzleOffset = Mathf.Clamp(PlayerPrefs.GetFloat(KeyNozzleOffset, DefaultNozzleOffset), NozzleMin, NozzleMax);
            _panel = new Vector3(
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyPanelX, 0f), -PanelLateralMax, PanelLateralMax),
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyPanelY, 0f), -PanelHeightMax, PanelHeightMax),
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyPanelZ, 0f), -PanelDistanceMax, PanelDistanceMax));
            _tankShift = new Vector3(
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyShiftX, DefaultTankShift.x), -ShiftMax, ShiftMax), 0f,
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyShiftZ, DefaultTankShift.z), -ShiftMax, ShiftMax));
            _tankRotation = new Vector3(
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyRotX, DefaultTankRotation.x), -RotationMax, RotationMax),
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyRotY, DefaultTankRotation.y), -RotationMax, RotationMax),
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyRotZ, DefaultTankRotation.z), -RotationMax, RotationMax));
            _nozzleShift = new Vector3(
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyNozzleShiftX, DefaultNozzleShift.x), -ShiftMax, ShiftMax),
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyNozzleShiftY, DefaultNozzleShift.y), -ShiftMax, ShiftMax), 0f);
            _nozzleRotation = new Vector3(
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyNozzleRotX, DefaultNozzleRotation.x), -RotationMax, RotationMax),
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyNozzleRotY, DefaultNozzleRotation.y), -RotationMax, RotationMax),
                Mathf.Clamp(PlayerPrefs.GetFloat(KeyNozzleRotZ, DefaultNozzleRotation.z), -RotationMax, RotationMax));

            int version = PlayerPrefs.GetInt(KeyVersion, 1);
            if (version < SettingsVersion)
            {
                if (version < 2)
                {
                    Debug.Log($"[GameSettings] Preferencias de la versión {version}: la calibración del " +
                              "extintor pasa a los defaults nuevos.");
                    SetTankDefaults();
                    SetNozzleDefaults();
                }
                else
                {
                    MigrateTankRotationToV3();
                }
                PlayerPrefs.SetInt(KeyVersion, SettingsVersion);
                Save();
            }

            // Para comprobar en el visor (adb logcat -s Unity) que la configuración sobrevivió al reinicio.
            Debug.Log($"[GameSettings] Cargado: volumen {_volume:0.0}, vibración {_vibration}, " +
                      $"mando {_tankHeight * 100f:0} cm, desplazamiento {_tankShift * 100f} cm, " +
                      $"rotación {_tankRotation}°, " +
                      $"boquilla {_nozzleOffset * 100f:0} cm, desplazamiento {_nozzleShift * 100f} cm, " +
                      $"rotación {_nozzleRotation}°, panel {_panel * 100f} cm.");
        }

        private static void Set(ref float field, float value)
        {
            if (Mathf.Approximately(field, value)) return;
            field = value;
            Save();
        }

        private static void Save()
        {
            PlayerPrefs.SetFloat(KeyVolume, _volume);
            PlayerPrefs.SetInt(KeyVibration, _vibration ? 1 : 0);
            PlayerPrefs.SetFloat(KeyTankHeight, _tankHeight);
            PlayerPrefs.SetFloat(KeyNozzleOffset, _nozzleOffset);
            PlayerPrefs.SetFloat(KeyShiftX, _tankShift.x);
            PlayerPrefs.SetFloat(KeyShiftZ, _tankShift.z);
            PlayerPrefs.SetFloat(KeyRotX, _tankRotation.x);
            PlayerPrefs.SetFloat(KeyRotY, _tankRotation.y);
            PlayerPrefs.SetFloat(KeyRotZ, _tankRotation.z);
            PlayerPrefs.SetFloat(KeyNozzleShiftX, _nozzleShift.x);
            PlayerPrefs.SetFloat(KeyNozzleShiftY, _nozzleShift.y);
            PlayerPrefs.SetFloat(KeyNozzleRotX, _nozzleRotation.x);
            PlayerPrefs.SetFloat(KeyNozzleRotY, _nozzleRotation.y);
            PlayerPrefs.SetFloat(KeyNozzleRotZ, _nozzleRotation.z);
            PlayerPrefs.SetFloat(KeyPanelX, _panel.x);
            PlayerPrefs.SetFloat(KeyPanelY, _panel.y);
            PlayerPrefs.SetFloat(KeyPanelZ, _panel.z);
            PlayerPrefs.Save();

            AudioListener.volume = _volume;
            Changed?.Invoke();
        }

        /// <summary>Redondea al paso: sumar 0,1 diez veces no da 1,0 exacto en float.</summary>
        private static float Round(float value, float step) => Mathf.Round(value / step) * step;
    }
}
