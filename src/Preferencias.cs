using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing;

namespace MonitorPC
{
    internal sealed class Preferencias
    {
        internal const int Triple = 7;
        internal static readonly string[] Bandeja = { "Carga general", "CPU", "RAM", "Disco activo", "GPU", "Subida", "Bajada", "CPU + RAM + Disco" };
        internal int Indicador = Triple, Aviso = 65, Alto = 90, RedAviso = 25, RedAlto = 100;
        private static readonly string Archivo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorPC", "preferencias.txt");

        internal static Preferencias Cargar()
        {
            try
            {
                var partes = File.ReadAllText(Archivo).Trim().Split(';');
                if (partes.Length == 5)
                {
                    var n = partes.Select(int.Parse).ToArray();
                    if (n[0] >= 0 && n[0] < Bandeja.Length && n[1] >= 1 && n[1] < n[2] && n[2] <= 100 &&
                        n[3] >= 1 && n[3] < n[4] && n[4] <= 100000)
                        return new Preferencias { Indicador = n[0], Aviso = n[1], Alto = n[2], RedAviso = n[3], RedAlto = n[4] };
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (FormatException) { }
            catch (OverflowException) { }
            return new Preferencias();
        }
        internal void Guardar()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Archivo));
            File.WriteAllText(Archivo, string.Join(";", Indicador, Aviso, Alto, RedAviso, RedAlto));
        }
        internal int Nivel(int indice, double? valor)
        {
            if (!valor.HasValue) return 0;
            double n = valor.Value / (indice < 4 ? 1 : 125000);
            return n >= (indice < 4 ? Alto : RedAlto) ? 3 : n >= (indice < 4 ? Aviso : RedAviso) ? 2 : 1;
        }
    }

    internal sealed class Iconos : IDisposable
    {
        internal static readonly string[] Etiquetas = { "Sin datos", "Baja", "Media", "Alta" };
        internal readonly Icon[] Icono = new Icon[4];
        private static readonly Color[] Colores = { Color.FromArgb(79, 88, 103), Color.FromArgb(13, 122, 76), Color.FromArgb(255, 202, 69), Color.FromArgb(187, 44, 58) };
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icono);
        internal Iconos()
        {
            for (int i = 0; i < 4; i++)
            using (var mapa = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(mapa))
            using (var b = new SolidBrush(Colores[i]))
            using (var borde = new Pen(Color.FromArgb(210, 226, 236)))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(b, 0.8f, 0.8f, 14.4f, 14.4f);
                g.DrawEllipse(borde, 1, 1, 14, 14);
                var contraste = i == 2 ? Brushes.Black : Brushes.White;
                if (i == 0) g.FillRectangle(contraste, 5, 7, 6, 2);
                for (int j = 0; j < i; j++) g.FillRectangle(contraste, 4 + j * 3, 11 - 2 - 2 * j, 2, 2 + 2 * j);
                IntPtr h = mapa.GetHicon();
                try { using (var temporal = Icon.FromHandle(h)) Icono[i] = (Icon)temporal.Clone(); }
                finally { DestroyIcon(h); }
            }
        }
        private const string Letras = "0123456789-";
        private static readonly string[] Glifos = { "75557", "26227", "71747", "71717", "55711", "74717", "74757", "71111", "75757", "75717", "00700" };
        private static void Escribir(Graphics g, string texto, Brush tinta, int ancho)
        {
            int x = (16 - (texto.Length * (3 * ancho + 1) - 1)) / 2;
            foreach (char c in texto)
            {
                string filas = Glifos[Letras.IndexOf(c)];
                for (int f = 0; f < 5; f++) for (int b = 0; b < 3; b++)
                    if (((filas[f] - '0') & (4 >> b)) != 0) g.FillRectangle(tinta, x + b * ancho, 3 + f * 2, ancho, 2);
                x += 3 * ancho + 1;
            }
        }
        internal static Icon Medida(int numero, int nivel)
        {
            using (var mapa = new Bitmap(16, 16)) using (var g = Graphics.FromImage(mapa))
            {
                g.Clear(Colores[nivel]); var tinta = nivel == 2 ? Brushes.Black : Brushes.White;
                string cifras = numero < 0 ? "-" : numero.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Escribir(g, cifras, tinta, cifras.Length < 3 ? 2 : 1);
                IntPtr h = mapa.GetHicon();
                try { using (var temporal = Icon.FromHandle(h)) return (Icon)temporal.Clone(); }
                finally { DestroyIcon(h); }
            }
        }
        public void Dispose() { foreach (var icono in Icono) icono.Dispose(); }
    }
}
