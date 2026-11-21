using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MonitorPC
{
    internal sealed class Proceso
    {
        internal int Pid;
        internal ulong Creado, CpuAcumulada;
        internal string Nombre, Ruta;
        internal double? Cpu, Ram;
        internal string Grupo { get { return Ruta ?? Nombre; } }
        internal string Clave { get { return Creado == 0 ? null : Pid + ":" + Creado; } }
    }

    internal sealed class MuestreadorProcesos
    {
        private Dictionary<int, Proceso> anterior = new Dictionary<int, Proceso>();
        private double ultimo;
        internal void Reiniciar() { if (anterior.Count != 0) anterior.Clear(); ultimo = 0; }

        internal List<Proceso> Tomar(double tiempo)
        {
            var actual = new Dictionary<int, Proceso>();
            double delta = tiempo - ultimo;
            using (var captura = WinProceso.CreateToolhelp32Snapshot(2, 0))
            {
                if (captura.IsInvalid) { Reiniciar(); return null; }
                var entrada = new WinProceso.Entrada { Largo = (uint)Marshal.SizeOf(typeof(WinProceso.Entrada)) };
                if (!WinProceso.Process32First(captura, ref entrada)) { Reiniciar(); return null; }
                do
                {
                    var p = new Proceso { Pid = entrada.Pid, Nombre = entrada.Nombre };
                    actual[p.Pid] = p;
                    using (var h = WinProceso.OpenProcess(WinProceso.Consulta, false, p.Pid))
                    {
                        ulong kernel, user, salida;
                        if (h.IsInvalid || !WinProceso.GetProcessTimes(h, out p.Creado, out salida, out kernel, out user) || salida != 0)
                        { p.Creado = 0; continue; }
                        p.CpuAcumulada = kernel + user;
                        Proceso previo;
                        if (anterior.TryGetValue(p.Pid, out previo) && previo.Creado == p.Creado && p.Creado != 0 && previo.Nombre == p.Nombre)
                        {
                            p.Ruta = previo.Ruta;
                            if (delta > 0 && delta <= 30 && p.CpuAcumulada >= previo.CpuAcumulada)
                                p.Cpu = Medidor.Limitar((p.CpuAcumulada - previo.CpuAcumulada) / (delta * Environment.ProcessorCount * 100000));
                        }
                        if (p.Ruta == null) p.Ruta = WinProceso.Ruta(h);
                        // El nombre del handle es más fiable si la captura coincidió con un PID reutilizado.
                        if (p.Ruta != null) p.Nombre = Path.GetFileName(p.Ruta);
                        var memoria = new WinProceso.Memoria { Largo = (uint)Marshal.SizeOf(typeof(WinProceso.Memoria)) };
                        if (WinProceso.GetProcessMemoryInfo(h, ref memoria, memoria.Largo)) p.Ram = memoria.Residente.ToUInt64();
                    }
                } while (WinProceso.Process32Next(captura, ref entrada));
            }
            anterior = actual; ultimo = tiempo;
            return new List<Proceso>(actual.Values);
        }
    }

    internal static class Acciones
    {
        private static readonly int Propio;
        private static readonly uint Sesion;
        private static readonly SecurityIdentifier Usuario;
        private static readonly string CarpetaWindows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        private static readonly HashSet<string> Reservados = new HashSet<string>(new[] {
            "System", "Registry", "Secure System", "Idle", "Memory Compression", "smss.exe", "csrss.exe", "wininit.exe",
            "services.exe", "lsass.exe", "winlogon.exe", "svchost.exe", "lsm.exe", "dwm.exe", "explorer.exe", "fontdrvhost.exe",
            "spoolsv.exe", "sihost.exe", "taskhostw.exe", "MsMpEng.exe", "SecurityHealthService.exe", "audiodg.exe", "LogonUI.exe"
        }, StringComparer.OrdinalIgnoreCase);
        static Acciones()
        {
            using (var p = Process.GetCurrentProcess()) Propio = p.Id;
            WinProceso.ProcessIdToSessionId(Propio, out Sesion);
            using (var identidad = WindowsIdentity.GetCurrent()) Usuario = identidad.User;
        }

        internal static bool Elegible(Proceso p)
        {
            return p != null && p.Pid > 4 && p.Pid != Propio && p.Creado != 0 && RutaPermitida(p.Ruta);
        }
        private static bool RutaPermitida(string ruta)
        {
            return !string.IsNullOrEmpty(ruta) && !ruta.StartsWith(CarpetaWindows, StringComparison.OrdinalIgnoreCase) &&
                   !Reservados.Contains(Path.GetFileName(ruta));
        }
        // El mismo handle comprobado se usa para terminar. No se vuelve a abrir por PID tras validar.
        internal static string Ejecutar(Proceso p, bool forzar)
        {
            if (!Elegible(p)) return "MonitorPC protege este proceso o no puede verificar su identidad.";
            using (var h = WinProceso.OpenProcess(WinProceso.Consulta | WinProceso.Sincronizar | (forzar ? 1u : 0u), false, p.Pid))
            {
                if (h.IsInvalid) return "No se pudo abrir el proceso. Puede haber terminado o Windows denegó el acceso.";
                ulong creado, salida, kernel, user;
                if (!WinProceso.GetProcessTimes(h, out creado, out salida, out kernel, out user) || creado != p.Creado || salida != 0)
                    return "La identidad cambió o el proceso ya no está activo. No se hizo ningún cambio.";
                string ruta = WinProceso.Ruta(h);
                bool critico;
                uint sesion;
                if (!RutaPermitida(ruta) || !string.Equals(p.Ruta, ruta, StringComparison.OrdinalIgnoreCase) ||
                    !WinProceso.IsProcessCritical(h, out critico) || critico ||
                    !WinProceso.ProcessIdToSessionId(p.Pid, out sesion) || sesion == 0 || sesion != Sesion || !MismoUsuario(h))
                    return "No se realizó el cierre: proceso protegido, de otra cuenta o sesión, o no se pudo verificar.";
                if (WinProceso.WaitForSingleObject(h, 0) != 258) return "El proceso ya no está activo o no se pudo verificar.";
                if (forzar)
                    return WinProceso.TerminateProcess(h, 1) ? "Terminación solicitada a Windows." : "Windows rechazó la terminación.";

                var ventana = IntPtr.Zero;
                WinProceso.EnumWindows(delegate(IntPtr hwnd, IntPtr ignorado)
                {
                    int dueno;
                    WinProceso.GetWindowThreadProcessId(hwnd, out dueno);
                    if (dueno != p.Pid || !WinProceso.IsWindowVisible(hwnd) || WinProceso.GetWindow(hwnd, 4) != IntPtr.Zero) return true;
                    ventana = hwnd;
                    return false;
                }, IntPtr.Zero);
                int pidVentana;
                if (ventana == IntPtr.Zero) return "No hay una ventana principal a la que pedirle el cierre.";
                if (WinProceso.GetWindowThreadProcessId(ventana, out pidVentana) == 0 || pidVentana != p.Pid || WinProceso.WaitForSingleObject(h, 0) != 258)
                    return "La ventana cambió o el proceso ya no está activo.";
                return WinProceso.PostMessage(ventana, 0x0010, IntPtr.Zero, IntPtr.Zero)
                    ? "Cierre normal solicitado. La aplicación puede pedir guardar cambios o rechazarlo."
                    : "Windows no aceptó la solicitud de cierre normal.";
            }
        }
        private static bool MismoUsuario(SafeFileHandle proceso)
        {
            SafeFileHandle token;
            if (Usuario == null || !WinProceso.OpenProcessToken(proceso, 8, out token)) return false;
            using (token)
            {
                int bytes;
                WinProceso.GetTokenInformation(token, 1, IntPtr.Zero, 0, out bytes);
                if (bytes <= 0 || bytes > 65536) return false;
                IntPtr b = Marshal.AllocHGlobal(bytes);
                try
                {
                    if (!WinProceso.GetTokenInformation(token, 1, b, bytes, out bytes)) return false;
                    return Usuario.Equals(new SecurityIdentifier(Marshal.ReadIntPtr(b)));
                }
                finally { Marshal.FreeHGlobal(b); }
            }
        }
    }

    internal static class WinProceso
    {
        internal const uint Consulta = 0x1000, Sincronizar = 0x100000;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct Entrada
        {
            internal uint Largo, Uso;
            internal int Pid;
            internal IntPtr Heap;
            internal uint Modulo, Hilos, Padre;
            internal int Prioridad;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Nombre;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Memoria
        {
            internal uint Largo, Fallos;
            internal UIntPtr PicoResidente, Residente, PicoPaged, Paged, PicoNoPaged, NoPaged, Pagina, PicoPagina;
        }
        internal static string Ruta(SafeFileHandle h)
        {
            int largo = 1024;
            var texto = new StringBuilder(largo);
            return QueryFullProcessImageName(h, 0, texto, ref largo) ? texto.ToString() : null;
        }
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern bool Process32First(SafeFileHandle snapshot, ref Entrada entrada);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern bool Process32Next(SafeFileHandle snapshot, ref Entrada entrada);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeFileHandle OpenProcess(uint flags, bool heredar, int pid);
        [DllImport("kernel32.dll")] internal static extern bool GetProcessTimes(SafeFileHandle h, out ulong creado, out ulong salida, out ulong kernel, out ulong user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(SafeFileHandle h, uint flags, StringBuilder ruta, ref int tamano);
        [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")] internal static extern bool GetProcessMemoryInfo(SafeFileHandle h, ref Memoria memoria, uint largo);
        [DllImport("kernel32.dll")] internal static extern bool IsProcessCritical(SafeFileHandle h, out bool critico);
        [DllImport("kernel32.dll")] internal static extern bool ProcessIdToSessionId(int id, out uint sesion);
        [DllImport("kernel32.dll")] internal static extern bool TerminateProcess(SafeFileHandle h, uint codigo);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(SafeFileHandle h, uint ms);
        [DllImport("advapi32.dll")] internal static extern bool OpenProcessToken(SafeFileHandle h, uint acceso, out SafeFileHandle token);
        [DllImport("advapi32.dll")] internal static extern bool GetTokenInformation(SafeFileHandle token, int clase, IntPtr buffer, int capacidad, out int bytes);
        internal delegate bool Ventana(IntPtr hwnd, IntPtr dato);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(Ventana visitar, IntPtr dato);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint relacion);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool PostMessage(IntPtr hwnd, uint mensaje, IntPtr w, IntPtr l);
    }
}
