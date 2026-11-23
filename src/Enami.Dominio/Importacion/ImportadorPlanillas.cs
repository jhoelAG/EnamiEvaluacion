using Enami.Dominio.Modelo;

namespace Enami.Dominio.Importacion;

/// <summary>Fila rechazada durante una carga, con su motivo.</summary>
public record FilaRechazada(int Fila, string Campo, string Motivo);

/// <summary>Observación que no rechaza la fila, pero deja el campo sin dato (por ejemplo, distancia en 0).</summary>
public record Observacion(int Fila, string Campo, string Detalle);

public class ResultadoImportacion<T>
{
    public List<T> Validas { get; } = [];
    public List<FilaRechazada> Rechazadas { get; } = [];
    public List<Observacion> Observaciones { get; } = [];
    public int FilasLeidas { get; set; }
}

/// <summary>Productor leído de 1_Ficha_Productor.xlsx, antes de resolver la comuna.</summary>
public record ProductorImportado(string Rut, string Tipo, string? Nombres, string? Apellidos, string? RazonSocial,
                                 string? Comuna, string? Region, string NivelCumplimiento, DateTime FechaActualizacion,
                                 string? Direccion = null, string? Telefono = null, string? Correo = null);

/// <summary>Faena leída de 2_Ficha_Faena.xlsx, antes de resolver productor, comuna y oficina.</summary>
public record FaenaImportada(string Codigo, string Rut, string Nombre, string Estado, string? Oficina, string? Comuna, string? Region, decimal? DistanciaKm,
                             string? Ubicacion = null, decimal? Norte = null, decimal? Este = null, short? ManoObra = null);

/// <summary>Padrón leído de 3_BD_Padrones.xlsx.</summary>
public record PadronImportado(string CodigoFaena, string NroPadron, bool Activo, DateTime? Vencimiento);

/// <summary>Lote leído de 4_Recepción_Minerales.xlsx, antes de resolver la faena.</summary>
public record RecepcionImportada(string NroLote, string CodigoFaena, DateTime FechaCierre, string Agencia, string Producto,
                                 decimal PesoSecoKg, decimal LeyCuPct, decimal LeyAuGt, decimal LeyAgGt);

/// <summary>
/// Convierte las planillas institucionales en registros listos para guardar (BOT-05, RF-26 y RF-07).
/// Las filas con errores no detienen la carga: se aíslan con su motivo y el resto continúa.
/// </summary>
public static class ImportadorPlanillas
{
    /// <summary>1_Ficha_Productor.xlsx. Elimina duplicados por RUT y conserva la ficha más reciente.</summary>
    public static ResultadoImportacion<ProductorImportado> Productores(IEnumerable<(int Fila, Dictionary<string, string> Valores)> filas)
    {
        var r = new ResultadoImportacion<ProductorImportado>();
        var porRut = new Dictionary<string, (ProductorImportado P, int Fila)>();
        foreach (var (fila, v) in filas)
        {
            r.FilasLeidas++;
            var rut = ReglasCalidad.NormalizarRut(Get(v, "RUT"));
            if (!ReglasCalidad.RutConFormatoValido(rut))
            {
                r.Rechazadas.Add(new(fila, "RUT", $"RUT vacío o con formato inválido («{Get(v, "RUT")}»)."));
                continue;
            }
            var nombre = ReglasCalidad.LimpiarTexto(Get(v, "Nombre"));
            if (nombre is null)
            {
                r.Rechazadas.Add(new(fila, "Nombre", "El nombre del productor está vacío."));
                continue;
            }
            bool empresa = (ReglasCalidad.LimpiarTexto(Get(v, "Tipo")) ?? "").StartsWith("Jur", StringComparison.OrdinalIgnoreCase);
            var (nombres, apellidos) = empresa ? (null, null) : SepararNombre(nombre);
            var fecha = ReglasCalidad.LeerFecha(Get(v, "Última Actualización Fecha")) ?? DateTime.MinValue;
            var p = new ProductorImportado(rut!, empresa ? TiposProductor.Empresa : TiposProductor.Natural, nombres, apellidos,
                empresa ? nombre : null, ReglasCalidad.LimpiarTexto(Get(v, "Comuna")), ReglasCalidad.LimpiarTexto(Get(v, "Región")),
                NivelCumplimiento(Get(v, "Nivel de Cumplimiento con deudas en ENAMI")), fecha,
                Recortar(ReglasCalidad.LimpiarTexto(Get(v, "Domicilio Particular")), 150),
                Recortar(ReglasCalidad.LimpiarTexto(Get(v, "Fono Celular Particular")) ?? ReglasCalidad.LimpiarTexto(Get(v, "Fono Fijo Particular")), 30),
                Recortar(ReglasCalidad.LimpiarTexto(Get(v, "Correo Electronico Particular")), 120));
            if (porRut.TryGetValue(rut!, out var previo))
            {
                r.Observaciones.Add(new(fila, "RUT", $"RUT duplicado (también en la fila {previo.Fila}); se conserva la ficha más reciente."));
                if (p.FechaActualizacion >= previo.P.FechaActualizacion) porRut[rut!] = (p, fila);
            }
            else porRut[rut!] = (p, fila);
        }
        r.Validas.AddRange(porRut.Values.Select(x => x.P));
        return r;
    }

    /// <summary>2_Ficha_Faena.xlsx. <paramref name="existeProductor"/> indica si el RUT del titular ya está cargado.</summary>
    public static ResultadoImportacion<FaenaImportada> Faenas(IEnumerable<(int Fila, Dictionary<string, string> Valores)> filas, Func<string, bool> existeProductor)
    {
        var r = new ResultadoImportacion<FaenaImportada>();
        var codigos = new HashSet<string>();
        foreach (var (fila, v) in filas)
        {
            r.FilasLeidas++;
            var codigo = ReglasCalidad.NormalizarCodigoFaena(Get(v, "Código"));
            if (codigo is null) { r.Rechazadas.Add(new(fila, "Código", "Código de faena vacío.")); continue; }
            if (!codigos.Add(codigo)) { r.Observaciones.Add(new(fila, "Código", $"Código {codigo} repetido; se conserva la primera fila.")); continue; }
            var rut = ReglasCalidad.NormalizarRut(Get(v, "RUT"));
            if (rut is null || !existeProductor(rut)) { r.Rechazadas.Add(new(fila, "RUT", $"El productor {rut ?? "(vacío)"} no existe en la ficha de productores.")); continue; }

            double? distancia = ReglasCalidad.LeerNumero(Get(v, "Distancia Poder de Compra"));
            var problemaDistancia = ReglasCalidad.ValidarDistancia(distancia);
            if (problemaDistancia is not null)
            {
                r.Observaciones.Add(new(fila, "Distancia Poder de Compra", problemaDistancia));
                distancia = null;
            }
            var oficina = ReglasCalidad.LimpiarTexto(Get(v, "Oficina"));
            if (oficina is null)
                r.Observaciones.Add(new(fila, "Oficina", "Faena sin oficina asignada; debe completarse antes de evaluar (define al Jefe de Oficina)."));
            var estado = ReglasCalidad.LimpiarTexto(Get(v, "Estado"));
            r.Validas.Add(new FaenaImportada(codigo, rut, Recortar(ReglasCalidad.LimpiarTexto(Get(v, "Faena Nombre")) ?? "(sin nombre)", 100),
                estado is not null && estado.StartsWith("Inac", StringComparison.OrdinalIgnoreCase) ? EstadosFaena.Inactiva : EstadosFaena.Activa,
                oficina, ReglasCalidad.LimpiarTexto(Get(v, "Comuna")), ReglasCalidad.LimpiarTexto(Get(v, "Region")), (decimal?)distancia,
                Ubicacion(Get(v, "Sierra"), Get(v, "Localidad")), Coordenada(Get(v, "Norte")), Coordenada(Get(v, "Este")),
                ManoObra(Get(v, "Mano de Obra Contratada - Formal"), Get(v, "Mano de Obra Contratada - Informal"))));
        }
        return r;
    }

    /// <summary>
    /// 3_BD_Padrones.xlsx. Una faena puede tener muchos padrones históricos: se conserva, por código de faena,
    /// el padrón activo con el vencimiento más lejano (o, si no hay activos, el último vencido).
    /// </summary>
    public static ResultadoImportacion<PadronImportado> Padrones(IEnumerable<(int Fila, Dictionary<string, string> Valores)> filas)
    {
        var r = new ResultadoImportacion<PadronImportado>();
        var porFaena = new Dictionary<string, PadronImportado>();
        foreach (var (fila, v) in filas)
        {
            r.FilasLeidas++;
            var codigo = ReglasCalidad.NormalizarCodigoFaena(Get(v, "Código Faena"));
            var nro = ReglasCalidad.LimpiarTexto(Get(v, "Nro Padrón"));
            if (codigo is null || nro is null) { r.Rechazadas.Add(new(fila, "Código Faena", "Fila sin código de faena o sin número de padrón.")); continue; }
            var p = new PadronImportado(codigo, Recortar(nro, 20), (ReglasCalidad.LimpiarTexto(Get(v, "Estado Padrón")) ?? "").StartsWith("Activ", StringComparison.OrdinalIgnoreCase),
                                        ReglasCalidad.LeerFecha(Get(v, "Fecha Vencimiento")));
            if (!porFaena.TryGetValue(codigo, out var previo) || Mejor(p, previo)) porFaena[codigo] = p;
        }
        r.Validas.AddRange(porFaena.Values);
        return r;
    }

    /// <summary>
    /// 4_Recepción_Minerales.xlsx: un lote por fila. Se rechazan los lotes sin número, sin faena cargada, sin fecha,
    /// con peso seco 0 o negativo, o con leyes fuera de rango (ley de cobre de 100 % o más, leyes negativas).
    /// <paramref name="existeFaena"/> indica si el código de mina ya está en la ficha de faenas.
    /// </summary>
    public static ResultadoImportacion<RecepcionImportada> Recepciones(IEnumerable<(int Fila, Dictionary<string, string> Valores)> filas, Func<string, bool> existeFaena)
    {
        var r = new ResultadoImportacion<RecepcionImportada>();
        var lotes = new HashSet<string>();
        var sinFaena = new Dictionary<string, int>();
        foreach (var (fila, v) in filas)
        {
            r.FilasLeidas++;
            var lote = ReglasCalidad.LimpiarTexto(Get(v, "N° LOTE"));
            if (lote is null || lote.Length > 12) { r.Rechazadas.Add(new(fila, "N° LOTE", "Lote sin número o con más de 12 caracteres.")); continue; }
            if (!lotes.Add(lote)) { r.Observaciones.Add(new(fila, "N° LOTE", $"Lote {lote} repetido; se conserva la primera fila.")); continue; }
            var codigo = ReglasCalidad.NormalizarCodigoFaena(Get(v, "COD MINA"));
            if (codigo is null || !existeFaena(codigo))
            {
                var k = codigo ?? "(vacío)";
                sinFaena[k] = sinFaena.GetValueOrDefault(k) + 1;
                r.Rechazadas.Add(new(fila, "COD MINA", $"La faena {k} no está en la ficha de faenas."));
                continue;
            }
            var fecha = ReglasCalidad.LeerFecha(Get(v, "FECHA CIERRE"));
            if (fecha is null) { r.Rechazadas.Add(new(fila, "FECHA CIERRE", "Lote sin fecha de cierre.")); continue; }
            var peso = ReglasCalidad.LeerNumero(Get(v, "P SECO (Kg)"));
            if (peso is null || peso <= 0) { r.Rechazadas.Add(new(fila, "P SECO (Kg)", $"Peso seco inválido («{Get(v, "P SECO (Kg)")}»); debe ser mayor que 0.")); continue; }
            double cu = ReglasCalidad.LeerNumero(Get(v, "LEY CU%")) ?? 0, au = ReglasCalidad.LeerNumero(Get(v, "LEY AU g/t")) ?? 0,
                   ag = ReglasCalidad.LeerNumero(Get(v, "LEY AG g/t")) ?? 0;
            if (cu < 0 || cu >= 100) { r.Rechazadas.Add(new(fila, "LEY CU%", $"Ley de cobre fuera de rango ({cu} %).")); continue; }
            if (au < 0 || ag < 0 || au > 999_999 || ag > 999_999) { r.Rechazadas.Add(new(fila, "LEY AU/AG", "Ley de oro o plata fuera de rango.")); continue; }
            r.Validas.Add(new RecepcionImportada(lote, codigo, fecha.Value.Date,
                Recortar(ReglasCalidad.LimpiarTexto(Get(v, "C_AGENCIA")) ?? "", 4), Recortar(ReglasCalidad.LimpiarTexto(Get(v, "C_PRODUCTO")) ?? "", 4),
                Math.Round((decimal)peso.Value, 0), Math.Round((decimal)cu, 4), Math.Round((decimal)au, 3), Math.Round((decimal)ag, 3)));
        }
        return r;
    }

    /// <summary>«Sierra Exploradora, Taltal»: sierra y localidad de la ficha de faena.</summary>
    public static string? Ubicacion(string? sierra, string? localidad)
    {
        var titulo = System.Globalization.CultureInfo.GetCultureInfo("es-CL").TextInfo;
        string? s = ReglasCalidad.LimpiarTexto(sierra), l = ReglasCalidad.LimpiarTexto(localidad);
        var partes = new List<string>();
        if (s is not null && !s.Equals(l, StringComparison.OrdinalIgnoreCase))
            partes.Add("Sierra " + titulo.ToTitleCase(s.ToLowerInvariant()).Replace(" De ", " de ").Replace(" La ", " la ").Replace(" Del ", " del "));
        if (l is not null) partes.Add(l);
        return partes.Count == 0 ? null : Recortar(string.Join(", ", partes), 150);
    }

    private static decimal? Coordenada(string? v) =>
        ReglasCalidad.LeerNumero(v) is double x && x > 0 && x < 100_000_000 ? Math.Round((decimal)x, 2) : null;

    /// <summary>Suma las personas de «Masculino:5, Femenino:1» de la mano de obra formal e informal; null si no hay datos.</summary>
    public static short? ManoObra(params string?[] textos)
    {
        int total = 0; bool hay = false;
        foreach (var t in textos.Where(t => !string.IsNullOrWhiteSpace(t)))
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(t!, @":\s*(\d+)"))
            {
                total += int.Parse(m.Groups[1].Value);
                hay = true;
            }
        return hay ? (short)Math.Min(total, short.MaxValue) : null;
    }

    private static bool Mejor(PadronImportado nuevo, PadronImportado actual)
    {
        if (nuevo.Activo != actual.Activo) return nuevo.Activo;
        return (nuevo.Vencimiento ?? DateTime.MinValue) > (actual.Vencimiento ?? DateTime.MinValue);
    }

    /// <summary>Alto y Medio se consideran al día; Bajo, moroso. Sin dato se considera al día (supuesto por validar con ENAMI).</summary>
    public static string NivelCumplimiento(string? valor) =>
        (ReglasCalidad.LimpiarTexto(valor) ?? "").ToUpperInvariant() switch
        {
            "BAJO" => NivelesCumplimiento.Moroso,
            "JUDICIAL" => NivelesCumplimiento.Judicial,
            _ => NivelesCumplimiento.AlDia
        };

    /// <summary>Separa «Juan Andrés Pérez Soto» en nombres («Juan Andrés») y apellidos («Pérez Soto»).</summary>
    public static (string? Nombres, string? Apellidos) SepararNombre(string completo)
    {
        var p = completo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length switch
        {
            0 => (null, null),
            1 => (Recortar(p[0], 80), null),
            2 => (p[0], Recortar(p[1], 80)),
            _ => (Recortar(string.Join(' ', p[..^2]), 80), Recortar(string.Join(' ', p[^2..]), 80))
        };
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(s))]
    private static string? Recortar(string? s, int largo) => s is null ? null : s.Length <= largo ? s : s[..largo];
    private static string? Get(Dictionary<string, string> v, string columna) => v.TryGetValue(columna, out var x) ? x : null;
}
