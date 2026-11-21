using System.Drawing;
using System.Windows.Forms;

namespace MonitorPC
{
    internal sealed class Ajustes : Form
    {
        internal Ajustes(Preferencias p)
        {
            Text = "Configurar bandeja"; Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false; ShowInTaskbar = true;
            var panel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14) };
            var indicador = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 165, AccessibleName = "Indicador junto al reloj" };
            indicador.Items.AddRange(Preferencias.Bandeja); indicador.SelectedIndex = p.Indicador;
            var aviso = Numero(p.Aviso, 100, "Amarillo (%)"); var alto = Numero(p.Alto, 100, "Rojo (%)");
            var redAviso = Numero(p.RedAviso, 100000, "Amarillo de red (Mb/s)"); var redAlto = Numero(p.RedAlto, 100000, "Rojo de red (Mb/s)");
            Fila(panel, "Indicador junto al reloj", indicador, 0);
            Fila(panel, "Amarillo desde (%)", aviso, 1); Fila(panel, "Rojo desde (%)", alto, 2);
            Fila(panel, "Red: amarillo desde (Mb/s)", redAviso, 3); Fila(panel, "Red: rojo desde (Mb/s)", redAlto, 4);
            var nota = new Label { Text = "El verde indica una carga inferior al umbral amarillo.\nLa carga general usa el mayor valor de CPU, RAM, disco o GPU.", AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
            panel.Controls.Add(nota, 0, 5); panel.SetColumnSpan(nota, 2);
            var botones = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var guardar = new Button { Text = "&Guardar", AutoSize = true, MinimumSize = new Size(85, 31) };
            var cancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(85, 31) };
            botones.Controls.Add(guardar); botones.Controls.Add(cancelar); panel.Controls.Add(botones, 0, 6); panel.SetColumnSpan(botones, 2);
            AcceptButton = guardar; CancelButton = cancelar; Controls.Add(panel);
            guardar.Click += delegate
            {
                if (aviso.Value >= alto.Value || redAviso.Value >= redAlto.Value)
                {
                    MessageBox.Show(this, "Cada umbral amarillo debe ser menor que su umbral rojo.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    (aviso.Value >= alto.Value ? aviso : redAviso).Focus(); return;
                }
                p.Indicador = indicador.SelectedIndex; p.Aviso = (int)aviso.Value; p.Alto = (int)alto.Value;
                p.RedAviso = (int)redAviso.Value; p.RedAlto = (int)redAlto.Value; DialogResult = DialogResult.OK;
            };
        }
        private static NumericUpDown Numero(int valor, int maximo, string nombre)
        { return new NumericUpDown { Minimum = 1, Maximum = maximo, Value = valor, Width = 90, AccessibleName = nombre }; }
        private static void Fila(TableLayoutPanel p, string texto, Control c, int fila)
        {
            p.Controls.Add(new Label { Text = texto, AutoSize = true, Anchor = AnchorStyles.Left }, 0, fila); p.Controls.Add(c, 1, fila);
        }
    }
}
