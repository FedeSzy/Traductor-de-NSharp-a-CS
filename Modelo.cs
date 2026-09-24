namespace TraduccionDeNSSaC_
{
    internal class Clase
    {
        public Clase(string nombre)
        {
            Nombre = nombre;
        }

        public string Nombre { get; }
        public string? Padre { get; set; }
        public List<string> Interfaces { get; } = new();
        public bool EsInterfaz { get; set; }
        public bool EsAbstracta { get; set; }
        public List<Atributo> Atributos { get; } = new();
        public List<string> Metodos { get; } = new();
        public List<MetodoUml> MetodosUml { get; } = new();
    }

    internal class MetodoUml
    {
        public MetodoUml(string visibilidad, string tipo, string nombre, List<string> parametros)
        {
            Visibilidad = visibilidad;
            Tipo = tipo;
            Nombre = nombre;
            Parametros = parametros;
        }

        public string Visibilidad { get; }
        public string Tipo { get; }
        public string Nombre { get; }
        public List<string> Parametros { get; }
    }

    internal class Atributo
    {
        public Atributo(string visibilidad, string tipo, string nombre, string? valor)
        {
            Visibilidad = visibilidad;
            Tipo = tipo;
            Nombre = nombre;
            Valor = valor;
        }

        public string Visibilidad { get; }
        public string Tipo { get; }
        public string Nombre { get; }
        public string? Valor { get; }
    }
}
