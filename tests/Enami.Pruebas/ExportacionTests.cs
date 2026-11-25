using Enami.Dominio.Exportacion;
using Enami.Dominio.Importacion;
using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Exportación del historial a Excel (RF-29): el archivo se vuelve a leer con el mismo lector de planillas del proyecto.</summary>
public class ExportacionTests
{
    [Fact]
    public void El_historial_exportado_se_abre_como_planilla_con_las_evaluaciones_que_ve_el_usuario()
    {
        var e = new Entorno();
        var a = new ServicioAprobaciones(e.Db, e.Solicitudes, e.Auditoria);
        var causal = e.Db.Tabla<CausalRechazo>().OrderBy(c => c.Id).First();
        long id = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible, 65_000), e.Sesion(e.IdIngeniero)).IdSolicitud;
        a.Enviar(id, e.Sesion(e.IdIngeniero));
        a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Rechazar, "Leyes bajo el mínimo.", causal.Id);
        e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible, 30_000), e.Sesion(e.IdIngeniero));

        var archivo = new ServicioExportacion(e.Db, e.Solicitudes, e.Auditoria).Evaluaciones(e.Sesion(e.IdIngeniero), null, null);

        var ruta = Path.Combine(Path.GetTempPath(), $"exportacion-{Guid.NewGuid()}.xlsx");
        try
        {
            File.WriteAllBytes(ruta, archivo);
            var filas = LectorXlsx.LeerFilas(ruta).Select(f => f.Valores).ToList();
            Assert.Equal(2, filas.Count);
            Assert.Equal(ServicioExportacion.Columnas, filas[0].Keys.ToArray());
            var rechazada = filas.Single(f => f["Estado"] == "Rechazada");
            Assert.Equal("65000", rechazada["Monto solicitado (US$)"]);
            Assert.Equal($"{causal.Codigo} · {causal.Descripcion}", rechazada["Causal de rechazo"]);
            Assert.Equal("Juan Pérez Soto", rechazada["Productor"]);
            // las fechas quedan como fechas de Excel (número de serie), no como texto
            Assert.True(double.TryParse(rechazada["Fecha del dictamen"], System.Globalization.CultureInfo.InvariantCulture, out var serie));
            Assert.Equal(DateTime.Today, DateTime.FromOADate(serie).Date);
            Assert.Equal("", filas.Single(f => f["Estado"] == "Borrador")["Fecha del dictamen"]);
        }
        finally { File.Delete(ruta); }

        Assert.Contains(e.Db.Tabla<Auditoria>(), x => x.Accion == "EXPORTACION" && x.IdUsuario == e.IdIngeniero);
    }

    [Fact]
    public void Las_columnas_se_nombran_como_en_Excel()
    {
        Assert.Equal(new[] { "A", "Z", "AA", "AZ", "BA" }, new[] { 0, 25, 26, 51, 52 }.Select(EscritorXlsx.Columna));
    }
}
