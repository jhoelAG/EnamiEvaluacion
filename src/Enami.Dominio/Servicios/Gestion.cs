using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>Pregunta 1: evaluaciones de un mes en una oficina.</summary>
public record FilaMesOficina(DateTime Mes, string Oficina, int Ingresadas, int Aprobadas, int Rechazadas)
{
    /// <summary>Aprobadas sobre las resueltas (aprobadas + rechazadas) del mes; nulo si no hubo dictámenes.</summary>
    public double? TasaAprobacion => Aprobadas + Rechazadas == 0 ? null : Math.Round(100.0 * Aprobadas / (Aprobadas + Rechazadas), 1);
}

/// <summary>Pregunta 2: demora promedio de un nivel de aprobación, en horas hábiles desde que se le asigna hasta que decide.</summary>
public record FilaNivel(string Nivel, int Decisiones, double HorasPromedio);

/// <summary>Pregunta 3: monto aprobado de un grupo (instrumento o tipo de productor) y su participación en el total.</summary>
public record FilaMonto(string Grupo, int Evaluaciones, decimal Monto, double Porcentaje);

/// <summary>Pregunta 4: una causal de rechazo y cuántas veces se usó.</summary>
public record FilaCausal(string Codigo, string Descripcion, string Tipo, int Veces, double Porcentaje);

public record ResultadoGestion(
    IReadOnlyList<FilaMesOficina> PorMesYOficina,
    IReadOnlyList<FilaNivel> PorNivel,
    IReadOnlyList<FilaMonto> MontoPorInstrumento,
    IReadOnlyList<FilaMonto> MontoPorTipoProductor,
    IReadOnlyList<FilaCausal> Causales);

/// <summary>
/// Datos de gestión del tablero: responden las preguntas de negocio 1 a 4 de la sección 4.3 del informe
/// (la pregunta 5 la responden los indicadores TEP e IAM, en <see cref="ServicioIndicadores"/>).
/// Las evaluaciones se cuentan por la fecha de ingreso (ingresadas) o por la fecha del dictamen (aprobadas y rechazadas).
/// </summary>
public class ServicioGestion(IAlmacen db)
{
    public ResultadoGestion Calcular(DateTime desde, DateTime hasta, int? idOficina = null)
    {
        desde = desde.Date;
        var finExclusivo = hasta.Date.AddDays(1);
        bool EnPeriodo(DateTime? f) => f is DateTime x && x >= desde && x < finExclusivo;

        var faenas = db.Tabla<Faena>().ToList().Where(f => idOficina is null || f.IdOficina == idOficina).ToDictionary(f => f.Id);
        var oficinas = db.Tabla<Oficina>().ToDictionary(o => o.Id, o => o.Nombre);
        var productores = db.Tabla<Productor>().ToDictionary(p => p.Id, p => p.TipoProductor);
        var evaluaciones = db.Tabla<SolicitudCredito>().ToList().Where(s => faenas.ContainsKey(s.IdFaena)).ToList();
        string Oficina(SolicitudCredito s) => faenas[s.IdFaena].IdOficina is int o ? oficinas.GetValueOrDefault(o, "Sin oficina") : "Sin oficina";
        static DateTime Mes(DateTime f) => new(f.Year, f.Month, 1);

        var resueltas = evaluaciones.Where(s => EnPeriodo(s.FechaDictamen) && s.Estado is EstadosSolicitud.Aprobada or EstadosSolicitud.Rechazada).ToList();
        var aprobadas = resueltas.Where(s => s.Estado == EstadosSolicitud.Aprobada).ToList();
        var rechazadas = resueltas.Where(s => s.Estado == EstadosSolicitud.Rechazada).ToList();

        // 1. Por mes y oficina
        var claves = evaluaciones.Where(s => EnPeriodo(s.FechaIngreso)).Select(s => (Mes(s.FechaIngreso), Oficina(s)))
            .Concat(resueltas.Select(s => (Mes(s.FechaDictamen!.Value), Oficina(s)))).Distinct();
        var porMes = claves.Select(k => new FilaMesOficina(k.Item1, k.Item2,
                evaluaciones.Count(s => EnPeriodo(s.FechaIngreso) && Mes(s.FechaIngreso) == k.Item1 && Oficina(s) == k.Item2),
                aprobadas.Count(s => Mes(s.FechaDictamen!.Value) == k.Item1 && Oficina(s) == k.Item2),
                rechazadas.Count(s => Mes(s.FechaDictamen!.Value) == k.Item1 && Oficina(s) == k.Item2)))
            .OrderBy(f => f.Mes).ThenBy(f => f.Oficina).ToList();

        // 2. Demora por nivel de aprobación (decisiones tomadas en el período)
        var ids = evaluaciones.Select(s => s.Id).ToHashSet();
        var roles = db.Tabla<Rol>().ToDictionary(r => r.Id, r => r.Nombre);
        var rolDe = db.Tabla<Usuario>().ToDictionary(u => u.Id, u => roles.GetValueOrDefault(u.IdRol, ""));
        var porNivel = db.Tabla<Aprobacion>().Where(a => a.FechaDecision != null).ToList()
            .Where(a => ids.Contains(a.IdSolicitud) && EnPeriodo(a.FechaDecision))
            .GroupBy(a => Roles.Nombre(rolDe.GetValueOrDefault(a.IdUsuario, "")))
            .Select(g => new FilaNivel(g.Key, g.Count(),
                Math.Round(g.Average(a => ServicioIndicadores.HorasHabiles(a.FechaAsignacion, a.FechaDecision!.Value)), 1)))
            .OrderByDescending(f => f.HorasPromedio).ToList();

        // 3. Monto aprobado por instrumento y por tipo de productor
        var instrumentos = db.Tabla<InstrumentoFomento>().ToDictionary(i => i.Id, i => i.Nombre);
        decimal total = aprobadas.Sum(s => s.MontoSolicitado);
        List<FilaMonto> Montos(Func<SolicitudCredito, string> grupo) => aprobadas.GroupBy(grupo)
            .Select(g => new FilaMonto(g.Key, g.Count(), g.Sum(s => s.MontoSolicitado),
                total == 0 ? 0 : Math.Round((double)(100 * g.Sum(s => s.MontoSolicitado) / total), 1)))
            .OrderByDescending(f => f.Monto).ToList();
        var porInstrumento = Montos(s => instrumentos.GetValueOrDefault(s.IdInstrumento, $"Instrumento {s.IdInstrumento}"));
        var porTipo = Montos(s => productores.GetValueOrDefault(faenas[s.IdFaena].IdProductor) == TiposProductor.Empresa ? "Empresa" : "Persona natural");

        // 4. Causales de rechazo
        var causales = db.Tabla<CausalRechazo>().ToDictionary(c => c.Id);
        var porCausal = rechazadas.GroupBy(s => s.IdCausal)
            .Select(g =>
            {
                var c = g.Key is short id ? causales.GetValueOrDefault(id) : null;
                return new FilaCausal(c?.Codigo ?? "—", c?.Descripcion ?? "Sin causal registrada", c?.Tipo ?? "", g.Count(),
                    Math.Round(100.0 * g.Count() / rechazadas.Count, 1));
            })
            .OrderByDescending(f => f.Veces).ThenBy(f => f.Codigo).ToList();

        return new ResultadoGestion(porMes, porNivel, porInstrumento, porTipo, porCausal);
    }
}
