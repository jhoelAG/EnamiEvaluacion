using Enami.Dominio.Importacion;
using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>Resumen de una carga para mostrar en pantalla.</summary>
public record ResumenCarga(string Planilla, int FilasLeidas, int Nuevos, int Actualizados, IReadOnlyList<FilaRechazada> Rechazadas,
                           IReadOnlyList<Observacion> Observaciones, TimeSpan Duracion)
{
    public int Cargados => Nuevos + Actualizados;
}

/// <summary>
/// Carga las planillas de ENAMI en la base de datos (HU-03 y HU-04, BOT-05). Si un productor o una faena ya existe,
/// actualiza sus datos en vez de duplicarlo, de modo que la carga se puede repetir con planillas más recientes.
/// </summary>
public class ServicioCargaPlanillas(IAlmacen db, ServicioAuditoria auditoria)
{
    public ResumenCarga CargarProductores(string ruta, int? idUsuario, string? robot = null)
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var r = ImportadorPlanillas.Productores(LectorXlsx.LeerFilas(ruta));
        int nuevos = 0, actualizados = 0;
        db.EnTransaccion(() =>
        {
            var comunas = Comunas();
            var existentes = db.Tabla<Productor>().ToDictionary(p => p.Rut);
            foreach (var x in r.Validas)
            {
                if (!existentes.TryGetValue(x.Rut, out var p))
                {
                    p = new Productor { Rut = x.Rut };
                    db.Agregar(p);
                    nuevos++;
                }
                else actualizados++;
                p.TipoProductor = x.Tipo;
                p.Nombres = x.Nombres;
                p.Apellidos = x.Apellidos;
                p.RazonSocial = x.RazonSocial is { Length: > 150 } rs ? rs[..150] : x.RazonSocial;
                p.NivelCumplimiento = x.NivelCumplimiento;
                p.IdComuna = ComunaId(comunas, x.Comuna, x.Region);
                // los datos de contacto solo se reemplazan si la planilla los trae (las planillas anonimizadas vienen sin ellos)
                p.Direccion = x.Direccion ?? p.Direccion;
                p.Telefono = x.Telefono ?? p.Telefono;
                p.Correo = x.Correo ?? p.Correo;
            }
            db.Guardar();
        });
        return Registrar("Ficha de productores", r, nuevos, actualizados, reloj.Elapsed, idUsuario, robot);
    }

    public ResumenCarga CargarFaenas(string ruta, int? idUsuario, string? robot = null)
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var productores = db.Tabla<Productor>().Select(p => new { p.Rut, p.Id }).ToList().ToDictionary(p => p.Rut, p => p.Id);
        var r = ImportadorPlanillas.Faenas(LectorXlsx.LeerFilas(ruta), productores.ContainsKey);
        int nuevos = 0, actualizados = 0;
        db.EnTransaccion(() =>
        {
            var comunas = Comunas();
            var oficinas = db.Tabla<Oficina>().ToList().ToDictionary(o => Clave(o.Nombre));
            var regionPorOficina = r.Validas.Where(f => f.Oficina is not null && f.Region is not null)
                .GroupBy(f => Clave(f.Oficina!))
                .ToDictionary(g => g.Key, g => g.GroupBy(f => f.Region!).OrderByDescending(x => x.Count()).First().Key);
            var existentes = db.Tabla<Faena>().ToDictionary(f => f.Codigo);
            foreach (var x in r.Validas)
            {
                if (!existentes.TryGetValue(x.Codigo, out var f))
                {
                    f = new Faena { Codigo = x.Codigo };
                    db.Agregar(f);
                    nuevos++;
                }
                else actualizados++;
                f.Nombre = x.Nombre;
                f.IdProductor = productores[x.Rut];
                f.Estado = x.Estado;
                f.DistanciaPoderCompraKm = x.DistanciaKm;
                f.Ubicacion = x.Ubicacion ?? f.Ubicacion;
                f.CoordenadaNorte = x.Norte ?? f.CoordenadaNorte;
                f.CoordenadaEste = x.Este ?? f.CoordenadaEste;
                f.ManoObra = x.ManoObra ?? f.ManoObra;
                f.IdComuna = ComunaId(comunas, x.Comuna, x.Region);
                if (x.Oficina is not null)
                {
                    var k = Clave(x.Oficina);
                    if (!oficinas.TryGetValue(k, out var o))
                    {
                        o = new Oficina { Nombre = x.Oficina.Length > 60 ? x.Oficina[..60] : x.Oficina, Region = regionPorOficina.GetValueOrDefault(k, "Sin región") };
                        db.Agregar(o);
                        db.Guardar();
                        oficinas[k] = o;
                    }
                    f.IdOficina = o.Id;
                }
                else f.IdOficina = null;
            }
            db.Guardar();
        });
        return Registrar("Ficha de faenas", r, nuevos, actualizados, reloj.Elapsed, idUsuario, robot);
    }

    /// <summary>Asigna a cada faena cargada su padrón vigente y la fecha de vencimiento.</summary>
    public ResumenCarga CargarPadrones(string ruta, int? idUsuario, string? robot = null)
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var r = ImportadorPlanillas.Padrones(LectorXlsx.LeerFilas(ruta));
        int actualizados = 0;
        db.EnTransaccion(() =>
        {
            var faenas = db.Tabla<Faena>().ToDictionary(f => f.Codigo);
            foreach (var p in r.Validas)
            {
                if (!faenas.TryGetValue(p.CodigoFaena, out var f)) continue;
                f.NroPadron = p.NroPadron;
                f.VencimientoPadron = p.Vencimiento;
                actualizados++;
            }
            db.Guardar();
        });
        return Registrar("Base de padrones", r, 0, actualizados, reloj.Elapsed, idUsuario, robot);
    }

    /// <summary>
    /// Carga los lotes de recepción de mineral. Los lotes que ya estaban cargados (mismo número) se omiten,
    /// así que se puede cargar cada semana el reporte nuevo sin duplicar datos.
    /// </summary>
    public ResumenCarga CargarRecepciones(string ruta, int? idUsuario, string? robot = null)
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var faenas = db.Tabla<Faena>().Select(f => new { f.Codigo, f.Id }).ToList().ToDictionary(f => f.Codigo, f => f.Id);
        var r = ImportadorPlanillas.Recepciones(LectorXlsx.LeerFilas(ruta), faenas.ContainsKey);
        var existentes = db.Tabla<RecepcionMineral>().Select(x => x.NroLote).ToHashSet();
        var nuevos = r.Validas.Where(x => !existentes.Contains(x.NroLote)).Select(x => new RecepcionMineral
        {
            NroLote = x.NroLote, IdFaena = faenas[x.CodigoFaena], FechaCierre = x.FechaCierre, Agencia = x.Agencia, Producto = x.Producto,
            PesoSecoKg = x.PesoSecoKg, LeyCuPct = x.LeyCuPct, LeyAuGt = x.LeyAuGt, LeyAgGt = x.LeyAgGt
        }).ToList();
        db.EnTransaccion(() => db.AgregarVarios(nuevos));
        int repetidos = r.Validas.Count - nuevos.Count;
        if (repetidos > 0) r.Observaciones.Add(new(0, "N° LOTE", $"{repetidos:N0} lotes ya estaban cargados y se omitieron."));
        return Registrar("Recepción de minerales", r, nuevos.Count, 0, reloj.Elapsed, idUsuario, robot);
    }

    private ResumenCarga Registrar<T>(string planilla, ResultadoImportacion<T> r, int nuevos, int actualizados, TimeSpan duracion, int? idUsuario, string? robot)
    {
        auditoria.Registrar("CARGA_PLANILLA", idUsuario, robot: robot,
            detalle: $"{planilla}: {r.FilasLeidas} filas leídas, {nuevos} nuevos, {actualizados} actualizados, {r.Rechazadas.Count} rechazadas, {r.Observaciones.Count} observaciones.");
        return new ResumenCarga(planilla, r.FilasLeidas, nuevos, actualizados, r.Rechazadas, r.Observaciones, duracion);
    }

    private Dictionary<string, Comuna> Comunas() => db.Tabla<Comuna>().ToList().ToDictionary(c => Clave(c.Nombre));

    private int? ComunaId(Dictionary<string, Comuna> comunas, string? nombre, string? region)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        var k = Clave(nombre);
        if (!comunas.TryGetValue(k, out var c))
        {
            c = new Comuna { Nombre = nombre.Length > 60 ? nombre[..60] : nombre, Region = (region ?? "Sin región") is var rg && rg.Length > 40 ? rg[..40] : rg };
            db.Agregar(c);
            db.Guardar();
            comunas[k] = c;
        }
        return c.Id;
    }

    private static string Clave(string s) => s.Trim().ToUpperInvariant();
}
