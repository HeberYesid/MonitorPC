using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace MonitorPC
{
    internal sealed class Muestra
    {
        internal const int PausaMaxima = 16;
        internal static readonly string[] Nombres = { "CPU", "RAM", "Disco activo", "GPU", "Subida", "Bajada" };
        internal readonly double?[] Valores = new double?[6];
        internal double Tiempo;
        internal DateTime Hora;
        internal long? RamTotal, RamLibre, EspacioTotal, EspacioLibre;
        internal string Unidad;
        internal List<Proceso> Procesos;
        internal static string Texto(int indice, double? valor)
        {
            if (!valor.HasValue) return "No disponible";
            if (indice < 4) return valor.Value.ToString("0.#") + " %";
            return (valor.Value / 125000).ToString("0.##") + " Mb/s";
        }
        internal static string GiB(long? bytes)
        {
            return bytes.HasValue ? (bytes.Value / 1073741824.0).ToString("0.0") + " GiB" : "No disponible";
        }
        internal string Espacio()
        {
            return EspacioLibre.HasValue ? Unidad + "  " + GiB(EspacioLibre) + " libres de " + GiB(EspacioTotal) : "No disponible";
        }
    }

    internal sealed class Medidor : IDisposable
    {
        private readonly Stopwatch reloj = Stopwatch.StartNew();
        private readonly Pdh pdh = new Pdh();
        private readonly MuestreadorProcesos procesos = new MuestreadorProcesos();
        private readonly string unidad = Path.GetPathRoot(Environment.SystemDirectory);
        private ulong anteriorTotal, anteriorIdle;
        private double ultimaRed, ultimoEspacio = -60;
        private long? libre, total;
        private struct Red { internal long Subida, Bajada; }
        private Dictionary<string, Red> redPrevia = new Dictionary<string, Red>();

        [StructLayout(LayoutKind.Sequential)]
        private struct Memoria
        {
            internal uint Largo, Carga;
            internal ulong TotalFisica, LibreFisica, TotalPagina, LibrePagina, TotalVirtual, LibreVirtual, LibreExtendida;
        }
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref Memoria memoria);

        internal Muestra NuevaMuestra()
        {
            return new Muestra { Tiempo = reloj.Elapsed.TotalSeconds, Hora = DateTime.Now, Unidad = unidad };
        }
        internal Muestra Tomar(bool listarProcesos)
        {
            var m = NuevaMuestra();
            ulong idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                ulong actual = kernel + user;
                if (anteriorTotal != 0 && actual > anteriorTotal && idle >= anteriorIdle)
                    m.Valores[0] = Limitar(100 - 100.0 * (idle - anteriorIdle) / (actual - anteriorTotal));
                anteriorTotal = actual;
                anteriorIdle = idle;
            }
            else anteriorTotal = 0;

            var mem = new Memoria { Largo = (uint)Marshal.SizeOf(typeof(Memoria)) };
            if (GlobalMemoryStatusEx(ref mem) && mem.TotalFisica > 0 && mem.TotalFisica >= mem.LibreFisica)
            {
                m.RamTotal = (long)mem.TotalFisica;
                m.RamLibre = (long)mem.LibreFisica;
                m.Valores[1] = 100.0 * (mem.TotalFisica - mem.LibreFisica) / mem.TotalFisica;
            }
            pdh.Leer(m);
            LeerRed(m);
            if (m.Tiempo - ultimoEspacio >= 30)
            {
                ultimoEspacio = m.Tiempo;
                libre = total = null;
                try
                {
                    var disco = new DriveInfo(unidad);
                    if (disco.IsReady) { total = disco.TotalSize; libre = disco.AvailableFreeSpace; }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            m.EspacioLibre = libre; m.EspacioTotal = total;
            if (listarProcesos) m.Procesos = procesos.Tomar(m.Tiempo);
            else procesos.Reiniciar();
            return m;
        }

        private void LeerRed(Muestra m)
        {
            var actual = new Dictionary<string, Red>();
            double subida = 0, bajada = 0;
            bool comparable = false, incompleta = false;
            try
            {
                foreach (var adaptador in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adaptador.OperationalStatus != OperationalStatus.Up || adaptador.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        adaptador.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    try
                    {
                        var stats = adaptador.GetIPStatistics();
                        var v = new Red { Subida = stats.BytesSent, Bajada = stats.BytesReceived };
                        actual[adaptador.Id] = v;
                        Red previo;
                        if (!redPrevia.TryGetValue(adaptador.Id, out previo) || v.Subida < previo.Subida || v.Bajada < previo.Bajada)
                        { incompleta = true; continue; }
                        comparable = true;
                        subida += v.Subida - previo.Subida;
                        bajada += v.Bajada - previo.Bajada;
                    }
                    catch (NetworkInformationException) { incompleta = true; }
                }
            }
            catch (NetworkInformationException) { incompleta = true; }
            double segundos = m.Tiempo - ultimaRed;
            if (comparable && !incompleta && segundos > 0 && segundos <= 30)
            {
                m.Valores[4] = subida / segundos;
                m.Valores[5] = bajada / segundos;
            }
            redPrevia = actual;
            ultimaRed = m.Tiempo;
        }

        internal static double Limitar(double v) { return Math.Max(0, Math.Min(100, v)); }
        public void Dispose() { pdh.Dispose(); }
    }

    internal sealed class Pdh : IDisposable
    {
        private readonly IntPtr consulta, disco, gpu;
        private IntPtr buffer;
        private uint capacidad;
        private bool baseLista;
        private const uint MasDatos = 0x800007D2, Doble = 0x200, SinTope = 0x8000;
        [StructLayout(LayoutKind.Sequential)] private struct Valor { internal uint Estado; internal double Dato; }
        [StructLayout(LayoutKind.Sequential)] private struct Entrada { internal IntPtr Nombre; internal Valor Valor; }
        private static readonly int TamanoEntrada = Marshal.SizeOf(typeof(Entrada));

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string origen, IntPtr usuario, out IntPtr consulta);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr consulta, string ruta, IntPtr usuario, out IntPtr contador);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr consulta);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr contador, uint formato, ref uint bytes, out uint cantidad, IntPtr datos);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr consulta);

        internal Pdh()
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out consulta) != 0) return;
            disco = Agregar(@"\PhysicalDisk(*)\% Idle Time");
            gpu = Agregar(@"\GPU Engine(*)\Utilization Percentage");
        }
        private IntPtr Agregar(string ruta)
        {
            IntPtr contador;
            return PdhAddEnglishCounter(consulta, ruta, IntPtr.Zero, out contador) == 0 ? contador : IntPtr.Zero;
        }
        internal void Leer(Muestra m)
        {
            if (consulta == IntPtr.Zero || PdhCollectQueryData(consulta) != 0) { baseLista = false; return; }
            if (!baseLista) { baseLista = true; return; }
            double? maxDisco = null;
            LeerVector(disco, delegate(string nombre, double idle)
            {
                if (string.Equals(nombre, "_Total", StringComparison.OrdinalIgnoreCase)) return;
                double ocupado = Medidor.Limitar(100 - idle);
                maxDisco = Math.Max(maxDisco.GetValueOrDefault(), ocupado);
            });
            m.Valores[2] = maxDisco;
            var motores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            LeerVector(gpu, delegate(string nombre, double carga)
            {
                int inicio = nombre.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                if (inicio < 0) return;
                string motor = nombre.Substring(inicio);
                int repetido = motor.LastIndexOf('#');
                if (repetido >= 0) motor = motor.Substring(0, repetido);
                double suma;
                motores.TryGetValue(motor, out suma);
                motores[motor] = suma + carga;
            });
            if (motores.Count != 0) m.Valores[3] = Medidor.Limitar(motores.Values.Max());
        }
        private void LeerVector(IntPtr contador, Action<string, double> usar)
        {
            if (contador == IntPtr.Zero) return;
            for (int intento = 0; intento < 2; intento++)
            {
                uint bytes = capacidad, cantidad;
                uint estado = PdhGetFormattedCounterArray(contador, Doble | SinTope, ref bytes, out cantidad, buffer);
                if (estado == MasDatos)
                {
                    bytes = 0;
                    PdhGetFormattedCounterArray(contador, Doble | SinTope, ref bytes, out cantidad, IntPtr.Zero);
                    if (bytes == 0 || bytes > 16 * 1024 * 1024) return;
                    if (bytes > capacidad)
                    {
                        IntPtr nuevo = Marshal.AllocHGlobal((IntPtr)bytes);
                        if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                        buffer = nuevo;
                        capacidad = bytes;
                    }
                    continue;
                }
                if (estado != 0 || cantidad > capacidad / TamanoEntrada) return;
                for (int i = 0; i < cantidad; i++)
                {
                    var e = (Entrada)Marshal.PtrToStructure(IntPtr.Add(buffer, i * TamanoEntrada), typeof(Entrada));
                    if (e.Valor.Estado <= 1 && e.Nombre != IntPtr.Zero && !double.IsNaN(e.Valor.Dato) && !double.IsInfinity(e.Valor.Dato))
                        usar(Marshal.PtrToStringUni(e.Nombre), e.Valor.Dato);
                }
                return;
            }
        }
        public void Dispose()
        {
            if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
            if (consulta != IntPtr.Zero) PdhCloseQuery(consulta);
        }
    }
}
