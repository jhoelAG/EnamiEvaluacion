using Enami.Dominio.Importacion;
using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>Motivo por el que una solicitud no es elegible.</summary>
public record MotivoNoElegible(string Codigo, string Mensaje);

/// <summary>
/// Reglas de elegibilidad (HU-05, RF-14). Una solicitud no puede avanzar si el productor está moroso o en cobranza judicial,
/// si la faena está inactiva, si el padrón está vencido o no existe, o si la faena no tiene oficina (no hay Jefe de Oficina a quien enrutar).
/// </summary>
public static class ReglasElegibilidad
{
    public static IReadOnlyList<MotivoNoElegible> Evaluar(Productor productor, Faena faena, DateTime hoy)
    {
        var motivos = new List<MotivoNoElegible>();
        if (faena.IdProductor != productor.Id)
            motivos.Add(new("TITULAR", "La faena no pertenece al productor indicado."));
        if (productor.NivelCumplimiento == NivelesCumplimiento.Moroso)
            motivos.Add(new("MOROSO", "El productor está moroso con ENAMI."));
        else if (productor.NivelCumplimiento == NivelesCumplimiento.Judicial)
            motivos.Add(new("JUDICIAL", "El productor tiene deudas en cobranza judicial con ENAMI."));
        if (faena.Estado != EstadosFaena.Activa)
            motivos.Add(new("FAENA_INACTIVA", "La faena está inactiva."));
        if (faena.VencimientoPadron is null)
            motivos.Add(new("SIN_PADRON", "La faena no tiene un padrón registrado."));
        else if (faena.VencimientoPadron.Value.Date < hoy.Date)
            motivos.Add(new("PADRON_VENCIDO", $"El padrón {faena.NroPadron} venció el {faena.VencimientoPadron:dd-MM-yyyy}."));
        if (faena.IdOficina is null)
            motivos.Add(new("SIN_OFICINA", "La faena no tiene oficina asignada; el administrador debe completarla antes de evaluar."));
        return motivos;
    }
}

/// <summary>Ficha del productor con sus faenas, lista para precargar el formulario (HU-04, RF-12).</summary>
public record FichaProductor(Productor Productor, string? Comuna, string? Region, IReadOnlyList<FichaFaena> Faenas);

/// <summary>Ficha de una faena con su oficina, comuna y resultado de elegibilidad (HU-04 y HU-05, RF-13 y RF-14).</summary>
public record FichaFaena(Faena Faena, string? Oficina, string? Comuna, string? Region, IReadOnlyList<MotivoNoElegible> MotivosNoElegible)
{
    public bool Elegible => MotivosNoElegible.Count == 0;
}

/// <summary>Resultado de la búsqueda por nombre.</summary>
public record CoincidenciaProductor(string Rut, string Nombre, string? Comuna, int Faenas, string? FaenaCoincidente);

/// <summary>Entregas de un mes. Las leyes se ponderan solo con los lotes que tienen alguna ley informada (<see cref="TmsConLey"/>).</summary>
public record ProduccionMes(DateTime Mes, int Lotes, double Tms, double TmsConLey, double LeyCuPct, double LeyAuGt, double LeyAgGt);

/// <summary>
/// Historial de entregas de la faena en los últimos 6 meses del reporte de recepción (HU-04 ampliada en el Sprint 4).
/// El promedio mensual divide por 6 aunque haya meses sin entregas; las leyes se ponderan por el peso seco de cada lote
/// y no consideran los lotes sin leyes informadas (141 de 66.550 en el reporte de 2024).
/// </summary>
public record HistorialProduccion(DateTime Desde, DateTime Hasta, IReadOnlyList<ProduccionMes> Meses)
{
    public const int MesesVentana = 6;
    public int Lotes => Meses.Sum(m => m.Lotes);
    public double TmsTotal => Meses.Sum(m => m.Tms);
    public double TmsPromedioMensual => TmsTotal / MesesVentana;
    public int MesesConEntregas => Meses.Count(m => m.Lotes > 0);
    public double LeyCuPct => Ponderar(m => m.LeyCuPct);
    public double LeyAuGt => Ponderar(m => m.LeyAuGt);
    public double LeyAgGt => Ponderar(m => m.LeyAgGt);
    private double Ponderar(Func<ProduccionMes, double> ley)
    {
        double conLey = Meses.Sum(m => m.TmsConLey);
        return conLey == 0 ? 0 : Meses.Sum(m => ley(m) * m.TmsConLey) / conLey;
    }
}

/// <summary>Datos para registrar los recursos de una categoría.</summary>
public class DatosRecurso
{
    public string Categoria { get; set; } = CategoriasRecurso.Medido;
    public decimal? Toneladas { get; set; }
    public decimal? LeyCuPct { get; set; }
    public decimal? LeyAuGt { get; set; }
    public decimal? LeyAgGt { get; set; }
    public string? Fuente { get; set; }
}

/// <summary>Búsqueda de productores y faenas con sus datos guardados (BOT-02 en el informe).</summary>
public class ServicioFichas(IAlmacen db, ServicioAuditoria auditoria)
{
    /// <summary>
    /// Busca productores por nombre o razón social, y también por el nombre de alguna de sus faenas.
    /// No distingue mayúsculas ni tildes. Devuelve como máximo 25 coincidencias.
    /// </summary>
    public IReadOnlyList<CoincidenciaProductor> BuscarPorNombre(string texto, int maximo = 25)
    {
        var t = Simplificar(texto);
        if (t.Length < 3) return [];
        var productores = db.Tabla<Productor>().ToList();
        var faenas = db.Tabla<Faena>().Select(f => new { f.IdProductor, f.Nombre }).ToList().ToLookup(f => f.IdProductor, f => f.Nombre);
        var comunas = db.Tabla<Comuna>().ToDictionary(c => c.Id, c => c.Nombre);
        var lista = new List<CoincidenciaProductor>();
        foreach (var p in productores)
        {
            bool porNombre = Simplificar(p.NombreMostrado).Contains(t);
            var faena = porNombre ? null : faenas[p.Id].FirstOrDefault(n => Simplificar(n).Contains(t));
            if (!porNombre && faena is null) continue;
            lista.Add(new CoincidenciaProductor(p.Rut, p.NombreMostrado, p.IdComuna is int c ? comunas.GetValueOrDefault(c) : null, faenas[p.Id].Count(), faena));
        }
        return lista.OrderBy(x => x.FaenaCoincidente is null ? 0 : 1).ThenBy(x => x.Nombre).Take(maximo).ToList();
    }

    /// <summary>Entregas de la faena en los 6 meses que terminan en el último mes del reporte de recepción cargado.</summary>
    public HistorialProduccion? Produccion(int idFaena) => Produccion([idFaena]);

    /// <summary>Entregas de todas las faenas del productor en los mismos 6 meses (sección 4.1 de la planilla de evaluación).</summary>
    public HistorialProduccion? ProduccionProductor(int idProductor) =>
        Produccion(db.Tabla<Faena>().Where(f => f.IdProductor == idProductor).Select(f => f.Id).ToArray());

    private HistorialProduccion? Produccion(int[] idsFaena)
    {
        var recepciones = db.Tabla<RecepcionMineral>();
        if (!recepciones.Any()) return null;
        var corte = recepciones.Max(r => r.FechaCierre);
        var hasta = new DateTime(corte.Year, corte.Month, 1).AddMonths(1);
        var desde = hasta.AddMonths(-HistorialProduccion.MesesVentana);
        var lotes = recepciones.Where(r => idsFaena.Contains(r.IdFaena) && r.FechaCierre >= desde && r.FechaCierre < hasta)
            .Select(r => new { r.FechaCierre, r.PesoSecoKg, r.LeyCuPct, r.LeyAuGt, r.LeyAgGt }).ToList();
        var meses = Enumerable.Range(0, HistorialProduccion.MesesVentana).Select(i =>
        {
            var mes = desde.AddMonths(i);
            var delMes = lotes.Where(l => l.FechaCierre >= mes && l.FechaCierre < mes.AddMonths(1)).ToList();
            double tms = delMes.Sum(l => (double)l.PesoSecoKg) / 1000;
            var conLey = delMes.Where(l => l.LeyCuPct + l.LeyAuGt + l.LeyAgGt > 0).ToList();
            double kg = conLey.Sum(l => (double)l.PesoSecoKg);
            double Ponderada(Func<decimal, decimal, decimal, decimal> ley) =>
                kg == 0 ? 0 : conLey.Sum(l => (double)(ley(l.LeyCuPct, l.LeyAuGt, l.LeyAgGt) * l.PesoSecoKg)) / kg;
            return new ProduccionMes(mes, delMes.Count, tms, kg / 1000, Ponderada((cu, _, _) => cu), Ponderada((_, au, _) => au), Ponderada((_, _, ag) => ag));
        }).ToList();
        return new HistorialProduccion(desde, hasta.AddDays(-1), meses);
    }

    /// <summary>Cantidad de lotes de recepción cargados y fecha del último.</summary>
    public (int Lotes, DateTime? Hasta) ResumenRecepcion()
    {
        var q = db.Tabla<RecepcionMineral>();
        int n = q.Count();
        return (n, n == 0 ? null : q.Max(r => r.FechaCierre));
    }

    public IReadOnlyList<RecursoFaena> Recursos(int idFaena) =>
        db.Tabla<RecursoFaena>().Where(r => r.IdFaena == idFaena).ToList()
          .OrderBy(r => Array.IndexOf(CategoriasRecurso.Todas, r.Categoria)).ToList();

    /// <summary>Registra o reemplaza los recursos de una categoría de la faena. Lo pueden hacer el ingeniero, el jefe de oficina y el administrador.</summary>
    public void GuardarRecurso(int idFaena, DatosRecurso d, UsuarioSesion usuario)
    {
        if (usuario.Rol is not (Roles.Ingeniero or Roles.JefeOficina or Roles.Administrador))
            throw new ErrorNegocio("Su rol no puede registrar recursos de faenas.");
        var faena = db.Tabla<Faena>().FirstOrDefault(f => f.Id == idFaena) ?? throw new ErrorNegocio("La faena no existe.");
        var errores = new List<string>();
        if (!CategoriasRecurso.Todas.Contains(d.Categoria)) errores.Add("Seleccione la categoría (medido, indicado o inferido).");
        if (d.Toneladas is null || d.Toneladas <= 0) errores.Add("Las toneladas deben ser mayores que cero.");
        else if (d.Toneladas >= 1_000_000_000_000m) errores.Add("Las toneladas son demasiado grandes.");
        if (d.LeyCuPct is < 0 or >= 100) errores.Add("La ley de cobre debe estar entre 0 y 100 %.");
        if (d.LeyAuGt is < 0 or >= 1_000_000 || d.LeyAgGt is < 0 or >= 1_000_000) errores.Add("Las leyes de oro y plata deben ser cero o positivas.");
        if ((d.LeyCuPct ?? 0) == 0 && (d.LeyAuGt ?? 0) == 0 && (d.LeyAgGt ?? 0) == 0) errores.Add("Ingrese al menos una ley (cobre, oro o plata).");
        if (d.Fuente is { Length: > 100 }) errores.Add("La fuente admite hasta 100 caracteres.");
        if (errores.Count > 0) throw new ErrorNegocio(string.Join(" ", errores));

        var r = db.Tabla<RecursoFaena>().FirstOrDefault(x => x.IdFaena == idFaena && x.Categoria == d.Categoria);
        if (r is null) { r = new RecursoFaena { IdFaena = idFaena, Categoria = d.Categoria }; db.Agregar(r); }
        r.Toneladas = Math.Round(d.Toneladas!.Value, 0);
        r.LeyCuPct = Math.Round(d.LeyCuPct ?? 0, 4);
        r.LeyAuGt = Math.Round(d.LeyAuGt ?? 0, 3);
        r.LeyAgGt = Math.Round(d.LeyAgGt ?? 0, 3);
        r.Fuente = string.IsNullOrWhiteSpace(d.Fuente) ? null : d.Fuente.Trim();
        r.IdUsuario = usuario.Id;
        r.FechaRegistro = DateTime.Now;
        db.Guardar();
        auditoria.Registrar("RECURSO_FAENA", usuario.Id, detalle: $"Faena {faena.Codigo}: {CategoriasRecurso.Nombre(d.Categoria)} {r.Toneladas:N0} t");
    }

    public void EliminarRecurso(int idRecurso, UsuarioSesion usuario)
    {
        if (usuario.Rol is not (Roles.Ingeniero or Roles.JefeOficina or Roles.Administrador))
            throw new ErrorNegocio("Su rol no puede modificar recursos de faenas.");
        var r = db.Tabla<RecursoFaena>().FirstOrDefault(x => x.Id == idRecurso) ?? throw new ErrorNegocio("El registro no existe.");
        db.Eliminar(r);
        db.Guardar();
        auditoria.Registrar("RECURSO_FAENA", usuario.Id, detalle: $"Se eliminó el recurso {CategoriasRecurso.Nombre(r.Categoria)} de la faena {r.IdFaena}");
    }

    /// <summary>Minúsculas y sin tildes, para comparar nombres.</summary>
    public static string Simplificar(string? s) =>
        new string((s ?? "").Trim().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToLowerInvariant();

    public FichaProductor? BuscarProductor(string rut, DateTime? hoy = null)
    {
        var normalizado = ReglasCalidad.NormalizarRut(rut);
        if (normalizado is null) return null;
        var p = db.Tabla<Productor>().FirstOrDefault(x => x.Rut == normalizado);
        if (p is null) return null;
        var comuna = p.IdComuna is int c ? db.Tabla<Comuna>().FirstOrDefault(x => x.Id == c) : null;
        var faenas = db.Tabla<Faena>().Where(f => f.IdProductor == p.Id).OrderBy(f => f.Codigo).ToList()
            .Select(f => Ficha(f, p, hoy ?? DateTime.Today)).ToList();
        return new FichaProductor(p, comuna?.Nombre, comuna?.Region, faenas);
    }

    public (FichaProductor Productor, FichaFaena Faena)? BuscarFaena(string codigo, DateTime? hoy = null)
    {
        var normalizado = ReglasCalidad.NormalizarCodigoFaena(codigo);
        if (normalizado is null) return null;
        var f = db.Tabla<Faena>().FirstOrDefault(x => x.Codigo == normalizado);
        if (f is null) return null;
        var p = db.Tabla<Productor>().First(x => x.Id == f.IdProductor);
        var ficha = BuscarProductor(p.Rut, hoy)!;
        return (ficha, ficha.Faenas.First(x => x.Faena.Id == f.Id));
    }

    public FichaFaena? Faena(int idFaena, DateTime? hoy = null)
    {
        var f = db.Tabla<Faena>().FirstOrDefault(x => x.Id == idFaena);
        if (f is null) return null;
        var p = db.Tabla<Productor>().First(x => x.Id == f.IdProductor);
        return Ficha(f, p, hoy ?? DateTime.Today);
    }

    public (int Productores, int Faenas, int FaenasElegibles) Totales()
    {
        var hoy = DateTime.Today;
        int elegibles = db.Tabla<Faena>().Count(f => f.Estado == EstadosFaena.Activa && f.IdOficina != null && f.VencimientoPadron >= hoy);
        return (db.Tabla<Productor>().Count(), db.Tabla<Faena>().Count(), elegibles);
    }

    private FichaFaena Ficha(Faena f, Productor p, DateTime hoy)
    {
        string? oficina = f.IdOficina is int o ? db.Tabla<Oficina>().Where(x => x.Id == o).Select(x => x.Nombre).FirstOrDefault() : null;
        var comuna = f.IdComuna is int c ? db.Tabla<Comuna>().FirstOrDefault(x => x.Id == c) : null;
        return new FichaFaena(f, oficina, comuna?.Nombre, comuna?.Region, ReglasElegibilidad.Evaluar(p, f, hoy));
    }
}
