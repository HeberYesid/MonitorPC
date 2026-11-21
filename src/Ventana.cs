using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MonitorPC
{
    internal sealed class Ventana : Form
    {
        private readonly Medidor medidor = new Medidor();
        private readonly Iconos iconos = new Iconos();
        private readonly NotifyIcon bandeja = new NotifyIcon();
        private readonly NotifyIcon[] recursos = { new NotifyIcon(), new NotifyIcon(), new NotifyIcon() };
        private readonly Icon[] dibujos = new Icon[3];
        private readonly int[] claves = { -1, -1, -1 };
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem[] indicadores = new ToolStripMenuItem[Preferencias.Bandeja.Length];
        private readonly ToolStripMenuItem estadoBandeja = new ToolStripMenuItem();
        private readonly System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly TabPage rendimiento = new TabPage("Rendimiento"), procesos = new TabPage("Procesos"), picos = new TabPage("Picos recientes");
        private readonly TablaProcesos tabla = new TablaProcesos();
        private readonly Grafica[] graficas = new Grafica[Muestra.Nombres.Length];
        private readonly List<Muestra> historial = new List<Muestra>(128);
        private readonly PanelPicos panelPicos = new PanelPicos();
        private readonly HistorialPicos historialPicos = new HistorialPicos();
        private readonly Label pie = new Label(), espacio = new Label(), ram = new Label();
        private readonly Preferencias configuracion;
        private Task<Muestra> trabajo;
        private readonly ContextMenuStrip resumen = new ContextMenuStrip { ShowImageMargin = false };
        private Muestra ultima;
        private bool salir, liberado;
        private int nivelActual = -1;
        private string aviso;
        private bool EnPrimerPlano { get { return Visible && WindowState != FormWindowState.Minimized; } }

        internal Ventana(Preferencias preferencias)
        {
            configuracion = preferencias;
            Text = "MonitorPC"; Font = SystemFonts.MessageBoxFont; AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(900, 610); MinimumSize = new Size(630, 460);
            BackColor = SystemColors.Control; Icon = iconos.Icono[1];
            var baseLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
            baseLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); baseLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            baseLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var cabecera = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 9) };
            var titulo = new Label { Text = "MonitorPC", AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Margin = new Padding(0, 0, 15, 0) };
            var ajustar = new Button { Text = "&Configurar bandeja", AutoSize = true, MinimumSize = new Size(0, 29) };
            var ocultar = new Button { Text = "&Ocultar en bandeja", AutoSize = true, MinimumSize = new Size(0, 29) };
            ajustar.Click += delegate { Configurar(); }; ocultar.Click += delegate { Hide(); };
            cabecera.Controls.Add(titulo); cabecera.Controls.Add(ajustar); cabecera.Controls.Add(ocultar);
            baseLayout.Controls.Add(cabecera, 0, 0); baseLayout.Controls.Add(tabs, 0, 1);
            pie.Dock = DockStyle.Fill; pie.AutoSize = true; pie.Padding = new Padding(0, 7, 0, 0); pie.Text = "Esperando la primera muestra de Windows…";
            baseLayout.Controls.Add(pie, 0, 2); Controls.Add(baseLayout);
            tabs.TabPages.AddRange(new[] { rendimiento, procesos, picos }); procesos.Controls.Add(tabla);
            CrearRendimiento(); picos.Controls.Add(panelPicos); CrearBandeja();
            tabs.SelectedIndexChanged += delegate { if (EnPrimerPlano) { MostrarDatos(); Acelerar(); } };
            Shown += delegate { Muestrear(); };
            VisibleChanged += delegate { if (EnPrimerPlano && IsHandleCreated) { MostrarDatos(); Acelerar(); } };
            Resize += delegate { if (EnPrimerPlano) Acelerar(); else if (WindowState == FormWindowState.Minimized) Hide(); };
            reloj.Tick += delegate { Muestrear(); };
        }

        private void CrearRendimiento()
        {
            var bloques = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(5) };
            bloques.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); bloques.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < 3; i++) bloques.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            bloques.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int i = 0; i < graficas.Length; i++)
            {
                graficas[i] = new Grafica(i) { Datos = historial }; bloques.Controls.Add(graficas[i], i % 2, i / 2);
            }
            var detalle = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(4) };
            espacio.AutoSize = ram.AutoSize = true; espacio.Text = "Espacio en unidad de Windows: no disponible"; ram.Text = "RAM disponible: no disponible";
            espacio.Margin = new Padding(0, 0, 18, 0);
            detalle.Controls.Add(espacio); detalle.Controls.Add(ram); bloques.Controls.Add(detalle, 0, 3); bloques.SetColumnSpan(detalle, 2);
            rendimiento.Controls.Add(bloques);
        }
        private void CrearBandeja()
        {
            menu.Items.Add("Abrir MonitorPC", null, delegate { Abrir(); });
            menu.Items.Add("Ver resumen", null, delegate { MostrarResumen(); });
            estadoBandeja.Enabled = false; menu.Items.Add(estadoBandeja);
            var elegir = new ToolStripMenuItem("Indicador junto al reloj");
            for (int i = 0; i < indicadores.Length; i++)
            {
                int indice = i;
                indicadores[i] = new ToolStripMenuItem(Preferencias.Bandeja[i]);
                indicadores[i].Click += delegate { configuracion.Indicador = indice; GuardarPreferencias(); ActualizarBandeja(); };
                elegir.DropDownItems.Add(indicadores[i]);
            }
            menu.Items.Add(elegir); menu.Items.Add("Umbrales…", null, delegate { Configurar(); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Salir de MonitorPC", null, delegate { Salir(); });
            bandeja.ContextMenuStrip = menu; bandeja.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MostrarResumen(); };
            foreach (var indicador in recursos) {
                indicador.ContextMenuStrip = menu;
                indicador.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MostrarResumen(); };
            }
            ActualizarBandeja();
        }
        private int Intervalo()
        {
            bool cargaAlta = ultima != null && ultima.Valores.Where((v, i) => configuracion.Nivel(i, v) == 3).Any();
            if (EnPrimerPlano) return tabs.SelectedTab == rendimiento ? 1000 : tabs.SelectedTab == procesos ? 1800 : 2500;
            return cargaAlta ? 4000 : 8000;
        }
        private void Acelerar()
        {
            if (salir || liberado || trabajo != null || !IsHandleCreated) return;
            reloj.Stop(); reloj.Interval = 180; reloj.Start();
        }
        private async void Muestrear()
        {
            if (salir || liberado || trabajo != null) return;
            reloj.Stop(); bool listar = EnPrimerPlano && tabs.SelectedTab == procesos;
            try
            {
                trabajo = Task.Run(() => medidor.Tomar(listar));
                var m = await trabajo;
                if (salir || liberado) return;
                var listaProcesos = m.Procesos;
                m.Procesos = null; // Las listas de procesos no forman parte del historial.
                aviso = null;
                ultima = m; RegistrarMuestra(m); ActualizarBandeja();
                if (EnPrimerPlano)
                {
                    if (listar && tabs.SelectedTab == procesos) tabla.Cargar(listaProcesos);
                    MostrarDatos();
                }
            }
            catch (Exception ex)
            {
                if (!salir && !liberado)
                {
                    if (trabajo != null && trabajo.IsFaulted) RegistrarMuestra(medidor.NuevaMuestra());
                    ultima = null;
                    aviso = "No se pudo tomar la muestra (" + ex.GetType().Name + "). Se reintentará.";
                    if (EnPrimerPlano && tabs.SelectedTab == procesos) tabla.Cargar(null);
                    MostrarDatos(); ActualizarBandeja();
                }
            }
            finally
            {
                trabajo = null;
                if (!salir && !liberado) { reloj.Interval = Intervalo(); reloj.Start(); }
            }
        }
        private void MostrarDatos()
        {
            if (!EnPrimerPlano) return;
            pie.Text = aviso ?? (ultima == null ? "Esperando la primera muestra de Windows…" :
                "Última muestra: " + ultima.Hora.ToString("HH:mm:ss") + "   ·   " + (Intervalo() / 1000.0).ToString("0.#") + " s entre tomas");
            if (tabs.SelectedTab == rendimiento)
            {
                espacio.Text = "Espacio libre: " + (ultima == null ? "No disponible" : ultima.Espacio());
                ram.Text = "RAM disponible: " + (ultima == null ? "No disponible" : Muestra.GiB(ultima.RamLibre) + " de " + Muestra.GiB(ultima.RamTotal));
                foreach (var g in graficas) { g.Datos = historial; g.Invalidate(); }
            }
            if (tabs.SelectedTab == picos) panelPicos.Cargar(historialPicos);
        }
        private void RegistrarMuestra(Muestra m)
        {
            historial.Add(m);
            int vigentes = historial.FindIndex(v => m.Tiempo - v.Tiempo <= Grafica.Segundos);
            if (vigentes > 0) historial.RemoveRange(0, vigentes);
            historialPicos.Registrar(m, configuracion);
        }
        private void ActualizarBandeja()
        {
            for (int j = 0; j < indicadores.Length; j++) indicadores[j].Checked = j == configuracion.Indicador;
            bool triple = configuracion.Indicador == Preferencias.Triple;
            if (triple) bandeja.Visible = false;
            // Explorer coloca el último icono agregado a la izquierda en esta bandeja.
            for (int j = recursos.Length - 1; j >= 0; j--)
            {
                if (!triple) { recursos[j].Visible = false; continue; }
                double? dato = ultima == null ? null : ultima.Valores[j];
                int carga = configuracion.Nivel(j, dato);
                int numero = dato.HasValue ? (int)Math.Round(Math.Max(0, Math.Min(100, dato.Value))) : -1;
                int clave = carga * 102 + numero + 1;
                if (claves[j] != clave) {
                    var nuevo = Iconos.Medida(numero, carga);
                    recursos[j].Icon = nuevo;
                    if (dibujos[j] != null) dibujos[j].Dispose();
                    dibujos[j] = nuevo; claves[j] = clave;
                    recursos[j].Text = Muestra.Nombres[j] + ": " + (numero < 0 ? "Sin datos" : numero + " %");
                }
                recursos[j].Visible = true;
            }
            if (triple) { estadoBandeja.Text = "CPU + RAM + Disco"; return; }
            int i = configuracion.Indicador - 1;
            double? valor = ultima == null ? null : i >= 0 ? ultima.Valores[i] : ultima.Valores.Take(4).Max();
            int nivel = configuracion.Nivel(i, valor);
            if (nivel != nivelActual) { bandeja.Icon = iconos.Icono[nivel]; nivelActual = nivel; }
            string texto = Preferencias.Bandeja[i + 1] + ": " + Muestra.Texto(i, valor) + " · " + Iconos.Etiquetas[nivel];
            if (estadoBandeja.Text != texto) estadoBandeja.Text = texto;
            string tooltip = "MonitorPC | " + texto;
            if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);
            if (bandeja.Text != tooltip) bandeja.Text = tooltip;
            bandeja.Visible = true;
        }
        private void GuardarPreferencias()
        {
            try { configuracion.Guardar(); }
            catch (IOException) { PreferenciasNoGuardadas(); }
            catch (UnauthorizedAccessException) { PreferenciasNoGuardadas(); }
        }
        private void PreferenciasNoGuardadas() { MessageBox.Show("Los cambios se aplicaron, pero no se pudieron guardar para la próxima ejecución.", "MonitorPC", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void Configurar()
        {
            int alto = configuracion.Alto, redAlto = configuracion.RedAlto;
            using (var dialogo = new Ajustes(configuracion))
            {
                var resultado = EnPrimerPlano ? dialogo.ShowDialog(this) : dialogo.ShowDialog();
                if (resultado != DialogResult.OK) return;
            }
            for (int i = 0; i < Muestra.Nombres.Length; i++)
                if (i < 4 ? alto != configuracion.Alto : redAlto != configuracion.RedAlto) historialPicos.Interrumpir(i);
            GuardarPreferencias(); ActualizarBandeja(); MostrarDatos();
        }
        private void MostrarResumen()
        {
            if (resumen.Visible) { resumen.Close(); return; }
            LimpiarResumen();
            resumen.Items.Add(new ToolStripLabel("MonitorPC · " + (ultima == null ? "Sin muestra" : ultima.Hora.ToString("HH:mm:ss"))));
            for (int i = 0; i < Muestra.Nombres.Length; i++)
            {
                double? valor = ultima == null ? null : ultima.Valores[i];
                resumen.Items.Add(new ToolStripLabel(Muestra.Nombres[i] + ": " + Muestra.Texto(i, valor) + " · " + Iconos.Etiquetas[configuracion.Nivel(i, valor)],
                    iconos.Icono[configuracion.Nivel(i, valor)].ToBitmap()));
            }
            resumen.Items.Add(new ToolStripLabel("Espacio libre: " + (ultima == null ? "No disponible" : ultima.Espacio())));
            resumen.Items.Add(new ToolStripSeparator()); resumen.Items.Add("Abrir monitor", null, delegate { Abrir(); });
            resumen.Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft);
        }
        private void LimpiarResumen()
        {
            while (resumen.Items.Count > 0)
            {
                var item = resumen.Items[0]; resumen.Items.RemoveAt(0);
                if (item.Image != null) item.Image.Dispose();
                item.Dispose();
            }
        }
        private void Abrir() { WindowState = FormWindowState.Normal; Show(); Activate(); }
        internal void Salir() { salir = true; Close(); }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!salir && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !liberado)
            {
                liberado = true;
                reloj.Dispose(); resumen.Close(); LimpiarResumen(); resumen.Dispose(); bandeja.Visible = false; bandeja.Dispose(); menu.Dispose();
                foreach (var recurso in recursos) recurso.Dispose();
                foreach (var dibujo in dibujos) if (dibujo != null) dibujo.Dispose();
                Icon = null; iconos.Dispose();
                var pendiente = trabajo;
                if (pendiente == null) medidor.Dispose();
                else pendiente.ContinueWith(t => { GC.KeepAlive(t.Exception); medidor.Dispose(); }, TaskScheduler.Default);
            }
            base.Dispose(disposing);
        }
    }
}
