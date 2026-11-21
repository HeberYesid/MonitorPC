using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("MonitorPC")]
[assembly: AssemblyDescription("Monitor de recursos y procesos para Windows")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace MonitorPC
{
    internal static class Programa
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--autoprueba")
            {
                using (var salida = new StreamWriter(args[1])) return Pruebas.Ejecutar(salida);
            }
            if (args.Length == 1 && args[0] == "--ventana-de-prueba")
            {
                using (var f = new Form { Text = "MonitorPC · proceso desechable" })
                using (var temporizador = new Timer { Interval = 20000 })
                {
                    temporizador.Tick += delegate { f.Close(); }; temporizador.Start(); Application.Run(f);
                }
                return 0;
            }
            using (var ventana = new Ventana(Preferencias.Cargar())) Application.Run(ventana);
            return 0;
        }
    }
}
