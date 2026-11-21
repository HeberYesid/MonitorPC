using System;
using System.Collections.Generic;
using System.Linq;

namespace MonitorPC
{
    internal sealed class EpisodioPico
    {
        internal readonly int Indice;
        internal readonly DateTime Inicio;
        internal readonly double PrimeraAlta;
        internal double UltimaAlta { get; private set; }
        internal double TiempoMaximo { get; private set; }
        internal double Maximo { get; private set; }
        internal bool Activo { get; private set; }
        internal double Duracion { get { return UltimaAlta - PrimeraAlta; } }

        internal EpisodioPico(int indice, Muestra m)
        {
            Indice = indice; Inicio = m.Hora; PrimeraAlta = m.Tiempo;
            Maximo = m.Valores[indice].Value; TiempoMaximo = m.Tiempo;
            RegistrarAlta(m); Activo = true;
        }
        internal void RegistrarAlta(Muestra m)
        {
            UltimaAlta = m.Tiempo;
            double valor = m.Valores[Indice].Value;
            if (valor > Maximo) { Maximo = valor; TiempoMaximo = m.Tiempo; }
        }
        internal void Cerrar() { Activo = false; }
        public override string ToString() { return Inicio.ToString("HH:mm:ss") + "   ·   " + Muestra.Nombres[Indice]; }
    }

    internal sealed class HistorialPicos
    {
        private const int Retencion = 900, Margen = 15, Capacidad = 24;
        private readonly Queue<Muestra> muestras = new Queue<Muestra>();
        private readonly List<EpisodioPico> episodios = new List<EpisodioPico>();
        private readonly EpisodioPico[] activos = new EpisodioPico[Muestra.Nombres.Length];
        internal IEnumerable<EpisodioPico> Episodios { get { return episodios; } }

        internal void Registrar(Muestra m, Preferencias configuracion)
        {
            muestras.Enqueue(m);
            while (m.Tiempo - muestras.Peek().Tiempo > Retencion + Margen) muestras.Dequeue();
            for (int i = 0; i < activos.Length; i++)
            {
                var anterior = activos[i];
                bool alta = configuracion.Nivel(i, m.Valores[i]) == 3;
                if (anterior != null && (!alta || m.Tiempo - anterior.UltimaAlta > Muestra.PausaMaxima)) Interrumpir(i);
                if (!alta) continue;
                if (activos[i] == null) { activos[i] = new EpisodioPico(i, m); episodios.Add(activos[i]); }
                else activos[i].RegistrarAlta(m);
            }
            episodios.RemoveAll(e => m.Tiempo - e.UltimaAlta > Retencion);
            // Al agotar el límite, conserva los episodios con actividad más reciente, incluidos los que siguen activos.
            while (episodios.Count > Capacidad)
                episodios.Remove(episodios.OrderBy(e => e.UltimaAlta).ThenBy(e => e.PrimeraAlta).First());
        }
        internal void Interrumpir(int indice)
        {
            if (activos[indice] == null) return;
            activos[indice].Cerrar(); activos[indice] = null;
        }
        internal List<Muestra> Contexto(EpisodioPico e)
        {
            return muestras.Where(m => m.Tiempo >= e.PrimeraAlta - Margen && m.Tiempo <= e.UltimaAlta + Margen).ToList();
        }
    }
}
