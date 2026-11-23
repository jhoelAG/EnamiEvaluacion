using Enami.Dominio.Calculo;
using Enami.Dominio.Persistencia;
using Enami.Dominio.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Enami.Datos;

/// <summary>
/// Registra la persistencia y los servicios de dominio según la configuración «Persistencia»:
/// <list type="bullet">
/// <item><c>SqlServer</c> (por defecto): base de datos SQL Server; la cadena está en ConnectionStrings:Enami.</item>
/// <item><c>Memoria</c>: datos en memoria, para demostraciones sin SQL Server (se pierden al cerrar la aplicación).</item>
/// </list>
/// </summary>
public static class RegistroPersistencia
{
    public static IServiceCollection AddPersistenciaEnami(this IServiceCollection s, IConfiguration config)
    {
        var modo = config["Persistencia"] ?? "SqlServer";
        if (modo.Equals("Memoria", StringComparison.OrdinalIgnoreCase))
        {
            s.AddSingleton<IAlmacen, AlmacenMemoria>();
        }
        else
        {
            var cadena = config.GetConnectionString("Enami")
                         ?? throw new InvalidOperationException("Falta ConnectionStrings:Enami en appsettings.json.");
            s.AddDbContext<EnamiDbContext>(o => o.UseSqlServer(cadena));
            s.AddScoped<IAlmacen, AlmacenEf>();
        }
        s.AddSingleton<IHasherClaves, HasherIdentity>();
        s.Configure<OpcionesSemilla>(config.GetSection("Semilla"));
        s.AddScoped<ServicioAuditoria>();
        s.AddScoped<IRepositorioPlantillas, RepositorioPlantillas>();
        s.AddScoped<IRepositorioParametros, RepositorioParametros>();
        s.AddScoped<ServicioUsuarios>();
        s.AddScoped<ServicioFichas>();
        s.AddScoped<ServicioCatalogos>();
        s.AddScoped<ServicioInstrumentos>();
        s.AddScoped<ServicioAprobaciones>();
        // aviso a la Logic App de aprobación (BOT-03); sin URL configurada la aprobación se hace solo en la aplicación
        s.Configure<OpcionesFlujos>(config.GetSection("Flujos"));
        s.AddHttpClient<INotificadorFlujos, NotificadorFlujosHttp>();
        s.AddScoped<ServicioSolicitudes>();
        s.AddScoped<ServicioCargaPlanillas>();
        s.AddScoped<ServicioDatosPrueba>();
        s.AddScoped<Semilla>();
        return s;
    }

    /// <summary>
    /// EnsureCreated no modifica una base que ya existe. Si la base se creó en un sprint anterior, aquí se agregan
    /// las tablas y columnas nuevas (sprints 4 y 5) sin tocar los datos que ya tiene.
    /// </summary>
    private static void ActualizarEsquema(EnamiDbContext contexto, ILogger log)
    {
        const string sql = """
            IF OBJECT_ID(N'RECEPCION_MINERAL') IS NULL
            BEGIN
                CREATE TABLE RECEPCION_MINERAL (
                    id_recepcion bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_RECEPCION_MINERAL PRIMARY KEY,
                    nro_lote varchar(12) NOT NULL,
                    id_faena int NOT NULL CONSTRAINT FK_RECEPCION_MINERAL_FAENA REFERENCES FAENA(id_faena),
                    fecha_cierre date NOT NULL,
                    agencia varchar(4) NOT NULL,
                    producto varchar(4) NOT NULL,
                    peso_seco_kg decimal(12,0) NOT NULL,
                    ley_cu_pct decimal(7,4) NOT NULL,
                    ley_au_gt decimal(9,3) NOT NULL,
                    ley_ag_gt decimal(9,3) NOT NULL,
                    CONSTRAINT CK_RECEPCION_peso CHECK (peso_seco_kg > 0),
                    CONSTRAINT CK_RECEPCION_ley_cu CHECK (ley_cu_pct >= 0 AND ley_cu_pct < 100),
                    CONSTRAINT CK_RECEPCION_leyes CHECK (ley_au_gt >= 0 AND ley_ag_gt >= 0));
                CREATE UNIQUE INDEX IX_RECEPCION_MINERAL_nro_lote ON RECEPCION_MINERAL(nro_lote);
                CREATE INDEX IX_RECEPCION_MINERAL_id_faena_fecha_cierre ON RECEPCION_MINERAL(id_faena, fecha_cierre);
            END;
            IF OBJECT_ID(N'RECURSO_FAENA') IS NULL
            BEGIN
                CREATE TABLE RECURSO_FAENA (
                    id_recurso int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RECURSO_FAENA PRIMARY KEY,
                    id_faena int NOT NULL CONSTRAINT FK_RECURSO_FAENA_FAENA REFERENCES FAENA(id_faena),
                    categoria varchar(10) NOT NULL,
                    toneladas decimal(12,0) NOT NULL,
                    ley_cu_pct decimal(7,4) NOT NULL,
                    ley_au_gt decimal(9,3) NOT NULL,
                    ley_ag_gt decimal(9,3) NOT NULL,
                    fuente nvarchar(100) NULL,
                    id_usuario int NOT NULL CONSTRAINT FK_RECURSO_FAENA_USUARIO REFERENCES USUARIO(id_usuario),
                    fecha_registro datetime2 NOT NULL,
                    CONSTRAINT CK_RECURSO_categoria CHECK (categoria IN ('MEDIDO','INDICADO','INFERIDO')),
                    CONSTRAINT CK_RECURSO_toneladas CHECK (toneladas > 0),
                    CONSTRAINT CK_RECURSO_leyes CHECK (ley_cu_pct >= 0 AND ley_cu_pct < 100 AND ley_au_gt >= 0 AND ley_ag_gt >= 0));
                CREATE UNIQUE INDEX IX_RECURSO_FAENA_id_faena_categoria ON RECURSO_FAENA(id_faena, categoria);
            END;
            IF COL_LENGTH('PLANTILLA_CALCULO', 'modo_calculo') IS NULL
                ALTER TABLE PLANTILLA_CALCULO ADD modo_calculo varchar(10) NOT NULL CONSTRAINT DF_PLANTILLA_modo DEFAULT 'FLUJO';
            IF COL_LENGTH('VARIABLE_CALCULO', 'tipo') IS NULL
                ALTER TABLE VARIABLE_CALCULO ADD seccion nvarchar(60) NULL, orden smallint NOT NULL CONSTRAINT DF_VARIABLE_orden DEFAULT 0,
                    tipo varchar(12) NOT NULL CONSTRAINT DF_VARIABLE_tipo DEFAULT 'NUMERO', fuente varchar(40) NULL, opciones nvarchar(200) NULL;
            IF COL_LENGTH('PRODUCTOR', 'direccion') IS NULL
                ALTER TABLE PRODUCTOR ADD direccion nvarchar(150) NULL, telefono varchar(30) NULL, correo nvarchar(120) NULL;
            IF COL_LENGTH('FAENA', 'ubicacion') IS NULL
                ALTER TABLE FAENA ADD ubicacion nvarchar(150) NULL, coordenada_norte decimal(12,2) NULL, coordenada_este decimal(12,2) NULL, mano_obra smallint NULL;
            IF OBJECT_ID(N'DATO_SOLICITUD') IS NULL
                CREATE TABLE DATO_SOLICITUD (
                    id_solicitud bigint NOT NULL CONSTRAINT FK_DATO_SOLICITUD_SOLICITUD REFERENCES SOLICITUD_CREDITO(id_solicitud) ON DELETE CASCADE,
                    codigo varchar(40) NOT NULL,
                    valor_numero decimal(18,6) NULL,
                    valor_texto nvarchar(2000) NULL,
                    CONSTRAINT PK_DATO_SOLICITUD PRIMARY KEY (id_solicitud, codigo));
            IF OBJECT_ID(N'LABOR_TARIFADA') IS NULL
            BEGIN
                CREATE TABLE LABOR_TARIFADA (
                    id_labor smallint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LABOR_TARIFADA PRIMARY KEY,
                    nombre nvarchar(80) NOT NULL,
                    unidad varchar(8) NOT NULL,
                    costo_unitario decimal(12,2) NOT NULL,
                    programa varchar(14) NOT NULL,
                    pct_financiamiento decimal(5,4) NOT NULL,
                    activa bit NOT NULL,
                    CONSTRAINT CK_LABOR_costo CHECK (costo_unitario >= 0),
                    CONSTRAINT CK_LABOR_programa CHECK (programa IN ('RECONOCIMIENTO','DESARROLLO','PROPIO')),
                    CONSTRAINT CK_LABOR_pct CHECK (pct_financiamiento >= 0 AND pct_financiamiento <= 1));
                CREATE UNIQUE INDEX IX_LABOR_TARIFADA_nombre ON LABOR_TARIFADA(nombre);
            END;
            IF OBJECT_ID(N'PRESUPUESTO_SOLICITUD') IS NULL
                CREATE TABLE PRESUPUESTO_SOLICITUD (
                    id_linea bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PRESUPUESTO_SOLICITUD PRIMARY KEY,
                    id_solicitud bigint NOT NULL CONSTRAINT FK_PRESUPUESTO_SOLICITUD REFERENCES SOLICITUD_CREDITO(id_solicitud) ON DELETE CASCADE,
                    id_labor smallint NOT NULL CONSTRAINT FK_PRESUPUESTO_LABOR REFERENCES LABOR_TARIFADA(id_labor),
                    programa varchar(14) NOT NULL,
                    cantidad decimal(12,2) NOT NULL,
                    costo_unitario decimal(12,2) NOT NULL,
                    pct_financiamiento decimal(5,4) NOT NULL,
                    CONSTRAINT CK_PRESUPUESTO_cantidad CHECK (cantidad > 0),
                    CONSTRAINT CK_PRESUPUESTO_costo CHECK (costo_unitario >= 0),
                    CONSTRAINT CK_PRESUPUESTO_programa CHECK (programa IN ('RECONOCIMIENTO','DESARROLLO','PROPIO')),
                    CONSTRAINT CK_PRESUPUESTO_pct CHECK (pct_financiamiento >= 0 AND pct_financiamiento <= 1));
            """;
        contexto.Database.ExecuteSqlRaw(sql);
        contexto.Database.ExecuteSqlRaw(SqlInstrumentos);
        contexto.Database.ExecuteSqlRaw(SqlFuentes);
        log.LogInformation("Esquema revisado: tablas y columnas de los sprints 4 y 5 presentes.");
    }

    /// <summary>
    /// Sprint 5: se admite la fuente API_EXTERNA (tipo de cambio de mercado, respaldo cuando el Banco Central no responde),
    /// y cada valor guarda el día al que corresponde (fecha_valor) y quién lo fijó (id_usuario), para elegir el dólar de una fecha.
    /// </summary>
    private const string SqlFuentes = """
        IF COL_LENGTH('PARAMETRO_ECONOMICO', 'fecha_valor') IS NULL
            ALTER TABLE PARAMETRO_ECONOMICO ADD fecha_valor date NULL,
                id_usuario int NULL CONSTRAINT FK_PARAMETRO_ECONOMICO_USUARIO_id_usuario REFERENCES USUARIO(id_usuario);
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PARAMETRO_fuente' AND definition LIKE '%API_EXTERNA%')
        BEGIN
            IF OBJECT_ID(N'CK_PARAMETRO_fuente') IS NOT NULL
                ALTER TABLE PARAMETRO_ECONOMICO DROP CONSTRAINT CK_PARAMETRO_fuente;
            ALTER TABLE PARAMETRO_ECONOMICO ADD CONSTRAINT CK_PARAMETRO_fuente
                CHECK (fuente IN ('API_BCCH','RPA_COCHILCO','API_EXTERNA','MANUAL'));
        END;
        """;

    /// <summary>
    /// Sprint 5 (observación del profesor guía): las fórmulas pasan del tipo de crédito a cada instrumento de fomento.
    /// En una base anterior, cada historial de versiones queda en el primer instrumento de su antiguo tipo de crédito
    /// (la semilla copia la versión vigente a los demás instrumentos de ese tipo) y luego se eliminan TIPO_CREDITO y sus columnas.
    /// Las sentencias que usan columnas nuevas van en EXEC para que SQL Server no las valide antes de crearlas.
    /// </summary>
    private const string SqlInstrumentos = """
        IF COL_LENGTH('INSTRUMENTO_FOMENTO', 'activo') IS NULL
            ALTER TABLE INSTRUMENTO_FOMENTO ADD activo bit NOT NULL CONSTRAINT DF_INSTRUMENTO_activo DEFAULT 1, descripcion nvarchar(200) NULL;
        IF COL_LENGTH('PLANTILLA_CALCULO', 'id_instrumento') IS NULL
        BEGIN
            ALTER TABLE PLANTILLA_CALCULO ADD id_instrumento tinyint NULL;
            EXEC(N'UPDATE p SET id_instrumento = (SELECT MIN(i.id_instrumento) FROM INSTRUMENTO_FOMENTO i WHERE i.id_tipo_credito = p.id_tipo_credito)
                   FROM PLANTILLA_CALCULO p;');
        END;
        IF COL_LENGTH('PLANTILLA_CALCULO', 'id_tipo_credito') IS NOT NULL
        BEGIN
            DECLARE @sqlPlantilla nvarchar(max) = N'';
            SELECT @sqlPlantilla += N'ALTER TABLE PLANTILLA_CALCULO DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
              FROM sys.foreign_keys fk JOIN sys.foreign_key_columns c ON c.constraint_object_id = fk.object_id
             WHERE fk.parent_object_id = OBJECT_ID(N'PLANTILLA_CALCULO') AND COL_NAME(c.parent_object_id, c.parent_column_id) = 'id_tipo_credito';
            SELECT @sqlPlantilla += N'DROP INDEX ' + QUOTENAME(x.name) + N' ON PLANTILLA_CALCULO;'
              FROM (SELECT DISTINCT ix.name FROM sys.indexes ix
                      JOIN sys.index_columns ic ON ic.object_id = ix.object_id AND ic.index_id = ix.index_id
                     WHERE ix.object_id = OBJECT_ID(N'PLANTILLA_CALCULO') AND ix.is_primary_key = 0
                       AND COL_NAME(ic.object_id, ic.column_id) = 'id_tipo_credito') x;
            SET @sqlPlantilla += N'ALTER TABLE PLANTILLA_CALCULO DROP COLUMN id_tipo_credito;
                ALTER TABLE PLANTILLA_CALCULO ALTER COLUMN id_instrumento tinyint NOT NULL;
                ALTER TABLE PLANTILLA_CALCULO ADD CONSTRAINT FK_PLANTILLA_CALCULO_INSTRUMENTO_FOMENTO_id_instrumento
                    FOREIGN KEY (id_instrumento) REFERENCES INSTRUMENTO_FOMENTO(id_instrumento);
                CREATE UNIQUE INDEX IX_PLANTILLA_CALCULO_id_instrumento_version ON PLANTILLA_CALCULO(id_instrumento, version);';
            EXEC(@sqlPlantilla);
        END;
        IF COL_LENGTH('INSTRUMENTO_FOMENTO', 'id_tipo_credito') IS NOT NULL
        BEGIN
            DECLARE @sqlInstrumento nvarchar(max) = N'';
            SELECT @sqlInstrumento += N'ALTER TABLE INSTRUMENTO_FOMENTO DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
              FROM sys.foreign_keys fk JOIN sys.foreign_key_columns c ON c.constraint_object_id = fk.object_id
             WHERE fk.parent_object_id = OBJECT_ID(N'INSTRUMENTO_FOMENTO') AND COL_NAME(c.parent_object_id, c.parent_column_id) = 'id_tipo_credito';
            SELECT @sqlInstrumento += N'DROP INDEX ' + QUOTENAME(x.name) + N' ON INSTRUMENTO_FOMENTO;'
              FROM (SELECT DISTINCT ix.name FROM sys.indexes ix
                      JOIN sys.index_columns ic ON ic.object_id = ix.object_id AND ic.index_id = ix.index_id
                     WHERE ix.object_id = OBJECT_ID(N'INSTRUMENTO_FOMENTO') AND ix.is_primary_key = 0
                       AND COL_NAME(ic.object_id, ic.column_id) = 'id_tipo_credito') x;
            SET @sqlInstrumento += N'ALTER TABLE INSTRUMENTO_FOMENTO DROP COLUMN id_tipo_credito;';
            EXEC(@sqlInstrumento);
        END;
        IF OBJECT_ID(N'TIPO_CREDITO') IS NOT NULL
            DROP TABLE TIPO_CREDITO;
        """;

    /// <summary>
    /// Crea la base de datos si no existe (con las 22 tablas), carga los catálogos y, si se configuró la carpeta,
    /// las planillas de ENAMI. Se llama una vez al iniciar la aplicación.
    /// </summary>
    public static void PrepararBaseDatos(this IServiceProvider proveedor, string raizContenido, bool cargarPlanillas)
    {
        using var scope = proveedor.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Enami.Datos");
        var contexto = sp.GetService<EnamiDbContext>();
        if (contexto is not null)
        {
            // Azure SQL sin servidor se pausa cuando no se usa y tarda hasta un minuto en reanudarse: se reintenta.
            for (int intento = 1; ; intento++)
            {
                try
                {
                    if (contexto.Database.EnsureCreated()) log.LogInformation("Base de datos creada con las tablas del modelo.");
                    else ActualizarEsquema(contexto, log);
                    break;
                }
                catch (Exception ex) when (intento < 6)
                {
                    log.LogWarning("No se pudo conectar con SQL Server (intento {Intento} de 6): {Mensaje}. Se reintenta en 15 s.", intento, ex.Message);
                    Thread.Sleep(TimeSpan.FromSeconds(15));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "No se pudo conectar con SQL Server. Revise ConnectionStrings:Enami en appsettings.json " +
                        "o use \"Persistencia\": \"Memoria\" para probar sin base de datos. Detalle: " + ex.Message, ex);
                }
            }
        }
        var configurada = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpcionesSemilla>>().Value;
        var opciones = new OpcionesSemilla
        {
            CorreoAdministrador = configurada.CorreoAdministrador,
            ClaveAdministrador = configurada.ClaveAdministrador,
            UsuariosDemostracion = configurada.UsuariosDemostracion,
            RutaPlanillas = string.IsNullOrWhiteSpace(configurada.RutaPlanillas) ? null : Path.GetFullPath(configurada.RutaPlanillas, raizContenido),
            CompletarDatosPrueba = configurada.CompletarDatosPrueba,
        };
        var semilla = sp.GetRequiredService<Semilla>();
        semilla.Ejecutar(opciones);
        if (cargarPlanillas)
        {
            foreach (var r in semilla.CargarPlanillasIniciales(opciones, sp.GetRequiredService<ServicioCargaPlanillas>()))
                log.LogInformation("Carga inicial de {Planilla}: {Leidas} filas leídas, {Cargados} cargadas, {Rechazadas} rechazadas ({Segundos:0.0} s).",
                    r.Planilla, r.FilasLeidas, r.Cargados, r.Rechazadas.Count, r.Duracion.TotalSeconds);
            if (opciones.CompletarDatosPrueba)
            {
                var resumen = sp.GetRequiredService<ServicioDatosPrueba>().Completar();
                if (resumen.Total > 0) log.LogWarning("Base de PRUEBA: se completaron campos vacíos con datos recreados ({Resumen}).", resumen);
            }
        }
    }
}
