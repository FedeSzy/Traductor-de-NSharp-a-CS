namespace TraduccionDeNSSaC_
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();

            if (args.Length == 0)
            {
                Application.Run(new VentanaPrincipal());
                return;
            }

            var informe = new List<string>();
            bool huboErrores = false;
            foreach (string ruta in args)
            {
                try
                {
                    string destino = Conversor.Convertir(ruta);
                    informe.Add("Listo: " + destino);
                }
                catch (Exception error)
                {
                    huboErrores = true;
                    informe.Add("No se pudo convertir " + Path.GetFileName(ruta) + ": " + error.Message);
                }
            }

            MessageBox.Show(
                string.Join(Environment.NewLine + Environment.NewLine, informe),
                "Traductor NS a C#",
                MessageBoxButtons.OK,
                huboErrores ? MessageBoxIcon.Error : MessageBoxIcon.Information);
        }
    }
}
