# INSTRUCCIONES PARA CARGAR EL PLUGIN EN REVIT 2025

## Problemas identificados y solucionados:

### 1. **Archivo .addin corregido**
   - Se eliminó el tag `<Description>` (no es necesario y puede causar problemas)
   - Se corrigió el formato del XML con indentación correcta

### 2. **FullClassName de la clase interna**
   - La clase `MyTest` está dentro de `Class1`, por lo que debe referenciarse como: `MYREVITPLUGIN.Class1+MyTest`
   - Se corrigió en el código C#

### 3. **Imagen del botón**
   - Se eliminó temporalmente la carga de imagen que podría causar excepciones si el archivo no existe

## PASOS PARA CARGAR EL PLUGIN:

### OPCIÓN 1: Cerrar Revit y Recompilar (RECOMENDADO)

1. **Cierra completamente Revit 2025** (Revit bloquea el DLL cuando está abierto)

2. **Compila el proyecto nuevamente** en Visual Studio

3. **Verifica que el archivo .addin esté en la ubicación correcta:**
   ```
   C:\Users\MI-STUDIO\AppData\Roaming\Autodesk\Revit\Addins\2025\MYREVITPLUGIN.addin
   ```

4. **Abre Revit 2025**

5. **Busca tu panel "MyRibbonPanel"** en la pestaña "Add-Ins" (Complementos)

6. **Deberías ver un botón llamado "MyTest"**

### OPCIÓN 2: Verificar errores de carga

Si no aparece el plugin:

1. Abre Revit 2025

2. Ve a: **Manage (Administrar) → Add-Ins (Complementos) → Manage Add-Ins (Administrar complementos)**

3. Busca "MYREVITPLUGIN" o "MyFirstRevit" en la lista

4. Verifica si hay algún mensaje de error

### VERIFICACIÓN DE ARCHIVOS:

Los siguientes archivos deben existir:

✓ `D:\Design Technologist\PROYECTOS\T0014 - PLUGIN RVT\MYREVITPLUGIN\MYREVITPLUGIN\bin\Debug\MYREVITPLUGIN.dll`
✓ `D:\Design Technologist\PROYECTOS\T0014 - PLUGIN RVT\MYREVITPLUGIN\MYREVITPLUGIN\bin\Debug\MYREVITPLUGIN.dll.config`
✓ `C:\Users\MI-STUDIO\AppData\Roaming\Autodesk\Revit\Addins\2025\MYREVITPLUGIN.addin`

### SOLUCIÓN DE PROBLEMAS COMUNES:

**Problema:** "El plugin no aparece en Revit"
- **Solución:** Verifica que el archivo .addin esté en la carpeta correcta y tenga extensión `.addin` (no `.addin.txt`)

**Problema:** "Error de carga del plugin"
- **Solución:** Revisa el Event Viewer de Windows o el archivo de log de Revit en:
  `C:\Users\MI-STUDIO\AppData\Local\Autodesk\Revit\Autodesk Revit 2025\Journals\`

**Problema:** "El botón aparece pero al hacer clic no funciona"
- **Solución:** Verifica que el nombre completo de la clase sea correcto: `MYREVITPLUGIN.Class1+MyTest`

### UBICACIÓN DEL PANEL EN REVIT:

Una vez cargado correctamente, encontrarás:
- **Pestaña:** Add-Ins (Complementos)
- **Panel:** MyRibbonPanel
- **Botón:** MyTest
- **Al hacer clic:** Debe aparecer un cuadro de diálogo que dice "Hello world, this is my first Revit Command"

---

## Archivo .addin correcto:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
	<Name>MyFirstRevit</Name>
	<FullClassName>MYREVITPLUGIN.Class1</FullClassName>
	<Assembly>D:\Design Technologist\PROYECTOS\T0014 - PLUGIN RVT\MYREVITPLUGIN\MYREVITPLUGIN\bin\Debug\MYREVITPLUGIN.dll</Assembly>
	<AddInId>358379E5-76A1-4F6C-A968-7ADE6025EED9</AddInId>
	<VendorId>PA</VendorId>
	<VendorDescription>Modelo Integrado Studio</VendorDescription>
  </AddIn>
</RevitAddIns>
```

Este archivo ya ha sido copiado a la ubicación correcta.
