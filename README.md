# MonitorPC

Monitor de recursos y procesos para Windows 10/11 x64 con .NET Framework 4.8 o posterior. Es una aplicación de escritorio en español hecha con controles nativos de Windows Forms. No requiere instalación, navegador, paquetes externos ni permisos de administrador; el ejecutable usa los permisos de la cuenta que lo abre.

## Uso

1. Si descargó el código, [compile primero la aplicación](#compilar-y-probar-localmente). Luego abra **`bin\MonitorPC.exe`**.
2. **Rendimiento** muestra los últimos 90 segundos de CPU, RAM, disco activo, GPU, subida y bajada. **Espacio libre** es otra lectura: indica el espacio disponible en la unidad donde está Windows, no el tiempo que el disco está ocupado.
3. En **Procesos**, busque por nombre, ruta o PID. Haga clic en una columna para ordenar (o use **Alt+1…5** con foco en la tabla). Active o desactive **Agrupar por aplicación**. Seleccione una fila de proceso y pulse **Fijar** para mantenerla arriba mientras esa instancia siga activa. La búsqueda también se aplica a las filas fijadas.
4. **Pedir cierre** solicita a la ventana principal que se cierre; la aplicación puede pedir que guarde, ignorar la solicitud o no tener ventana. **Forzar cierre** exige confirmación y puede perder cambios sin guardar. Las acciones nunca se aplican a un grupo completo.
5. Al cerrar, minimizar u ocultar la ventana, MonitorPC sigue en la bandeja. Haga **clic izquierdo** en el indicador para ver el resumen o **clic derecho** para abrir el monitor, elegir qué se muestra junto al reloj, ajustar umbrales o **Salir de MonitorPC**. Si Windows guarda el icono en la zona de desbordamiento, búsquelo bajo la flecha de iconos ocultos y arrástrelo junto al reloj, si lo desea.
6. **CPU + RAM + Disco** muestra tres porcentajes junto al reloj, sin etiquetas ni símbolo de porcentaje. El orden previsto de izquierda a derecha es CPU, RAM y actividad del disco. Al pasar el puntero aparece el nombre. Un guion sobre fondo gris indica que no hay lectura. Es el modo inicial en instalaciones nuevas; también puede elegirlo en **Configurar bandeja**. Si Windows oculta los iconos o conserva otro orden, arrástrelos a la posición deseada.
7. **Picos recientes** conserva hasta 24 episodios con lecturas en rojo durante los últimos 15 minutos; se borran al salir. Seleccione uno para ver cuándo empezó, si sigue en curso, su máximo y una gráfica con hasta 15 segundos de contexto antes y después. La duración es el tiempo entre la primera y la última lectura alta del episodio; una sola muestra no permite estimarla. Los huecos de muestreo no cuentan como carga sostenida. Los episodios muy largos pueden mostrar solo el tramo reciente de la gráfica. Pueden perderse picos entre muestras.

En la bandeja, **verde** (una barra) es carga baja, **amarillo** (dos) es media, **rojo** (tres) es alta y **gris** (guion) significa sin lectura. La carga general usa el mayor valor disponible entre CPU, RAM, disco y GPU. Los umbrales por defecto son 65/90 % y 25/100 Mb/s para red. Solo se guardan las preferencias explícitas en `%LOCALAPPDATA%\MonitorPC\preferencias.txt`; no hay recopilación continua en disco, inicio automático ni tráfico propio de la app.

## Qué mide y qué protege

| Lectura | Alcance |
| --- | --- |
| CPU | Tiempo activo de procesador calculado a partir de `GetSystemTimes`; no pondera la frecuencia. Puede diferir del Administrador de tareas, y en equipos con varios grupos de procesadores esta API puede representar solo un grupo. |
| RAM | Memoria física total menos disponible. Por proceso, RAM residente (puede incluir páginas compartidas; sumar filas no equivale a RAM física exclusiva). |
| Disco | Mayor porcentaje de actividad entre los discos físicos visibles a PDH. El espacio libre se lee aparte, cada 30 s, en la unidad de Windows. |
| GPU | Motor de GPU más ocupado entre adaptadores, agregando las instancias de procesos de ese motor. Depende del controlador y de los contadores de Windows. |
| Red | Tasas de envío y recepción sumadas para interfaces activas, excluidos bucle local y túneles declarados como tales. Adaptadores virtuales o VPN pueden contabilizar parte del tráfico más de una vez. La gráfica usa una escala común para subida y bajada. |
| Procesos | CPU normalizada respecto a la capacidad lógica de la máquina y RAM residente. Los grupos usan ruta de ejecutable (nombre como alternativa si no se puede leer la ruta), no una deducción de familias entre ejecutables distintos. |

Una primera muestra, un cambio de adaptador, las restricciones de acceso o la ausencia de un contador pueden producir **No disponible** o **N/D**, y se dejan huecos en las gráficas en vez de inventar ceros. La tabla no muestra disco, GPU ni red por proceso.

El cierre requiere un proceso de la misma cuenta y sesión interactiva. MonitorPC excluye su propio proceso, ejecutables bajo la carpeta de Windows, nombres reservados y procesos que Windows marque como críticos. Si no puede verificar PID, hora de creación, ruta, cuenta y estado del proceso, no actúa. El cierre forzado usa el mismo identificador de proceso abierto para verificar y terminar; el cierre normal vuelve a comprobar el propietario de la ventana antes de enviar la solicitud. La aplicación no se eleva para saltar un acceso denegado.

La app obtiene muestras en segundo plano: aprox. cada 1 s en Rendimiento, 1,8 s en Procesos, 2,5 s en Picos y 4–8 s en la bandeja según la carga. Solo enumera procesos con esa pestaña visible y no redibuja las gráficas ocultas.

## Compilar y probar localmente

Desde PowerShell, en esta carpeta:

```powershell
.\compilar.ps1
.\probar.ps1
```

El script de compilación usa el `csc.exe` de .NET Framework instalado en Windows y crea `bin\MonitorPC.exe`. La autoprueba necesita una sesión de escritorio interactiva. Toma muestras reales, comprueba umbrales y valores ausentes, y prueba cierre normal/forzado e identidad incorrecta **solo** con dos instancias desechables de MonitorPC que crea ella misma. Prueba también filtro, grupos, fijación, selección, ordenación, el resumen de bandeja y el detalle de picos en una ventana WinForms real. Los contadores de disco, GPU y red se informan incluso cuando no están disponibles en el equipo. El informe se guarda en una carpeta temporal, no como registro de la app.

## Integración continua

[GitHub Actions](.github/workflows/ci.yml) compila con advertencias tratadas como errores y ejecuta la autoprueba sin escritorio en Windows Server 2022 y 2025 x64. Se ejecuta en cada push a `main`, en los pull requests hacia `main` o manualmente desde **Actions → CI**. Los informes de prueba quedan disponibles durante 14 días, también si una autoprueba falla y genera un informe. Un push a `main` conserva además el ejecutable que pasó la autoprueba en Windows Server 2025. No crea releases ni despliega.

Para reproducir la comprobación de CI en Windows:

```powershell
.\compilar.ps1 -Directorio 'bin\ci'
.\probar.ps1 -Directorio 'bin\ci' -SinEscritorio
```

Ese modo comprueba umbrales, picos y mediciones de Windows, pero no prueba ventanas, bandeja ni acciones de cierre. Para esos casos ejecute `probar.ps1` sin `-SinEscritorio` desde un escritorio interactivo. Las versiones de Windows Server de CI no sustituyen una prueba manual en Windows 10/11.
