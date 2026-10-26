# Entrenamiento en uso de extintores (RV, Meta Quest)

Simulador de Realidad Virtual para practicar el uso de extintores, hecho en Unity para Meta Quest. Se utiliza un extintor real descargado para mejorar la sensación física.
El usuario recorre un patio industrial con fuegos de distintas clases (A sólidos, B líquidos inflamables, C eléctrico, K cocina), elige el extintor adecuado y lo apaga con el chorro.

## Video

▶️ **Demo del proyecto:** https://youtu.be/hH53c2kRR_0

[![Demo del proyecto](https://img.youtube.com/vi/hH53c2kRR_0/hqdefault.jpg)](https://youtu.be/hH53c2kRR_0)

## Qué incluye

- **Tres modos de juego** desde el menú del lobby:
  - **Tutorial**: los 4 fuegos en orden, con ayudas; sólo se pueden elegir los extintores efectivos.
  - **Aleatorio**: un fuego al azar, sin ayudas (ni letra de clase ni colores en el panel).
  - **Personalizado**: se elige el puesto apuntando al elemento del patio.
- **Clases de fuego y agentes extintores** (agua, espuma, CO2, polvo ABC/BC, químico húmedo) con una
  matriz de efectividad: el extintor equivocado no apaga y el juego avisa.
- **Uso a dos manos**: el tanque va en la mano derecha y la boquilla en la izquierda, unidas por una
  manguera; el grip del mando derecho acciona la palanca.
- **Modelo propio del extintor real** (ABC de 1 kg), paramétrico en Blender y también imprimible en
  3D, para usarlo como prop físico junto al mando.
- **Configuración** de volumen, vibración y calibración del prop desde el menú.
- **Manos y controles a la vez** (modo multimodal del Meta XR SDK).

## Tecnología

Unity 6000.5.4f1 · URP 17.5 · OpenXR + Meta XR Core SDK 203 · Android (Quest 2, 3, 3S y Pro).

## Compilar e instalar

Con el editor cerrado, desde la raíz del repo:

```bash
powershell -ExecutionPolicy Bypass -File tools\build-quest.ps1 -Run
```

Detalle en [`docs/BUILD-quest.md`](docs/BUILD-quest.md).

## Documentación

| Documento | Contenido |
|---|---|
| [`docs/PLAN-ejercicio-entrenamiento.md`](docs/PLAN-ejercicio-entrenamiento.md) | Estado actual: modos, puestos, patio industrial |
| [`docs/PLAN-fuegos-y-extintores.md`](docs/PLAN-fuegos-y-extintores.md) | Diseño: clases de fuego, agentes, matriz de efectividad |
| [`docs/BUILD-quest.md`](docs/BUILD-quest.md) | Build e instalación en el visor |
| [`docs/CREDITS.md`](docs/CREDITS.md) | Licencias de los assets de terceros |
| [`CLAUDE.md`](CLAUDE.md) | Configuración XR, rig y notas técnicas |
