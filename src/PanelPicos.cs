using System;
using System.Linq;
using System.Windows.Forms;

namespace MonitorPC
{
    internal sealed class PanelPicos : UserControl
    {
        private readonly ListBox lista = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Episodios de carga alta" };
        private readonly Label detalle = new Label { Dock = DockStyle.Fill, AutoSize = true };
        private readonly Grafica grafica = new Grafica();
        private HistorialPicos historial;
        private bool actualizando;

        internal PanelPicos()
        {
            Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var nota = new Label { Text = "Últimos 15 minutos de actividad · hasta 24 episodios · solo en memoria", AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
            layout.Controls.Add(nota, 0, 0); layout.SetColumnSpan(nota, 2);
            lista.HorizontalScrollbar = true; lista.IntegralHeight = false;
            layout.Controls.Add(lista, 0, 1);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(10, 3, 3, 3) };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(detalle, 0, 0); panel.Controls.Add(grafica, 0, 1);
            layout.Controls.Add(panel, 1, 1); Controls.Add(layout);
            lista.SelectedIndexChanged += delegate { if (!actualizando) MostrarDetalle(); };
            MostrarDetalle();
        }
        internal void Cargar(HistorialPicos datos)
        {
            historial = datos;
            var episodios = datos.Episodios.Reverse().ToArray();
            if (!lista.Items.Cast<EpisodioPico>().SequenceEqual(episodios))
            {
                var elegido = lista.SelectedItem as EpisodioPico;
                int indice = Array.IndexOf(episodios, elegido);
                actualizando = true; lista.BeginUpdate();
                try
                {
                    lista.Items.Clear(); lista.Items.AddRange(episodios);
                    if (episodios.Length > 0) lista.SelectedIndex = Math.Max(0, indice);
                }
                finally { lista.EndUpdate(); actualizando = false; }
            }
            MostrarDetalle();
        }
        private void MostrarDetalle()
        {
            var episodio = lista.SelectedItem as EpisodioPico;
            grafica.Episodio = episodio;
            grafica.Visible = episodio != null;
            if (episodio == null)
            {
                detalle.Text = "Todavía no se detectaron picos.";
                grafica.Datos = null;
                return;
            }
            grafica.Datos = historial.Contexto(episodio);
            string texto = Muestra.Nombres[episodio.Indice] + " · " + (episodio.Activo ? "En curso" : "Finalizado") +
                "\nInicio: " + episodio.Inicio.ToString("HH:mm:ss") +
                "\nDuración observada: " + Duracion(episodio.Duracion) +
                "\nMáximo: " + Muestra.Texto(episodio.Indice, episodio.Maximo);
            if (grafica.Datos.Count > 0 && grafica.Datos[0].Tiempo > episodio.PrimeraAlta)
                texto += "\nLa gráfica muestra solo el tramo reciente.";
            texto += "\nLíneas punteadas: primera y última lectura alta.";
            detalle.Text = texto;
            grafica.AccessibleName = "Muestras alrededor del pico de " + Muestra.Nombres[episodio.Indice];
            grafica.Invalidate();
        }
        private static string Duracion(double segundos)
        {
            if (segundos == 0) return "Una sola muestra";
            if (segundos < 1) return "Menos de 1 s";
            var tiempo = TimeSpan.FromSeconds(Math.Round(segundos));
            return (tiempo.TotalHours >= 1 ? (long)tiempo.TotalHours + " h " : "") +
                (tiempo.TotalMinutes >= 1 ? tiempo.Minutes + " min " : "") + tiempo.Seconds + " s";
        }
    }
}
