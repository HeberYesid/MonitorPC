# MonitorPC

Monitor nativo de recursos y procesos para Windows 10/11 x64. Permite ver tendencias, inspeccionar procesos y mantener un indicador de carga en la bandeja. No requiere cuenta, servidor ni elevación para iniciarse.

## Arquitectura y flujos

[`Programa.cs`](src/Programa.cs) es la entrada de Windows Forms. [`Ventana.cs`](src/Ventana.cs) coordina pantalla, eventos y bandeja. [`Mediciones.cs`](src/Mediciones.cs) obtiene lecturas y [`Grafica.cs`](src/Grafica.cs) las presenta. [`Procesos.cs`](src/Procesos.cs) y [`TablaProcesos.cs`](src/TablaProcesos.cs) separan los procesos del modo de mostrarlos. [`Preferencias.cs`](src/Preferencias.cs) guarda la configuración del usuario.

**Muestreo:** los contadores del equipo alimentan la gráfica y el indicador de bandeja. La aplicación conserva una ventana reciente y un resumen temporal de picos; el [README](README.md) distingue esos datos volátiles de las preferencias persistidas. No hay telemetría remota documentada.

**Cerrar un proceso:** el usuario selecciona una instancia en la tabla. «Pedir cierre» solicita a la ventana que se cierre; «Forzar cierre» implica confirmación y puede perder datos no guardados. No se aplica a un grupo completo ni eleva los permisos del usuario.

## Arranque y lectura

Abre `bin\MonitorPC.exe` según el README, o usa [`compilar.ps1`](compilar.ps1) y [`probar.ps1`](probar.ps1) en el entorno indicado. Requiere .NET Framework 4.8 o posterior. Lee `Programa.cs` → `Ventana.cs` → `Mediciones.cs` y, para las acciones de procesos, `TablaProcesos.cs` → `Procesos.cs`.
