# Script para compilar el plugin de Revit
# Cierra Revit, compila el proyecto y copia el .addin

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "COMPILADOR DE PLUGIN REVIT" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Verificar si Revit está ejecutándose
$revitProcess = Get-Process -Name "Revit" -ErrorAction SilentlyContinue

if ($revitProcess) {
	Write-Host "ADVERTENCIA: Revit está ejecutándose" -ForegroundColor Yellow
	Write-Host "El archivo DLL está bloqueado por Revit" -ForegroundColor Yellow
	Write-Host ""
	$response = Read-Host "¿Deseas cerrar Revit ahora? (S/N)"

	if ($response -eq "S" -or $response -eq "s") {
		Write-Host "Cerrando Revit..." -ForegroundColor Yellow
		Stop-Process -Name "Revit" -Force
		Start-Sleep -Seconds 3
		Write-Host "Revit cerrado exitosamente" -ForegroundColor Green
	} else {
		Write-Host "No se puede compilar mientras Revit esté abierto" -ForegroundColor Red
		Write-Host "Por favor cierra Revit manualmente y ejecuta este script nuevamente" -ForegroundColor Red
		pause
		exit
	}
}

Write-Host ""
Write-Host "Compilando proyecto..." -ForegroundColor Cyan

# 2. Compilar el proyecto
$msbuildPath = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"

# Buscar MSBuild en diferentes ubicaciones
if (-not (Test-Path $msbuildPath)) {
	$msbuildPath = "C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
}

if (Test-Path $msbuildPath) {
	& $msbuildPath "MYREVITPLUGIN\MYREVITPLUGIN.csproj" /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal

	if ($LASTEXITCODE -eq 0) {
		Write-Host ""
		Write-Host "========================================" -ForegroundColor Green
		Write-Host "COMPILACIÓN EXITOSA" -ForegroundColor Green
		Write-Host "========================================" -ForegroundColor Green
		Write-Host ""
		Write-Host "Archivo DLL creado en:" -ForegroundColor Green
		Write-Host "D:\Design Technologist\PROYECTOS\T0014 - PLUGIN RVT\MYREVITPLUGIN\MYREVITPLUGIN\bin\Debug\MYREVITPLUGIN.dll" -ForegroundColor White
		Write-Host ""
		Write-Host "Archivo .addin ubicado en:" -ForegroundColor Green
		Write-Host "C:\Users\MI-STUDIO\AppData\Roaming\Autodesk\Revit\Addins\2025\MYREVITPLUGIN.addin" -ForegroundColor White
		Write-Host ""
		Write-Host "SIGUIENTE PASO:" -ForegroundColor Cyan
		Write-Host "1. Abre Revit 2025" -ForegroundColor White
		Write-Host "2. Ve a la pestaña 'Add-Ins' (Complementos)" -ForegroundColor White
		Write-Host "3. Busca el panel 'MyRibbonPanel'" -ForegroundColor White
		Write-Host "4. Haz clic en el botón 'MyTest'" -ForegroundColor White
		Write-Host ""
	} else {
		Write-Host ""
		Write-Host "ERROR EN LA COMPILACIÓN" -ForegroundColor Red
		Write-Host "Revisa los errores arriba" -ForegroundColor Red
	}
} else {
	Write-Host ""
	Write-Host "ERROR: No se encontró MSBuild" -ForegroundColor Red
	Write-Host "Por favor compila el proyecto desde Visual Studio" -ForegroundColor Yellow
}

Write-Host ""
pause
