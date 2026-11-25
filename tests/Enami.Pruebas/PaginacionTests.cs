using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Historial sin el tope de 500: la auditoría se recorre por páginas y la bandeja entrega todas las evaluaciones (y la exportación también, porque usa la misma lista).</summary>
public class PaginacionTests
{
    [Fact]
    public void La_auditoria_se_recorre_por_paginas_con_los_mas_recientes_primero()
    {
        var e = new Entorno();
        for (int i = 1; i <= 620; i++) e.Auditoria.Registrar("PRUEBA_PAGINA", e.IdAdmin, null, $"registro {i}");

        var p1 = e.Auditoria.Paginar(null, null, "PRUEBA_PAGINA", null, null, pagina: 1, tamanoPagina: 50);
        Assert.Equal(620, p1.Total);
        Assert.Equal(13, p1.Paginas);
        Assert.Equal(50, p1.Filas.Count);
        Assert.Equal("registro 620", p1.Filas[0].Detalle);

        var ultima = e.Auditoria.Paginar(null, null, "PRUEBA_PAGINA", null, null, pagina: 13, tamanoPagina: 50);
        Assert.Equal(20, ultima.Filas.Count);
        Assert.Equal("registro 1", ultima.Filas[^1].Detalle);   // el registro más antiguo, que con el tope de 500 no se veía

        // una página fuera de rango se ajusta a la última; sin coincidencias hay una sola página vacía
        Assert.Equal(13, e.Auditoria.Paginar(null, null, "PRUEBA_PAGINA", null, null, pagina: 99, tamanoPagina: 50).Pagina);
        var vacia = e.Auditoria.Paginar(null, null, "NO_EXISTE", null, null, pagina: 3);
        Assert.Equal(0, vacia.Total);
        Assert.Equal(1, vacia.Pagina);
    }

    [Fact]
    public void La_bandeja_incluye_mas_de_500_evaluaciones()
    {
        var e = new Entorno();
        for (int i = 1; i <= 520; i++)
            e.Db.Agregar(new SolicitudCredito { NroSolicitud = $"COP-2026-{i:0000}", IdFaena = e.IdFaenaElegible, IdInstrumento = 1,
                IdIngeniero = e.IdIngeniero, MontoSolicitado = 10_000, PlazoMeses = 12, FechaIngreso = DateTime.Today });

        Assert.Equal(520, e.Solicitudes.Listar(e.Sesion(e.IdJefatura)).Count);
    }
}
