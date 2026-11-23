namespace Enami.Dominio.Calculo;

/// <summary>
/// Plantilla inicial para probar la plataforma. Desde el Sprint 4 calcula en dólares (US$), como la planilla de evaluación de ENAMI;
/// el dólar observado solo se usa para mostrar los montos en pesos como referencia.
/// IMPORTANTE: las fórmulas y los valores son de EJEMPLO y están simplificados.
/// Deben reemplazarse por las fórmulas reales de la planilla Excel de ENAMI desde el editor de fórmulas,
/// y validarse comparando resultados con la planilla (prueba de regresión del motor financiero).
/// </summary>
public static class PlantillaEjemplo
{
    public static PlantillaCalculo Crear(int id = 1) => new()
    {
        Id = id,
        Nombre = "Capital de trabajo (ejemplo)",
        Instrumento = "Capital de trabajo",
        Version = 1,
        Estado = EstadoPlantilla.Vigente,
        Autor = "Sistema",
        AprobadaPor = "Sistema (plantilla inicial)",
        FechaAprobacion = DateTime.Now,
        Comentario = "Plantilla de ejemplo con valores ilustrativos; reemplazar por el modelo de ENAMI.",
        Variables =
        [
            new() { Seccion = "Datos del proyecto", Fuente = "RECEPCION.TMS_MES", Codigo = "TonelajeMensual", Nombre = "Tonelaje mensual", Unidad = "t/mes", Origen = OrigenVariable.Formulario, ValorPorDefecto = 300, Minimo = 1, Maximo = 100_000 },
            new() { Seccion = "Datos del proyecto", Fuente = "RECEPCION.LEY_CU", Codigo = "LeyCu", Nombre = "Ley de cobre", Unidad = "%", Origen = OrigenVariable.Formulario, ValorPorDefecto = 2.5, Minimo = 0.01, Maximo = 99.99 },
            new() { Seccion = "Datos del proyecto", Codigo = "Recuperacion", Nombre = "Recuperación metalúrgica", Unidad = "%", Origen = OrigenVariable.Formulario, ValorPorDefecto = 85, Minimo = 1, Maximo = 100 },
            new() { Seccion = "Datos del proyecto", Fuente = "FAENA.DISTANCIA", Codigo = "DistanciaKm", Nombre = "Distancia al poder de compra", Unidad = "km", Origen = OrigenVariable.Formulario, ValorPorDefecto = 60, Minimo = 0.1, Maximo = 999.99 },
            new() { Seccion = "Datos del proyecto", Codigo = "CostoMinaTon", Nombre = "Costo de extracción por tonelada", Unidad = "US$/t", Origen = OrigenVariable.Formulario, ValorPorDefecto = 95, Minimo = 0 },
            new() { Codigo = "Inversion", Nombre = "Inversión inicial (monto del crédito)", Unidad = "US$", Origen = OrigenVariable.Formulario, ValorPorDefecto = 65_000, Minimo = 1 },
            new() { Codigo = "PlazoMeses", Nombre = "Plazo de evaluación", Unidad = "meses", Origen = OrigenVariable.Formulario, ValorPorDefecto = 24, Minimo = 1, Maximo = 120 },
            new() { Codigo = "TarifaFleteTonKm", Nombre = "Tarifa de flete", Unidad = "US$/t·km", Origen = OrigenVariable.Mantenedor, ValorPorDefecto = 0.13, Minimo = 0 },
            new() { Codigo = "FactorPago", Nombre = "Factor de pago del poder de compra", Unidad = "%", Origen = OrigenVariable.Mantenedor, ValorPorDefecto = 60, Minimo = 0, Maximo = 100 },
            new() { Codigo = "TasaDescuentoAnual", Nombre = "Tasa de descuento anual", Unidad = "%", Origen = OrigenVariable.Mantenedor, ValorPorDefecto = 12, Minimo = 0, Maximo = 100 },
            new() { Codigo = "PrecioCuLb", Nombre = "Precio del cobre", Unidad = "US$/lb", Origen = OrigenVariable.Robot, ValorPorDefecto = 4.30, Minimo = 0.01 },
            new() { Codigo = "Dolar", Nombre = "Dólar observado", Unidad = "CLP/US$", Origen = OrigenVariable.Robot, ValorPorDefecto = 940, Minimo = 1 },
        ],
        Formulas =
        [
            new() { Codigo = "CobreFinoLb", Nombre = "Cobre fino mensual", Expresion = "TonelajeMensual * LeyCu / 100 * Recuperacion / 100 * 2204,62", Descripcion = "Libras de cobre fino recuperado por mes (1 t = 2.204,62 lb)." },
            new() { Codigo = "IngresoMensual", Nombre = "Ingreso mensual", Expresion = "CobreFinoLb * PrecioCuLb * FactorPago / 100", Descripcion = "Pago estimado por el mineral entregado, en US$." },
            new() { Codigo = "CostoFlete", Nombre = "Costo de flete", Expresion = "TonelajeMensual * DistanciaKm * TarifaFleteTonKm", Descripcion = "Transporte del mineral al poder de compra." },
            new() { Codigo = "CostoOperacion", Nombre = "Costo de operación", Expresion = "TonelajeMensual * CostoMinaTon", Descripcion = "Extracción, explosivos, combustible, agua y personal." },
            new() { Codigo = "FlujoPeriodo", Nombre = "Flujo de caja del período", Expresion = "IngresoMensual - CostoFlete - CostoOperacion", Descripcion = "Obligatoria: flujo de caja de cada mes." },
            new() { Codigo = "CuotaCredito", Nombre = "Cuota mensual equivalente del crédito", Expresion = "-PAGO(TasaPeriodo; PlazoMeses; Inversion)", Descripcion = "Informativa: cuota que pagaría el crédito con la tasa de descuento (función PAGO, igual que en Excel)." },
            new() { Codigo = "TasaPeriodo", Nombre = "Tasa de descuento mensual", Expresion = "(1 + TasaDescuentoAnual / 100) ^ (1 / 12) - 1", Descripcion = "Obligatoria: tasa anual convertida a mensual." },
        ]
    };
}
