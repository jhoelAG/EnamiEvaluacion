using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>Cuántos campos se completaron, para el registro y la pantalla.</summary>
public record ResumenDatosPrueba(int Oficinas, int Comunas, int Distancias, int Padrones, int ComunasProductor,
                                 int Nombres, int Representantes, int Recursos, int Contactos = 0, int Ubicaciones = 0)
{
    public int Total => Oficinas + Comunas + Distancias + Padrones + ComunasProductor + Nombres + Representantes + Recursos + Contactos + Ubicaciones;
    public override string ToString() =>
        $"faenas: {Oficinas} oficinas, {Comunas} comunas, {Distancias} distancias, {Padrones} padrones; " +
        $"productores: {ComunasProductor} comunas, {Nombres} nombres, {Representantes} representantes, {Contactos} datos de contacto; " +
        $"{Ubicaciones} ubicaciones de faena; {Recursos} registros de recursos";
}

/// <summary>
/// SOLO PARA LA BASE DE PRUEBA. Completa los campos que las planillas de ENAMI dejan vacíos con datos recreados pero coherentes:
/// <list type="bullet">
/// <item>la oficina de la faena se deduce de su comuna o región (la oficina más frecuente entre las faenas de esa comuna o región);</item>
/// <item>la comuna de la faena es la del productor, y la del productor, la de su primera faena (o una comuna minera si no tiene faenas);</item>
/// <item>la distancia al poder de compra se toma de faenas de la misma comuna (mediana con una variación de ±20 %);</item>
/// <item>el padrón se crea con un número nuevo y un vencimiento entre −6 y +24 meses (cerca de 1 de cada 5 queda vencido);</item>
/// <item>los nombres anonimizados (solo números o siglas) se cambian por nombres y razones sociales ficticios;</item>
/// <item>los datos de contacto (dirección, teléfono y correo con el dominio reservado ejemplo.cl) y la ubicación de la faena
///       (sierra, coordenadas UTM aproximadas a su región y mano de obra) se inventan;</item>
/// <item>los recursos se calculan a partir de las entregas de los últimos 6 meses (o con valores típicos si la faena no tiene entregas),
///       con la fuente «Dato de prueba».</item>
/// </list>
/// Solo llena lo que está vacío y usa una semilla fija, así que se puede ejecutar varias veces y siempre da el mismo resultado.
/// </summary>
public class ServicioDatosPrueba(IAlmacen db, ServicioFichas fichas, ServicioAuditoria auditoria)
{
    public const string FuenteRecursos = "Dato de prueba";

    private static readonly string[] Nombres =
    [
        "Juan", "Pedro", "Luis", "Carlos", "Jorge", "Manuel", "José", "Raúl", "Héctor", "Sergio", "Víctor", "Patricio", "Ricardo",
        "Hernán", "Ramón", "Mario", "Óscar", "Rodrigo", "Cristián", "Claudio", "María", "Rosa", "Ana", "Carmen", "Patricia",
        "Marcela", "Verónica", "Gloria", "Elena", "Sandra", "Lorena", "Claudia", "Juana", "Silvia", "Mónica"
    ];
    private static readonly string[] Apellidos =
    [
        "González", "Muñoz", "Rojas", "Díaz", "Pérez", "Soto", "Contreras", "Silva", "Martínez", "Sepúlveda", "Morales", "Rodríguez",
        "López", "Fuentes", "Hernández", "Torres", "Araya", "Flores", "Espinoza", "Valenzuela", "Castillo", "Tapia", "Reyes",
        "Gutiérrez", "Castro", "Pizarro", "Álvarez", "Vásquez", "Sánchez", "Fernández", "Ramírez", "Carrasco", "Cortés", "Herrera",
        "Núñez", "Jara", "Vergara", "Rivera", "Figueroa", "Riquelme", "Campos", "Vera", "Cerda", "Olivares", "Cáceres", "Alvarado"
    ];
    private static readonly string[] NombresEmpresa =
    [
        "Los Pircos", "El Inca", "San Lorenzo", "Cerro Blanco", "La Fortuna", "Los Mantos", "Santa Rosa", "El Cobre", "Tres Puntas",
        "Las Ánimas", "Punta del Cobre", "La Estrella", "El Morado", "Quebrada Seca", "Los Loros", "Carrizalillo", "El Algarrobo",
        "Cerro Imán", "La Higuera", "Agua Amarga", "El Bronce", "La Candelaria", "San Pedro", "Domeyko", "Las Bombas", "El Durazno"
    ];
    private static readonly string[] Sufijos = ["Ltda.", "SpA", "S.A.", "E.I.R.L.", "Ltda.", "SpA"];

    public ResumenDatosPrueba Completar(int? idUsuario = null)
    {
        var azar = new Random(2026);
        int oficinas = 0, comunas = 0, distancias = 0, padrones = 0, comunasProd = 0, nombres = 0, representantes = 0, recursos = 0, contactos = 0, ubicaciones = 0;

        db.EnTransaccion(() =>
        {
            var faenas = db.Tabla<Faena>().OrderBy(f => f.Id).ToList();
            var productores = db.Tabla<Productor>().OrderBy(p => p.Id).ToList();
            var listaComunas = db.Tabla<Comuna>().ToList();
            var comunaPorId = listaComunas.ToDictionary(c => c.Id);
            var listaOficinas = db.Tabla<Oficina>().ToList();
            var productorPorId = productores.ToDictionary(p => p.Id);
            var faenasPorProductor = faenas.ToLookup(f => f.IdProductor);

            // 1. Comuna de la faena: la del productor; si no tiene, una comuna de otra faena del mismo productor o la más frecuente.
            int? comunaFrecuente = faenas.Where(f => f.IdComuna != null).GroupBy(f => f.IdComuna).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            foreach (var f in faenas.Where(f => f.IdComuna == null))
            {
                f.IdComuna = productorPorId[f.IdProductor].IdComuna
                             ?? faenasPorProductor[f.IdProductor].Select(x => x.IdComuna).FirstOrDefault(c => c != null)
                             ?? comunaFrecuente;
                if (f.IdComuna != null) comunas++;
            }

            // 2. Comuna del productor: la de su primera faena; si no tiene faenas, una comuna minera al azar (según cuántas faenas tiene cada una).
            var comunasMineras = faenas.Where(f => f.IdComuna != null).Select(f => f.IdComuna!.Value).ToList();
            foreach (var p in productores.Where(p => p.IdComuna == null))
            {
                p.IdComuna = faenasPorProductor[p.Id].Select(f => f.IdComuna).FirstOrDefault(c => c != null)
                             ?? (comunasMineras.Count > 0 ? comunasMineras[azar.Next(comunasMineras.Count)] : null);
                if (p.IdComuna != null) comunasProd++;
            }

            // 3. Oficina: la más frecuente entre las faenas de la misma comuna; si no hay, la de la misma región.
            string? Region(int? idComuna) => idComuna is int c && comunaPorId.TryGetValue(c, out var x) ? x.Region : null;
            var oficinaPorComuna = faenas.Where(f => f.IdOficina != null && f.IdComuna != null).GroupBy(f => f.IdComuna!.Value)
                .ToDictionary(g => g.Key, g => g.GroupBy(f => f.IdOficina!.Value).OrderByDescending(x => x.Count()).First().Key);
            var oficinaPorRegion = faenas.Where(f => f.IdOficina != null && Region(f.IdComuna) != null).GroupBy(f => Region(f.IdComuna)!)
                .ToDictionary(g => g.Key, g => g.GroupBy(f => f.IdOficina!.Value).OrderByDescending(x => x.Count()).First().Key);
            var oficinaRegionPropia = listaOficinas.GroupBy(o => o.Region).ToDictionary(g => g.Key, g => g.First().Id);
            int oficinaFrecuente = faenas.Where(f => f.IdOficina != null).GroupBy(f => f.IdOficina!.Value).OrderByDescending(g => g.Count()).Select(g => g.Key)
                .DefaultIfEmpty(listaOficinas.FirstOrDefault()?.Id ?? 0).First();
            foreach (var f in faenas.Where(f => f.IdOficina == null))
            {
                var region = Region(f.IdComuna);
                f.IdOficina = f.IdComuna is int c && oficinaPorComuna.TryGetValue(c, out var o1) ? o1
                    : region is not null && oficinaPorRegion.TryGetValue(region, out var o2) ? o2
                    : region is not null && oficinaRegionPropia.TryGetValue(region, out var o3) ? o3
                    : oficinaFrecuente;
                if (f.IdOficina is 0) f.IdOficina = null; else oficinas++;
            }

            // 4. Distancia: mediana de las faenas de la misma comuna (o de la misma oficina) con una variación de ±20 %.
            var conDistancia = faenas.Where(f => f.DistanciaPoderCompraKm != null).ToList();
            var medianaComuna = conDistancia.Where(f => f.IdComuna != null).GroupBy(f => f.IdComuna!.Value).ToDictionary(g => g.Key, g => Mediana(g));
            var medianaOficina = conDistancia.Where(f => f.IdOficina != null).GroupBy(f => f.IdOficina!.Value).ToDictionary(g => g.Key, g => Mediana(g));
            double medianaGeneral = conDistancia.Count > 0 ? Mediana(conDistancia) : 60;
            foreach (var f in faenas.Where(f => f.DistanciaPoderCompraKm == null))
            {
                double base_ = f.IdComuna is int c && medianaComuna.TryGetValue(c, out var m1) ? m1
                             : f.IdOficina is int o && medianaOficina.TryGetValue(o, out var m2) ? m2 : medianaGeneral;
                double km = Math.Clamp(base_ * (0.8 + 0.4 * azar.NextDouble()), 2, 600);
                f.DistanciaPoderCompraKm = Math.Round((decimal)km, 1);
                distancias++;
            }

            // 5. Padrón: número nuevo y vencimiento entre −6 y +24 meses (alrededor de 1 de cada 5 queda vencido).
            int correlativo = 900_000;
            foreach (var f in faenas.Where(f => f.NroPadron == null || f.VencimientoPadron == null))
            {
                f.NroPadron ??= (correlativo++).ToString();
                f.VencimientoPadron ??= DateTime.Today.AddDays(azar.Next(-180, 730));
                padrones++;
            }

            // 6. Nombres anonimizados (solo números o siglas de hasta 4 letras) y representantes legales.
            foreach (var p in productores)
            {
                if (p.TipoProductor == TiposProductor.Empresa)
                {
                    if (Anonimizado(p.RazonSocial))
                    {
                        string nucleo = azar.Next(3) == 0 ? Apellido(azar) + " y " + Apellido(azar) : NombresEmpresa[azar.Next(NombresEmpresa.Length)];
                        p.RazonSocial = $"{(azar.Next(2) == 0 ? "Sociedad Minera" : "Minera")} {nucleo} {Sufijos[azar.Next(Sufijos.Length)]}";
                        nombres++;
                    }
                    if (string.IsNullOrWhiteSpace(p.RepresentanteLegal))
                    {
                        p.RepresentanteLegal = $"{Nombres[azar.Next(Nombres.Length)]} {Apellido(azar)} {Apellido(azar)}";
                        representantes++;
                    }
                }
                else if (Anonimizado(p.Nombres) || string.IsNullOrWhiteSpace(p.Apellidos))
                {
                    if (Anonimizado(p.Nombres)) p.Nombres = Nombres[azar.Next(Nombres.Length)];
                    if (string.IsNullOrWhiteSpace(p.Apellidos) || Anonimizado(p.Apellidos)) p.Apellidos = $"{Apellido(azar)} {Apellido(azar)}";
                    nombres++;
                }
            }

            // 7. Nombres de faena anonimizados (siglas de 4 letras de la ficha).
            foreach (var f in faenas.Where(f => Anonimizado(f.Nombre)))
                f.Nombre = (azar.Next(3) == 0 ? "Mina " : "") + NombresEmpresa[azar.Next(NombresEmpresa.Length)] + (azar.Next(4) == 0 ? " " + (azar.Next(2, 6)) : "");

            // 7b. Datos de contacto del productor y ubicación de la faena (las planillas anonimizadas no los traen).
            //     Correos con el dominio reservado ejemplo.cl y teléfonos ficticios; coordenadas UTM aproximadas a la región.
            foreach (var p in productores)
            {
                bool cambio = false;
                string comuna = p.IdComuna is int c && comunaPorId.TryGetValue(c, out var co) ? co.Nombre : "Copiapó";
                if (string.IsNullOrWhiteSpace(p.Direccion)) { p.Direccion = $"{Calles[azar.Next(Calles.Length)]} {azar.Next(10, 2500)}, {comuna}"; cambio = true; }
                if (string.IsNullOrWhiteSpace(p.Telefono)) { p.Telefono = $"+56 9 {azar.Next(5000, 9999)} {azar.Next(1000, 9999)}"; cambio = true; }
                if (string.IsNullOrWhiteSpace(p.Correo))
                {
                    var b = ServicioFichas.Simplificar(p.TipoProductor == TiposProductor.Empresa ? p.RazonSocial : $"{p.Nombres} {p.Apellidos?.Split(' ')[0]}");
                    p.Correo = $"{new string(b.Select(ch => char.IsLetterOrDigit(ch) ? ch : '.').ToArray()).Trim('.')}{azar.Next(1, 99)}@ejemplo.cl";
                    cambio = true;
                }
                if (cambio) contactos++;
            }
            foreach (var f in faenas)
            {
                bool cambio = false;
                var region = Region(f.IdComuna) ?? "";
                string comuna = f.IdComuna is int c && comunaPorId.TryGetValue(c, out var co) ? co.Nombre : "";
                if (string.IsNullOrWhiteSpace(f.Ubicacion)) { f.Ubicacion = $"Sierra {Sierras[azar.Next(Sierras.Length)]}{(comuna == "" ? "" : ", " + comuna)}"; cambio = true; }
                if (f.CoordenadaNorte is null || f.CoordenadaEste is null)
                {
                    double norte = NorteRegion(region) + (azar.NextDouble() - 0.5) * 160_000;
                    f.CoordenadaNorte ??= Math.Round((decimal)norte, 0);
                    f.CoordenadaEste ??= Math.Round((decimal)(300_000 + azar.NextDouble() * 110_000), 0);
                    cambio = true;
                }
                if (f.ManoObra is null) { f.ManoObra = (short)azar.Next(2, 16); cambio = true; }
                if (cambio) ubicaciones++;
            }
            db.Guardar();

            // 8. Recursos: coherentes con lo que la faena entrega (o valores típicos de la pequeña minería si no entrega).
            var conRecursos = db.Tabla<RecursoFaena>().Select(r => r.IdFaena).Distinct().ToHashSet();
            var conEntregas = db.Tabla<RecepcionMineral>().Select(r => r.IdFaena).Distinct().ToHashSet();
            int idAutor = idUsuario ?? db.Tabla<Usuario>().OrderBy(u => u.Id).Select(u => u.Id).First();
            var nuevos = new List<RecursoFaena>();
            foreach (var f in faenas.Where(f => f.Estado == EstadosFaena.Activa && !conRecursos.Contains(f.Id)))
            {
                var h = conEntregas.Contains(f.Id) ? fichas.Produccion(f.Id) : null;
                double tmsMes = h is { Lotes: > 0 } ? Math.Max(h.TmsPromedioMensual, 20) : 80 + azar.NextDouble() * 400;
                double cu = h is { Lotes: > 0 } && h.LeyCuPct > 0 ? h.LeyCuPct : (h is { Lotes: > 0 } ? 0 : 0.8 + azar.NextDouble() * 2.2);
                double au = h is { Lotes: > 0 } ? h.LeyAuGt : (azar.Next(3) == 0 ? 1 + azar.NextDouble() * 6 : 0);
                double ag = h is { Lotes: > 0 } ? h.LeyAgGt : azar.NextDouble() * 15;
                if (cu == 0 && au == 0 && ag == 0) cu = 1 + azar.NextDouble();
                // medido: entre 1 y 3 años de producción; indicado y inferido: más tonelaje y leyes algo menores
                double medido = tmsMes * azar.Next(12, 37);
                foreach (var (cat, factorT, factorLey) in new[] { (CategoriasRecurso.Medido, 1.0, 1.0), (CategoriasRecurso.Indicado, 1.5 + azar.NextDouble() * 1.5, 0.93),
                                                                  (CategoriasRecurso.Inferido, 2 + azar.NextDouble() * 3, 0.85) })
                {
                    double variacion = 0.9 + azar.NextDouble() * 0.2;
                    nuevos.Add(new RecursoFaena
                    {
                        IdFaena = f.Id, Categoria = cat, Toneladas = Math.Round((decimal)(medido * factorT), 0),
                        LeyCuPct = Math.Round((decimal)Math.Min(cu * factorLey * variacion, 60), 4),
                        LeyAuGt = Math.Round((decimal)(au * factorLey * variacion), 3),
                        LeyAgGt = Math.Round((decimal)(ag * factorLey * variacion), 3),
                        Fuente = FuenteRecursos, IdUsuario = idAutor, FechaRegistro = DateTime.Now
                    });
                }
            }
            db.AgregarVarios(nuevos);
            recursos = nuevos.Count;
        });

        var resumen = new ResumenDatosPrueba(oficinas, comunas, distancias, padrones, comunasProd, nombres, representantes, recursos, contactos, ubicaciones);
        if (resumen.Total > 0) auditoria.Registrar("DATOS_PRUEBA", idUsuario, robot: idUsuario is null ? "SISTEMA" : null, detalle: "Se completaron datos de prueba: " + resumen);
        return resumen;
    }

    private static readonly string[] Calles =
    [
        "Los Carrera", "O'Higgins", "Atacama", "Chacabuco", "Colipí", "Yerbas Buenas", "Vallejos", "Rodríguez", "Maipú", "Las Heras",
        "Los Aromos", "El Morro", "Pasaje Las Rocas", "Camino a Tierra Amarilla", "Avenida Copayapu", "Prat", "Freire", "Infante"
    ];
    private static readonly string[] Sierras =
    [
        "Exploradora", "Ánimas", "La Candelaria", "Los Bronces", "Pajonales", "Cerro Imán", "Las Pintadas", "El Inca", "Chañarcillo", "La Higuera"
    ];

    /// <summary>Coordenada norte UTM aproximada del centro de la zona minera de cada región.</summary>
    private static double NorteRegion(string region) => region switch
    {
        var r when r.Contains("Tarapacá") => 7_750_000,
        var r when r.Contains("Antofagasta") => 7_250_000,
        var r when r.Contains("Atacama") => 6_980_000,
        var r when r.Contains("Coquimbo") => 6_650_000,
        var r when r.Contains("Valparaíso") => 6_400_000,
        var r when r.Contains("Metropolitana") => 6_300_000,
        var r when r.Contains("Higgins") => 6_200_000,
        _ => 6_900_000
    };

    private static string Apellido(Random azar) => Apellidos[azar.Next(Apellidos.Length)];

    /// <summary>Las planillas vienen anonimizadas: nombres que son solo números (por ejemplo «6753») o siglas de hasta 4 letras («COMP»).</summary>
    public static bool Anonimizado(string? s)
    {
        var t = s?.Trim();
        if (string.IsNullOrEmpty(t)) return true;
        return t.All(c => char.IsDigit(c) || c == '.' || c == ' ') || (t.Length <= 4 && t.All(c => char.IsUpper(c) || c == '.' || c == ' '));
    }

    private static double Mediana(IEnumerable<Faena> fs)
    {
        var v = fs.Select(f => (double)f.DistanciaPoderCompraKm!.Value).OrderBy(x => x).ToList();
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}
