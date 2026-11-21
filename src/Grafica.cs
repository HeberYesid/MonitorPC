using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MonitorPC
{
    internal sealed class Grafica : Control
    {
        private static readonly Color[] Tintas = { Color.FromArgb(20, 91, 168), Color.FromArgb(112, 53, 169), Color.FromArgb(14, 113, 106), Color.FromArgb(156, 71, 12), Color.FromArgb(30, 99, 187), Color.FromArgb(142, 61, 126) };
        private static readonly Color Regla = Color.FromArgb(218, 225, 234);
        private readonly int indice;
        internal List<Muestra> Datos;
        internal EpisodioPico Episodio;
        internal const int Segundos = 90;
        internal Grafica() : this(-1) { }
        internal Grafica(int indice)
        {
            this.indice = indice;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            ForeColor = Color.FromArgb(24, 40, 61);
            AccessibleName = indice < 0 ? "Gráfica del episodio" : "Historial de " + Muestra.Nombres[indice];
            Dock = DockStyle.Fill;
            Margin = new Padding(4);
            Font = SystemFonts.MessageBoxFont;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Visible || Datos == null || Datos.Count == 0 || Width < 125 || Height < 80) return;
            bool detalle = Episodio != null;
            int serie = detalle ? Episodio.Indice : indice;
            if (serie < 0) return;
            var primero = Datos[0];
            var ultimo = Datos[Datos.Count - 1];
            double inicio = detalle ? primero.Tiempo : ultimo.Tiempo - Segundos;
            double lapso = ultimo.Tiempo - inicio;
            bool contraste = SystemInformation.HighContrast;
            Color texto = contraste ? SystemColors.WindowText : ForeColor;
            if (contraste) e.Graphics.Clear(SystemColors.Window);
            var rect = Rectangle.FromLTRB(10, 33, Width - 12, Height - 19);
            double escala = serie < 4 ? 100 : Math.Max(125000, Datos.Max(m => detalle ? m.Valores[serie].GetValueOrDefault() :
                Math.Max(m.Valores[4].GetValueOrDefault(), m.Valores[5].GetValueOrDefault())));
            string tope = "Tope: " + (serie < 4 ? "100 %" : Muestra.Texto(serie, escala));
            string titulo = detalle ? "Muestras de " + Muestra.Nombres[serie] : Muestra.Nombres[serie] + "   ·   " + Muestra.Texto(serie, ultimo.Valores[serie]);
            int anchoTope = detalle ? TextRenderer.MeasureText(tope, Font).Width : 0;
            TextRenderer.DrawText(e.Graphics, titulo, Font, new Rectangle(8, 6, Math.Max(0, Width - 16 - anchoTope), 24), texto,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (detalle) TextRenderer.DrawText(e.Graphics, tope, Font, new Rectangle(8, 6, Width - 16, 24), texto, TextFormatFlags.Right);
            Color tinta = contraste ? SystemColors.WindowText : Tintas[serie];
            using (var regla = new Pen(contraste ? SystemColors.GrayText : Regla))
            using (var trazo = new Pen(tinta, 2))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int y = rect.Top + rect.Height * i / 4;
                    int x = rect.Left + rect.Width * i / 4;
                    e.Graphics.DrawLine(regla, rect.Left, y, rect.Right, y);
                    e.Graphics.DrawLine(regla, x, rect.Top, x, rect.Bottom);
                }
                if (detalle)
                {
                    using (var limite = new Pen(texto) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                    {
                        if (Episodio.PrimeraAlta >= inicio)
                            Marca(e.Graphics, limite, rect, inicio, lapso, Episodio.PrimeraAlta);
                        if (Episodio.UltimaAlta != Episodio.PrimeraAlta)
                            Marca(e.Graphics, limite, rect, inicio, lapso, Episodio.UltimaAlta);
                    }
                }
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                PointF? previo = null;
                PointF? maximo = null;
                double previoTiempo = 0;
                foreach (var dato in Datos)
                {
                    if (!dato.Valores[serie].HasValue || dato.Tiempo < inicio) { previo = null; continue; }
                    var punto = new PointF(Posicion(rect, inicio, lapso, dato.Tiempo),
                        rect.Bottom - (float)(rect.Height * Math.Min(1, dato.Valores[serie].Value / escala)));
                    if (previo.HasValue && dato.Tiempo - previoTiempo <= Muestra.PausaMaxima) e.Graphics.DrawLine(trazo, previo.Value, punto);
                    else e.Graphics.DrawEllipse(trazo, punto.X - 1, punto.Y - 1, 2, 2);
                    if (detalle && dato.Tiempo == Episodio.TiempoMaximo) maximo = punto;
                    previo = punto;
                    previoTiempo = dato.Tiempo;
                }
                if (maximo.HasValue)
                    using (var puntoMaximo = new SolidBrush(tinta))
                        e.Graphics.FillEllipse(puntoMaximo, maximo.Value.X - 3, maximo.Value.Y - 3, 6, 6);
            }
            string izquierda = detalle ? primero.Hora.ToString("HH:mm:ss") : "−90 s";
            string derecha = detalle ? (lapso > 0 ? ultimo.Hora.ToString("HH:mm:ss") : "") : tope;
            TextRenderer.DrawText(e.Graphics, izquierda, Font, new Point(rect.Left, rect.Bottom + 1), texto);
            TextRenderer.DrawText(e.Graphics, derecha, Font, new Rectangle(rect.Left, rect.Bottom + 1, rect.Width, 18), texto, TextFormatFlags.Right);
        }
        private static float Posicion(Rectangle rect, double inicio, double lapso, double tiempo)
        {
            return rect.Left + (float)(rect.Width * (lapso > 0 ? (tiempo - inicio) / lapso : 0.5));
        }
        private static void Marca(Graphics g, Pen limite, Rectangle rect, double inicio, double lapso, double tiempo)
        {
            float x = Posicion(rect, inicio, lapso, tiempo);
            g.DrawLine(limite, x, rect.Top, x, rect.Bottom);
        }
    }
}
