using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace MonitorPC
{
    internal static class Pruebas
    {
        internal static int Ejecutar(TextWriter salida)
        {
            try
            {
                Registrar(salida, "Autoprueba PID " + Process.GetCurrentProcess().Id + " · " + DateTime.Now.ToString("O"));
                var prefs = new Preferencias();
                Exigir(prefs.Nivel(1, null) == 0 && prefs.Nivel(0, 64.9) == 1 && prefs.Nivel(2, 65) == 2 &&
                    prefs.Nivel(3, 90) == 3 && prefs.Nivel(4, 25 * 125000) == 2 && prefs.Nivel(5, 100 * 125000) == 3, "umbrales y datos ausentes");
                ProbarPicos(salida);
                using (var identidad = System.Security.Principal.WindowsIdentity.GetCurrent())
                    Registrar(salida, "Token administrador activo: " + new System.Security.Principal.WindowsPrincipal(identidad).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
                using (var medidor = new Medidor())
                {
                    var primera = medidor.Tomar(true); Thread.Sleep(1050); var actual = medidor.Tomar(true);
                    for (int i = 0; i < Muestra.Nombres.Length; i++) Registrar(salida, Muestra.Nombres[i] + ": " + Muestra.Texto(i, actual.Valores[i]));
                    Registrar(salida, "Espacio libre: " + actual.Espacio());
                    Exigir(!primera.Valores[0].HasValue && actual.Valores[0].HasValue && actual.Valores[0] >= 0 && actual.Valores[0] <= 100, "CPU real y calentamiento");
                    Exigir(actual.RamTotal > actual.RamLibre && actual.RamLibre > 0 && actual.Valores[1] > 0, "RAM real");
                    Exigir(actual.EspacioTotal >= actual.EspacioLibre && actual.EspacioTotal > 0, "espacio real");
                    Exigir(actual.Valores.All(v => !v.HasValue || (v.Value >= 0 && !double.IsInfinity(v.Value))), "rango de los contadores");
                    Exigir(actual.Procesos != null && actual.Procesos.Count > 5, "enumeración real de procesos");
                    using (var propio = Process.GetCurrentProcess())
                    {
                        var fila = actual.Procesos.Single(p => p.Pid == propio.Id);
                        Exigir(fila.Creado != 0 && fila.Ram > 0 && fila.Cpu.HasValue && !Acciones.Elegible(fila), "identidad, CPU y RAM por proceso; autoprotección");
                    }
                    Exigir(medidor.Tomar(false).Procesos == null, "la toma de fondo no enumera procesos");
                }
                ProbarCierre(salida, false); ProbarCierre(salida, true);
                ProbarVentana(salida);
                Registrar(salida, "Autoprueba completada sin fallos."); return 0;
            }
            catch (Exception e) { Registrar(salida, "FALLO: " + e); return 1; }
        }
        private static void ProbarPicos(TextWriter salida)
        {
            var prefs = new Preferencias();
            var picos = new HistorialPicos();
            picos.Registrar(MuestraPico(10, 83), prefs);
            picos.Registrar(MuestraPico(12, 90), prefs);
            var pico = picos.Episodios.Single();
            Exigir(pico.Indice == 0 && pico.Inicio == MuestraPico(12, 90).Hora && pico.Activo && pico.Duracion == 0,
                "un pico comienza en la primera muestra alta y una sola muestra no estima su duración");
            picos.Registrar(MuestraPico(16, 99), prefs);
            picos.Registrar(MuestraPico(19, 91), prefs);
            picos.Registrar(MuestraPico(21, 88), prefs);
            Exigir(!pico.Activo && pico.Duracion == 7 && pico.Maximo == 99,
                "la duración termina en la última muestra alta y el máximo se conserva");
            picos.Registrar(MuestraPico(36, 75), prefs);
            var contexto = picos.Contexto(pico);
            Exigir(contexto.Select(m => m.Tiempo).SequenceEqual(new double[] { 10, 12, 16, 19, 21 }),
                "la gráfica conserva contexto antes y después y excluye muestras fuera de los 15 s");
            picos.Registrar(MuestraPico(40, 98), prefs);
            picos.Registrar(MuestraPico(42, null), prefs);
            picos.Registrar(MuestraPico(44, 96), prefs);
            var trasHueco = picos.Episodios.Last();
            Exigir(picos.Episodios.Count() == 3 && trasHueco.Duracion == 0 && picos.Contexto(trasHueco).Any(m => m.Tiempo == 42 && !m.Valores[0].HasValue),
                "una lectura ausente separa episodios y es un hueco en la gráfica");
            picos.Registrar(MuestraPico(61, 97), prefs);
            Exigir(!trasHueco.Activo && picos.Episodios.Count() == 4 && picos.Episodios.Last().Duracion == 0,
                "una pausa de muestreo separa episodios aunque ambas lecturas sean altas");
            picos.Registrar(MuestraPico(961, 20), prefs);
            Exigir(picos.Episodios.Count() == 1, "un episodio permanece hasta 15 minutos desde su última lectura alta");
            picos.Registrar(MuestraPico(962, 20), prefs);
            Exigir(!picos.Episodios.Any(), "los episodios caducan después de 15 minutos");

            var limite = new HistorialPicos();
            for (int i = 0; i < 25; i++)
            {
                var m = MuestraPico(2 * i, 94); m.Valores[1] = 93;
                limite.Registrar(m, prefs);
                m = MuestraPico(2 * i + 1, 80); m.Valores[1] = 93;
                limite.Registrar(m, prefs);
            }
            Exigir(limite.Episodios.Count() == 24 && limite.Episodios.Single(e => e.Indice == 1).Duracion == 49,
                "el límite de 24 no descarta un episodio que sigue activo");
            Registrar(salida, "Picos: duración, máximo, contexto, datos ausentes, pausas, caducidad y límite de episodios.");
        }
        private static Muestra MuestraPico(double tiempo, double? cpu)
        {
            var m = new Muestra { Tiempo = tiempo, Hora = new DateTime(2026, 1, 1, 12, 0, 0).AddSeconds(tiempo) };
            m.Valores[0] = cpu;
            return m;
        }
        private static void ProbarCierre(TextWriter salida, bool forzar)
        {
            using (var hijo = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--ventana-de-prueba") { UseShellExecute = false }))
            {
                Registrar(salida, "Proceso desechable PID " + hijo.Id + " · " + hijo.StartTime.ToString("O") + " · --ventana-de-prueba");
                try
                {
                    Exigir(hijo.WaitForInputIdle(3000), "inicio de la ventana de prueba");
                    for (int i = 0; i < 30; i++) { hijo.Refresh(); if (hijo.MainWindowHandle != IntPtr.Zero) break; Thread.Sleep(50); }
                    Exigir(hijo.MainWindowHandle != IntPtr.Zero, "ventana desechable visible");
                    var lista = new MuestreadorProcesos().Tomar(1);
                    var fila = lista.Single(p => p.Pid == hijo.Id);
                    Exigir(Acciones.Elegible(fila), "el proceso desechable es elegible");
                    fila.Creado++;
                    Exigir(Acciones.Ejecutar(fila, true).StartsWith("La identidad cambió") && !hijo.HasExited, "rechazo de identidad incorrecta");
                    fila.Creado--;
                    string r = Acciones.Ejecutar(fila, forzar); Registrar(salida, r);
                    Exigir(r.StartsWith(forzar ? "Terminación solicitada" : "Cierre normal solicitado") && hijo.WaitForExit(3000), "salida real del proceso desechable");
                    Exigir(hijo.ExitCode == (forzar ? 1 : 0), "código de salida esperado");
                }
                finally
                {
                    if (!hijo.HasExited && hijo.CloseMainWindow()) hijo.WaitForExit(4500);
                    if (!hijo.HasExited) { hijo.Kill(); hijo.WaitForExit(3000); }
                    Registrar(salida, "Proceso desechable cerrado: " + hijo.HasExited);
                }
            }
        }
        private static T Campo<T>(object o, string nombre) { return (T)o.GetType().GetField(nombre, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
        private static void ProbarVentana(TextWriter salida)
        {
            Exception fallo = null;
            string pidSeleccionado = null;
            object muestraProcesos = null;
            using (var ventana = new Ventana(new Preferencias()))
            using (var reloj = new System.Windows.Forms.Timer { Interval = 200 })
            {
                var tiempo = Stopwatch.StartNew(); int paso = 0;
                reloj.Tick += delegate
                {
                    try
                    {
                        Exigir(tiempo.Elapsed.TotalSeconds < 12, "límite de prueba de interfaz");
                        var tabs = Campo<TabControl>(ventana, "tabs");
                        var tabla = Campo<TablaProcesos>(ventana, "tabla");
                        var grid = Campo<DataGridView>(tabla, "tabla");
                        var filtro = Campo<TextBox>(tabla, "buscar");
                        if (paso == 0 && Campo<Muestra>(ventana, "ultima") != null)
                        {
                            Exigir(tabs.SelectedTab.Text == "Rendimiento", "pestaña inicial");
                            tabs.SelectedIndex = 1; paso++;
                        }
                        else if (paso == 1 && grid.RowCount > 1)
                        {
                            filtro.Text = Application.ExecutablePath;
                            Exigir(grid.RowCount == 2 && grid[0, 0].Value.ToString() == "Grupo", "filtro y agrupación reales");
                            grid.CurrentCell = grid[1, 1];
                            Campo<Button>(tabla, "fijar").PerformClick();
                            Exigir(grid[0, 0].Value.ToString() == "Fijo" && grid.CurrentCell.RowIndex == 0, "fijación conserva selección");
                            pidSeleccionado = grid[2, 0].Value.ToString();
                            typeof(TablaProcesos).GetMethod("Ordenar", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tabla, new object[] { 4 });
                            Exigir(grid[2, grid.CurrentCell.RowIndex].Value.ToString() == pidSeleccionado, "ordenación conserva selección");
                            filtro.Text = "zzz_sin_resultado_monitorpc_zzz";
                            Exigir(grid.RowCount == 0 && grid.CurrentCell == null, "búsqueda vacía no selecciona otro proceso");
                            filtro.Clear(); Exigir(grid[2, grid.CurrentCell.RowIndex].Value.ToString() == pidSeleccionado, "selección restaurada al quitar filtro");
                            muestraProcesos = Campo<object>(tabla, "datos"); paso++;
                        }
                        else if (paso == 2 && !ReferenceEquals(Campo<object>(tabla, "datos"), muestraProcesos))
                        {
                            Exigir(grid[0, grid.CurrentCell.RowIndex].Value.ToString() == "Fijo" &&
                                grid[2, grid.CurrentCell.RowIndex].Value.ToString() == pidSeleccionado, "la selección y fijación sobreviven a otra muestra real");
                            tabs.SelectedIndex = 0;
                            ventana.Hide(); var iconos = Campo<NotifyIcon[]>(ventana, "recursos");
                            Exigir(!ventana.Visible && !Campo<NotifyIcon>(ventana, "bandeja").Visible && iconos.Length == 3 &&
                                iconos.Select((n, i) => n.Visible && n.Icon != null && n.Text.StartsWith(Muestra.Nombres[i] + ":")).All(v => v), "CPU, RAM y disco simultáneos con la ventana oculta");
                            var tipo = typeof(Ventana);
                            tipo.GetMethod("MostrarResumen", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ventana, null);
                            var resumen = Campo<ContextMenuStrip>(ventana, "resumen");
                            Exigir(resumen != null && resumen.Visible && resumen.Items.Cast<ToolStripItem>().Any(it => it.Text.StartsWith("Espacio libre")), "resumen visible desde el fondo");
                            resumen.Close();
                            typeof(Ventana).GetMethod("Abrir", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ventana, null);
                            Exigir(ventana.Visible, "restaurar desde la bandeja");
                            Registrar(salida, "Interfaz: rendimiento, filtro, agrupación, fijación, ordenación, selección entre muestras y resumen de bandeja.");
                            tabs.SelectedIndex = 2;
                            ventana.Size = ventana.MinimumSize;
                            ProbarPanelPicos(tabs.SelectedTab.Controls.OfType<PanelPicos>().Single(), salida);
                            reloj.Stop(); ventana.Salir();
                        }
                    }
                    catch (Exception e) { fallo = e; reloj.Stop(); ventana.Salir(); }
                };
                reloj.Start(); Application.Run(ventana);
            }
            if (fallo != null) ExceptionDispatchInfo.Capture(fallo).Throw();
        }
        private static void ProbarPanelPicos(PanelPicos panel, TextWriter salida)
        {
            // Alimenta el panel real sin generar carga en el equipo ni depender de sus contadores.
            var picos = new HistorialPicos();
            var lista = Campo<ListBox>(panel, "lista");
            var grafica = Campo<Grafica>(panel, "grafica");
            var detalle = Campo<Label>(panel, "detalle");
            var prefs = new Preferencias();
            panel.Cargar(picos);
            Exigir(!grafica.Visible && detalle.Text.Contains("no se detectaron"), "estado sin picos");
            picos.Registrar(MuestraPico(10, 70), prefs);
            picos.Registrar(MuestraPico(12, 98), prefs);
            panel.Cargar(picos);
            Exigir(lista.Items.Count == 1 && grafica.Visible && detalle.Text.Contains("En curso") && detalle.Text.Contains("Una sola muestra"),
                "un pico activo aparece seleccionado con su gráfica y duración indeterminada");
            picos.Registrar(MuestraPico(16, 91), prefs);
            picos.Registrar(MuestraPico(20, 75), prefs);
            picos.Registrar(MuestraPico(22, 92), prefs);
            panel.Cargar(picos);
            Exigir(lista.SelectedIndex == 1 && detalle.Text.Contains("Finalizado") && detalle.Text.Contains("4 s") && detalle.Text.Contains("98 %"),
                "se conserva el pico seleccionado y se actualiza su detalle al aparecer otro");
            lista.SelectedIndex = 0;
            Exigir(detalle.Text.Contains("92 %") && grafica.Visible, "seleccionar otro episodio cambia el detalle");
            Exigir(grafica.Width >= 125 && grafica.Height >= 80, "la gráfica cabe en la ventana al tamaño mínimo");
            ExigirGraficaPicos(grafica);
            Registrar(salida, "Interfaz de picos: estado vacío, detalle, selección estable y trazado de la gráfica al tamaño mínimo.");
        }
        private static void ExigirGraficaPicos(Grafica grafica)
        {
            using (var mapa = new System.Drawing.Bitmap(grafica.Width, grafica.Height))
            {
                grafica.DrawToBitmap(mapa, grafica.ClientRectangle);
                // Entre las etiquetas no basta con que pinte el fondo: debe verse al menos parte de la serie.
                bool trazo = false;
                var tinta = SystemInformation.HighContrast ? System.Drawing.SystemColors.WindowText : System.Drawing.Color.FromArgb(20, 91, 168);
                for (int y = 35; y < mapa.Height - 20 && !trazo; y++)
                    for (int x = 12; x < mapa.Width - 16; x++)
                        if (mapa.GetPixel(x, y).ToArgb() == tinta.ToArgb()) { trazo = true; break; }
                Exigir(trazo, "la gráfica del pico pinta muestras, no solo el fondo y los ejes");
            }
        }
        private static void Exigir(bool ok, string titulo) { if (!ok) throw new InvalidOperationException(titulo); }
        private static void Registrar(TextWriter w, string linea) { w.WriteLine(linea); w.Flush(); }
    }
}
