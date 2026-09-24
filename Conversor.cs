using System.Text;
using System.Text.Json;

namespace TraduccionDeNSSaC_
{
    internal static class Conversor
    {
        public static string Convertir(string ruta)
        {
            if (!File.Exists(ruta))
            {
                throw new FileNotFoundException("No existe el archivo.");
            }

            JsonElement proyecto = LeerProyecto(ruta);
            string codigo = new Traductor(proyecto, Path.GetFileNameWithoutExtension(ruta)).Traducir();
            string destino = Path.ChangeExtension(ruta, ".cs");
            File.WriteAllText(destino, codigo, new UTF8Encoding(true));
            return destino;
        }

        private static JsonElement LeerProyecto(string ruta)
        {
            JsonElement envoltorio;
            try
            {
                envoltorio = JsonDocument.Parse(File.ReadAllText(ruta)).RootElement;
            }
            catch (JsonException)
            {
                throw new InvalidDataException("El archivo no es un proyecto .nsplus valido.");
            }

            double version = 0.1;
            if (envoltorio.ValueKind == JsonValueKind.Object
                && envoltorio.TryGetProperty("ver", out JsonElement campoVersion)
                && campoVersion.ValueKind == JsonValueKind.Number)
            {
                version = campoVersion.GetDouble();
            }

            if (version <= 0.1)
            {
                return envoltorio;
            }

            if (!envoltorio.TryGetProperty("data", out JsonElement campoDatos) || campoDatos.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("El archivo no tiene datos para leer.");
            }

            try
            {
                string invertido = campoDatos.GetString() ?? "";
                string base64 = new string(invertido.Reverse().ToArray());
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                return JsonDocument.Parse(json).RootElement;
            }
            catch (Exception error) when (error is FormatException || error is JsonException)
            {
                throw new InvalidDataException("Los datos del proyecto no se pueden leer.");
            }
        }
    }
}
