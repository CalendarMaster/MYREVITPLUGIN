# Script de Instalación del Plugin MI-Exportador de Vistas
# =========================================================
# Ejecutar con: powershell -ExecutionPolicy Bypass -File INSTALAR.ps1

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  INSTALADOR - MI-EXPORTADOR DE VISTAS PARA REVIT 2025          ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Detectar la versión de Revit instalada
Write-Host "🔍 Detectando instalación de Revit..." -ForegroundColor Yellow

$revitVersions = @("2025", "2024", "2023", "2022")
$revitPath = $null
$revitVersion = $null

foreach ($version in $revitVersions) {
	$revitInstallPath = "C:\Program Files\Autodesk\Revit $version"
	if (Test-Path $revitInstallPath) {
		$revitPath = $revitInstallPath
		$revitVersion = $version
		break
	}
}

if (-not $revitVersion) {
	Write-Host "❌ ERROR: No se encontró Revit instalado" -ForegroundColor Red
	Write-Host "   Asegúrate de tener Revit 2022 o posterior instalado" -ForegroundColor Red
	Read-Host "Presiona Enter para salir"
	exit 1
}

Write-Host "✓ Revit $revitVersion encontrado en: $revitPath" -ForegroundColor Green
Write-Host ""

# Obtener ruta del usuario actual
$username = $env:USERNAME
$addinFolder = "C:\Users\$username\AppData\Roaming\Autodesk\Revit\Addins\$revitVersion"

Write-Host "📂 Carpeta de Add-ins de Revit:" -ForegroundColor Yellow
Write-Host "   $addinFolder" -ForegroundColor White
Write-Host ""

# Verificar que existan los archivos necesarios
Write-Host "🔍 Verificando archivos necesarios..." -ForegroundColor Yellow

$requiredFiles = @(
	".\bin\MYREVITPLUGIN.dll",
	".\bin\MYREVITPLUGIN.dll.config",
	".\bin\logo_large.png",
	".\bin\logo_small.png"
)

$missingFiles = @()
foreach ($file in $requiredFiles) {
	if (-not (Test-Path $file)) {
		$missingFiles += $file
	}
}

if ($missingFiles.Count -gt 0) {
	Write-Host "❌ ERROR: Faltan los siguientes archivos:" -ForegroundColor Red
	foreach ($file in $missingFiles) {
		Write-Host "   - $file" -ForegroundColor Red
	}
	Read-Host "Presiona Enter para salir"
	exit 1
}

Write-Host "✓ Todos los archivos necesarios encontrados" -ForegroundColor Green
Write-Host ""

# Crear la carpeta de Add-ins si no existe
Write-Host "📁 Preparando carpeta de instalación..." -ForegroundColor Yellow
if (-not (Test-Path $addinFolder)) {
	New-Item -ItemType Directory -Path $addinFolder -Force | Out-Null
	Write-Host "✓ Carpeta creada: $addinFolder" -ForegroundColor Green
} else {
	Write-Host "✓ Carpeta existe: $addinFolder" -ForegroundColor Green
}

Write-Host ""

# Copiar archivos DLL y configuración
Write-Host "📋 Copiando archivos del plugin..." -ForegroundColor Yellow

$dllPath = ".\bin\MYREVITPLUGIN.dll"
$configPath = ".\bin\MYREVITPLUGIN.dll.config"
$logoLargePath = ".\bin\logo_large.png"
$logoSmallPath = ".\bin\logo_small.png"

$targetDllPath = Join-Path $addinFolder "MYREVITPLUGIN.dll"
$targetConfigPath = Join-Path $addinFolder "MYREVITPLUGIN.dll.config"
$targetLogoLargePath = Join-Path $addinFolder "logo_large.png"
$targetLogoSmallPath = Join-Path $addinFolder "logo_small.png"

try {
	Copy-Item $dllPath -Destination $targetDllPath -Force
	Write-Host "✓ DLL copiado" -ForegroundColor Green

	Copy-Item $configPath -Destination $targetConfigPath -Force
	Write-Host "✓ Config copiado" -ForegroundColor Green

	Copy-Item $logoLargePath -Destination $targetLogoLargePath -Force
	Write-Host "✓ Logo grande copiado" -ForegroundColor Green

	Copy-Item $logoSmallPath -Destination $targetLogoSmallPath -Force
	Write-Host "✓ Logo pequeño copiado" -ForegroundColor Green
} catch {
	Write-Host "❌ ERROR al copiar archivos: $_" -ForegroundColor Red
	Read-Host "Presiona Enter para salir"
	exit 1
}

Write-Host ""

# Crear archivo .addin con ruta correcta
Write-Host "⚙️ Generando archivo de configuración del plugin (.addin)..." -ForegroundColor Yellow

$addinContent = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
	<Name>MI-Exportador de Vistas</Name>
	<FullClassName>MYREVITPLUGIN.Class1</FullClassName>
	<Assembly>$targetDllPath</Assembly>
	<AddInId>358379E5-76A1-4F6C-A968-7ADE6025EED9</AddInId>
	<VendorId>MISTUDIO</VendorId>
	<VendorDescription>M.I. Studio - Soluciones de Modelado e Integración</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

$addinFilePath = Join-Path $addinFolder "MYREVITPLUGIN.addin"

try {
	$addinContent | Out-File -FilePath $addinFilePath -Encoding UTF8 -Force
	Write-Host "✓ Archivo .addin creado: $addinFilePath" -ForegroundColor Green
} catch {
	Write-Host "❌ ERROR al crear archivo .addin: $_" -ForegroundColor Red
	Read-Host "Presiona Enter para salir"
	exit 1
}

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "✅ INSTALACIÓN COMPLETADA EXITOSAMENTE" -ForegroundColor Green
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "📍 Plugin instalado en:" -ForegroundColor Cyan
Write-Host "   $addinFolder" -ForegroundColor White
Write-Host ""
Write-Host "🔧 Próximos pasos:" -ForegroundColor Cyan
Write-Host "   1. Cierra Revit completamente (si está abierto)" -ForegroundColor White
Write-Host "   2. Abre Revit 2025" -ForegroundColor White
Write-Host "   3. Ve a la pestaña 'Add-Ins' (Complementos)" -ForegroundColor White
Write-Host "   4. Busca el panel 'M.I. Studio'" -ForegroundColor White
Write-Host "   5. Haz clic en el botón 'Exportador de Vistas'" -ForegroundColor White
Write-Host ""
Write-Host "⚠️  IMPORTANTE:" -ForegroundColor Yellow
Write-Host "   - Si Revit estaba abierto, ciérralo completamente y vuelve a abrirlo" -ForegroundColor White
Write-Host "   - El plugin debería aparecer automáticamente en la siguiente sesión" -ForegroundColor White
Write-Host ""

Read-Host "Presiona Enter para cerrar este instalador"
