using Enami.Dominio.Calculo;
using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>Opciones de la carga inicial (sección «Semilla» de appsettings.json).</summary>
public class OpcionesSemilla
{
    public string CorreoAdministrador { get; set; } = "admin@enami.cl";
    public string ClaveAdministrador { get; set; } = "";
    /// <summary>Crea un usuario de cada rol con la misma clave del administrador, para probar el sistema.</summary>
    public bool UsuariosDemostracion { get; set; }
    /// <summary>Carpeta con 1_Ficha_Productor.xlsx, 2_Ficha_Faena.xlsx y 3_BD_Padrones.xlsx; si existe y la base no tiene productores, se cargan al iniciar.</summary>
    public string? RutaPlanillas { get; set; }
    /// <summary>
    /// SOLO PARA PRUEBAS: después de cargar las planillas, completa los campos vacíos con datos recreados pero coherentes
    /// (ver <see cref="ServicioDatosPrueba"/>). Nunca debe activarse con datos reales de producción.
    /// </summary>
    public bool CompletarDatosPrueba { get; set; }
}

/// <summary>
/// Datos iniciales: catálogos del modelo (roles, oficinas, instrumentos de fomento y causales), el administrador,
/// la versión 1 de las fórmulas de cada instrumento y los valores iniciales de las variables de mantenedor y de mercado.
/// Solo agrega lo que falta, así que se puede ejecutar en cada inicio.
/// </summary>
public class Semilla(IAlmacen db, IHasherClaves hasher)
{
    public static readonly (string Nombre, byte? Nivel)[] RolesBase =
    [
        (Roles.Ingeniero, 1), (Roles.JefeOficina, 2), (Roles.Subgerente, 3), (Roles.Jefatura, null), (Roles.Administrador, null)
    ];

    /// <summary>Las 11 oficinas mineras de ENAMI que aparecen en la ficha de faenas.</summary>
    public static readonly (string Nombre, string Region)[] OficinasBase =
    [
        ("Oficina Minera Tocopilla", "Antofagasta"), ("Oficina Minera Antofagasta", "Antofagasta"), ("Oficina Minera Taltal", "Antofagasta"),
        ("Oficina Minera El Salado", "Atacama"), ("Oficina Minera Copiapó", "Atacama"), ("Oficina Minera Vallenar", "Atacama"),
        ("Oficina Minera La Serena", "Coquimbo"), ("Oficina Minera Ovalle", "Coquimbo"), ("Oficina Minera Illapel", "Coquimbo"),
        ("Oficina Minera Cabildo", "Valparaíso"), ("Oficina Minera Rancagua", "Libertador General Bernardo O'Higgins"),
    ];

    /// <summary>
    /// Los nueve instrumentos de fomento de ENAMI con que parte el sistema (por validar con la Gerencia de Fomento).
    /// La Jefatura puede agregar más desde «Instrumentos de fomento»; cada uno tiene sus propias fórmulas.
    /// <c>Grupo</c> indica qué instrumentos compartían fórmulas cuando estas se definían por tipo de crédito (hasta el Sprint 5):
    /// sirve para migrar una base anterior y para elegir la plantilla inicial (grupo <see cref="GrupoAnexo2"/>: Anexo 2).
    /// </summary>
    public static readonly (string Nombre, byte Grupo)[] InstrumentosBase =
    [
        ("Crédito para la Operación (capital de trabajo)", 1), ("Apoyo a la Producción", 1),
        ("Crédito para Inversiones (maquinaria y equipos)", 2), ("Reactivación de Faenas Mineras", 2),
        ("Crédito para el Desarrollo y Preparación de Minas", 3), ("Créditos para Desarrollo Minero", 3),
        ("Crédito de Emergencia", 4),
        ("Reconocimiento de Recursos y/o Reservas", 5), ("Estudios Distritales", 5),
    ];

    /// <summary>Grupo de instrumentos que se evalúa con el Anexo 2 de ENAMI (por escenarios de precio).</summary>
    public const byte GrupoAnexo2 = 5;

    /// <summary>Instrumento «Reconocimiento de Recursos y/o Reservas», el del caso de ejemplo de la Gerencia (Anexo 2).</summary>
    public const byte InstrumentoAnexo2 = 8;

    /// <summary>
    /// Actividades del Anexo 1 con su costo medio en US$ por unidad, tomadas de las hojas «Tarificación» y «DE» de la planilla de ENAMI
    /// (concurso 2026). Las que no traen costo en la planilla quedan en 0 y el ingeniero lo ingresa en cada solicitud.
    /// La fracción es lo que financia ENAMI en el reconocimiento de reservas (80 % en labores mineras; 100 % en sondajes y análisis).
    /// </summary>
    public static readonly (string Nombre, string Unidad, decimal Costo, string Programa, decimal Pct)[] LaboresBase =
    [
        ("Servicios de geología", "hr", 0, ProgramasPresupuesto.Reconocimiento, 1),
        ("Servicios de ingeniería", "hr", 0, ProgramasPresupuesto.Reconocimiento, 1),
        ("Estudio geológico", "c/u", 0, ProgramasPresupuesto.Reconocimiento, 1),
        ("Levantamiento topográfico", "c/u", 0, ProgramasPresupuesto.Reconocimiento, 1),
        ("Sondajes", "m", 131, ProgramasPresupuesto.Reconocimiento, 1),
        ("Insumos de sondajes", "c/u", 0, ProgramasPresupuesto.Reconocimiento, 1),
        ("Geología de superficie", "c/u", 0, ProgramasPresupuesto.Reconocimiento, 1),
        ("Piques 2,0 x 2,0", "m", 690, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Contrapiques 2,0 x 2,0", "m", 755, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Chimeneas 1,5 x 1,5", "m", 645, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Galerías 1,8 x 2,0", "m", 495, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Galerías 2,5 x 2,5", "m", 590, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Galerías 3,0 x 3,0", "m", 690, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Rampas 3,0 x 4,0", "m", 800, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Rampas 4,0 x 4,0", "m", 945, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Otras excavaciones (in situ)", "m3", 100, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Desatierro horizontal", "m3", 25, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Desatierro vertical", "m3", 60, ProgramasPresupuesto.Reconocimiento, 0.8m),
        ("Caminos de acceso", "m", 0, ProgramasPresupuesto.Reconocimiento, 1),
        ("Análisis químico", "N°", 36, ProgramasPresupuesto.Reconocimiento, 1),
        ("Portales de bocamina y fortificación", "c/u", 0, ProgramasPresupuesto.Reconocimiento, 1),
        ("Gastos notariales y legales", "US$", 1, ProgramasPresupuesto.Propio, 0),
    ];

    public static readonly (string Codigo, string Descripcion, string Tipo)[] CausalesBase =
    [
        ("FIN-01", "VAN negativo con los supuestos del proyecto", "FINANCIERA"),
        ("FIN-02", "El flujo de caja no permite pagar el crédito en el plazo", "FINANCIERA"),
        ("FIN-03", "El monto excede el tope del instrumento", "FINANCIERA"),
        ("TEC-01", "Recursos o reservas no acreditados", "TECNICA"),
        ("TEC-02", "Ley de mineral insuficiente para el poder de compra", "TECNICA"),
        ("TEC-03", "Faena sin condiciones de seguridad", "TECNICA"),
        ("LEG-01", "Padrón vencido o inexistente", "LEGAL"),
        ("LEG-02", "Productor moroso o en cobranza judicial con ENAMI", "LEGAL"),
        ("LEG-03", "Faena sin regularización ante SERNAGEOMIN", "LEGAL"),
    ];

    /// <summary>Usuarios de prueba: uno por rol, en la oficina de Copiapó (piloto).</summary>
    public static readonly (string Correo, string Nombre, string Rol, bool ConOficina)[] UsuariosDemo =
    [
        ("ingeniero.copiapo@enami.cl", "Ingeniero de Fomento (demo)", Roles.Ingeniero, true),
        ("jefe.copiapo@enami.cl", "Jefe Oficina Copiapó (demo)", Roles.JefeOficina, true),
        ("subgerente@enami.cl", "Subgerente de Fomento (demo)", Roles.Subgerente, false),
        ("jefatura@enami.cl", "Jefatura de Fomento (demo)", Roles.Jefatura, false),
    ];

    public void Ejecutar(OpcionesSemilla o)
    {
        db.EnTransaccion(() =>
        {
            for (int i = 0; i < RolesBase.Length; i++)
            {
                var (nombre, nivel) = RolesBase[i];
                if (!db.Tabla<Rol>().Any(r => r.Nombre == nombre))
                    db.Agregar(new Rol { Id = (byte)(i + 1), Nombre = nombre, NivelAprobacion = nivel });
            }
            foreach (var (nombre, region) in OficinasBase)
                if (!db.Tabla<Oficina>().Any(x => x.Nombre == nombre)) db.Agregar(new Oficina { Nombre = nombre, Region = region });
            for (int i = 0; i < InstrumentosBase.Length; i++)
            {
                byte id = (byte)(i + 1);
                if (!db.Tabla<InstrumentoFomento>().Any(t => t.Id == id))
                    db.Agregar(new InstrumentoFomento { Id = id, Nombre = InstrumentosBase[i].Nombre, Activo = true });
            }
            for (int i = 0; i < CausalesBase.Length; i++)
            {
                short id = (short)(i + 1);
                if (!db.Tabla<CausalRechazo>().Any(c => c.Id == id))
                    db.Agregar(new CausalRechazo { Id = id, Codigo = CausalesBase[i].Codigo, Descripcion = CausalesBase[i].Descripcion, Tipo = CausalesBase[i].Tipo });
            }
            db.Guardar();

            // Administrador inicial
            var correo = o.CorreoAdministrador.Trim().ToLowerInvariant();
            var admin = db.Tabla<Usuario>().FirstOrDefault(u => u.Correo == correo);
            if (admin is null)
            {
                if (string.IsNullOrWhiteSpace(o.ClaveAdministrador))
                    throw new InvalidOperationException("Configure Semilla:ClaveAdministrador en appsettings.json para crear el administrador inicial.");
                admin = new Usuario
                {
                    Correo = correo, NombreCompleto = "Administrador del sistema", IdRol = Rol(Roles.Administrador),
                    ClaveHash = hasher.Calcular(o.ClaveAdministrador), Activo = true
                };
                db.Agregar(admin);
                db.Guardar();
            }

            if (o.UsuariosDemostracion && !string.IsNullOrWhiteSpace(o.ClaveAdministrador))
            {
                int copiapo = db.Tabla<Oficina>().First(x => x.Nombre == "Oficina Minera Copiapó").Id;
                foreach (var (c, nombre, rol, conOficina) in UsuariosDemo)
                    if (!db.Tabla<Usuario>().Any(u => u.Correo == c))
                        db.Agregar(new Usuario
                        {
                            Correo = c, NombreCompleto = nombre, IdRol = Rol(rol), IdOficina = conOficina ? copiapo : null,
                            ClaveHash = hasher.Calcular(o.ClaveAdministrador), Activo = true
                        });
                db.Guardar();
            }

            // Fórmulas de cada instrumento. Los del grupo «Reconocimiento» se evalúan con el Anexo 2 de ENAMI (escenarios de precio);
            // el resto parte con la plantilla de ejemplo, que la Jefatura reemplaza desde el editor de fórmulas.
            var repo = new RepositorioPlantillas(db);
            var ejemplo = PlantillaEjemplo.Crear(0);
            var anexo2 = PlantillaAnexo2.Crear(0);
            for (int i = 0; i < InstrumentosBase.Length; i++)
            {
                byte id = (byte)(i + 1);
                byte grupo = InstrumentosBase[i].Grupo;
                var versiones = db.Tabla<VersionPlantilla>().Where(v => v.IdInstrumento == id).ToList();
                if (versiones.Count == 0)
                {
                    // En una base anterior, los instrumentos del mismo grupo compartían las fórmulas: se copia la versión vigente
                    // del primero que la tenga, para no perder los cambios que haya hecho la Jefatura.
                    PlantillaCalculo? delGrupo = null;
                    for (int j = 0; j < InstrumentosBase.Length && delGrupo is null; j++)
                        if (j != i && InstrumentosBase[j].Grupo == grupo) delGrupo = repo.ObtenerVigente((byte)(j + 1));
                    repo.RegistrarVigente(delGrupo ?? (grupo == GrupoAnexo2 ? anexo2 : ejemplo), id, admin.Id);
                }
                else if (grupo == GrupoAnexo2)
                {
                    // Sprint 5: estos instrumentos se evalúan con el Anexo 2. En una base anterior se registra como versión nueva.
                    if (!versiones.Any(v => v.ModoCalculo == "ESCENARIOS"))
                        repo.RegistrarVigente(anexo2, id, admin.Id);
                    else
                    {
                        // si la vigente es de la carga inicial (no editada por la Jefatura) y le faltan campos de esta versión
                        // del sistema, se registra la nueva
                        var vigente = versiones.FirstOrDefault(v => v.Estado == "VIGENTE");
                        if (vigente is not null && vigente.ModoCalculo == "ESCENARIOS" && vigente.IdAutor == admin.Id)
                        {
                            var codigos = db.Tabla<VariablePlantilla>().Where(x => x.IdPlantilla == vigente.Id).Select(x => x.Codigo).ToList();
                            if (anexo2.Variables.Any(x => !codigos.Contains(x.Codigo, StringComparer.OrdinalIgnoreCase)))
                                repo.RegistrarVigente(anexo2, id, admin.Id);
                        }
                    }
                }
            }

            // Catálogo de actividades del presupuesto (Anexo 1)
            if (!db.Tabla<LaborTarifada>().Any())
            {
                foreach (var (nombre, unidad, costo, programa, pct) in LaboresBase)
                    db.Agregar(new LaborTarifada { Nombre = nombre, Unidad = unidad, CostoUnitario = costo, Programa = programa, PorcentajeFinanciamiento = pct });
                db.Guardar();
            }

            // Valores iniciales de las variables de mantenedor y de mercado (los actualiza BOT-01 o el administrador).
            // Se agregan las que falten, así una base anterior recibe las del Anexo 2.
            var registrados = db.Tabla<ParametroEconomico>().Select(x => x.CodigoVariable).Distinct().ToList().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var v in ejemplo.Variables.Concat(anexo2.Variables).Where(v => v.Origen != OrigenVariable.Formulario))
                if (registrados.Add(v.Codigo))
                    db.Agregar(new ParametroEconomico { Fecha = DateTime.Today, CodigoVariable = v.Codigo, Valor = (decimal)v.ValorPorDefecto, Fuente = "MANUAL" });
            db.Guardar();
        });
    }

    /// <summary>
    /// Si la carpeta de planillas existe, carga las fichas cuando la base no tiene productores, y la recepción de minerales
    /// cuando aún no tiene lotes (así una base creada antes del Sprint 4 recibe la recepción al actualizar).
    /// </summary>
    public IReadOnlyList<ResumenCarga> CargarPlanillasIniciales(OpcionesSemilla o, ServicioCargaPlanillas carga)
    {
        var resultado = new List<ResumenCarga>();
        if (string.IsNullOrWhiteSpace(o.RutaPlanillas) || !Directory.Exists(o.RutaPlanillas)) return resultado;
        string Ruta(string nombre) => Path.Combine(o.RutaPlanillas!, nombre);
        if (!db.Tabla<Productor>().Any())
        {
            if (File.Exists(Ruta("1_Ficha_Productor.xlsx"))) resultado.Add(carga.CargarProductores(Ruta("1_Ficha_Productor.xlsx"), null, "BOT-05"));
            if (File.Exists(Ruta("2_Ficha_Faena.xlsx"))) resultado.Add(carga.CargarFaenas(Ruta("2_Ficha_Faena.xlsx"), null, "BOT-05"));
            if (File.Exists(Ruta("3_BD_Padrones.xlsx"))) resultado.Add(carga.CargarPadrones(Ruta("3_BD_Padrones.xlsx"), null, "BOT-05"));
        }
        // El nombre lleva tilde («Recepción»); se busca por patrón para no depender de cómo la guarda el sistema de archivos.
        var recepcion = Directory.EnumerateFiles(o.RutaPlanillas!, "4_Recep*.xlsx").FirstOrDefault();
        if (recepcion is not null && db.Tabla<Faena>().Any() && !db.Tabla<RecepcionMineral>().Any())
            resultado.Add(carga.CargarRecepciones(recepcion, null, "BOT-05"));
        return resultado;
    }

    private byte Rol(string nombre) => db.Tabla<Rol>().First(r => r.Nombre == nombre).Id;
}
