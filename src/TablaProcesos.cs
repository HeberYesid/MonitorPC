using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MonitorPC
{
    internal sealed class TablaProcesos : UserControl
    {
        private sealed class Fila
        {
            internal Proceso Proceso;
            internal string Clave, Nombre, Conteo;
            internal double? Cpu, Ram;
            internal bool Fija, Grupo;
        }
        private sealed class Rejilla : DataGridView { internal Rejilla() { DoubleBuffered = true; } }
        private readonly Rejilla tabla = new Rejilla();
        private readonly TextBox buscar = new TextBox { Width = 210, AccessibleName = "Buscar por nombre, ruta o PID" };
        private readonly CheckBox agrupar = new CheckBox { Text = "Agrupar por aplicación", AutoSize = true, Checked = true, Margin = new Padding(12, 7, 4, 0) };
        private readonly Button fijar = Boton("Fijar"), cerrar = Boton("Pedir cierre"), forzar = Boton("Forzar cierre");
        private readonly Label estado = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly HashSet<string> fijas = new HashSet<string>();
        private List<Proceso> datos;
        private List<Fila> filas = new List<Fila>();
        private int orden = 3;
        private bool descendente = true, reconstruyendo;
        private string seleccion;

        internal TablaProcesos()
        {
            Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var filtros = Barra();
            filtros.Controls.Add(new Label { Text = "&Buscar:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            filtros.Controls.Add(buscar); filtros.Controls.Add(agrupar);
            layout.Controls.Add(filtros, 0, 0);
            tabla.Dock = DockStyle.Fill; tabla.ReadOnly = true; tabla.VirtualMode = true; tabla.RowHeadersVisible = false;
            tabla.AllowUserToAddRows = false; tabla.AllowUserToDeleteRows = false; tabla.AllowUserToResizeRows = false;
            tabla.MultiSelect = false; tabla.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            tabla.BackgroundColor = SystemColors.Window; tabla.BorderStyle = BorderStyle.FixedSingle;
            tabla.GridColor = Color.FromArgb(224, 231, 240); tabla.RowTemplate.Height = 25;
            tabla.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 249, 253);
            tabla.AccessibleName = "Tabla de procesos";
            Columna("Estado", 68, false); Columna("Aplicación / proceso", 180, true); Columna("PID", 102, false);
            Columna("CPU", 95, false); Columna("RAM residente", 120, false);
            tabla.Columns[orden].HeaderCell.SortGlyphDirection = SortOrder.Descending;
            tabla.CellValueNeeded += Valor; tabla.CellFormatting += Formato;
            tabla.CellToolTipTextNeeded += delegate(object s, DataGridViewCellToolTipTextNeededEventArgs e)
            {
                if (e.ColumnIndex != 1 || e.RowIndex < 0 || e.RowIndex >= filas.Count) return;
                var f = filas[e.RowIndex];
                if (f.Proceso != null) e.ToolTipText = f.Proceso.Ruta ?? "Ruta no disponible";
            };
            tabla.ColumnHeaderMouseClick += delegate(object s, DataGridViewCellMouseEventArgs e) { Ordenar(e.ColumnIndex); };
            tabla.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D5) { Ordenar((int)e.KeyCode - (int)Keys.D1); e.Handled = true; }
            };
            tabla.CurrentCellChanged += delegate { if (!reconstruyendo) { var f = Seleccionada(); seleccion = f == null ? null : f.Clave; Botones(); } };
            layout.Controls.Add(tabla, 0, 1);
            var acciones = Barra(); acciones.Controls.Add(fijar); acciones.Controls.Add(cerrar); acciones.Controls.Add(forzar);
            layout.Controls.Add(acciones, 0, 2); estado.AutoSize = true; estado.Padding = new Padding(0, 3, 0, 0);
            layout.Controls.Add(estado, 0, 3); Controls.Add(layout);
            buscar.TextChanged += delegate { Reconstruir(true); }; agrupar.CheckedChanged += delegate { Reconstruir(true); };
            fijar.Click += delegate
            {
                var f = Seleccionada();
                if (f == null || f.Proceso == null || f.Proceso.Clave == null) return;
                if (!fijas.Add(f.Clave)) fijas.Remove(f.Clave);
                Reconstruir(true);
            };
            cerrar.Click += delegate { Cerrar(false); }; forzar.Click += delegate { Cerrar(true); };
            Reconstruir(false);
        }

        private static FlowLayoutPanel Barra() { return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 4) }; }
        private static Button Boton(string texto) { return new Button { Text = texto, AutoSize = true, MinimumSize = new Size(86, 30) }; }
        private void Columna(string titulo, int ancho, bool llenar)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = titulo, Width = ancho, SortMode = DataGridViewColumnSortMode.Programmatic };
            if (llenar) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.MinimumWidth = 150; }
            tabla.Columns.Add(c);
        }
        internal void Cargar(List<Proceso> lista)
        {
            datos = lista;
            if (datos != null && fijas.Count > 0)
            {
                var existentes = new HashSet<string>(datos.Select(p => p.Clave));
                fijas.RemoveWhere(p => !existentes.Contains(p));
            }
            Reconstruir(false);
        }
        private void Ordenar(int columna)
        {
            tabla.Columns[orden].HeaderCell.SortGlyphDirection = SortOrder.None;
            descendente = orden == columna ? !descendente : columna >= 3;
            orden = columna;
            tabla.Columns[orden].HeaderCell.SortGlyphDirection = descendente ? SortOrder.Descending : SortOrder.Ascending;
            Reconstruir(true);
        }
        private Fila Seleccionada()
        {
            var celda = tabla.CurrentCell;
            return celda != null && celda.RowIndex >= 0 && celda.RowIndex < filas.Count ? filas[celda.RowIndex] : null;
        }
        private void Reconstruir(bool forzarVisibilidad)
        {
            var top = tabla.FirstDisplayedScrollingRowIndex;
            string ancla = top >= 0 && top < filas.Count ? filas[top].Clave : null;
            var visibles = new List<Fila>();
            string filtro = buscar.Text.Trim();
            if (datos != null)
            foreach (var p in datos)
            {
                if (filtro.Length > 0 && p.Nombre.IndexOf(filtro, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    p.Pid.ToString().IndexOf(filtro, StringComparison.Ordinal) < 0 &&
                    (p.Ruta == null || p.Ruta.IndexOf(filtro, StringComparison.CurrentCultureIgnoreCase) < 0)) continue;
                visibles.Add(new Fila { Proceso = p, Clave = p.Clave, Nombre = p.Nombre, Cpu = p.Cpu, Ram = p.Ram, Fija = p.Clave != null && fijas.Contains(p.Clave) });
            }
            var salida = visibles.Where(f => f.Fija).ToList();
            salida.Sort(Comparar);
            var normales = visibles.Where(f => !f.Fija).ToList();
            if (agrupar.Checked)
            {
                var grupos = normales.GroupBy(f => f.Proceso.Grupo, StringComparer.OrdinalIgnoreCase).Select(g =>
                {
                    var hijos = g.ToList(); hijos.Sort(Comparar);
                    var cabecera = new Fila { Grupo = true, Clave = "grupo:" + g.Key.ToUpperInvariant(), Nombre = hijos[0].Nombre,
                        Conteo = hijos.Count.ToString(), Cpu = Suma(hijos.Select(f => f.Cpu)), Ram = Suma(hijos.Select(f => f.Ram)) };
                    return new KeyValuePair<Fila, List<Fila>>(cabecera, hijos);
                }).ToList();
                grupos.Sort((a, b) => Comparar(a.Key, b.Key));
                foreach (var g in grupos) { salida.Add(g.Key); salida.AddRange(g.Value); }
            }
            else { normales.Sort(Comparar); salida.AddRange(normales); }
            reconstruyendo = true;
            try
            {
                int sel = seleccion == null ? -1 : salida.FindIndex(f => f.Clave == seleccion);
                int col = tabla.CurrentCell == null ? 1 : tabla.CurrentCell.ColumnIndex;
                int nuevoTop = ancla == null ? -1 : salida.FindIndex(f => f.Clave == ancla);
                filas = salida;
                if (tabla.RowCount != filas.Count) tabla.RowCount = filas.Count;
                if (sel < 0) { if (tabla.CurrentCell != null) tabla.CurrentCell = null; tabla.ClearSelection(); }
                else if (tabla.CurrentCell == null || tabla.CurrentCell.RowIndex != sel) tabla.CurrentCell = tabla[col, sel];
                if (nuevoTop >= 0 && (!forzarVisibilidad || sel < 0) && tabla.IsHandleCreated && tabla.FirstDisplayedScrollingRowIndex != nuevoTop)
                    tabla.FirstDisplayedScrollingRowIndex = nuevoTop;
                tabla.Invalidate();
            }
            finally { reconstruyendo = false; }
            estado.Text = datos == null ? "Lista de procesos: no disponible hasta la próxima muestra." :
                visibles.Count + " de " + datos.Count + " procesos   ·   N/D = no disponible   ·   Alt+1…5 para ordenar";
            Botones();
        }
        private static double? Suma(IEnumerable<double?> datos)
        {
            double suma = 0;
            foreach (var v in datos) { if (!v.HasValue) return null; suma += v.Value; }
            return suma;
        }
        private int Comparar(Fila a, Fila b)
        {
            int cmp;
            if (orden == 3 || orden == 4)
            {
                double? va = orden == 3 ? a.Cpu : a.Ram, vb = orden == 3 ? b.Cpu : b.Ram;
                if (va.HasValue != vb.HasValue) return va.HasValue ? -1 : 1;
                cmp = Nullable.Compare(va, vb);
            }
            else if (orden == 2) cmp = IdOrden(a).CompareTo(IdOrden(b));
            else if (orden == 0) cmp = a.Fija.CompareTo(b.Fija);
            else cmp = StringComparer.CurrentCultureIgnoreCase.Compare(a.Nombre, b.Nombre);
            if (cmp != 0) return descendente ? -cmp : cmp;
            cmp = StringComparer.CurrentCultureIgnoreCase.Compare(a.Nombre, b.Nombre);
            if (cmp != 0) return cmp;
            cmp = IdOrden(a).CompareTo(IdOrden(b));
            return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a.Clave, b.Clave);
        }
        private static int IdOrden(Fila f) { return f.Proceso != null ? f.Proceso.Pid : int.Parse(f.Conteo); }
        private void Valor(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= filas.Count) return;
            var f = filas[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0: e.Value = f.Fija ? "Fijo" : f.Grupo ? "Grupo" : ""; break;
                case 1: e.Value = !f.Grupo && agrupar.Checked && !f.Fija ? "    " + f.Nombre : f.Nombre; break;
                case 2: e.Value = f.Grupo ? f.Conteo + " proc." : f.Proceso.Pid.ToString(); break;
                case 3: e.Value = f.Cpu.HasValue ? Muestra.Texto(0, f.Cpu) : "N/D"; break;
                case 4: e.Value = f.Ram.HasValue ? (f.Ram.Value / 1048576).ToString("0.0") + " MiB" : "N/D"; break;
            }
        }
        private void Formato(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= filas.Count || SystemInformation.HighContrast) return;
            if (filas[e.RowIndex].Grupo) e.CellStyle.BackColor = Color.FromArgb(225, 235, 248);
            if (filas[e.RowIndex].Fija) e.CellStyle.BackColor = Color.FromArgb(245, 237, 211);
        }
        private void Botones()
        {
            var f = Seleccionada(); var p = f == null ? null : f.Proceso;
            fijar.Enabled = p != null && p.Clave != null; fijar.Text = f != null && f.Fija ? "Desfijar" : "Fijar";
            cerrar.Enabled = forzar.Enabled = Acciones.Elegible(p);
        }
        private void Cerrar(bool forzado)
        {
            var f = Seleccionada();
            if (f == null || !Acciones.Elegible(f.Proceso)) return;
            var p = f.Proceso;
            if (forzado && MessageBox.Show(this, "¿Forzar el cierre de " + p.Nombre + " (PID " + p.Pid + ")?\n\nSe pueden perder cambios sin guardar.",
                "Confirmar cierre forzado", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            MessageBox.Show(this, Acciones.Ejecutar(p, forzado), forzado ? "Cierre forzado" : "Cierre normal", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
