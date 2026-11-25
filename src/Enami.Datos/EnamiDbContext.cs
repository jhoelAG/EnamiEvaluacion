using Enami.Dominio.Modelo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Enami.Datos;

/// <summary>
/// Contexto de Entity Framework Core para SQL Server (LocalDB en desarrollo, Azure SQL en el piloto).
/// Crea las 22 tablas: 17 del informe, RECEPCION_MINERAL y RECURSO_FAENA (Sprint 4), y DATO_SOLICITUD, LABOR_TARIFADA y PRESUPUESTO_SOLICITUD (Sprint 5) del modelo normalizado en 3FN con los mismos nombres de tablas, columnas, claves y
/// restricciones CHECK del informe (sección 3.4 y Anexo A). TIPO_CREDITO se eliminó en el Sprint 5: las fórmulas se definen por instrumento de fomento.
/// </summary>
public class EnamiDbContext(DbContextOptions<EnamiDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Oficina>(e =>
        {
            e.ToTable("OFICINA");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_oficina").ValueGeneratedOnAdd();
            e.Property(x => x.Nombre).HasColumnName("nombre_oficina").HasMaxLength(60).IsRequired();
            e.Property(x => x.Region).HasColumnName("region").HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        m.Entity<Comuna>(e =>
        {
            e.ToTable("COMUNA");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_comuna").ValueGeneratedOnAdd();
            e.Property(x => x.Nombre).HasColumnName("nombre_comuna").HasMaxLength(60).IsRequired();
            e.Property(x => x.Region).HasColumnName("region").HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        m.Entity<Rol>(e =>
        {
            e.ToTable("ROL", t => t.HasCheckConstraint("CK_ROL_nivel", "nivel_aprobacion IS NULL OR nivel_aprobacion BETWEEN 1 AND 3"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_rol").ValueGeneratedNever();
            e.Property(x => x.Nombre).HasColumnName("nombre_rol").HasColumnType("varchar(20)").IsRequired();
            e.Property(x => x.NivelAprobacion).HasColumnName("nivel_aprobacion");
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        m.Entity<Usuario>(e =>
        {
            e.ToTable("USUARIO");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_usuario").ValueGeneratedOnAdd();
            e.Property(x => x.Correo).HasColumnName("correo").HasMaxLength(120).IsRequired();
            e.Property(x => x.NombreCompleto).HasColumnName("nombre_completo").HasMaxLength(120).IsRequired();
            e.Property(x => x.IdRol).HasColumnName("id_rol");
            e.Property(x => x.IdOficina).HasColumnName("id_oficina");
            e.Property(x => x.Activo).HasColumnName("activo");
            e.Property(x => x.ClaveHash).HasColumnName("clave_hash").HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Correo).IsUnique();
            e.HasOne<Rol>().WithMany().HasForeignKey(x => x.IdRol).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Oficina>().WithMany().HasForeignKey(x => x.IdOficina).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Productor>(e =>
        {
            e.ToTable("PRODUCTOR", t =>
            {
                t.HasCheckConstraint("CK_PRODUCTOR_tipo", "tipo_productor IN ('NATURAL','EMPRESA')");
                t.HasCheckConstraint("CK_PRODUCTOR_cumplimiento", "nivel_cumplimiento IN ('AL_DIA','MOROSO','JUDICIAL')");
                t.HasCheckConstraint("CK_PRODUCTOR_nombre", "(tipo_productor = 'NATURAL' AND nombres IS NOT NULL) OR (tipo_productor = 'EMPRESA' AND razon_social IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_productor").ValueGeneratedOnAdd();
            e.Property(x => x.Rut).HasColumnName("rut").HasColumnType("varchar(12)").IsRequired();
            e.Property(x => x.TipoProductor).HasColumnName("tipo_productor").HasColumnType("varchar(10)").IsRequired();
            e.Property(x => x.Nombres).HasColumnName("nombres").HasMaxLength(80);
            e.Property(x => x.Apellidos).HasColumnName("apellidos").HasMaxLength(80);
            e.Property(x => x.RazonSocial).HasColumnName("razon_social").HasMaxLength(150);
            e.Property(x => x.RepresentanteLegal).HasColumnName("representante_legal").HasMaxLength(120);
            e.Property(x => x.IdComuna).HasColumnName("id_comuna");
            e.Property(x => x.Direccion).HasColumnName("direccion").HasMaxLength(150);
            e.Property(x => x.Telefono).HasColumnName("telefono").HasColumnType("varchar(30)");
            e.Property(x => x.Correo).HasColumnName("correo").HasMaxLength(120);
            e.Property(x => x.NivelCumplimiento).HasColumnName("nivel_cumplimiento").HasColumnType("varchar(15)").IsRequired();
            e.Ignore(x => x.NombreMostrado);
            e.HasIndex(x => x.Rut).IsUnique();
            e.HasOne<Comuna>().WithMany().HasForeignKey(x => x.IdComuna).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Faena>(e =>
        {
            e.ToTable("FAENA", t =>
            {
                t.HasCheckConstraint("CK_FAENA_estado", "estado_faena IN ('ACTIVA','INACTIVA')");
                t.HasCheckConstraint("CK_FAENA_distancia", "distancia_poder_compra_km IS NULL OR (distancia_poder_compra_km > 0 AND distancia_poder_compra_km < 1000)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_faena").ValueGeneratedOnAdd();
            e.Property(x => x.Codigo).HasColumnName("codigo_faena").HasColumnType("varchar(10)").IsRequired();
            e.Property(x => x.Nombre).HasColumnName("nombre_faena").HasMaxLength(100).IsRequired();
            e.Property(x => x.IdProductor).HasColumnName("id_productor");
            e.Property(x => x.IdComuna).HasColumnName("id_comuna");
            e.Property(x => x.IdOficina).HasColumnName("id_oficina");
            e.Property(x => x.Estado).HasColumnName("estado_faena").HasColumnType("varchar(10)").IsRequired();
            e.Property(x => x.DistanciaPoderCompraKm).HasColumnName("distancia_poder_compra_km").HasColumnType("decimal(8,2)");
            e.Property(x => x.NroPadron).HasColumnName("nro_padron").HasColumnType("varchar(20)");
            e.Property(x => x.VencimientoPadron).HasColumnName("vencimiento_padron").HasColumnType("date");
            e.Property(x => x.Ubicacion).HasColumnName("ubicacion").HasMaxLength(150);
            e.Property(x => x.CoordenadaNorte).HasColumnName("coordenada_norte").HasColumnType("decimal(12,2)");
            e.Property(x => x.CoordenadaEste).HasColumnName("coordenada_este").HasColumnType("decimal(12,2)");
            e.Property(x => x.ManoObra).HasColumnName("mano_obra");
            e.HasIndex(x => x.Codigo).IsUnique();
            e.HasOne<Productor>().WithMany().HasForeignKey(x => x.IdProductor).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Comuna>().WithMany().HasForeignKey(x => x.IdComuna).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Oficina>().WithMany().HasForeignKey(x => x.IdOficina).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<DatoSolicitud>(e =>
        {
            e.ToTable("DATO_SOLICITUD");
            e.HasKey(x => new { x.IdSolicitud, x.Codigo });
            e.Property(x => x.IdSolicitud).HasColumnName("id_solicitud");
            e.Property(x => x.Codigo).HasColumnName("codigo").HasColumnType("varchar(40)");
            e.Property(x => x.ValorNumero).HasColumnName("valor_numero").HasColumnType("decimal(18,6)");
            e.Property(x => x.ValorTexto).HasColumnName("valor_texto").HasMaxLength(2000);
            e.HasOne<SolicitudCredito>().WithMany().HasForeignKey(x => x.IdSolicitud).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<LaborTarifada>(e =>
        {
            e.ToTable("LABOR_TARIFADA", t =>
            {
                t.HasCheckConstraint("CK_LABOR_costo", "costo_unitario >= 0");
                t.HasCheckConstraint("CK_LABOR_programa", "programa IN ('RECONOCIMIENTO','DESARROLLO','PROPIO')");
                t.HasCheckConstraint("CK_LABOR_pct", "pct_financiamiento >= 0 AND pct_financiamiento <= 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_labor").ValueGeneratedOnAdd();
            e.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(80).IsRequired();
            e.Property(x => x.Unidad).HasColumnName("unidad").HasColumnType("varchar(8)").IsRequired();
            e.Property(x => x.CostoUnitario).HasColumnName("costo_unitario").HasColumnType("decimal(12,2)");
            e.Property(x => x.Programa).HasColumnName("programa").HasColumnType("varchar(14)").IsRequired();
            e.Property(x => x.PorcentajeFinanciamiento).HasColumnName("pct_financiamiento").HasColumnType("decimal(5,4)");
            e.Property(x => x.Activa).HasColumnName("activa");
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        m.Entity<LineaPresupuesto>(e =>
        {
            e.ToTable("PRESUPUESTO_SOLICITUD", t =>
            {
                t.HasCheckConstraint("CK_PRESUPUESTO_cantidad", "cantidad > 0");
                t.HasCheckConstraint("CK_PRESUPUESTO_costo", "costo_unitario >= 0");
                t.HasCheckConstraint("CK_PRESUPUESTO_programa", "programa IN ('RECONOCIMIENTO','DESARROLLO','PROPIO')");
                t.HasCheckConstraint("CK_PRESUPUESTO_pct", "pct_financiamiento >= 0 AND pct_financiamiento <= 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_linea").ValueGeneratedOnAdd();
            e.Property(x => x.IdSolicitud).HasColumnName("id_solicitud");
            e.Property(x => x.IdLabor).HasColumnName("id_labor");
            e.Property(x => x.Programa).HasColumnName("programa").HasColumnType("varchar(14)").IsRequired();
            e.Property(x => x.Cantidad).HasColumnName("cantidad").HasColumnType("decimal(12,2)");
            e.Property(x => x.CostoUnitario).HasColumnName("costo_unitario").HasColumnType("decimal(12,2)");
            e.Property(x => x.PorcentajeFinanciamiento).HasColumnName("pct_financiamiento").HasColumnType("decimal(5,4)");
            e.HasOne<SolicitudCredito>().WithMany().HasForeignKey(x => x.IdSolicitud).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<LaborTarifada>().WithMany().HasForeignKey(x => x.IdLabor).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<RecepcionMineral>(e =>
        {
            e.ToTable("RECEPCION_MINERAL", t =>
            {
                t.HasCheckConstraint("CK_RECEPCION_peso", "peso_seco_kg > 0");
                t.HasCheckConstraint("CK_RECEPCION_ley_cu", "ley_cu_pct >= 0 AND ley_cu_pct < 100");
                t.HasCheckConstraint("CK_RECEPCION_leyes", "ley_au_gt >= 0 AND ley_ag_gt >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_recepcion").ValueGeneratedOnAdd();
            e.Property(x => x.NroLote).HasColumnName("nro_lote").HasColumnType("varchar(12)").IsRequired();
            e.Property(x => x.IdFaena).HasColumnName("id_faena");
            e.Property(x => x.FechaCierre).HasColumnName("fecha_cierre").HasColumnType("date");
            e.Property(x => x.Agencia).HasColumnName("agencia").HasColumnType("varchar(4)").IsRequired();
            e.Property(x => x.Producto).HasColumnName("producto").HasColumnType("varchar(4)").IsRequired();
            e.Property(x => x.PesoSecoKg).HasColumnName("peso_seco_kg").HasColumnType("decimal(12,0)");
            e.Property(x => x.LeyCuPct).HasColumnName("ley_cu_pct").HasColumnType("decimal(7,4)");
            e.Property(x => x.LeyAuGt).HasColumnName("ley_au_gt").HasColumnType("decimal(9,3)");
            e.Property(x => x.LeyAgGt).HasColumnName("ley_ag_gt").HasColumnType("decimal(9,3)");
            e.HasIndex(x => x.NroLote).IsUnique();
            e.HasIndex(x => new { x.IdFaena, x.FechaCierre });
            e.HasOne<Faena>().WithMany().HasForeignKey(x => x.IdFaena).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<RecursoFaena>(e =>
        {
            e.ToTable("RECURSO_FAENA", t =>
            {
                t.HasCheckConstraint("CK_RECURSO_categoria", "categoria IN ('MEDIDO','INDICADO','INFERIDO')");
                t.HasCheckConstraint("CK_RECURSO_toneladas", "toneladas > 0");
                t.HasCheckConstraint("CK_RECURSO_leyes", "ley_cu_pct >= 0 AND ley_cu_pct < 100 AND ley_au_gt >= 0 AND ley_ag_gt >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_recurso").ValueGeneratedOnAdd();
            e.Property(x => x.IdFaena).HasColumnName("id_faena");
            e.Property(x => x.Categoria).HasColumnName("categoria").HasColumnType("varchar(10)").IsRequired();
            e.Property(x => x.Toneladas).HasColumnName("toneladas").HasColumnType("decimal(12,0)");
            e.Property(x => x.LeyCuPct).HasColumnName("ley_cu_pct").HasColumnType("decimal(7,4)");
            e.Property(x => x.LeyAuGt).HasColumnName("ley_au_gt").HasColumnType("decimal(9,3)");
            e.Property(x => x.LeyAgGt).HasColumnName("ley_ag_gt").HasColumnType("decimal(9,3)");
            e.Property(x => x.Fuente).HasColumnName("fuente").HasMaxLength(100);
            e.Property(x => x.IdUsuario).HasColumnName("id_usuario");
            e.Property(x => x.FechaRegistro).HasColumnName("fecha_registro").HasColumnType("datetime2");
            e.HasIndex(x => new { x.IdFaena, x.Categoria }).IsUnique();
            e.HasOne<Faena>().WithMany().HasForeignKey(x => x.IdFaena).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuario).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<InstrumentoFomento>(e =>
        {
            e.ToTable("INSTRUMENTO_FOMENTO");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_instrumento").ValueGeneratedNever();
            e.Property(x => x.Nombre).HasColumnName("nombre_instrumento").HasMaxLength(80).IsRequired();
            e.Property(x => x.Descripcion).HasColumnName("descripcion").HasMaxLength(200);
            e.Property(x => x.Activo).HasColumnName("activo");
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        m.Entity<CausalRechazo>(e =>
        {
            e.ToTable("CAUSAL_RECHAZO", t => t.HasCheckConstraint("CK_CAUSAL_tipo", "tipo_causal IN ('TECNICA','FINANCIERA','LEGAL')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_causal").ValueGeneratedNever();
            e.Property(x => x.Codigo).HasColumnName("codigo_causal").HasColumnType("varchar(10)").IsRequired();
            e.Property(x => x.Descripcion).HasColumnName("descripcion").HasMaxLength(150).IsRequired();
            e.Property(x => x.Tipo).HasColumnName("tipo_causal").HasColumnType("varchar(10)").IsRequired();
            e.HasIndex(x => x.Codigo).IsUnique();
        });

        m.Entity<SolicitudCredito>(e =>
        {
            e.ToTable("SOLICITUD_CREDITO", t =>
            {
                t.HasCheckConstraint("CK_SOLICITUD_monto", "monto_solicitado > 0");
                t.HasCheckConstraint("CK_SOLICITUD_plazo", "plazo_meses BETWEEN 1 AND 120");
                t.HasCheckConstraint("CK_SOLICITUD_estado", "estado IN ('BORRADOR','EN_REVISION','OBSERVADA','APROBADA','RECHAZADA')");
                t.HasCheckConstraint("CK_SOLICITUD_causal", "estado <> 'RECHAZADA' OR id_causal IS NOT NULL");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_solicitud").ValueGeneratedOnAdd();
            e.Property(x => x.NroSolicitud).HasColumnName("nro_solicitud").HasColumnType("varchar(15)").IsRequired();
            e.Property(x => x.IdFaena).HasColumnName("id_faena");
            e.Property(x => x.IdInstrumento).HasColumnName("id_instrumento");
            e.Property(x => x.IdIngeniero).HasColumnName("id_ingeniero");
            e.Property(x => x.MontoSolicitado).HasColumnName("monto_solicitado").HasColumnType("decimal(14,0)");
            e.Property(x => x.PlazoMeses).HasColumnName("plazo_meses");
            e.Property(x => x.FechaIngreso).HasColumnName("fecha_ingreso").HasColumnType("datetime2");
            e.Property(x => x.Estado).HasColumnName("estado").HasColumnType("varchar(12)").IsRequired();
            e.Property(x => x.IdCausal).HasColumnName("id_causal");
            e.Property(x => x.FechaDictamen).HasColumnName("fecha_dictamen").HasColumnType("datetime2");
            e.HasIndex(x => x.NroSolicitud).IsUnique();
            e.HasOne<Faena>().WithMany().HasForeignKey(x => x.IdFaena).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<InstrumentoFomento>().WithMany().HasForeignKey(x => x.IdInstrumento).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdIngeniero).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CausalRechazo>().WithMany().HasForeignKey(x => x.IdCausal).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<VersionPlantilla>(e =>
        {
            e.ToTable("PLANTILLA_CALCULO", t => t.HasCheckConstraint("CK_PLANTILLA_estado", "estado IN ('BORRADOR','VIGENTE','RETIRADA')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_plantilla").ValueGeneratedOnAdd();
            e.Property(x => x.IdInstrumento).HasColumnName("id_instrumento");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Estado).HasColumnName("estado").HasColumnType("varchar(10)").IsRequired();
            e.Property(x => x.IdAutor).HasColumnName("id_autor");
            e.Property(x => x.IdAprobador).HasColumnName("id_aprobador");
            e.Property(x => x.FechaAprobacion).HasColumnName("fecha_aprobacion").HasColumnType("datetime2");
            e.Property(x => x.ModoCalculo).HasColumnName("modo_calculo").HasColumnType("varchar(10)").IsRequired();
            e.HasIndex(x => new { x.IdInstrumento, x.Version }).IsUnique();
            e.HasOne<InstrumentoFomento>().WithMany().HasForeignKey(x => x.IdInstrumento).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdAutor).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdAprobador).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<VariablePlantilla>(e =>
        {
            e.ToTable("VARIABLE_CALCULO", t => t.HasCheckConstraint("CK_VARIABLE_origen", "origen IN ('FORMULARIO','MANTENEDOR','ROBOT')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_variable").ValueGeneratedOnAdd();
            e.Property(x => x.IdPlantilla).HasColumnName("id_plantilla");
            e.Property(x => x.Codigo).HasColumnName("codigo").HasColumnType("varchar(30)").IsRequired();
            e.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(80).IsRequired();
            e.Property(x => x.Unidad).HasColumnName("unidad").HasMaxLength(15).IsRequired();
            e.Property(x => x.Origen).HasColumnName("origen").HasColumnType("varchar(10)").IsRequired();
            e.Property(x => x.Minimo).HasColumnName("minimo").HasColumnType("decimal(18,4)");
            e.Property(x => x.Maximo).HasColumnName("maximo").HasColumnType("decimal(18,4)");
            e.Property(x => x.ValorReferencia).HasColumnName("valor_referencia").HasColumnType("decimal(18,4)");
            e.Property(x => x.Seccion).HasColumnName("seccion").HasMaxLength(60);
            e.Property(x => x.Orden).HasColumnName("orden");
            e.Property(x => x.Tipo).HasColumnName("tipo").HasColumnType("varchar(12)").IsRequired();
            e.Property(x => x.Fuente).HasColumnName("fuente").HasColumnType("varchar(40)");
            e.Property(x => x.Opciones).HasColumnName("opciones").HasMaxLength(200);
            e.HasIndex(x => new { x.IdPlantilla, x.Codigo }).IsUnique();
            e.HasOne<VersionPlantilla>().WithMany().HasForeignKey(x => x.IdPlantilla).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<FormulaPlantilla>(e =>
        {
            e.ToTable("FORMULA_CALCULO");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_formula").ValueGeneratedOnAdd();
            e.Property(x => x.IdPlantilla).HasColumnName("id_plantilla");
            e.Property(x => x.Codigo).HasColumnName("codigo").HasColumnType("varchar(30)").IsRequired();
            e.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(80).IsRequired();
            e.Property(x => x.Expresion).HasColumnName("expresion").HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.IdPlantilla, x.Codigo }).IsUnique();
            e.HasOne<VersionPlantilla>().WithMany().HasForeignKey(x => x.IdPlantilla).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<ResultadoCalculo>(e =>
        {
            e.ToTable("RESULTADO_EVALUACION");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_resultado").ValueGeneratedOnAdd();
            e.Property(x => x.IdSolicitud).HasColumnName("id_solicitud");
            e.Property(x => x.IdPlantilla).HasColumnName("id_plantilla");
            e.Property(x => x.FechaCalculo).HasColumnName("fecha_calculo").HasColumnType("datetime2");
            e.Property(x => x.Van).HasColumnName("van").HasColumnType("decimal(16,2)");
            e.HasOne<SolicitudCredito>().WithMany().HasForeignKey(x => x.IdSolicitud).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<VersionPlantilla>().WithMany().HasForeignKey(x => x.IdPlantilla).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<ValorEntrada>(e =>
        {
            e.ToTable("VALOR_ENTRADA");
            e.HasKey(x => new { x.IdResultado, x.IdVariable });
            e.Property(x => x.IdResultado).HasColumnName("id_resultado");
            e.Property(x => x.IdVariable).HasColumnName("id_variable");
            e.Property(x => x.Valor).HasColumnName("valor").HasColumnType("decimal(18,4)");
            e.HasOne<ResultadoCalculo>().WithMany().HasForeignKey(x => x.IdResultado).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<VariablePlantilla>().WithMany().HasForeignKey(x => x.IdVariable).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Aprobacion>(e =>
        {
            e.ToTable("APROBACION", t => t.HasCheckConstraint("CK_APROBACION_decision", "decision IS NULL OR decision IN ('APROBADA','RECHAZADA','OBSERVADA')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_aprobacion").ValueGeneratedOnAdd();
            e.Property(x => x.IdSolicitud).HasColumnName("id_solicitud");
            e.Property(x => x.IdUsuario).HasColumnName("id_usuario");
            e.Property(x => x.FechaAsignacion).HasColumnName("fecha_asignacion").HasColumnType("datetime2");
            e.Property(x => x.FechaDecision).HasColumnName("fecha_decision").HasColumnType("datetime2");
            e.Property(x => x.Decision).HasColumnName("decision").HasColumnType("varchar(10)");
            e.Property(x => x.Comentario).HasColumnName("comentario").HasMaxLength(500);
            e.HasOne<SolicitudCredito>().WithMany().HasForeignKey(x => x.IdSolicitud).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuario).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<ParametroEconomico>(e =>
        {
            e.ToTable("PARAMETRO_ECONOMICO", t =>
            {
                t.HasCheckConstraint("CK_PARAMETRO_valor", "valor > 0");
                t.HasCheckConstraint("CK_PARAMETRO_fuente", "fuente IN ('API_BCCH','RPA_COCHILCO','API_EXTERNA','MANUAL')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_parametro").ValueGeneratedOnAdd();
            e.Property(x => x.Fecha).HasColumnName("fecha").HasColumnType("date");
            e.Property(x => x.CodigoVariable).HasColumnName("codigo_variable").HasColumnType("varchar(30)").IsRequired();
            e.Property(x => x.Valor).HasColumnName("valor").HasColumnType("decimal(14,4)");
            e.Property(x => x.Fuente).HasColumnName("fuente").HasColumnType("varchar(15)").IsRequired();
            e.Property(x => x.FechaValor).HasColumnName("fecha_valor").HasColumnType("date");
            e.Property(x => x.IdUsuario).HasColumnName("id_usuario");
            e.HasIndex(x => new { x.Fecha, x.CodigoVariable, x.Fuente }).IsUnique();
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuario).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Auditoria>(e =>
        {
            // la tabla tiene un trigger de solo inserción (RegistroPersistencia): EF Core debe saberlo para guardar sin la cláusula OUTPUT
            e.ToTable("AUDITORIA", t => t.HasTrigger("TR_AUDITORIA_solo_insercion"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_auditoria").ValueGeneratedOnAdd();
            e.Property(x => x.Fecha).HasColumnName("fecha").HasColumnType("datetime2");
            e.Property(x => x.IdUsuario).HasColumnName("id_usuario");
            e.Property(x => x.Robot).HasColumnName("robot").HasColumnType("varchar(10)");
            e.Property(x => x.Accion).HasColumnName("accion").HasColumnType("varchar(40)").IsRequired();
            e.Property(x => x.IdSolicitud).HasColumnName("id_solicitud");
            e.Property(x => x.Detalle).HasColumnName("detalle").HasMaxLength(1000);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuario).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SolicitudCredito>().WithMany().HasForeignKey(x => x.IdSolicitud).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
