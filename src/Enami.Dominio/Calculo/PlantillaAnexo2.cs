namespace Enami.Dominio.Calculo;

/// <summary>
/// Plantilla «Anexo 2 – Evaluación estimada previa al proyecto minero», armada con la planilla
/// Ev.Proyectos_Reconocimiento_Minerales_enero_2026_ejemplo.xlsx que entregó ENAMI (hojas DE, anexo 1 y 3, anexos 2 y 4, tarifa).
/// <para>
/// Todo es configurable desde el editor de fórmulas: secciones y campos del formulario, de dónde se precarga cada campo,
/// la tarifa de compra (como fórmulas y parámetros) y el cálculo de la recuperación y del VAN.
/// Los valores por defecto son los del ejemplo de ENAMI, así que con ellos se obtiene lo mismo que la planilla:
/// recuperación US$ 161.450, VAN US$ 7,68 millones, precio crítico 372 y precio mínimo de retorno 488 US$/oz.
/// </para>
/// Simplificaciones respecto de la planilla (por validar con ENAMI):
/// <list type="bullet">
/// <item>solo están las tarifas de mineral de oro y de cobre por flotación (la planilla trae además lixiviación, SX-EW y plantas experimentales);</item>
/// <item>los costos «Otros» y «Maquila» se suman por tonelada (la planilla los convierte en % de las ventas al precio proyectado);</item>
/// <item>el precio proyectado es un parámetro; la planilla lo calcula con los escenarios pesimistas ponderados por año.</item>
/// </list>
/// </summary>
public static class PlantillaAnexo2
{
    public const string SeccionAntecedentes = "Antecedentes del proyecto";
    public const string SeccionSolicitante = "Empresa solicitante y faena";
    public const string SeccionDeudas = "Deudas con ENAMI";
    public const string SeccionRecursos = "Recursos minerales";
    public const string SeccionProductor = "Producción del productor (todas sus faenas)";
    public const string SeccionProduccion = "Producción de la faena y explotación";
    public const string SeccionCostos = "Costos proyectados";
    public const string SeccionFinanciamiento = "Condiciones del financiamiento";
    public const string SeccionPresupuesto = "Presupuesto (Anexo 1)";
    public const string SeccionMercado = "Mercado";
    public const string SeccionTarifa = "Tarifa de compra ENAMI 2026";

    public static PlantillaCalculo Crear(int id = 0)
    {
        int orden = 0;
        VariableCalculo F(string seccion, string codigo, string nombre, string unidad, double valor, double? min = 0, double? max = null,
                          string? fuente = null, string tipo = TiposCampo.Numero, string? opciones = null) => new()
        {
            Seccion = seccion, Orden = ++orden, Codigo = codigo, Nombre = nombre, Unidad = unidad, Origen = OrigenVariable.Formulario,
            ValorPorDefecto = valor, Minimo = tipo == TiposCampo.Numero ? min : null, Maximo = max, Fuente = fuente, Tipo = tipo, Opciones = opciones
        };
        VariableCalculo T(string seccion, string codigo, string nombre, string tipo = TiposCampo.Texto, string? fuente = null) =>
            F(seccion, codigo, nombre, "", 0, null, null, fuente, tipo);
        VariableCalculo M(string seccion, string codigo, string nombre, string unidad, double valor, OrigenVariable origen = OrigenVariable.Mantenedor) => new()
        {
            Seccion = seccion, Orden = ++orden, Codigo = codigo, Nombre = nombre, Unidad = unidad, Origen = origen, ValorPorDefecto = valor, Minimo = 0
        };

        return new PlantillaCalculo
        {
            Id = id,
            Nombre = "Anexo 2 – Evaluación estimada previa (reconocimiento de reservas)",
            Instrumento = "Reconocimiento de Recursos y/o Reservas",
            Modo = ModoCalculo.Escenarios,
            Estado = EstadoPlantilla.Vigente,
            Autor = "Equipo de proyecto",
            Variables =
            [
                T(SeccionAntecedentes, "ResenaProyecto", "Breve reseña del proyecto", TiposCampo.TextoLargo),
                F(SeccionAntecedentes, "ManoObra", "Mano de obra estable en el proyecto", "N°", 0, 0, fuente: "FAENA.MANO_OBRA"),
                T(SeccionAntecedentes, "ProfesionalApoyo", "Profesional de apoyo de ENAMI", fuente: "USUARIO.NOMBRE"),
                T(SeccionAntecedentes, "FechaComite", "Fecha de presentación al comité"),
                F(SeccionAntecedentes, "PlazoEscrituracion", "Plazo de escrituración", "meses", 1, 0),
                F(SeccionAntecedentes, "PresentaProgramasRR", "¿Presenta programas de reconocimiento en los últimos 4 años?", "", 0,
                  tipo: TiposCampo.Seleccion, opciones: "0=No;1=Sí"),
                T(SeccionAntecedentes, "ProgramasAnteriores", "Programas ejecutados en años anteriores (tipo, servicio y % de cumplimiento)", TiposCampo.TextoLargo),

                T(SeccionSolicitante, "Direccion", "Dirección del solicitante", fuente: "PRODUCTOR.DIRECCION"),
                T(SeccionSolicitante, "ComunaSolicitante", "Comuna y región del solicitante", fuente: "PRODUCTOR.COMUNA"),
                T(SeccionSolicitante, "Telefono", "Teléfono", fuente: "PRODUCTOR.TELEFONO"),
                T(SeccionSolicitante, "Correo", "Correo electrónico", fuente: "PRODUCTOR.CORREO"),
                T(SeccionSolicitante, "RepresentanteLegal", "Representante legal", fuente: "PRODUCTOR.REPRESENTANTE"),
                T(SeccionSolicitante, "RutRepresentante", "RUT del representante legal"),
                T(SeccionSolicitante, "UbicacionFaena", "Ubicación de la faena", fuente: "FAENA.UBICACION"),
                T(SeccionSolicitante, "ComunaFaena", "Comuna y región de la faena", fuente: "FAENA.COMUNA"),
                F(SeccionSolicitante, "CoordenadaNorte", "Coordenada norte de la faena (UTM)", "m", 0, 0, fuente: "FAENA.NORTE"),
                F(SeccionSolicitante, "CoordenadaEste", "Coordenada este de la faena (UTM)", "m", 0, 0, fuente: "FAENA.ESTE"),

                F(SeccionDeudas, "DeudaTotal", "Deuda total", "US$", 0),
                F(SeccionDeudas, "DeudaAmortizacion", "Amortización", "US$/mes", 0),
                F(SeccionDeudas, "DeudaIntereses", "Intereses", "US$/mes", 0),
                F(SeccionDeudas, "DeudaVencida", "Deuda vencida", "US$", 0),

                F(SeccionRecursos, "RecursosHipoteticos", "Recursos hipotéticos", "t", 0),
                F(SeccionRecursos, "RecursosInferidos", "Recursos inferidos", "t", 0, fuente: "RECURSO.INFERIDO"),
                F(SeccionRecursos, "RecursosIndicados", "Recursos indicados", "t", 45_311, fuente: "RECURSO.INDICADO"),
                F(SeccionRecursos, "RecursosMedidos", "Recursos medidos", "t", 0, fuente: "RECURSO.MEDIDO"),
                F(SeccionRecursos, "LeyCu", "Ley geológica de cobre", "%", 0.87, 0, 99.99, "RECURSO.LEY_CU"),
                F(SeccionRecursos, "LeyAu", "Ley geológica de oro", "g/t", 7.58, fuente: "RECURSO.LEY_AU"),
                F(SeccionRecursos, "LeyAg", "Ley geológica de plata", "g/t", 14.79, fuente: "RECURSO.LEY_AG"),

                F(SeccionProductor, "ProduccionProductor", "Producción promedio del productor", "TMS/mes", 0, fuente: "PRODUCTOR.TMS_MES"),
                F(SeccionProductor, "LeyCuProductor", "Ley de cobre", "%", 0, 0, 99.99, "PRODUCTOR.LEY_CU"),
                F(SeccionProductor, "LeyAuProductor", "Ley de oro", "g/t", 0, fuente: "PRODUCTOR.LEY_AU"),
                F(SeccionProductor, "LeyAgProductor", "Ley de plata", "g/t", 0, fuente: "PRODUCTOR.LEY_AG"),

                F(SeccionProduccion, "Producto", "Tipo de producto (tarifa)", "", 3, tipo: TiposCampo.Seleccion,
                  opciones: "1=Mineral de cobre por flotación;3=Mineral de oro por flotación"),
                F(SeccionProduccion, "ProduccionMensual", "Producción de la faena", "TMS/mes", 300, 0.01, fuente: "RECEPCION.TMS_MES"),
                F(SeccionProduccion, "MesesConEntregas", "Meses con entregas (últimos 6)", "N°", 6, 0, 6, "RECEPCION.MESES"),
                F(SeccionProduccion, "RecuperacionMinera", "Recuperación minera", "%", 85, 0, 100),
                F(SeccionProduccion, "EscogidoCancha", "Escogido en cancha", "%", 80, 0, 100),
                F(SeccionProduccion, "Dilucion", "Dilución", "%", 5, 0, 100),
                F(SeccionProduccion, "PeriodoGracia", "Período de gracia", "meses", 0, 0),

                F(SeccionCostos, "CostoPerforacion", "Perforación y tronadura", "US$/TMS", 32.58),
                F(SeccionCostos, "CostoCarguio", "Carguío y transporte", "US$/TMS", 16.86),
                F(SeccionCostos, "CostoAdministracion", "Administración mina", "US$/TMS", 9.73),
                F(SeccionCostos, "CostoFlete", "Flete de mineral", "US$/TMS", 2.43),
                F(SeccionCostos, "CostoOtros", "Otros costos no operacionales", "US$/TMS", 0),
                F(SeccionCostos, "CostoMaquila", "Maquila planta", "US$/TMS", 0),
                F(SeccionCostos, "RegaliaMinera", "Regalía minera (fracción de las ventas)", "fracción", 0.22, 0, 1),
                F(SeccionCostos, "ImpuestoSonami", "SONAMI e impuesto (fracción de las ventas)", "fracción", 0.004, 0, 1),

                F(SeccionFinanciamiento, "InteresAnual", "Interés anual del crédito", "%", 9.21, 0, 100),
                F(SeccionFinanciamiento, "ProvisionMinima", "Provisión mínima (0 a 15 % de las ventas)", "fracción", 0.05, 0, 0.15),

                F(SeccionPresupuesto, "CapitalRiesgo", "Aporte ENAMI en capital de riesgo", "US$", 131_900, fuente: "PRESUPUESTO.CAPITAL_RIESGO"),
                F(SeccionPresupuesto, "CreditoDesarrollo", "Crédito para el desarrollo", "US$", 29_550, fuente: "PRESUPUESTO.CREDITO"),
                F(SeccionPresupuesto, "AporteBeneficiario", "Aporte del beneficiario", "US$", 31_614, fuente: "PRESUPUESTO.APORTE_PROPIO"),

                M(SeccionMercado, "PrecioAu", "Precio del oro (proyectado)", "US$/oz", 3_224.5, OrigenVariable.Robot),
                M(SeccionMercado, "PrecioCuLb", "Precio del cobre", "US$/lb", 4.043, OrigenVariable.Robot),
                M(SeccionMercado, "PrecioAg", "Precio de la plata", "US$/oz", 39.4, OrigenVariable.Robot),
                M(SeccionMercado, "Dolar", "Dólar observado", "CLP/US$", 907.13, OrigenVariable.Robot),
                M(SeccionMercado, "TasaDescuentoAnual", "Tasa de descuento anual", "%", 12),
                M(SeccionMercado, "PasoPrecio", "Paso entre precios de la tabla de escenarios (US$/oz o ¢US$/lb)", "", 20),
                M(SeccionMercado, "ImpuestoTimbres", "Impuesto de timbres y estampillas", "%", 0.8),

                M(SeccionTarifa, "AuCargoRefino", "Oro: cargo de refino", "US$/oz", 6),
                M(SeccionTarifa, "AuLeyBase", "Oro: ley de producto", "g/t", 50),
                M(SeccionTarifa, "AuDescuento", "Oro: descuento metalúrgico", "fracción", 0.04),
                M(SeccionTarifa, "AuCargoFusion", "Oro: cargo de fusión", "US$/t", 54.89),
                M(SeccionTarifa, "AuRecuperacion", "Oro: recuperación de pago", "fracción", 0.6742),
                M(SeccionTarifa, "AuCargoFlotacion", "Oro: cargo de flotación", "US$/TMS", 40.19),
                M(SeccionTarifa, "AuSubproducto", "Oro como subproducto: pago", "fracción", 0.6),
                M(SeccionTarifa, "CuCargoRefino", "Cobre: cargo de refino", "¢US$/lb", 5.49),
                M(SeccionTarifa, "CuLeyBase", "Cobre: ley de concentrado", "%", 26.21),
                M(SeccionTarifa, "CuDescuento", "Cobre: descuento metalúrgico", "fracción", 0.038),
                M(SeccionTarifa, "CuCargoFusion", "Cobre: cargo de fusión", "US$/t", 54.89),
                M(SeccionTarifa, "CuRecuperacion", "Cobre: recuperación de pago", "fracción", 0.9098),
                M(SeccionTarifa, "CuCargoFlotacion", "Cobre: cargo de flotación", "US$/TMS", 39.92),
                M(SeccionTarifa, "CuSubproducto", "Cobre como subproducto: pago", "fracción", 0.75),
                M(SeccionTarifa, "AgCargoRefino", "Plata: cargo de refino", "US$/oz", 0.365),
                M(SeccionTarifa, "AgDescuento", "Plata: descuento metalúrgico", "fracción", 0.04),
                M(SeccionTarifa, "AgSubproducto", "Plata como subproducto: pago", "fracción", 0.6),
                M(SeccionTarifa, "CargoStock", "Cargo de stock", "US$/TMS", 2.73),
                M(SeccionTarifa, "CargoPlanCierre", "Cargo plan de cierre", "US$/TMS", 0.35),
            ],
            Formulas =
            [
                new() { Codigo = "RecursosMineros", Nombre = "Recursos mineros (indicados + medidos)", Expresion = "RecursosIndicados + RecursosMedidos" },
                new() { Codigo = "Reservas", Nombre = "Reservas mineras (probables y probadas)", Expresion = "RecursosMineros * RecuperacionMinera / 100 * (1 + Dilucion / 100) * EscogidoCancha / 100" },
                new() { Codigo = "FactorLey", Nombre = "Factor de ley de producción", Expresion = "SI(EscogidoCancha = 0; 0; 1 / (1 + Dilucion / 100) * (1 - (1 - EscogidoCancha / 100) / 5) / (EscogidoCancha / 100))" },
                new() { Codigo = "LeyCuProd", Nombre = "Ley de producción de cobre (%)", Expresion = "LeyCu * FactorLey" },
                new() { Codigo = "LeyAuProd", Nombre = "Ley de producción de oro (g/t)", Expresion = "LeyAu * FactorLey" },
                new() { Codigo = "LeyAgProd", Nombre = "Ley de producción de plata (g/t)", Expresion = "LeyAg * FactorLey" },
                new() { Codigo = "Horizonte", Nombre = "Horizonte de reservas (meses)", Expresion = "SI(ProduccionMensual = 0; 0; Reservas / ProduccionMensual)" },
                new() { Codigo = "PeriodoPago", Nombre = "Período de pago máximo (meses)", Expresion = "MIN(Horizonte; 60)" },
                new() { Codigo = "CostoMina", Nombre = "Costo total mina, incluye flete (US$/TMS)", Expresion = "CostoPerforacion + CostoCarguio + CostoAdministracion + CostoFlete" },
                new() { Codigo = "InteresMensual", Nombre = "Interés mensual", Expresion = "InteresAnual / 12 / 100" },
                new() { Codigo = "Financiamiento", Nombre = "Financiamiento ENAMI (US$)", Expresion = "CapitalRiesgo + CreditoDesarrollo" },
                new() { Codigo = "PrecioProyectado", Nombre = "Precio proyectado", Expresion = "SI(Producto = 3; PrecioAu; PrecioCuLb * 100)" },
                new() { Codigo = "PagoCuSub", Nombre = "Pago por cada 1 % de cobre como subproducto (US$/TMS)", Expresion = "22.046223 * ((PrecioCuLb * 100 - CuCargoRefino) / 100 * (1 - CuDescuento) * CuSubproducto)" },
                new() { Codigo = "PagoAuSub", Nombre = "Pago por cada g/t de oro como subproducto (US$/TMS)", Expresion = "(PrecioAu - AuCargoRefino) / 31.10348 * (1 - AuDescuento) * AuSubproducto" },
                new() { Codigo = "PagoAgSub", Nombre = "Pago por cada g/t de plata como subproducto (US$/TMS)", Expresion = "(PrecioAg - AgCargoRefino) / 31.10348 * (1 - AgDescuento) * AgSubproducto" },
                new() { Codigo = "IngresoOro", Nombre = "Tarifa mineral de oro por flotación (US$/TMS)", Expresion =
                    "SI(LeyAuProd < 0.15; 0; ((Precio - AuCargoRefino) / 31.10348 * (1 - AuDescuento) * AuLeyBase - AuCargoFusion) * AuRecuperacion * LeyAuProd / AuLeyBase - AuCargoFlotacion - CargoStock - CargoPlanCierre)" +
                    " + SI(LeyAgProd < 20; 0; (LeyAgProd - 5) * PagoAgSub) + LeyCuProd * PagoCuSub" },
                new() { Codigo = "IngresoCobre", Nombre = "Tarifa mineral de cobre por flotación (US$/TMS)", Expresion =
                    "(22.046223 * (Precio - CuCargoRefino) / 100 * CuLeyBase * (1 - CuDescuento) - CuCargoFusion) * CuRecuperacion * LeyCuProd / CuLeyBase - CuCargoFlotacion - CargoStock - CargoPlanCierre" +
                    " + SI(LeyAuProd < 0.15; 0; (LeyAuProd - 0.15) * PagoAuSub) + SI(LeyAgProd - 5 < 15; 0; (LeyAgProd - 5) * PagoAgSub)" },
                new() { Codigo = "Ingreso", Nombre = "Ingreso por venta (US$/TMS)", Expresion = "SI(Producto = 3; IngresoOro; IngresoCobre)" },
                new() { Codigo = "CostoTotal", Nombre = "Costo (US$/TMS)", Expresion = "CostoMina + CostoOtros + CostoMaquila + Ingreso * (RegaliaMinera + ImpuestoSonami)" },
                new() { Codigo = "Utilidad", Nombre = "Utilidad (US$/TMS)", Expresion = "Ingreso - CostoTotal" },
                new() { Codigo = "Excedente", Nombre = "Excedente (US$/TMS)", Expresion = "Utilidad - ProvisionMinima * Ingreso" },
                new() { Codigo = "CapacidadPago", Nombre = "Capacidad de pago en el plazo máximo (US$)", Expresion = "MAX(0; -VA(InteresMensual; PeriodoPago; Excedente * ProduccionMensual))" },
                new() { Codigo = "TasaDevolucion", Nombre = "Tasa de devolución (% de los ingresos)", Expresion = "SI(Ingreso = 0; 0; MIN(MAX(TRUNCAR(Excedente / Ingreso; 2); 0.03); 0.2))" },
                new() { Codigo = "Recuperacion", Nombre = "Recuperación de capitales (US$)", Expresion = "MIN(CapacidadPago; Financiamiento)" },
                new() { Codigo = "SaldoFinanciamiento", Nombre = "Financiamiento menos capacidad de pago (US$)", Expresion = "Financiamiento + VA(InteresMensual; PeriodoPago; Excedente * ProduccionMensual)" },
                new() { Codigo = "VanProyecto", Nombre = "VAN del proyecto (US$)", Expresion = "-VA(TasaDescuentoAnual / 100 / 12; Horizonte; Utilidad * ProduccionMensual) - Recuperacion - AporteBeneficiario" },
            ]
        };
    }
}
