# Créditos de assets de terceros

Atribución de los modelos/packs usados en el proyecto. Los marcados **CC-BY** (o similar con
atribución) DEBEN figurar en los créditos del TP. Formato de cada entrada: título — autor —
fuente (link) — licencia.

> **Ubicación en el proyecto:** `Assets/ThirdParty/FireCauses/<Asset>/` (FBX + texturas).
> Los materiales ya están en `Universal Render Pipeline/Lit` y las texturas capadas a 2048
> (`Extintores ▸ Fix Sketchfab Textures`). El peso de malla lo ajusta
> `Extintores ▸ Optimize FireCause Models`.

## En uso — una causa por clase

Estas son las que instancia `FirePrefabBuilder` en `Fire_A/B/C/K`:

| Clase | Carpeta | Modelo |
|---|---|---|
| A — Sólidos | `Cardboard_Box/` | Cardboard box 3d model |
| B — Líquido inflamable | `Sports_Car/` | Low-Poly Sports Car Free |
| C — Eléctrico | `Electrical_Breaker_Panel/` | Electrical Breaker Panel Box – LP |
| K — Cocina | `Gas_Stove/` | Gas stove |

### Clase A — Sólidos (cartón)
- "Cardboard+box+3d+model" (<https://skfb.ly/pL967>) by Jerovdl is licensed under Creative Commons
  Attribution (<http://creativecommons.org/licenses/by/4.0/>). — **CC-BY: acreditar.**

### Clase B — Líquido inflamable (vehículo)
- "Low-Poly Sports Car Free" (<https://skfb.ly/6YnLC>) by donnichols is licensed under Creative
  Commons Attribution (<http://creativecommons.org/licenses/by/4.0/>). — **CC-BY: acreditar.**

### Clase C — Tablero eléctrico
- "Electrical Breaker Panel Box – LP model" (<https://skfb.ly/pzHtH>) by Nikoleta.Zhecheva is
  licensed under Creative Commons Attribution (<http://creativecommons.org/licenses/by/4.0/>).
  — **CC-BY: acreditar.**

### Clase K — Hornalla / cocina
- "Gas stove" (<https://skfb.ly/pLHxI>) by Stanz is licensed under Creative Commons Attribution
  (<http://creativecommons.org/licenses/by/4.0/>). — **CC-BY: acreditar.**

## VFX y base
- **Free Fire VFX – URP** — Vefects — Unity Asset Store (productId 266226) — Asset Store EULA.
- Extintor: FBX de pack "Police" (ver `CLAUDE.md`) — revisar/registrar licencia de origen.

## Alternativas descargadas (no usadas por los prefabs)
- "VR-Ready Fuse Box" (<https://skfb.ly/pxXKE>) by Jimmy Johansson — **CC-BY.**
  Está en `VR_Fuse_Box/` (537 KB) como recambio de la clase C. **Si se usa, acreditar.**

## Descartados
- "Audi Novulari" by hartwelkisaka (CC-BY) — **eliminado del proyecto** (37 MB, redundante con
  Low-Poly Sports Car).
- "Old Rusty Car" (<https://skfb.ly/LxRy>) by Renafox — CC-BY-**NC**, solo uso no comercial.
  **Nunca se incluyó.**
- **Awesome Free Scans – Wood Logs** — Freya Gameworks — Unity Asset Store. Evaluado para clase A;
  no se descargó (se usa el cartón).

---
> Para CC-BY, pegar acá el texto de atribución que da Sketchfab (título, autor, URL, licencia).
> Ejemplo del formato de Sketchfab:
> "«Nombre del modelo» (URL) by Autor (perfil) licensed under CC-BY-4.0 (https://creativecommons.org/licenses/by/4.0/)".
