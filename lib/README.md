# ASComm IoT Library

Esta carpeta debe contener la DLL de ASComm IoT para que el proyecto compile.

## Instrucciones

1. **Obtener la DLL de ASComm IoT**:
   - Descarga desde: https://automatedsolutions.com/products/iot/ascommiot/
   - O copia desde tu instalación de ASComm IoT

2. **Copiar la DLL aquí**:
   ```
   Conduit/lib/AutomatedSolutions.ASCommStd.dll
   ```

3. **Estructura esperada**:
   ```
   Conduit/
   ├── lib/
   │   └── AutomatedSolutions.ASCommStd.dll  ← Coloca la DLL aquí
   └── src/
       └── Conduit.AsComm/
   ```

## Alternativas

### Opción 1: NuGet (si está disponible)
Descomenta en `Conduit.AsComm.csproj`:
```xml
<PackageReference Include="AutomatedSolutions.ASCommStd" Version="1.4.*" />
```

### Opción 2: Variable de entorno
```bash
export ASCommPath="/ruta/a/tu/instalacion/ASComm IoT 1.4 Developer"
```

### Opción 3: Ruta personalizada
Edita `Conduit.AsComm.csproj` y ajusta la ruta en Option 3.
