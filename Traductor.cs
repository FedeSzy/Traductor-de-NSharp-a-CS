using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace TraduccionDeNSSaC_
{
    internal class Traductor
    {
        private static readonly Dictionary<string, string> TiposEquivalentes = new()
        {
            ["String"] = "string",
            ["boolean"] = "bool",
            ["Boolean"] = "bool",
            ["Integer"] = "int",
            ["Double"] = "double",
            ["Float"] = "float",
            ["Long"] = "long",
            ["Character"] = "char",
            ["Object"] = "object",
            ["ArrayList"] = "List",
            ["HashMap"] = "Dictionary"
        };

        private static readonly Dictionary<string, string> Visibilidades = new()
        {
            ["+"] = "public",
            ["-"] = "private",
            ["#"] = "protected",
            ["~"] = "internal"
        };

        private static readonly HashSet<string> TiposConParse = new()
        {
            "int", "long", "short", "byte", "double", "float", "decimal", "bool", "char"
        };

        private readonly JsonElement proyecto;
        private readonly string nombreArchivo;
        private readonly StringBuilder salida = new();
        private readonly List<Clase> clases = new();
        private Dictionary<string, string> tiposConocidos = new();
        private int tab;

        public Traductor(JsonElement proyecto, string nombreArchivo)
        {
            this.proyecto = proyecto;
            this.nombreArchivo = nombreArchivo;
        }

        public string Traducir()
        {
            ArmarClases();

            Linea("using System;");
            Linea("using System.Collections.Generic;");
            Linea("");
            Linea("namespace " + NombreDelEspacio());
            Abrir();
            for (int i = 0; i < clases.Count; i++)
            {
                if (i > 0)
                {
                    Linea("");
                }
                EscribirClase(clases[i]);
            }
            Cerrar();

            return salida.ToString();
        }

        private void ArmarClases()
        {
            JsonElement extra = Propiedad(proyecto, "nsharp");
            var carpetas = new Dictionary<string, string>();
            var padresDeCarpetas = new Dictionary<string, string>();

            foreach (JsonElement carpeta in Lista(extra, "folders"))
            {
                string id = Texto(carpeta, "id");
                string nombre = Identificador(Texto(carpeta, "name"));
                if (id == "" || nombre == "")
                {
                    continue;
                }
                carpetas[id] = nombre;
                ObtenerClase(nombre);
                string padre = Texto(carpeta, "parent");
                if (padre != "")
                {
                    padresDeCarpetas[id] = padre;
                }
            }

            foreach (KeyValuePair<string, string> par in padresDeCarpetas)
            {
                if (carpetas.TryGetValue(par.Value, out string? nombrePadre) && nombrePadre != carpetas[par.Key])
                {
                    ObtenerClase(carpetas[par.Key]).Padre = nombrePadre;
                }
            }

            LeerDiagramaUml(Propiedad(extra, "uml"));

            List<JsonElement> ubicaciones = Lista(extra, "map").ToList();
            int indice = 0;
            foreach (JsonElement diagrama in Lista(proyecto, "diagrams"))
            {
                string nombreClase = "";
                if (indice < ubicaciones.Count
                    && ubicaciones[indice].ValueKind == JsonValueKind.String
                    && carpetas.TryGetValue(ubicaciones[indice].GetString() ?? "", out string? deCarpeta))
                {
                    nombreClase = deCarpeta;
                }
                if (nombreClase == "")
                {
                    nombreClase = Identificador(Texto(diagrama, "theClass"));
                }
                if (nombreClase == "")
                {
                    nombreClase = "Programa";
                }
                ObtenerClase(nombreClase).Metodos.Add(Texto(diagrama, "code"));
                indice++;
            }

            if (clases.Count == 0)
            {
                ObtenerClase("Programa");
            }
        }

        private void LeerDiagramaUml(JsonElement uml)
        {
            var nombresPorId = new Dictionary<string, string>();

            foreach (JsonElement cosa in Lista(uml, "cosas"))
            {
                if (Texto(cosa, "k") != "clase")
                {
                    continue;
                }

                List<List<string>> tramos = Tramos(Texto(cosa, "txt"));
                string nombre = "";
                bool esInterfaz = false;
                bool esAbstracta = false;

                foreach (string cruda in tramos[0])
                {
                    string linea = cruda.Trim();
                    if (linea == "")
                    {
                        continue;
                    }
                    Match estereotipo = Regex.Match(linea, @"^<<\s*(.+?)\s*>>$");
                    if (estereotipo.Success)
                    {
                        string cual = estereotipo.Groups[1].Value.ToLowerInvariant();
                        esInterfaz |= cual.Contains("interface") || cual.Contains("interfaz");
                        esAbstracta |= cual.Contains("abstract");
                        continue;
                    }
                    if (nombre == "")
                    {
                        esAbstracta |= Regex.IsMatch(linea, @"^/.+/$");
                        nombre = Identificador(Regex.Replace(linea, @"^[+\-#~]\s*", "").Replace("_", "").Replace("/", ""));
                    }
                }

                if (nombre == "")
                {
                    continue;
                }

                string id = Texto(cosa, "id");
                if (id != "")
                {
                    nombresPorId[id] = nombre;
                }

                Clase clase = ObtenerClase(nombre);
                clase.EsInterfaz |= esInterfaz;
                clase.EsAbstracta |= esAbstracta;

                for (int i = 1; i < tramos.Count; i++)
                {
                    foreach (string linea in tramos[i])
                    {
                        MetodoUml? metodo = LeerMetodoUml(linea);
                        if (metodo != null)
                        {
                            if (!clase.MetodosUml.Any(m => m.Nombre == metodo.Nombre))
                            {
                                clase.MetodosUml.Add(metodo);
                            }
                            continue;
                        }
                        Atributo? atributo = LeerAtributo(linea);
                        if (atributo != null && !clase.Atributos.Any(a => a.Nombre == atributo.Nombre))
                        {
                            clase.Atributos.Add(atributo);
                        }
                    }
                }
            }

            foreach (JsonElement relacion in Lista(uml, "lineas"))
            {
                string tipo = Texto(relacion, "t");
                if (!nombresPorId.TryGetValue(Texto(relacion, "de"), out string? hija)
                    || !nombresPorId.TryGetValue(Texto(relacion, "a"), out string? madre)
                    || hija == madre)
                {
                    continue;
                }

                Clase clase = ObtenerClase(hija);
                if (tipo == "her" && !ObtenerClase(madre).EsInterfaz)
                {
                    clase.Padre ??= madre;
                }
                else if ((tipo == "her" || tipo == "imp") && !clase.Interfaces.Contains(madre))
                {
                    clase.Interfaces.Add(madre);
                }
            }
        }

        private static List<List<string>> Tramos(string texto)
        {
            var tramos = new List<List<string>> { new() };
            foreach (string linea in texto.Split('\n'))
            {
                string limpia = linea.TrimEnd('\r');
                if (Regex.IsMatch(limpia, @"^\s*-{2,}\s*$"))
                {
                    tramos.Add(new List<string>());
                }
                else
                {
                    tramos[^1].Add(limpia);
                }
            }
            return tramos;
        }

        private static (string Visibilidad, bool Estatico, string Resto) LeerPrefijo(string linea, string visibilidad)
        {
            Match signo = Regex.Match(linea, @"^([+\-#~])\s*");
            if (signo.Success)
            {
                visibilidad = Visibilidades[signo.Groups[1].Value];
                linea = linea.Substring(signo.Length);
            }

            bool estatico = false;
            if (Regex.IsMatch(linea, @"^_.+_$"))
            {
                estatico = true;
                linea = linea.Substring(1, linea.Length - 2);
            }
            else if (Regex.IsMatch(linea, @"^_[^_\s:(]+_"))
            {
                estatico = true;
                linea = Regex.Replace(linea, @"^_([^_\s:(]+)_", "$1");
            }

            return (visibilidad, estatico, linea.Trim());
        }

        private static MetodoUml? LeerMetodoUml(string cruda)
        {
            string linea = cruda.Trim();
            int abre = linea.IndexOf('(');
            int cierra = linea.LastIndexOf(')');
            if (abre == -1 || cierra < abre)
            {
                return null;
            }

            (string visibilidad, bool estatico, string resto) = LeerPrefijo(linea, "public");
            abre = resto.IndexOf('(');
            cierra = resto.LastIndexOf(')');
            if (abre == -1 || cierra < abre)
            {
                return null;
            }

            string nombre = Identificador(resto.Substring(0, abre).Replace("_", "").Replace("/", ""));
            if (nombre == "")
            {
                return null;
            }

            string tipo = Tipo(Regex.Replace(resto.Substring(cierra + 1), @"^\s*:?\s*", ""));
            var parametros = new List<string>();
            foreach (string crudo in resto.Substring(abre + 1, cierra - abre - 1).Split(','))
            {
                string parametro = crudo.Trim();
                if (parametro == "")
                {
                    continue;
                }
                int dosPuntos = parametro.IndexOf(':');
                string[] palabras = parametro.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (dosPuntos >= 0)
                {
                    string tipoParametro = Tipo(parametro.Substring(dosPuntos + 1));
                    parametros.Add((tipoParametro == "" ? "object" : tipoParametro) + " " + Identificador(parametro.Substring(0, dosPuntos)));
                }
                else if (palabras.Length > 1)
                {
                    parametros.Add(Tipo(string.Join(" ", palabras[..^1])) + " " + Identificador(palabras[^1]));
                }
                else
                {
                    parametros.Add("object " + Identificador(parametro));
                }
            }

            return new MetodoUml(estatico ? visibilidad + " static" : visibilidad, tipo == "" ? "void" : tipo, nombre, parametros);
        }

        private static Atributo? LeerAtributo(string cruda)
        {
            string linea = cruda.Trim();
            if (linea == "" || linea.Contains('('))
            {
                return null;
            }

            (string visibilidad, bool estatico, string resto) = LeerPrefijo(linea, "private");
            linea = resto;
            if (estatico)
            {
                visibilidad += " static";
            }

            string? valor = null;
            int igual = linea.IndexOf('=');
            if (igual >= 0)
            {
                valor = linea.Substring(igual + 1).Trim();
                linea = linea.Substring(0, igual).Trim();
                if (valor == "")
                {
                    valor = null;
                }
            }

            string nombre;
            string tipo;
            int dosPuntos = linea.IndexOf(':');
            if (dosPuntos >= 0)
            {
                nombre = linea.Substring(0, dosPuntos);
                tipo = linea.Substring(dosPuntos + 1);
            }
            else
            {
                string[] palabras = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (palabras.Length > 1)
                {
                    tipo = string.Join(" ", palabras[..^1]);
                    nombre = palabras[^1];
                }
                else
                {
                    tipo = "object";
                    nombre = linea;
                }
            }

            nombre = Identificador(nombre.Replace("/", ""));
            tipo = Tipo(tipo);
            if (nombre == "")
            {
                return null;
            }
            return new Atributo(visibilidad, tipo == "" ? "object" : tipo, nombre, valor);
        }

        private Clase ObtenerClase(string nombre)
        {
            Clase? existente = clases.FirstOrDefault(c => c.Nombre == nombre);
            if (existente != null)
            {
                return existente;
            }
            var nueva = new Clase(nombre);
            clases.Add(nueva);
            return nueva;
        }

        private void EscribirClase(Clase clase)
        {
            var herencia = new List<string>();
            if (!string.IsNullOrEmpty(clase.Padre))
            {
                herencia.Add(clase.Padre);
            }
            herencia.AddRange(clase.Interfaces.Where(i => i != clase.Padre));

            string clave = clase.EsInterfaz ? "interface" : clase.EsAbstracta ? "abstract class" : "class";
            Linea("public " + clave + " " + clase.Nombre + (herencia.Count > 0 ? " : " + string.Join(", ", herencia) : ""));
            Abrir();

            bool hayMiembros = false;
            foreach (Atributo atributo in clase.Atributos)
            {
                if (clase.EsInterfaz)
                {
                    Linea(atributo.Tipo + " " + atributo.Nombre + " { get; set; }");
                }
                else
                {
                    Linea(atributo.Visibilidad + " " + atributo.Tipo + " " + atributo.Nombre
                        + (atributo.Valor == null ? "" : " = " + atributo.Valor) + ";");
                }
                hayMiembros = true;
            }

            List<Atributo> deInstancia = clase.Atributos.Where(a => !a.Visibilidad.Contains("static")).ToList();
            if (!clase.EsInterfaz && deInstancia.Count > 0 && !TieneConstructorPropio(clase))
            {
                if (hayMiembros)
                {
                    Linea("");
                }
                EscribirConstructor(clase, deInstancia);
                hayMiembros = true;
            }

            var escritos = new HashSet<string>();
            foreach (string metodo in clase.Metodos)
            {
                if (hayMiembros)
                {
                    Linea("");
                }
                escritos.Add(EscribirMetodo(metodo, clase));
                hayMiembros = true;
            }

            foreach (MetodoUml metodo in clase.MetodosUml.Where(m => !escritos.Contains(m.Nombre)))
            {
                if (hayMiembros)
                {
                    Linea("");
                }
                EscribirMetodoUml(metodo, clase);
                hayMiembros = true;
            }

            Cerrar();
        }

        private void EscribirConstructor(Clase clase, List<Atributo> atributos)
        {
            Linea("public " + clase.Nombre + "(" + string.Join(", ", atributos.Select(a => a.Tipo + " " + a.Nombre)) + ")");
            Abrir();
            foreach (Atributo atributo in atributos)
            {
                Linea("this." + atributo.Nombre + " = " + atributo.Nombre + ";");
            }
            Cerrar();
        }

        private static bool TieneConstructorPropio(Clase clase)
        {
            return clase.MetodosUml.Any(m => m.Nombre == clase.Nombre)
                || clase.Metodos.Any(codigo => NombreDeMetodo(codigo) == clase.Nombre);
        }

        private static string NombreDeMetodo(string codigo)
        {
            var documento = new HtmlDocument();
            documento.LoadHtml(codigo);
            HtmlNode? firma = documento.DocumentNode.Descendants().FirstOrDefault(n => n.HasClass("method-signature"));
            return Identificador(ValorDe(Hijo(firma, "method-name")));
        }

        private void EscribirMetodoUml(MetodoUml metodo, Clase clase)
        {
            var partes = new List<string>();
            if (!clase.EsInterfaz)
            {
                partes.Add(metodo.Visibilidad);
            }
            if (metodo.Nombre != clase.Nombre)
            {
                partes.Add(metodo.Tipo);
            }
            partes.Add(metodo.Nombre + "(" + string.Join(", ", metodo.Parametros) + ")");
            string cabecera = string.Join(" ", partes);

            if (clase.EsInterfaz)
            {
                Linea(cabecera + ";");
                return;
            }

            Linea(cabecera);
            Abrir();
            if (metodo.Nombre != clase.Nombre)
            {
                Linea("throw new NotImplementedException();");
            }
            Cerrar();
        }

        private string EscribirMetodo(string codigo, Clase clase)
        {
            var documento = new HtmlDocument();
            documento.LoadHtml(codigo);
            HtmlNode raiz = documento.DocumentNode;

            HtmlNode? firma = raiz.Descendants().FirstOrDefault(n => n.HasClass("method-signature"));
            string modificadores = Regex.Replace(ValorDe(Hijo(firma, "method-modifiers")), @"\bpackage\b", "internal");
            string tipo = Tipo(ValorDe(Hijo(firma, "method-type")));
            string nombre = Identificador(ValorDe(Hijo(firma, "method-name")));
            if (nombre == "")
            {
                nombre = "Metodo";
            }
            bool esConstructor = nombre == clase.Nombre;
            if (tipo == "" && !esConstructor)
            {
                tipo = "void";
            }

            tiposConocidos = new Dictionary<string, string>();
            var parametros = new List<string>();
            HtmlNode? zonaParametros = Hijo(firma, "method-parameters");
            if (zonaParametros != null)
            {
                foreach (HtmlNode parametro in zonaParametros.Descendants().Where(n => n.HasClass("parameter-declaration")))
                {
                    string nombreParametro = ValorDe(Hijo(parametro, "name"));
                    if (nombreParametro == "")
                    {
                        continue;
                    }
                    string tipoParametro = Tipo(ValorDe(Hijo(parametro, "type")));
                    if (tipoParametro == "")
                    {
                        tipoParametro = "object";
                    }
                    tiposConocidos[nombreParametro] = tipoParametro;
                    parametros.Add(tipoParametro + " " + nombreParametro);
                }
            }

            var partes = new List<string>();
            if (!clase.EsInterfaz && modificadores != "")
            {
                partes.Add(modificadores);
            }
            if (!esConstructor)
            {
                partes.Add(tipo);
            }
            partes.Add(nombre + "(" + string.Join(", ", parametros) + ")");
            string cabecera = string.Join(" ", partes);

            if (clase.EsInterfaz)
            {
                Linea(cabecera + ";");
                return nombre;
            }

            Linea(cabecera);
            Abrir();

            HtmlNode? locales = raiz.Descendants().FirstOrDefault(n => n.HasClass("local-variable-declaration"));
            foreach (HtmlNode declaracion in Elementos(locales))
            {
                EscribirDeclaracion(declaracion);
            }

            HtmlNode? cuerpo = raiz.ChildNodes.FirstOrDefault(n => n.HasClass("statements"))
                ?? raiz.Descendants().FirstOrDefault(n => n.HasClass("statements"));
            EscribirSentencias(cuerpo);

            Cerrar();
            return nombre;
        }

        private void EscribirDeclaracion(HtmlNode nodo)
        {
            if (nodo.HasClass("initialized-variable-declaration"))
            {
                (string tipo, bool constante) = TipoLocal(ValorDe(Hijo(nodo, "type")));
                List<string> valores = Entradas(Hijo(nodo, "assignment-statement"));
                if (valores.Count < 2 || valores[0] == "")
                {
                    return;
                }
                tiposConocidos[valores[0]] = tipo;
                Linea((constante ? "const " : "") + tipo + " " + valores[0] + " = " + valores[1] + ";");
            }
            else if (nodo.HasClass("variable-declaration") || nodo.HasClass("parameter-declaration"))
            {
                (string tipo, _) = TipoLocal(ValorDe(Hijo(nodo, "type")));
                string nombre = ValorDe(Hijo(nodo, "name"));
                if (nombre == "")
                {
                    return;
                }
                if (tipo == "var")
                {
                    tipo = "object";
                }
                tiposConocidos[nombre] = tipo;
                Linea(tipo + " " + nombre + ";");
            }
        }

        private void EscribirSentencias(HtmlNode? lista)
        {
            foreach (HtmlNode nodo in Elementos(lista))
            {
                EscribirSentencia(nodo);
            }
        }

        private void EscribirSentencia(HtmlNode nodo)
        {
            if (nodo.HasClass("assignment-statement"))
            {
                List<string> valores = Entradas(nodo);
                if (valores.Count >= 2 && valores[0] != "")
                {
                    Linea(valores[0] + " = " + valores[1] + ";");
                }
            }
            else if (nodo.HasClass("block-statement"))
            {
                EscribirInstruccion(ValorDe(Hijo(nodo, "content")));
            }
            else if (nodo.HasClass("call-statement"))
            {
                EscribirInstruccion(ValorDe(Hijo(nodo, "call")));
            }
            else if (nodo.HasClass("comment-statement"))
            {
                string texto = Entradas(nodo).FirstOrDefault() ?? "";
                if (texto != "")
                {
                    Linea("// " + texto);
                }
            }
            else if (nodo.HasClass("input-statement"))
            {
                EscribirLectura(ValorDe(Hijo(nodo, "body")));
            }
            else if (nodo.HasClass("output-statement"))
            {
                Linea("Console.WriteLine(" + ValorDe(Hijo(nodo, "body")) + ");");
            }
            else if (nodo.HasClass("return-statement"))
            {
                string valor = Entradas(nodo).FirstOrDefault() ?? "";
                Linea(valor == "" ? "return;" : "return " + valor + ";");
            }
            else if (nodo.HasClass("break-statement"))
            {
                Linea("break;");
            }
            else if (nodo.HasClass("throw-statement"))
            {
                string valor = Regex.Replace(TextoPlano(nodo), @"^throw\b", "").Trim();
                Linea(valor == "" ? "throw;" : "throw " + valor.TrimEnd(';') + ";");
            }
            else if (nodo.HasClass("conditional-statement") && nodo.HasClass("switch"))
            {
                EscribirSegun(nodo);
            }
            else if (nodo.HasClass("conditional-statement"))
            {
                EscribirSi(nodo, "if");
            }
            else if (nodo.HasClass("while-statement"))
            {
                EscribirMientras(nodo);
            }
            else if (nodo.HasClass("dowhile-statement"))
            {
                EscribirHacerMientras(nodo);
            }
            else if (nodo.HasClass("for-statement"))
            {
                EscribirPara(nodo);
            }
            else if (nodo.HasClass("try-statement"))
            {
                EscribirBloqueSimple(nodo, "try");
            }
            else if (nodo.HasClass("catch-statement"))
            {
                EscribirCaptura(nodo);
            }
            else if (nodo.HasClass("finally-statement"))
            {
                EscribirBloqueSimple(nodo, "finally");
            }
            else if (nodo.HasClass("variable-declaration") || nodo.HasClass("initialized-variable-declaration"))
            {
                EscribirDeclaracion(nodo);
            }
        }

        private void EscribirInstruccion(string texto)
        {
            if (texto == "")
            {
                return;
            }
            Linea(texto.EndsWith(";") || texto.EndsWith("}") ? texto : texto + ";");
        }

        private void EscribirLectura(string variable)
        {
            if (variable == "")
            {
                return;
            }
            string lectura = "Console.ReadLine()";
            if (tiposConocidos.TryGetValue(variable, out string? tipo) && TiposConParse.Contains(tipo))
            {
                lectura = tipo + ".Parse(Console.ReadLine())";
            }
            Linea(variable + " = " + lectura + ";");
        }

        private void EscribirSi(HtmlNode nodo, string palabra)
        {
            string condicion = ValorDe(Hijo(Hijo(nodo, "header"), "condition"));
            HtmlNode? cuerpo = Hijo(nodo, "body");
            HtmlNode? entonces = Hijo(cuerpo, "then");
            List<HtmlNode> sino = Elementos(Hijo(cuerpo, "else")).ToList();

            Linea(palabra + " (" + condicion + ")");
            Abrir();
            EscribirSentencias(entonces);
            Cerrar();

            if (sino.Count == 1 && sino[0].HasClass("conditional-statement") && !sino[0].HasClass("switch"))
            {
                EscribirSi(sino[0], "else if");
            }
            else if (sino.Count > 0)
            {
                Linea("else");
                Abrir();
                foreach (HtmlNode hijo in sino)
                {
                    EscribirSentencia(hijo);
                }
                Cerrar();
            }
        }

        private void EscribirSegun(HtmlNode nodo)
        {
            string expresion = ValorDe(Hijo(Hijo(nodo, "header"), "condition"));
            Linea("switch (" + expresion + ")");
            Abrir();

            foreach (HtmlNode caso in Elementos(Hijo(nodo, "body")).Where(n => n.HasClass("case")))
            {
                string valor = ValorDe(Hijo(caso, "test-value"));
                bool porDefecto = valor == "" || valor.Equals("default", StringComparison.OrdinalIgnoreCase);
                Linea(porDefecto ? "default:" : "case " + valor + ":");
                tab++;
                HtmlNode? lista = Hijo(caso, "statements");
                EscribirSentencias(lista);
                if (!TerminaConSalida(lista))
                {
                    Linea("break;");
                }
                tab--;
            }

            Cerrar();
        }

        private static bool TerminaConSalida(HtmlNode? lista)
        {
            HtmlNode? ultimo = Elementos(lista).LastOrDefault();
            if (ultimo == null)
            {
                return false;
            }
            if (ultimo.HasClass("break-statement") || ultimo.HasClass("return-statement") || ultimo.HasClass("throw-statement"))
            {
                return true;
            }
            if (ultimo.HasClass("block-statement") || ultimo.HasClass("call-statement"))
            {
                string texto = ValorDe(Hijo(ultimo, ultimo.HasClass("block-statement") ? "content" : "call"));
                return Regex.IsMatch(texto, @"^(break|return|throw|continue|goto)\b");
            }
            return false;
        }

        private void EscribirMientras(HtmlNode nodo)
        {
            string condicion = ValorDe(Hijo(Hijo(nodo, "condition-block"), "condition"));
            Linea("while (" + condicion + ")");
            Abrir();
            EscribirSentencias(Hijo(Hijo(nodo, "container"), "statements"));
            Cerrar();
        }

        private void EscribirHacerMientras(HtmlNode nodo)
        {
            string condicion = ValorDe(Hijo(Hijo(nodo, "condition-block"), "condition"));
            Linea("do");
            Abrir();
            EscribirSentencias(Hijo(Hijo(nodo, "container"), "statements"));
            tab--;
            Linea("} while (" + condicion + ");");
        }

        private void EscribirPara(HtmlNode nodo)
        {
            HtmlNode? contenedor = Hijo(nodo, "container");
            HtmlNode? contenido = Hijo(Hijo(Hijo(contenedor, "controller"), "content-block"), "content");
            List<string> valores = Entradas(contenido);
            string tipoBloque = nodo.GetAttributeValue("type", "");

            if (tipoBloque == "foreach" || (tipoBloque != "for" && valores.Count == 3))
            {
                while (valores.Count < 3)
                {
                    valores.Add("");
                }
                string tipo = Tipo(valores[0]);
                if (tipo == "")
                {
                    tipo = "var";
                }
                Linea("foreach (" + tipo + " " + valores[1] + " in " + valores[2] + ")");
            }
            else
            {
                while (valores.Count < 4)
                {
                    valores.Add("");
                }
                string variable = valores[0];
                string nombre = variable.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                string inicio = valores[1];
                string fin = valores[2];
                string paso = valores[3] == "" ? "1" : valores[3];
                bool baja = paso.StartsWith("-");
                string magnitud = baja ? paso.Substring(1).Trim() : paso;
                string avance = Regex.IsMatch(paso, @"\+\+|--|=")
                    ? paso
                    : magnitud == "1"
                        ? nombre + (baja ? "--" : "++")
                        : nombre + (baja ? " -= " : " += ") + magnitud;
                string condicion = Regex.IsMatch(fin, "[<>=!]") ? fin : nombre + (baja ? " >= " : " <= ") + fin;
                string declaracion = variable.Contains(' ') || tiposConocidos.ContainsKey(variable) ? "" : "int ";
                Linea("for (" + declaracion + variable + " = " + inicio + "; " + condicion + "; " + avance + ")");
            }

            Abrir();
            EscribirSentencias(Hijo(contenedor, "statements"));
            Cerrar();
        }

        private void EscribirBloqueSimple(HtmlNode nodo, string palabra)
        {
            Linea(palabra);
            Abrir();
            EscribirSentencias(Hijo(Hijo(nodo, "container"), "statements"));
            Cerrar();
        }

        private void EscribirCaptura(HtmlNode nodo)
        {
            HtmlNode? contenedor = Hijo(nodo, "container");
            HtmlNode? excepcion = Hijo(contenedor, "exception");
            string tipo = TextoPlano(Hijo(excepcion, "identifier"));
            string variable = TextoPlano(Hijo(excepcion, "variable"));
            Linea(tipo == "" ? "catch" : "catch (" + tipo + (variable == "" ? "" : " " + variable) + ")");
            Abrir();
            EscribirSentencias(Hijo(contenedor, "statements"));
            Cerrar();
        }

        private static (string Tipo, bool Constante) TipoLocal(string texto)
        {
            string limpio = texto.Trim();
            bool constante = false;
            Match prefijo = Regex.Match(limpio, @"^(final|const)\s+");
            if (prefijo.Success)
            {
                constante = true;
                limpio = limpio.Substring(prefijo.Length);
            }
            string tipo = Tipo(limpio);
            return (tipo == "" ? "var" : tipo, constante);
        }

        private static string Tipo(string texto)
        {
            return Regex.Replace(texto.Trim(), @"\b[A-Za-z]+\b",
                m => TiposEquivalentes.TryGetValue(m.Value, out string? equivalente) ? equivalente : m.Value);
        }

        private static string Identificador(string texto)
        {
            string limpio = new string(texto.Trim().Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            return limpio != "" && char.IsDigit(limpio[0]) ? "_" + limpio : limpio;
        }

        private string NombreDelEspacio()
        {
            string nombre = Texto(proyecto, "name");
            if (nombre == "")
            {
                nombre = nombreArchivo;
            }

            string sinTildes = new string(nombre.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray()).Normalize(NormalizationForm.FormC);

            var resultado = new StringBuilder();
            foreach (string palabra in Regex.Split(sinTildes, @"[^A-Za-z0-9]+"))
            {
                if (palabra != "")
                {
                    resultado.Append(char.ToUpperInvariant(palabra[0])).Append(palabra.Substring(1));
                }
            }

            if (resultado.Length == 0)
            {
                return "Proyecto";
            }
            return char.IsDigit(resultado[0]) ? "Proyecto" + resultado : resultado.ToString();
        }

        private static IEnumerable<HtmlNode> Elementos(HtmlNode? nodo)
        {
            if (nodo == null)
            {
                return Enumerable.Empty<HtmlNode>();
            }
            return nodo.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element && !n.HasClass("empty"));
        }

        private static HtmlNode? Hijo(HtmlNode? nodo, string clase)
        {
            return nodo?.ChildNodes.FirstOrDefault(n => n.NodeType == HtmlNodeType.Element && n.HasClass(clase));
        }

        private static List<string> Entradas(HtmlNode? nodo)
        {
            if (nodo == null)
            {
                return new List<string>();
            }
            return nodo.ChildNodes
                .Where(n => n.Name == "input")
                .Select(n => HtmlEntity.DeEntitize(n.GetAttributeValue("value", "")).Trim())
                .ToList();
        }

        private static string ValorDe(HtmlNode? contenedor)
        {
            return Entradas(contenedor).FirstOrDefault() ?? "";
        }

        private static string TextoPlano(HtmlNode? nodo)
        {
            return nodo == null ? "" : HtmlEntity.DeEntitize(nodo.InnerText).Trim();
        }

        private static JsonElement Propiedad(JsonElement elemento, string nombre)
        {
            if (elemento.ValueKind == JsonValueKind.Object && elemento.TryGetProperty(nombre, out JsonElement valor))
            {
                return valor;
            }
            return default;
        }

        private static IEnumerable<JsonElement> Lista(JsonElement elemento, string nombre)
        {
            JsonElement valor = Propiedad(elemento, nombre);
            if (valor.ValueKind != JsonValueKind.Array)
            {
                return Enumerable.Empty<JsonElement>();
            }
            return valor.EnumerateArray();
        }

        private static string Texto(JsonElement elemento, string nombre)
        {
            JsonElement valor = Propiedad(elemento, nombre);
            return valor.ValueKind == JsonValueKind.String ? (valor.GetString() ?? "").Trim() : "";
        }

        private void Linea(string texto)
        {
            if (texto != "")
            {
                salida.Append(' ', tab * 4).Append(texto);
            }
            salida.Append("\r\n");
        }

        private void Abrir()
        {
            Linea("{");
            tab++;
        }

        private void Cerrar()
        {
            tab--;
            Linea("}");
        }
    }
}
