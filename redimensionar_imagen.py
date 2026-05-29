#!/usr/bin/env python3
# Script para redimensionar imagen para Revit Plugin

from PIL import Image
import os

# Rutas
img_dir = r"D:\Design Technologist\PROYECTOS\T0014 - PLUGIN RVT\MYREVITPLUGIN\MYREVITPLUGIN\img"
bin_debug_dir = r"D:\Design Technologist\PROYECTOS\T0014 - PLUGIN RVT\MYREVITPLUGIN\MYREVITPLUGIN\bin\Debug"

# Archivo de imagen original
original_file = os.path.join(img_dir, "Gemini_Generated_Image_nrevabnrevabnrev (1).png")

# Archivos de salida
large_output = os.path.join(bin_debug_dir, "logo_large.png")
small_output = os.path.join(bin_debug_dir, "logo_small.png")

print("=" * 60)
print("REDIMENSIONADOR DE IMAGEN PARA REVIT PLUGIN")
print("=" * 60)

# Verificar que el archivo existe
if not os.path.exists(original_file):
    print(f"ERROR: No se encontró el archivo {original_file}")
    exit(1)

# Abrir imagen original
img = Image.open(original_file)
print(f"\nImagen original: {os.path.basename(original_file)}")
print(f"Tamaño original: {img.width} x {img.height} píxeles")

# Redimensionar para LargeImage (32x32)
print("\nCreando logo grande (32x32)...")
large_img = img.resize((32, 32), Image.Resampling.LANCZOS)
large_img.save(large_output)
print(f"✓ Guardado en: {large_output}")

# Redimensionar para Image (16x16)
print("\nCreando logo pequeño (16x16)...")
small_img = img.resize((16, 16), Image.Resampling.LANCZOS)
small_img.save(small_output)
print(f"✓ Guardado en: {small_output}")

print("\n" + "=" * 60)
print("¡IMÁGENES REDIMENSIONADAS EXITOSAMENTE!")
print("=" * 60)
print("\nAhora debes:")
print("1. Cerrar Revit")
print("2. Compilar el proyecto en Visual Studio")
print("3. Abrir Revit nuevamente")
print("\n¡El logo debería aparecer en tu plugin!")
