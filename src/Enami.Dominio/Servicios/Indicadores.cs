using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>TPE: una evaluación con dictamen y sus horas hábiles de tramitación.</summary>
public record FilaTpe(long Id, string Nro, string Estado, DateTime Ingreso, DateTime Dictamen, double HorasHabiles);

/// <summary>TEP: la evaluación calculada y, si lo hay, el valor de mercado desactualizado que usó.</summary>
public record FilaTep(long Id, string Nro, DateTime FechaCalculo, string? Problema);

/// <summary>ITR: la evaluación con dictamen y lo que le falta a su registro (vacío si está completo).</summary>
public record FilaItr(long Id, string Nro, string Estado, IReadOnlyList<string> Faltas)
{
    public bool Trazable => Faltas.Count == 0;
}

/// <summary>IAM: un día hábil y la hora del primer registro automático del dólar y del cobre.</summary>
public record FilaIam(DateTime Dia, DateTime? Dolar, DateTime? Cobre, bool Cumple);

/// <summary>Los cuatro indicadores del período, con el detalle que los respalda.</summary>
public record ResultadoIndicadores(
    DateTime Desde, DateTime Hasta,
    double? Tpe, IReadOnlyList<FilaTpe> DetalleTpe,
    double? Tep, IReadOnlyList<FilaTep> DetalleTep,
    double? Itr, IReadOnlyList<FilaItr> DetalleItr,
    double? Iam, IReadOnlyList<FilaIam> DetalleIam);

/// <summary>
/// Indicadores clave del tablero (RF-09, RF-30, HU-19; sección 4.1 del informe). Se calculan con los datos que la plataforma
/// ya registra, sin ingreso manual:
/// TPE, tiempo promedio de tramitación en horas hábiles entre el ingreso y el dictamen;
/// TEP, % de evaluaciones calculadas con un dólar o un cobre de más de un día hábil de antigüedad;
/// ITR, % de evaluaciones con dictamen y registro completo (decisiones, causal y auditoría);
/// IAM, % de días hábiles en que el dólar y el cobre se actualizaron solos (BOT-01 o la consulta automática) antes de las 09:30.
/// </summary>
public class ServicioIndicadores(IAlmacen db)
{
    public static readonly TimeSpan InicioJornada = new(8, 30, 0), FinJornada = new(17, 30, 0), LimiteMercado = new(9, 30, 0);
    public const double MetaTep = 0, MetaItr = 100, MetaIam = 95;
    /// <summary>Variables de mercado que miden TEP e IAM, con su nombre para mostrar.</summary>
    public static readonly IReadOnlyDictionary<string, string> VariablesMercado = new Dictionary<string, string>
    {
        ["Dolar"] = "Dólar observado",
        ["PrecioCuLb"] = "Precio del cobre",
    };

    public ResultadoIndicadores Calcular(DateTime desde, DateTime hasta, int? idOficina = null, DateTime? ahora = null)
    {
        desde = desde.Date;
        hasta = hasta.Date;
        var finExclusivo = hasta.AddDays(1);
        var momento = ahora ?? DateTime.Now;

        HashSet<int>? faenas = idOficina is int o ? db.Tabla<Faena>().Where(f => f.IdOficina == o).Select(f => f.Id).ToHashSet() : null;
        var evaluaciones = db.Tabla<SolicitudCredito>().ToList().Where(s => faenas is null || faenas.Contains(s.IdFaena)).ToList();

        // TPE e ITR: evaluaciones con dictamen (aprobadas o rechazadas) en el período
        var conDictamen = evaluaciones
            .Where(s => s.FechaDictamen is DateTime f && f >= desde && f < finExclusivo
                        && s.Estado is EstadosSolicitud.Aprobada or EstadosSolicitud.Rechazada)
            .OrderBy(s => s.FechaDictamen).ToList();
        var tpe = conDictamen.Select(s => new FilaTpe(s.Id, s.NroSolicitud, s.Estado, s.FechaIngreso, s.FechaDictamen!.Value,
            Math.Round(HorasHabiles(s.FechaIngreso, s.FechaDictamen.Value), 1))).ToList();
        var itr = Trazabilidad(conDictamen);

        // TEP: el último cálculo del período de cada evaluación
        var ids = evaluaciones.ToDictionary(s => s.Id, s => s.NroSolicitud);
        var calculos = db.Tabla<ResultadoCalculo>().Where(r => r.FechaCalculo >= desde && r.FechaCalculo < finExclusivo).ToList()
            .Where(r => ids.ContainsKey(r.IdSolicitud))
            .GroupBy(r => r.IdSolicitud).Select(g => g.OrderBy(r => r.FechaCalculo).ThenBy(r => r.Id).Last())
            .OrderBy(r => r.FechaCalculo).ToList();
        var codigos = VariablesMercado.Keys.ToList();
        var parametros = db.Tabla<ParametroEconomico>().Where(p => codigos.Contains(p.CodigoVariable)).ToList();
        var tep = calculos.Select(r => new FilaTep(r.IdSolicitud, ids[r.IdSolicitud], r.FechaCalculo, Desactualizado(r.FechaCalculo, parametros))).ToList();

        // IAM: días hábiles del período ya transcurridos (hoy cuenta desde las 09:30)
        var iam = new List<FilaIam>();
        for (var dia = desde; dia <= hasta; dia = dia.AddDays(1))
        {
            if (!EsHabil(dia) || dia > momento.Date || (dia == momento.Date && momento.TimeOfDay < LimiteMercado)) continue;
            DateTime? Primero(string codigo) => parametros
                .Where(p => p.CodigoVariable == codigo && p.Fecha.Date == dia && p.IdUsuario is null && p.Fuente != "MANUAL")
                .Select(p => (DateTime?)p.Fecha).Min();
            var dolar = Primero("Dolar");
            var cobre = Primero("PrecioCuLb");
            bool cumple = dolar?.TimeOfDay <= LimiteMercado && cobre?.TimeOfDay <= LimiteMercado;
            iam.Add(new FilaIam(dia, dolar, cobre, cumple));
        }

        return new ResultadoIndicadores(desde, hasta,
            tpe.Count == 0 ? null : Math.Round(tpe.Average(f => f.HorasHabiles), 1), tpe,
            Porcentaje(tep.Count(f => f.Problema is not null), tep.Count), tep,
            Porcentaje(itr.Count(f => f.Trazable), itr.Count), itr,
            Porcentaje(iam.Count(f => f.Cumple), iam.Count), iam);
    }

    /// <summary>ITR: una decisión por cada nivel que intervino, la causal si se rechazó, y el envío y cada decisión en la auditoría.</summary>
    private List<FilaItr> Trazabilidad(List<SolicitudCredito> conDictamen)
    {
        var ids = conDictamen.Select(s => s.Id).ToList();
        var aprobaciones = db.Tabla<Aprobacion>().Where(a => ids.Contains(a.IdSolicitud)).ToList().ToLookup(a => a.IdSolicitud);
        var acciones = db.Tabla<Auditoria>().Where(a => a.IdSolicitud != null && ids.Contains(a.IdSolicitud.Value))
            .Select(a => new { a.IdSolicitud, a.Accion }).ToList().ToLookup(a => a.IdSolicitud!.Value, a => a.Accion);
        return conDictamen.Select(s =>
        {
            var faltas = new List<string>();
            var cadena = aprobaciones[s.Id].ToList();
            int decididas = cadena.Count(a => a.Decision is not null);
            if (cadena.Count == 0) faltas.Add("sin decisiones registradas");
            else if (decididas < cadena.Count) faltas.Add("hay un nivel sin decisión");
            if (s.Estado == EstadosSolicitud.Rechazada && s.IdCausal is null) faltas.Add("rechazada sin causal");
            if (!acciones[s.Id].Contains("SOLICITUD_ENVIADA")) faltas.Add("sin el envío en la auditoría");
            if (acciones[s.Id].Count(a => a.StartsWith("DECISION_")) < decididas) faltas.Add("decisiones sin registro en la auditoría");
            return new FilaItr(s.Id, s.NroSolicitud, s.Estado, faltas);
        }).ToList();
    }

    /// <summary>
    /// TEP: un cálculo usó un valor desactualizado si el dólar o el cobre vigente en ese momento correspondía a más de un día hábil
    /// antes. Un valor que fijó una persona (eligiendo una fecha o a mano) no se cuenta: se usó a propósito.
    /// </summary>
    private static string? Desactualizado(DateTime fechaCalculo, List<ParametroEconomico> parametros)
    {
        var problemas = new List<string>();
        foreach (var (codigo, nombre) in VariablesMercado)
        {
            var vigente = parametros.Where(p => p.CodigoVariable == codigo && p.Fecha <= fechaCalculo)
                .OrderBy(p => p.Fecha).ThenBy(p => p.Id).LastOrDefault();
            if (vigente is null) { problemas.Add($"{nombre}: sin valor registrado"); continue; }
            if (vigente.IdUsuario is not null) continue;
            var delDia = (vigente.FechaValor ?? vigente.Fecha).Date;
            int dias = DiasHabilesEntre(delDia, fechaCalculo.Date);
            if (dias > 1) problemas.Add($"{nombre} del {delDia:dd-MM-yyyy} ({dias} días hábiles de antigüedad)");
        }
        return problemas.Count == 0 ? null : string.Join("; ", problemas);
    }

    public static bool EsHabil(DateTime dia) => dia.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>Días hábiles después de <paramref name="desde"/> hasta <paramref name="hasta"/> inclusive (viernes a lunes = 1).</summary>
    public static int DiasHabilesEntre(DateTime desde, DateTime hasta)
    {
        int n = 0;
        for (var d = desde.Date.AddDays(1); d <= hasta.Date; d = d.AddDays(1))
            if (EsHabil(d)) n++;
        return n;
    }

    /// <summary>Horas entre dos momentos contando solo la jornada hábil: lunes a viernes de 08:30 a 17:30 (sin feriados).</summary>
    public static double HorasHabiles(DateTime inicio, DateTime fin)
    {
        if (fin <= inicio) return 0;
        double horas = 0;
        for (var dia = inicio.Date; dia <= fin.Date; dia = dia.AddDays(1))
        {
            if (!EsHabil(dia)) continue;
            var desde = Max(inicio, dia + InicioJornada);
            var hasta = Min(fin, dia + FinJornada);
            if (hasta > desde) horas += (hasta - desde).TotalHours;
        }
        return horas;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static double? Porcentaje(int parte, int total) => total == 0 ? null : Math.Round(100.0 * parte / total, 1);
}
