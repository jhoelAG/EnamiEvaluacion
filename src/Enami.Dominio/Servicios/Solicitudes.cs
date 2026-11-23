using System.Globalization;
using System.Text;
using Enami.Dominio.Calculo;
using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>Datos del formulario de solicitud (HU-06, RF-15, RF-16 y RF-19).</summary>
public class DatosSolicitud
{
    public int? IdFaena { get; set; }
    public byte? IdInstrumento { get; set; }
    public decimal? MontoSolicitado { get; set; }
    public short? PlazoMeses { get; set; }
    /// <summary>Valores de las variables numéricas de origen FORMULARIO de la plantilla (los datos del proyecto).</summary>
    public Dictionary<string, double?> DatosProyecto { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Valores de los campos de texto del formulario (reseña, dirección, etc.).</summary>
    public Dictionary<string, string?> Textos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Actividades del presupuesto (Anexo 1); solo en las plantillas que usan el presupuesto.</summary>
    public List<DatosLineaPresupuesto> Presupuesto { get; set; } = [];
}

/// <summary>Fila de la bandeja de solicitudes.</summary>
public record SolicitudVista(long Id, string NroSolicitud, DateTime FechaIngreso, string Estado, string Productor, string RutProductor,
                             string Faena, string CodigoFaena, string? Oficina, string Instrumento, decimal Monto, short Plazo,
                             string Ingeniero, decimal? UltimoVan);

/// <summary>Detalle de una solicitud para retomarla o revisarla.</summary>
public record SolicitudDetalle(SolicitudCredito Solicitud, FichaProductor Productor, FichaFaena Faena, InstrumentoFomento Instrumento,
                               string Ingeniero, ResultadoGuardado? UltimoResultado, DatosSolicitud Datos);

/// <summary>Valor precargado de un campo y de dónde salió.</summary>
public record ValorPrecargado(double? Valor, string? Texto, string Origen);

/// <summary>Último cálculo guardado de la solicitud, con los valores que se usaron.</summary>
public record ResultadoGuardado(long Id, DateTime Fecha, int IdPlantilla, int VersionPlantilla, decimal Van, IReadOnlyDictionary<string, decimal> Valores);

/// <summary>Resultado de guardar o calcular.</summary>
public record ResultadoOperacion(long IdSolicitud, string NroSolicitud, IReadOnlyList<string> Avisos, ResultadoEvaluacion? Calculo);

/// <summary>
/// Registro de solicitudes de crédito: datos precargados, verificación de elegibilidad, guardado como borrador
/// (todos los campos en DATO_SOLICITUD y el presupuesto en PRESUPUESTO_SOLICITUD) y cálculo con la plantilla vigente
/// del instrumento de fomento (que queda en RESULTADO_EVALUACION y VALOR_ENTRADA).
/// </summary>
public class ServicioSolicitudes(IAlmacen db, IRepositorioPlantillas plantillas, IRepositorioParametros parametros,
                                 ServicioFichas fichas, ServicioAuditoria auditoria)
{
    public const int PlazoMaximo = 120;

    /// <summary>
    /// Instrumentos para el selector del formulario: los activos y, si se indica, el que ya tiene la evaluación
    /// (aunque se haya desactivado después).
    /// </summary>
    public IReadOnlyList<InstrumentoFomento> Instrumentos(byte? incluir = null) =>
        db.Tabla<InstrumentoFomento>().Where(i => i.Activo || i.Id == incluir).OrderBy(i => i.Id).ToList();

    /// <summary>Versión vigente de las fórmulas del instrumento.</summary>
    public PlantillaCalculo? PlantillaPara(byte idInstrumento) => plantillas.ObtenerVigente(idInstrumento);

    /// <summary>Bandeja según el rol: el ingeniero ve las suyas, el jefe las de su oficina y los roles nacionales todas.</summary>
    public IReadOnlyList<SolicitudVista> Listar(UsuarioSesion usuario, string? estado = null, string? texto = null)
    {
        var q = db.Tabla<SolicitudCredito>();
        if (usuario.Rol == Roles.Ingeniero) q = q.Where(s => s.IdIngeniero == usuario.Id);
        if (!string.IsNullOrEmpty(estado)) q = q.Where(s => s.Estado == estado);
        var lista = q.OrderByDescending(s => s.Id).Take(500).ToList();

        var idsFaena = lista.Select(s => s.IdFaena).Distinct().ToList();
        var faenas = db.Tabla<Faena>().Where(f => idsFaena.Contains(f.Id)).ToDictionary(f => f.Id);
        var idsProd = faenas.Values.Select(f => f.IdProductor).Distinct().ToList();
        var productores = db.Tabla<Productor>().Where(p => idsProd.Contains(p.Id)).ToDictionary(p => p.Id);
        var oficinas = db.Tabla<Oficina>().ToDictionary(o => o.Id, o => o.Nombre);
        var instrumentos = db.Tabla<InstrumentoFomento>().ToDictionary(i => i.Id, i => i.Nombre);
        var idsIng = lista.Select(s => s.IdIngeniero).Distinct().ToList();
        var ingenieros = db.Tabla<Usuario>().Where(u => idsIng.Contains(u.Id)).ToDictionary(u => u.Id, u => u.NombreCompleto);
        var ids = lista.Select(s => s.Id).ToList();
        var vans = db.Tabla<ResultadoCalculo>().Where(r => ids.Contains(r.IdSolicitud)).ToList()
            .GroupBy(r => r.IdSolicitud).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First().Van);

        var vista = lista.Select(s =>
        {
            var f = faenas[s.IdFaena];
            var p = productores[f.IdProductor];
            return new SolicitudVista(s.Id, s.NroSolicitud, s.FechaIngreso, s.Estado, p.NombreMostrado, p.Rut, f.Nombre, f.Codigo,
                f.IdOficina is int o ? oficinas.GetValueOrDefault(o) : null, instrumentos.GetValueOrDefault(s.IdInstrumento, "?"),
                s.MontoSolicitado, s.PlazoMeses, ingenieros.GetValueOrDefault(s.IdIngeniero, "?"),
                vans.TryGetValue(s.Id, out var v) ? v : null);
        });
        if (usuario.Rol == Roles.JefeOficina)
        {
            var oficina = usuario.IdOficina is int io ? oficinas.GetValueOrDefault(io) : null;
            vista = vista.Where(v => v.Oficina == oficina);
        }
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim();
            vista = vista.Where(v => v.NroSolicitud.Contains(t, StringComparison.OrdinalIgnoreCase) || v.Productor.Contains(t, StringComparison.OrdinalIgnoreCase)
                                     || v.RutProductor.Contains(t, StringComparison.OrdinalIgnoreCase) || v.CodigoFaena.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        return vista.ToList();
    }

    public SolicitudDetalle? Obtener(long id, UsuarioSesion usuario)
    {
        var s = db.Tabla<SolicitudCredito>().FirstOrDefault(x => x.Id == id);
        if (s is null || !PuedeVer(s, usuario)) return null;
        var faena = fichas.Faena(s.IdFaena)!;
        var productor = fichas.BuscarProductor(db.Tabla<Productor>().First(p => p.Id == faena.Faena.IdProductor).Rut)!;
        var instrumento = db.Tabla<InstrumentoFomento>().First(i => i.Id == s.IdInstrumento);
        var ingeniero = db.Tabla<Usuario>().Where(u => u.Id == s.IdIngeniero).Select(u => u.NombreCompleto).FirstOrDefault() ?? "?";
        var ultimo = UltimoResultado(s.Id);
        return new SolicitudDetalle(s, productor, faena, instrumento, ingeniero, ultimo, DatosGuardados(s, ultimo));
    }

    /// <summary>
    /// Datos del formulario guardados en DATO_SOLICITUD y PRESUPUESTO_SOLICITUD. Las solicitudes del Sprint 4 o anteriores
    /// no tienen DATO_SOLICITUD: en ese caso se usan los valores del último cálculo.
    /// </summary>
    private DatosSolicitud DatosGuardados(SolicitudCredito s, ResultadoGuardado? ultimo)
    {
        var d = new DatosSolicitud { IdFaena = s.IdFaena, IdInstrumento = s.IdInstrumento, MontoSolicitado = s.MontoSolicitado, PlazoMeses = s.PlazoMeses };
        var datos = db.Tabla<DatoSolicitud>().Where(x => x.IdSolicitud == s.Id).ToList();
        if (datos.Count == 0 && ultimo is not null)
            foreach (var (codigo, valor) in ultimo.Valores) d.DatosProyecto[codigo] = (double)valor;
        foreach (var x in datos)
        {
            if (x.ValorTexto is not null) d.Textos[x.Codigo] = x.ValorTexto;
            else d.DatosProyecto[x.Codigo] = (double?)x.ValorNumero;
        }
        d.Presupuesto = db.Tabla<LineaPresupuesto>().Where(x => x.IdSolicitud == s.Id).OrderBy(x => x.Id).ToList()
            .Select(x => new DatosLineaPresupuesto
            {
                IdLabor = x.IdLabor, Programa = x.Programa, Cantidad = x.Cantidad, CostoUnitario = x.CostoUnitario, PorcentajeFinanciamiento = x.PorcentajeFinanciamiento
            }).ToList();
        return d;
    }

    /// <summary>Actividades que se pueden financiar (catálogo LABOR_TARIFADA).</summary>
    public IReadOnlyList<LaborTarifada> Labores() => db.Tabla<LaborTarifada>().Where(l => l.Activa).OrderBy(l => l.Id).ToList();

    /// <summary>¿La plantilla tiene campos que salen del presupuesto (Anexo 1)?</summary>
    public static bool UsaPresupuesto(PlantillaCalculo? p) => p?.Variables.Any(v => FuentesCampo.EsCalculada(v.Fuente)) == true;

    /// <summary>Tasa del impuesto de timbres y estampillas (%): último valor registrado o el de la plantilla.</summary>
    public decimal TasaTimbres(PlantillaCalculo p)
    {
        if (parametros.Ultimos().TryGetValue("ImpuestoTimbres", out var t)) return (decimal)t.Valor;
        return (decimal)(p.Variables.FirstOrDefault(v => v.Codigo.Equals("ImpuestoTimbres", StringComparison.OrdinalIgnoreCase))?.ValorPorDefecto ?? 0);
    }

    /// <summary>
    /// Calcula el presupuesto y pasa sus totales a los campos que salen de él. Si hay actividades, el monto solicitado
    /// pasa a ser lo que financia ENAMI (capital de riesgo más crédito).
    /// </summary>
    public TotalesPresupuesto? AplicarPresupuesto(PlantillaCalculo? plantilla, DatosSolicitud d)
    {
        if (!UsaPresupuesto(plantilla)) return null;
        var labores = db.Tabla<LaborTarifada>().ToDictionary(l => l.Id);
        var errores = CalculoPresupuesto.Validar(d.Presupuesto, labores);
        if (errores.Count > 0) throw new ErrorNegocio("Revise el presupuesto: " + string.Join(" ", errores));
        var t = CalculoPresupuesto.Calcular(d.Presupuesto, TasaTimbres(plantilla!));
        foreach (var v in plantilla!.Variables.Where(v => FuentesCampo.EsCalculada(v.Fuente)))
            d.DatosProyecto[v.Codigo] = (double)(v.Fuente switch
            {
                "PRESUPUESTO.CAPITAL_RIESGO" => t.CapitalRiesgo,
                "PRESUPUESTO.CREDITO" => t.Credito,
                _ => t.AportePropio
            });
        if (t.Financiamiento > 0) d.MontoSolicitado = t.Financiamiento;
        return t;
    }

    /// <summary>
    /// Valores que se precargan en los campos con fuente: fichas del productor y de la faena, recepción de los últimos 6 meses
    /// (de la faena y de todas las faenas del productor), recursos de la faena y el usuario que registra.
    /// Las leyes de los recursos se ponderan por las toneladas indicadas y medidas (si no hay, por todas las categorías).
    /// </summary>
    public IReadOnlyDictionary<string, ValorPrecargado> Precarga(PlantillaCalculo plantilla, int idFaena, string? nombreUsuario = null)
    {
        var r = new Dictionary<string, ValorPrecargado>(StringComparer.OrdinalIgnoreCase);
        var campos = plantilla.Variables.Where(v => v.Origen == OrigenVariable.Formulario && !string.IsNullOrEmpty(v.Fuente)
                                                    && !FuentesCampo.EsCalculada(v.Fuente) && !v.Fuente!.StartsWith("SOLICITUD.")).ToList();
        if (campos.Count == 0) return r;
        var faena = db.Tabla<Faena>().FirstOrDefault(f => f.Id == idFaena);
        if (faena is null) return r;
        var productor = db.Tabla<Productor>().First(p => p.Id == faena.IdProductor);
        string? Lugar(int? idComuna) => idComuna is int c && db.Tabla<Comuna>().FirstOrDefault(x => x.Id == c) is Comuna co ? $"{co.Nombre}, {co.Region}" : null;
        var h = campos.Any(c => c.Fuente!.StartsWith("RECEPCION.")) ? fichas.Produccion(idFaena) : null;
        var hp = campos.Any(c => c.Fuente is "PRODUCTOR.TMS_MES" or "PRODUCTOR.LEY_CU" or "PRODUCTOR.LEY_AU" or "PRODUCTOR.LEY_AG") ? fichas.ProduccionProductor(productor.Id) : null;
        var recursos = campos.Any(c => c.Fuente!.StartsWith("RECURSO.")) ? fichas.Recursos(idFaena) : [];
        var paraLey = recursos.Where(x => x.Categoria is CategoriasRecurso.Indicado or CategoriasRecurso.Medido).ToList();
        if (paraLey.Sum(x => x.Toneladas) == 0) paraLey = recursos.ToList();
        decimal tonLey = paraLey.Sum(x => x.Toneladas);
        double? LeyRecurso(Func<RecursoFaena, decimal> ley) => tonLey == 0 ? null : Math.Round((double)(paraLey.Sum(x => ley(x) * x.Toneladas) / tonLey), 2);
        double? Toneladas(string cat) => recursos.Count == 0 ? null : (double)recursos.Where(x => x.Categoria == cat).Sum(x => x.Toneladas);
        // la producción de la faena se usa en el cálculo: sin entregas queda vacía para que el ingeniero la ingrese;
        // la del productor es informativa: sin entregas es 0
        double? DeFaena(Func<HistorialProduccion, double> f, int dec) => h is { Lotes: > 0 } ? Math.Round(f(h), dec) : null;
        double? DeProductor(Func<HistorialProduccion, double> f, int dec) => hp is null ? null : hp.Lotes == 0 ? 0 : Math.Round(f(hp), dec);
        const string oRecepcion = "Promedio de los últimos 6 meses de recepción.", oRecursos = "Recursos registrados de la faena.",
                     oProductorRecepcion = "Recepción de los últimos 6 meses de todas las faenas del productor.",
                     oFichaProductor = "Ficha del productor.", oFichaFaena = "Ficha de la faena.";
        static (object? Valor, string Origen) V(object? valor, string origen) => (valor, origen);
        foreach (var c in campos)
        {
            (object? valor, string origen) = c.Fuente switch
            {
                "RECEPCION.TMS_MES" => V(DeFaena(x => x.TmsPromedioMensual, 1), oRecepcion),
                "RECEPCION.LEY_CU" => V(DeFaena(x => x.LeyCuPct, 2), oRecepcion),
                "RECEPCION.LEY_AU" => V(DeFaena(x => x.LeyAuGt, 2), oRecepcion),
                "RECEPCION.LEY_AG" => V(DeFaena(x => x.LeyAgGt, 2), oRecepcion),
                "RECEPCION.MESES" => V((double?)h?.MesesConEntregas, "Meses con entregas en los últimos 6 meses de recepción."),
                "PRODUCTOR.TMS_MES" => V(DeProductor(x => x.TmsPromedioMensual, 1), oProductorRecepcion),
                "PRODUCTOR.LEY_CU" => V(DeProductor(x => x.LeyCuPct, 2), oProductorRecepcion),
                "PRODUCTOR.LEY_AU" => V(DeProductor(x => x.LeyAuGt, 2), oProductorRecepcion),
                "PRODUCTOR.LEY_AG" => V(DeProductor(x => x.LeyAgGt, 2), oProductorRecepcion),
                "RECURSO.INFERIDO" => V(Toneladas(CategoriasRecurso.Inferido), oRecursos),
                "RECURSO.INDICADO" => V(Toneladas(CategoriasRecurso.Indicado), oRecursos),
                "RECURSO.MEDIDO" => V(Toneladas(CategoriasRecurso.Medido), oRecursos),
                "RECURSO.LEY_CU" => V(LeyRecurso(x => x.LeyCuPct), oRecursos),
                "RECURSO.LEY_AU" => V(LeyRecurso(x => x.LeyAuGt), oRecursos),
                "RECURSO.LEY_AG" => V(LeyRecurso(x => x.LeyAgGt), oRecursos),
                "FAENA.DISTANCIA" => V((double?)faena.DistanciaPoderCompraKm, oFichaFaena),
                "FAENA.NORTE" => V((double?)faena.CoordenadaNorte, oFichaFaena),
                "FAENA.ESTE" => V((double?)faena.CoordenadaEste, oFichaFaena),
                "FAENA.MANO_OBRA" => V((double?)faena.ManoObra, oFichaFaena),
                "FAENA.UBICACION" => V(faena.Ubicacion, oFichaFaena),
                "FAENA.COMUNA" => V(Lugar(faena.IdComuna), oFichaFaena),
                "PRODUCTOR.DIRECCION" => V(productor.Direccion, oFichaProductor),
                "PRODUCTOR.COMUNA" => V(Lugar(productor.IdComuna), oFichaProductor),
                "PRODUCTOR.TELEFONO" => V(productor.Telefono, oFichaProductor),
                "PRODUCTOR.CORREO" => V(productor.Correo, oFichaProductor),
                "PRODUCTOR.REPRESENTANTE" => V(productor.RepresentanteLegal, oFichaProductor),
                "USUARIO.NOMBRE" => V(nombreUsuario, "Usuario que registra la evaluación de proyecto."),
                _ => V(null, "")
            };
            if (valor is double x && c.EsNumerica) r[c.Codigo] = new ValorPrecargado(x, null, origen);
            else if (valor is string t && !string.IsNullOrWhiteSpace(t) && !c.EsNumerica) r[c.Codigo] = new ValorPrecargado(null, t, origen);
        }
        return r;
    }

    public ResultadoGuardado? UltimoResultado(long idSolicitud)
    {
        var r = db.Tabla<ResultadoCalculo>().Where(x => x.IdSolicitud == idSolicitud).OrderByDescending(x => x.Id).FirstOrDefault();
        if (r is null) return null;
        var version = db.Tabla<VersionPlantilla>().Where(v => v.Id == r.IdPlantilla).Select(v => (int)v.Version).FirstOrDefault();
        var variables = db.Tabla<VariablePlantilla>().Where(v => v.IdPlantilla == r.IdPlantilla).ToDictionary(v => v.Id, v => v.Codigo);
        var valores = db.Tabla<ValorEntrada>().Where(v => v.IdResultado == r.Id).ToList()
            .ToDictionary(v => variables.GetValueOrDefault(v.IdVariable, "?"), v => v.Valor, StringComparer.OrdinalIgnoreCase);
        return new ResultadoGuardado(r.Id, r.FechaCalculo, r.IdPlantilla, version, r.Van, valores);
    }

    /// <summary>
    /// Crea o actualiza la solicitud en estado BORRADOR. Exige faena, instrumento, monto y plazo; si los datos del proyecto
    /// están completos, además calcula el VAN y guarda el resultado, de modo que al retomarla se recuperan esos valores.
    /// </summary>
    public ResultadoOperacion GuardarBorrador(long? id, DatosSolicitud d, UsuarioSesion usuario)
    {
        if (usuario.Rol != Roles.Ingeniero && usuario.Rol != Roles.Administrador)
            throw new ErrorNegocio("Solo un Ingeniero de Fomento puede registrar evaluaciones de proyecto.");
        var plantilla = d.IdInstrumento is byte idi ? PlantillaPara(idi) : null;
        AplicarPresupuesto(plantilla, d);
        ValidarTextos(plantilla, d);
        var (faena, instrumento) = ValidarEncabezado(d);
        var avisos = new List<string>();
        var ficha = fichas.Faena(faena.Id)!;
        if (!ficha.Elegible)
            avisos.Add("La evaluación se guardó, pero el proyecto no es elegible: " + string.Join(" ", ficha.MotivosNoElegible.Select(m => m.Mensaje)));
        if (usuario.Rol == Roles.Ingeniero && usuario.IdOficina is int io && faena.IdOficina is int fo && io != fo)
            avisos.Add("La faena pertenece a otra oficina; el visado lo hará el Jefe de esa oficina.");

        SolicitudCredito s;
        const string inactivo = "El instrumento seleccionado está desactivado; elija otro instrumento de fomento.";
        if (id is null)
        {
            if (!instrumento.Activo) throw new ErrorNegocio(inactivo);
            s = new SolicitudCredito
            {
                NroSolicitud = NuevoNumero(faena.IdOficina), IdIngeniero = usuario.Id,
                FechaIngreso = DateTime.Now, Estado = EstadosSolicitud.Borrador
            };
            Copiar(d, s);
            db.Agregar(s);
            db.Guardar();
            auditoria.Registrar("SOLICITUD_CREADA", usuario.Id, s.Id, s.NroSolicitud);
        }
        else
        {
            s = db.Tabla<SolicitudCredito>().FirstOrDefault(x => x.Id == id) ?? throw new ErrorNegocio("La evaluación de proyecto no existe.");
            if (!PuedeEditar(s, usuario)) throw new ErrorNegocio("Solo el ingeniero que la registró puede modificar la evaluación, y solo mientras está en borrador u observada.");
            if (!instrumento.Activo && instrumento.Id != s.IdInstrumento) throw new ErrorNegocio(inactivo);
            Copiar(d, s);
            db.Guardar();
            auditoria.Registrar("SOLICITUD_ACTUALIZADA", usuario.Id, s.Id, s.NroSolicitud);
        }
        GuardarDatos(s.Id, plantilla, d);

        ResultadoEvaluacion? calculo = null;
        if (plantilla is null)
            avisos.Add("No hay una versión vigente de fórmulas para este instrumento; no se puede calcular el VAN. La Jefatura debe activarla en el editor de fórmulas.");
        else
        {
            var faltantes = Faltantes(plantilla, d);
            if (faltantes.Count > 0)
                avisos.Add("Faltan datos del proyecto para calcular el VAN: " + string.Join(", ", faltantes) + ". Se guardarán al calcular.");
            else
                calculo = CalcularYGuardar(s, plantilla, d, usuario, avisos);
        }
        return new ResultadoOperacion(s.Id, s.NroSolicitud, avisos, calculo);
    }

    /// <summary>Calcula el VAN sin guardar (vista previa del formulario).</summary>
    public ResultadoEvaluacion Simular(DatosSolicitud d)
    {
        var plantilla = (d.IdInstrumento is byte idi ? PlantillaPara(idi) : null);
        AplicarPresupuesto(plantilla, d);
        ValidarEncabezado(d);
        if (plantilla is null) throw new ErrorNegocio("No hay una versión vigente de fórmulas para este instrumento. La Jefatura debe activarla en el editor de fórmulas.");
        var faltantes = Faltantes(plantilla, d);
        if (faltantes.Count > 0) throw new ErrorNegocio("Complete los datos del proyecto: " + string.Join(", ", faltantes) + ".");
        return MotorEvaluacion.Evaluar(plantilla, Entradas(plantilla, d, out _));
    }

    public bool PuedeEditar(SolicitudCredito s, UsuarioSesion u) =>
        (s.Estado is EstadosSolicitud.Borrador or EstadosSolicitud.Observada) && (s.IdIngeniero == u.Id || u.Rol == Roles.Administrador);

    private bool PuedeVer(SolicitudCredito s, UsuarioSesion u)
    {
        if (u.Rol == Roles.Ingeniero) return s.IdIngeniero == u.Id;
        if (u.Rol == Roles.JefeOficina)
            return db.Tabla<Faena>().Any(f => f.Id == s.IdFaena && f.IdOficina == u.IdOficina);
        return true;
    }

    private ResultadoEvaluacion CalcularYGuardar(SolicitudCredito s, PlantillaCalculo plantilla, DatosSolicitud d, UsuarioSesion usuario, List<string> avisos)
    {
        var entradas = Entradas(plantilla, d, out var sinParametro);
        if (sinParametro.Count > 0)
            avisos.Add("Se usó el valor de referencia de la plantilla porque no hay valores registrados de: " + string.Join(", ", sinParametro) + ".");
        var r = MotorEvaluacion.Evaluar(plantilla, entradas);
        if (!r.Exitoso)
        {
            avisos.Add("No se pudo calcular el VAN: " + string.Join(" ", r.Errores));
            return r;
        }
        var idsVariables = db.Tabla<VariablePlantilla>().Where(v => v.IdPlantilla == plantilla.Id).ToDictionary(v => v.Codigo, v => v.Id, StringComparer.OrdinalIgnoreCase);
        db.EnTransaccion(() =>
        {
            var fila = new ResultadoCalculo { IdSolicitud = s.Id, IdPlantilla = plantilla.Id, FechaCalculo = DateTime.Now, Van = Math.Round((decimal)r.Van, 2) };
            db.Agregar(fila);
            db.Guardar();
            foreach (var (codigo, valor) in r.Entradas)
                if (idsVariables.TryGetValue(codigo, out var idVar))
                    db.Agregar(new ValorEntrada { IdResultado = fila.Id, IdVariable = idVar, Valor = Math.Round((decimal)valor, 4) });
            db.Guardar();
        });
        auditoria.Registrar("CALCULO", usuario.Id, s.Id, r.Modo == ModoCalculo.Escenarios
            ? $"Recuperación US$ {r.Recuperacion:N0} de US$ {r.Inversion:N0}; VAN US$ {r.Van:N0} con {plantilla.Nombre}; dictamen {(r.Viable ? "VIABLE" : "NO VIABLE")}"
            : $"VAN US$ {r.Van:N0} con {plantilla.Nombre}; dictamen {(r.Viable ? "VIABLE" : "NO VIABLE")}");
        return r;
    }

    private Dictionary<string, double> Entradas(PlantillaCalculo plantilla, DatosSolicitud d, out List<string> sinParametro)
    {
        sinParametro = [];
        var ultimos = parametros.Ultimos();
        var valores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in plantilla.Variables.Where(v => v.EsNumerica))
        {
            if (v.Codigo.Equals(MotorFormulas.CodigoInversion, StringComparison.OrdinalIgnoreCase)) valores[v.Codigo] = (double)d.MontoSolicitado!.Value;
            else if (v.Codigo.Equals(MotorFormulas.CodigoPlazo, StringComparison.OrdinalIgnoreCase)) valores[v.Codigo] = d.PlazoMeses!.Value;
            else if (v.Origen == OrigenVariable.Formulario)
                valores[v.Codigo] = d.DatosProyecto.GetValueOrDefault(v.Codigo) ?? v.ValorPorDefecto;   // los no usados en fórmulas pueden quedar vacíos
            else if (ultimos.TryGetValue(v.Codigo, out var p)) valores[v.Codigo] = p.Valor;
            else { valores[v.Codigo] = v.ValorPorDefecto; sinParametro.Add(v.Nombre); }
        }
        return valores;
    }

    /// <summary>
    /// Campos del formulario de la plantilla, en orden (se excluyen la inversión y el plazo del modo por flujo,
    /// que salen del monto y del plazo de la solicitud).
    /// </summary>
    public static IEnumerable<VariableCalculo> CamposFormulario(PlantillaCalculo plantilla) =>
        plantilla.Variables.Where(v => v.Origen == OrigenVariable.Formulario
                                       && !(plantilla.Modo == ModoCalculo.Flujo && v.Codigo.Equals(MotorFormulas.CodigoInversion, StringComparison.OrdinalIgnoreCase))
                                       && !(plantilla.Modo == ModoCalculo.Flujo && v.Codigo.Equals(MotorFormulas.CodigoPlazo, StringComparison.OrdinalIgnoreCase)))
            .Select((v, i) => (v, i)).OrderBy(x => x.v.Orden).ThenBy(x => x.i).Select(x => x.v);

    /// <summary>Campos numéricos que el ingeniero puede completar (los de texto van aparte y los del presupuesto se calculan).</summary>
    public static IEnumerable<VariableCalculo> VariablesProyecto(PlantillaCalculo plantilla) =>
        CamposFormulario(plantilla).Where(v => v.EsNumerica && !FuentesCampo.EsCalculada(v.Fuente));

    /// <summary>Campos obligatorios para calcular: los numéricos que usa alguna fórmula. Los demás (deudas, coordenadas, etc.) son informativos.</summary>
    public static IEnumerable<VariableCalculo> VariablesObligatorias(PlantillaCalculo plantilla)
    {
        var usados = MotorFormulas.IdentificadoresUsados(plantilla);
        return VariablesProyecto(plantilla).Where(v => usados.Contains(v.Codigo));
    }

    private static List<string> Faltantes(PlantillaCalculo plantilla, DatosSolicitud d) =>
        VariablesObligatorias(plantilla).Where(v => !d.DatosProyecto.TryGetValue(v.Codigo, out var x) || x is null).Select(v => v.Nombre).ToList();

    private static void ValidarTextos(PlantillaCalculo? plantilla, DatosSolicitud d)
    {
        if (plantilla is null) return;
        var largos = CamposFormulario(plantilla).Where(v => !v.EsNumerica && d.Textos.TryGetValue(v.Codigo, out var t) && t?.Length > 2000).Select(v => v.Nombre).ToList();
        if (largos.Count > 0) throw new ErrorNegocio("Estos campos admiten hasta 2.000 caracteres: " + string.Join(", ", largos) + ".");
    }

    /// <summary>Reemplaza los datos del formulario y el presupuesto guardados de la solicitud.</summary>
    private void GuardarDatos(long idSolicitud, PlantillaCalculo? plantilla, DatosSolicitud d)
    {
        db.EnTransaccion(() =>
        {
            foreach (var x in db.Tabla<DatoSolicitud>().Where(x => x.IdSolicitud == idSolicitud).ToList()) db.Eliminar(x);
            foreach (var x in db.Tabla<LineaPresupuesto>().Where(x => x.IdSolicitud == idSolicitud).ToList()) db.Eliminar(x);
            db.Guardar();
            var codigos = plantilla is null ? null : CamposFormulario(plantilla).ToDictionary(v => v.Codigo, StringComparer.OrdinalIgnoreCase);
            foreach (var (codigo, valor) in d.DatosProyecto)
                if (valor is double v && !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) < 1e12 && (codigos is null || codigos.ContainsKey(codigo)))
                    db.Agregar(new DatoSolicitud { IdSolicitud = idSolicitud, Codigo = codigo, ValorNumero = Math.Round((decimal)v, 6) });
            foreach (var (codigo, texto) in d.Textos)
                if (!string.IsNullOrWhiteSpace(texto) && (codigos is null || codigos.ContainsKey(codigo)) && !d.DatosProyecto.ContainsKey(codigo))
                    db.Agregar(new DatoSolicitud { IdSolicitud = idSolicitud, Codigo = codigo, ValorTexto = texto.Trim() });
            if (UsaPresupuesto(plantilla))
                foreach (var l in d.Presupuesto)
                    db.Agregar(new LineaPresupuesto
                    {
                        IdSolicitud = idSolicitud, IdLabor = l.IdLabor, Programa = l.Programa, Cantidad = l.Cantidad!.Value,
                        CostoUnitario = l.CostoUnitario!.Value, PorcentajeFinanciamiento = l.PorcentajeFinanciamiento!.Value
                    });
            db.Guardar();
        });
    }

    private (Faena Faena, InstrumentoFomento Instrumento) ValidarEncabezado(DatosSolicitud d)
    {
        var errores = new List<string>();
        Faena? faena = d.IdFaena is int idf ? db.Tabla<Faena>().FirstOrDefault(f => f.Id == idf) : null;
        if (faena is null) errores.Add("Seleccione la faena.");
        InstrumentoFomento? instrumento = d.IdInstrumento is byte idi ? db.Tabla<InstrumentoFomento>().FirstOrDefault(i => i.Id == idi) : null;
        if (instrumento is null) errores.Add("Seleccione el instrumento de fomento.");
        if (d.MontoSolicitado is null || d.MontoSolicitado <= 0) errores.Add("El monto solicitado debe ser mayor que cero.");
        else if (d.MontoSolicitado > 99_999_999_999_999m) errores.Add("El monto solicitado es demasiado grande.");
        if (d.PlazoMeses is null || d.PlazoMeses < 1 || d.PlazoMeses > PlazoMaximo) errores.Add($"El plazo debe estar entre 1 y {PlazoMaximo} meses.");
        if (errores.Count > 0) throw new ErrorNegocio(string.Join(" ", errores));
        return (faena!, instrumento!);
    }

    private static void Copiar(DatosSolicitud d, SolicitudCredito s)
    {
        s.IdFaena = d.IdFaena!.Value;
        s.IdInstrumento = d.IdInstrumento!.Value;
        s.MontoSolicitado = Math.Round(d.MontoSolicitado!.Value, 0);
        s.PlazoMeses = d.PlazoMeses!.Value;
    }

    /// <summary>Número visible con el prefijo de la oficina, el año y un correlativo (por ejemplo, COP-2026-0042).</summary>
    private string NuevoNumero(int? idOficina)
    {
        string nombre = idOficina is int o ? db.Tabla<Oficina>().Where(x => x.Id == o).Select(x => x.Nombre).FirstOrDefault() ?? "" : "";
        string prefijo = Prefijo(nombre);
        string inicio = $"{prefijo}-{DateTime.Now.Year}-";
        int ultimo = db.Tabla<SolicitudCredito>().Where(s => s.NroSolicitud.StartsWith(inicio)).Select(s => s.NroSolicitud).ToList()
            .Select(n => int.TryParse(n[inicio.Length..], out var x) ? x : 0).DefaultIfEmpty(0).Max();
        return $"{inicio}{ultimo + 1:0000}";
    }

    public static string Prefijo(string oficina)
    {
        var palabra = oficina.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        var sinTildes = new string(palabra.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetter(c)).ToArray()).ToUpperInvariant();
        return sinTildes.Length >= 3 ? sinTildes[..3] : "ENA";
    }
}
