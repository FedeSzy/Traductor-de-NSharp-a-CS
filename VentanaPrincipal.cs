namespace TraduccionDeNSSaC_
{
    internal class VentanaPrincipal : Form
    {
        private const string MensajeInicial = "Arrastra un archivo .nsplus aca\r\n\r\no hace clic para buscarlo";
        private static readonly Color FondoNormal = Color.White;
        private static readonly Color FondoResaltado = Color.FromArgb(232, 240, 254);

        private readonly Label zona;

        public VentanaPrincipal()
        {
            Text = "Traductor NS a C#";
            ClientSize = new Size(480, 280);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Padding = new Padding(16);
            BackColor = FondoNormal;

            zona = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 12F),
                ForeColor = Color.DimGray,
                BackColor = FondoNormal,
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand,
                AllowDrop = true,
                Text = MensajeInicial
            };
            zona.DragEnter += AlEntrarArchivo;
            zona.DragLeave += AlSalirArchivo;
            zona.DragDrop += AlSoltarArchivo;
            zona.Click += AlHacerClic;
            Controls.Add(zona);
        }

        private void AlEntrarArchivo(object? emisor, DragEventArgs evento)
        {
            if (evento.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                evento.Effect = DragDropEffects.Copy;
                zona.BackColor = FondoResaltado;
            }
            else
            {
                evento.Effect = DragDropEffects.None;
            }
        }

        private void AlSalirArchivo(object? emisor, EventArgs evento)
        {
            zona.BackColor = FondoNormal;
        }

        private void AlSoltarArchivo(object? emisor, DragEventArgs evento)
        {
            zona.BackColor = FondoNormal;
            if (evento.Data?.GetData(DataFormats.FileDrop) is string[] rutas)
            {
                Procesar(rutas);
            }
        }

        private void AlHacerClic(object? emisor, EventArgs evento)
        {
            using var selector = new OpenFileDialog
            {
                Title = "Elegir un proyecto de NS Sharp",
                Filter = "Proyectos NS Sharp (*.nsplus)|*.nsplus|Todos los archivos (*.*)|*.*",
                Multiselect = true
            };
            if (selector.ShowDialog(this) == DialogResult.OK)
            {
                Procesar(selector.FileNames);
            }
        }

        private void Procesar(string[] rutas)
        {
            var informe = new List<string>();
            bool huboErrores = false;

            foreach (string ruta in rutas)
            {
                try
                {
                    string destino = Conversor.Convertir(ruta);
                    informe.Add("Listo, se creo " + Path.GetFileName(destino) + "\r\nen la misma carpeta que el original");
                }
                catch (Exception error)
                {
                    huboErrores = true;
                    informe.Add("No se pudo convertir " + Path.GetFileName(ruta) + "\r\n" + error.Message);
                }
            }

            zona.ForeColor = huboErrores ? Color.Firebrick : Color.SeaGreen;
            zona.Text = string.Join("\r\n\r\n", informe) + "\r\n\r\nArrastra otro archivo para seguir";
        }
    }
}
