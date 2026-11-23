using Enami.Dominio.Calculo;
using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Pruebas de las historias del Sprint 3: HU-01, HU-02, HU-04, HU-05 y HU-06.</summary>
public class Sprint3Tests
{
    // ---------- Carga inicial ----------

    [Fact]
    public void La_carga_inicial_crea_los_catalogos_del_modelo()
    {
        var e = new Entorno();
        Assert.Equal(5, e.Db.Tabla<Rol>().Count());
        Assert.Equal(11, e.Db.Tabla<Oficina>().Count());
        Assert.Equal(9, e.Db.Tabla<InstrumentoFomento>().Count());
        Assert.Equal(9, e.Db.Tabla<CausalRechazo>().Count());
        // una versión vigente de fórmulas por instrumento de fomento
        Assert.Equal(9, e.Db.Tabla<VersionPlantilla>().Count(v => v.Estado == "VIGENTE"));
        // es idempotente: ejecutarla otra vez no duplica nada
        new Semilla(e.Db, new HasherPrueba()).Ejecutar(new OpcionesSemilla { ClaveAdministrador = Entorno.Clave, UsuariosDemostracion = true });
        Assert.Equal(5, e.Db.Tabla<Rol>().Count());
        Assert.Equal(9, e.Db.Tabla<VersionPlantilla>().Count());
        Assert.Equal(5, e.Db.Tabla<Usuario>().Count());
    }

    // ---------- HU-01 y HU-02: usuarios e inicio de sesión ----------

    [Fact]
    public void Autentica_con_correo_y_clave_y_no_guarda_la_clave_en_texto()
    {
        var e = new Entorno();
        var s = e.Usuarios.Autenticar("  INGENIERO.Copiapo@enami.cl ", Entorno.Clave);
        Assert.True(s is not null);
        Assert.Equal(Roles.Ingeniero, s!.Rol);
        Assert.Equal("Oficina Minera Copiapó", s.Oficina);
        Assert.Null(e.Usuarios.Autenticar("ingeniero.copiapo@enami.cl", "otra-clave1"));
        Assert.False(e.Db.Tabla<Usuario>().Any(u => u.ClaveHash.Contains(Entorno.Clave)));
        Assert.Contains(e.Db.Tabla<Auditoria>(), a => a.Accion == "INGRESO_FALLIDO");
    }

    [Fact]
    public void Un_usuario_deshabilitado_no_puede_ingresar_y_su_sesion_deja_de_ser_valida()
    {
        var e = new Entorno();
        e.Usuarios.CambiarEstado(e.IdIngeniero, false, e.IdAdmin);
        Assert.Null(e.Usuarios.Autenticar("ingeniero.copiapo@enami.cl", Entorno.Clave));
        Assert.Null(e.Usuarios.Sesion(e.IdIngeniero));
        e.Usuarios.CambiarEstado(e.IdIngeniero, true, e.IdAdmin);
        Assert.True(e.Usuarios.Autenticar("ingeniero.copiapo@enami.cl", Entorno.Clave) is not null);
    }

    [Fact]
    public void Crea_usuarios_validando_correo_unico_clave_y_oficina_segun_el_rol()
    {
        var e = new Entorno();
        int id = e.Usuarios.Crear(new DatosUsuario { Correo = "Ana.Rojas@enami.cl", NombreCompleto = "Ana Rojas", Rol = Roles.Ingeniero, IdOficina = e.IdCopiapo, Clave = "Clave2026" }, e.IdAdmin);
        Assert.Equal("ana.rojas@enami.cl", e.Db.Tabla<Usuario>().Single(u => u.Id == id).Correo);

        Assert.Throws<ErrorNegocio>(() => e.Usuarios.Crear(new DatosUsuario { Correo = "ana.rojas@enami.cl", NombreCompleto = "Otra", Rol = Roles.Subgerente, Clave = "Clave2026" }, e.IdAdmin));
        Assert.Throws<ErrorNegocio>(() => e.Usuarios.Crear(new DatosUsuario { Correo = "sin-oficina@enami.cl", NombreCompleto = "Sin oficina", Rol = Roles.JefeOficina, Clave = "Clave2026" }, e.IdAdmin));
        Assert.Throws<ErrorNegocio>(() => e.Usuarios.Crear(new DatosUsuario { Correo = "corta@enami.cl", NombreCompleto = "Clave corta", Rol = Roles.Subgerente, Clave = "abc12" }, e.IdAdmin));
        Assert.Throws<ErrorNegocio>(() => e.Usuarios.Crear(new DatosUsuario { Correo = "no-es-correo", NombreCompleto = "X", Rol = Roles.Subgerente, Clave = "Clave2026" }, e.IdAdmin));

        // los roles de alcance nacional no guardan oficina aunque se envíe
        int sub = e.Usuarios.Crear(new DatosUsuario { Correo = "sub2@enami.cl", NombreCompleto = "Sub", Rol = Roles.Subgerente, IdOficina = e.IdCopiapo, Clave = "Clave2026" }, e.IdAdmin);
        Assert.Null(e.Db.Tabla<Usuario>().Single(u => u.Id == sub).IdOficina);
    }

    [Fact]
    public void El_administrador_no_puede_deshabilitarse_ni_quitarse_el_rol()
    {
        var e = new Entorno();
        Assert.Throws<ErrorNegocio>(() => e.Usuarios.CambiarEstado(e.IdAdmin, false, e.IdAdmin));
        Assert.Throws<ErrorNegocio>(() => e.Usuarios.Editar(e.IdAdmin, new DatosUsuario { Correo = "admin@enami.cl", NombreCompleto = "Admin", Rol = Roles.Subgerente }, e.IdAdmin));
    }

    // ---------- HU-04: búsqueda con datos precargados ----------

    [Fact]
    public void Busca_el_productor_por_RUT_en_cualquier_formato_con_sus_faenas()
    {
        var e = new Entorno();
        var f = e.Fichas.BuscarProductor("12.345.678-5");
        Assert.True(f is not null);
        Assert.Equal("Juan Pérez Soto", f!.Productor.NombreMostrado);
        Assert.Equal("Copiapó", f.Comuna);
        Assert.Equal(3, f.Faenas.Count);
        Assert.Null(e.Fichas.BuscarProductor("11.111.111-1"));
        Assert.Null(e.Fichas.BuscarProductor("abc"));
    }

    [Fact]
    public void Busca_la_faena_por_codigo_normalizado()
    {
        var e = new Entorno();
        var r = e.Fichas.BuscarFaena("12396");
        Assert.True(r is not null);
        Assert.Equal("San José", r!.Value.Faena.Faena.Nombre);
        Assert.Equal("Oficina Minera Copiapó", r.Value.Faena.Oficina);
        Assert.Null(e.Fichas.BuscarFaena("99999"));
    }

    // ---------- HU-05: elegibilidad ----------

    [Fact]
    public void La_faena_activa_con_padron_vigente_es_elegible()
    {
        var e = new Entorno();
        Assert.True(e.Fichas.Faena(e.IdFaenaElegible)!.Elegible);
    }

    [Fact]
    public void Bloquea_faena_inactiva_padron_vencido_y_productor_moroso_indicando_el_motivo()
    {
        var e = new Entorno();
        Assert.Contains(e.Fichas.Faena(e.IdFaenaInactiva)!.MotivosNoElegible, m => m.Codigo == "FAENA_INACTIVA");
        Assert.Contains(e.Fichas.Faena(e.IdFaenaPadronVencido)!.MotivosNoElegible, m => m.Codigo == "PADRON_VENCIDO");

        var p = e.Db.Tabla<Productor>().Single();
        p.NivelCumplimiento = NivelesCumplimiento.Moroso;
        var motivos = e.Fichas.Faena(e.IdFaenaElegible)!.MotivosNoElegible;
        Assert.Contains(motivos, m => m.Codigo == "MOROSO");
        Assert.Contains(motivos, m => m.Mensaje.Contains("moroso"));
    }

    [Fact]
    public void Una_faena_sin_oficina_o_sin_padron_no_es_elegible()
    {
        var p = new Productor { Id = 1 };
        var f = new Faena { IdProductor = 1, Estado = EstadosFaena.Activa };
        var motivos = ReglasElegibilidad.Evaluar(p, f, DateTime.Today);
        Assert.Contains(motivos, m => m.Codigo == "SIN_OFICINA");
        Assert.Contains(motivos, m => m.Codigo == "SIN_PADRON");
    }

    // ---------- HU-06: registro de la solicitud con borrador ----------

    [Fact]
    public void Exige_los_campos_obligatorios_de_la_solicitud()
    {
        var e = new Entorno();
        var ex = Assert.Throws<ErrorNegocio>(() => e.Solicitudes.GuardarBorrador(null, new DatosSolicitud(), e.Sesion(e.IdIngeniero)));
        Assert.Contains("faena", ex.Message);
        Assert.Contains("instrumento", ex.Message);
        Assert.Contains("monto", ex.Message);
        var d = e.DatosCompletos(e.IdFaenaElegible, plazo: 121);
        Assert.Throws<ErrorNegocio>(() => e.Solicitudes.GuardarBorrador(null, d, e.Sesion(e.IdIngeniero)));
    }

    [Fact]
    public void Guarda_el_borrador_con_numero_de_oficina_y_calcula_el_VAN_si_los_datos_estan_completos()
    {
        var e = new Entorno();
        var ingeniero = e.Sesion(e.IdIngeniero);
        var r = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), ingeniero);
        Assert.Equal($"COP-{DateTime.Now.Year}-0001", r.NroSolicitud);
        Assert.True(r.Calculo is not null && r.Calculo.Exitoso, string.Join(" ", r.Avisos));

        var s = e.Db.Tabla<SolicitudCredito>().Single();
        Assert.Equal(EstadosSolicitud.Borrador, s.Estado);
        // el resultado y los valores usados quedan guardados para reconstruir el cálculo (RF-08)
        var guardado = e.Solicitudes.UltimoResultado(s.Id)!;
        Assert.Equal(Math.Round((decimal)r.Calculo!.Van, 2), guardado.Van);
        Assert.Equal(65_000m, guardado.Valores["Inversion"]);
        Assert.Equal(24m, guardado.Valores["PlazoMeses"]);
        Assert.Equal(1, guardado.VersionPlantilla);
        Assert.Contains(e.Db.Tabla<Auditoria>(), a => a.Accion == "CALCULO" && a.IdSolicitud == s.Id);

        // la segunda solicitud de la misma oficina continúa el correlativo
        Assert.Equal($"COP-{DateTime.Now.Year}-0002", e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), ingeniero).NroSolicitud);
    }

    [Fact]
    public void Un_borrador_incompleto_se_guarda_y_se_puede_retomar()
    {
        var e = new Entorno();
        var ingeniero = e.Sesion(e.IdIngeniero);
        var d = new DatosSolicitud { IdFaena = e.IdFaenaElegible, IdInstrumento = 1, MontoSolicitado = 30_000_000, PlazoMeses = 12 };
        var r = e.Solicitudes.GuardarBorrador(null, d, ingeniero);
        Assert.Null(r.Calculo);
        Assert.Contains(r.Avisos, a => a.StartsWith("Faltan datos del proyecto"));

        // se retoma, se completan los datos y se guarda de nuevo
        var detalle = e.Solicitudes.Obtener(r.IdSolicitud, ingeniero)!;
        Assert.Equal(30_000_000m, detalle.Solicitud.MontoSolicitado);
        var completos = e.DatosCompletos(e.IdFaenaElegible, monto: 30_000_000, plazo: 12);
        var r2 = e.Solicitudes.GuardarBorrador(r.IdSolicitud, completos, ingeniero);
        Assert.Equal(r.NroSolicitud, r2.NroSolicitud);
        Assert.True(r2.Calculo?.Exitoso == true);
        Assert.Equal(1, e.Db.Tabla<SolicitudCredito>().Count());
    }

    [Fact]
    public void Advierte_cuando_la_faena_no_es_elegible()
    {
        var e = new Entorno();
        var r = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaInactiva), e.Sesion(e.IdIngeniero));
        Assert.Contains(r.Avisos, a => a.Contains("no es elegible") && a.Contains("inactiva"));
    }

    [Fact]
    public void Un_valor_fuera_de_rango_impide_calcular()
    {
        var e = new Entorno();
        var d = e.DatosCompletos(e.IdFaenaElegible);
        d.DatosProyecto["LeyCu"] = 150;
        var r = e.Solicitudes.GuardarBorrador(null, d, e.Sesion(e.IdIngeniero));
        Assert.False(r.Calculo!.Exitoso);
        Assert.Contains(r.Avisos, a => a.Contains("No se pudo calcular"));
        Assert.Equal(0, e.Db.Tabla<ResultadoCalculo>().Count());
    }

    [Fact]
    public void Usa_el_ultimo_valor_registrado_de_las_variables_de_mercado()
    {
        var e = new Entorno();
        var antes = e.Solicitudes.Simular(e.DatosCompletos(e.IdFaenaElegible));
        e.Parametros.Registrar("PrecioCuLb", 5, "RPA COCHILCO");
        Assert.Equal("RPA_COCHILCO", e.Parametros.Ultimos()["PrecioCuLb"].Fuente);
        var despues = e.Solicitudes.Simular(e.DatosCompletos(e.IdFaenaElegible));
        Assert.True(despues.Van > antes.Van, "con un precio del cobre más alto el ingreso sube");
        Assert.Equal(5, despues.Entradas["PrecioCuLb"]);
    }

    [Fact]
    public void Cada_rol_ve_solo_las_solicitudes_que_le_corresponden()
    {
        var e = new Entorno();
        var ingeniero = e.Sesion(e.IdIngeniero);
        var r = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), ingeniero);
        int otro = e.Usuarios.Crear(new DatosUsuario { Correo = "otro@enami.cl", NombreCompleto = "Otro Ingeniero", Rol = Roles.Ingeniero, IdOficina = e.IdCopiapo, Clave = "Clave2026" }, e.IdAdmin);
        int idVallenar = e.Db.Tabla<Oficina>().Single(o => o.Nombre == "Oficina Minera Vallenar").Id;
        int jefeVallenar = e.Usuarios.Crear(new DatosUsuario { Correo = "jefe.vallenar@enami.cl", NombreCompleto = "Jefe Vallenar", Rol = Roles.JefeOficina, IdOficina = idVallenar, Clave = "Clave2026" }, e.IdAdmin);

        Assert.Single(e.Solicitudes.Listar(ingeniero));
        Assert.Empty(e.Solicitudes.Listar(e.Sesion(otro)));
        Assert.Single(e.Solicitudes.Listar(e.Sesion(e.IdJefeCopiapo)));
        Assert.Empty(e.Solicitudes.Listar(e.Sesion(jefeVallenar)));
        Assert.Single(e.Solicitudes.Listar(e.Sesion(e.IdJefatura)));
        Assert.Null(e.Solicitudes.Obtener(r.IdSolicitud, e.Sesion(otro)));
        // solo quien la registró puede modificarla
        Assert.Throws<ErrorNegocio>(() => e.Solicitudes.GuardarBorrador(r.IdSolicitud, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(otro)));
        Assert.Throws<ErrorNegocio>(() => e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdJefatura)));
    }

    [Theory]
    [InlineData("Oficina Minera Copiapó", "COP")]
    [InlineData("Oficina Minera El Salado", "SAL")]
    [InlineData("Oficina Minera La Serena", "SER")]
    [InlineData("", "ENA")]
    public void El_prefijo_del_numero_sale_de_la_oficina(string oficina, string prefijo) =>
        Assert.Equal(prefijo, ServicioSolicitudes.Prefijo(oficina));
}
