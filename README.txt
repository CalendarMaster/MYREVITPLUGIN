╔════════════════════════════════════════════════════════════════╗
║     MI-EXPORTADOR DE VISTAS - GUÍA DE INSTALACIÓN               ║
║     Para: Compañeros de trabajo                                 ║
╚════════════════════════════════════════════════════════════════╝

═══════════════════════════════════════════════════════════════════
 📋 REQUISITOS PREVIOS
═══════════════════════════════════════════════════════════════════

✓ Tener Revit 2025 instalado (también funciona con 2024, 2023, 2022)
✓ Windows 10 o superior
✓ Acceso de administrador en la computadora

═══════════════════════════════════════════════════════════════════
 🚀 INSTALACIÓN EN 3 PASOS
═══════════════════════════════════════════════════════════════════

PASO 1: Descargar los archivos
─────────────────────────────
Tu compañero que te compartió este archivo ya preparó todo.
Solo necesitas esta carpeta que contiene:

📁 MI-ExportadorVistas/
├── INSTALAR.ps1      ← Este archivo
├── README.txt        ← Este documento
└── bin/
	├── MYREVITPLUGIN.dll
	├── MYREVITPLUGIN.dll.config
	├── logo_large.png
	└── logo_small.png

PASO 2: Ejecutar el instalador
──────────────────────────────

OPCIÓN A: Windows 10/11 (Recomendado)
   1. Click derecho sobre INSTALAR.ps1
   2. Selecciona "Ejecutar con PowerShell"
   3. Presiona Enter cuando aparezca el aviso de seguridad
   4. ¡Listo! El script se ejecutará automáticamente

OPCIÓN B: PowerShell Manual
   1. Abre PowerShell (búscalo en el menú de inicio)
   2. Navega a la carpeta donde están los archivos
   3. Copia y pega esto:
	  powershell -ExecutionPolicy Bypass -File .\INSTALAR.ps1
   4. Presiona Enter
   5. ¡Listo!

PASO 3: Abrir Revit
───────────────────
   1. Cierra Revit completamente (si está abierto)
   2. Abre Revit 2025
   3. Busca la pestaña "Add-Ins" (Complementos)
   4. Busca el panel "M.I. Studio"
   5. ¡Deberías ver el botón "Exportador de Vistas"!

═══════════════════════════════════════════════════════════════════
 ❓ SOLUCIÓN DE PROBLEMAS
═══════════════════════════════════════════════════════════════════

PROBLEMA: No aparece el plugin en Revit
SOLUCIÓN:
   1. Verifica que Revit se cerrara completamente
   2. Vuelve a abrir Revit
   3. Si aún no aparece, en Revit ve a:
	  Manage → Add-Ins → Manage Add-Ins
	  Busca "MI-Exportador de Vistas"
   4. Si hay error, muestra la captura al equipo de IT

PROBLEMA: Error "PowerShell execution policy"
SOLUCIÓN:
   Abre PowerShell como administrador y ejecuta:
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

PROBLEMA: El instalador dice "Revit no encontrado"
SOLUCIÓN:
   Asegúrate de tener Revit 2025, 2024, 2023 ó 2022 instalado
   Si tienes otra versión, contacta al que te pasó el plugin

═══════════════════════════════════════════════════════════════════
 🎯 ¿QUÉ HACE EL PLUGIN?
═══════════════════════════════════════════════════════════════════

MI-Exportador de Vistas es un plugin para Revit que:

✓ Aparece en la pestaña "Add-Ins" bajo el panel "M.I. Studio"
✓ Permite exportar planos de manera automatizada
✓ Está diseñado para agilizar flujos de trabajo en Revit
✓ Se integra perfectamente con la interfaz de Revit

═══════════════════════════════════════════════════════════════════
 📞 SOPORTE
═══════════════════════════════════════════════════════════════════

¿Preguntas? Contacta a:
   [Nombre del equipo IT / Desarrollador]
   [Email]
   [Teléfono]

═══════════════════════════════════════════════════════════════════
 ✅ VERIFICACIÓN FINAL
═══════════════════════════════════════════════════════════════════

Después de instalar, verifica que:

□ Revit abre sin errores
□ Aparece la pestaña "Add-Ins" normalmente
□ Existe el panel "M.I. Studio"
□ El botón "Exportador de Vistas" es visible
□ Al pasar el mouse, aparece el tooltip del plugin
□ Al hacer clic, funciona correctamente

Si todos estos puntos ✓, ¡la instalación fue exitosa!

═══════════════════════════════════════════════════════════════════

¡Gracias por usar MI-Exportador de Vistas! 🎉
