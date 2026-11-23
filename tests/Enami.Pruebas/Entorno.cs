using System.Security.Cryptography;
using System.Text;
using Enami.Dominio.Calculo;
using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Hash simple para las pruebas (la aplicación usa el PasswordHasher de ASP.NET Core Identity).</summary>
public class HasherPrueba : IHasherClaves
{
    public string Calcular(string clave)
    {
        var sal = RandomNumberGenerator.GetBytes(8);
        return Convert.ToHexString(sal) + ":" + Convert.ToHexString(SHA256.HashData([.. sal, .. Encoding.UTF8.GetBytes(clave)]));
    }

    public bool Verificar(string hash, string clave)
    {
        var partes = hash.Split(':');
        if (partes.Length != 2) return false;
        var sal = Convert.FromHexString(partes[0]);
        return partes[1] == Convert.ToHexString(SHA256.HashData([.. sal, .. Encoding.UTF8.GetBytes(clave)]));
    }
}

/// <summary>
/// Entorno de prueba: almacén en memoria con la carga inicial (catálogos, administrador, usuarios de demostración,
/// fórmulas y parámetros) y un productor con tres faenas: una elegible, una inactiva y una con el padrón vencido.
/// </summary>
public class Entorno
{
    public const string Clave = "Prueba.2026";
    public readonly AlmacenMemoria Db = new();
    public readonly ServicioAuditoria Auditoria;
    public readonly ServicioUsuarios Usuarios;
    public readonly RepositorioPlantillas Plantillas;
    public readonly RepositorioParametros Parametros;
    public readonly ServicioFichas Fichas;
    public readonly ServicioSolicitudes Solicitudes;
    public readonly int IdAdmin, IdIngeniero, IdJefatura, IdJefeCopiapo, IdCopiapo;
    public readonly int IdFaenaElegible, IdFaenaInactiva, IdFaenaPadronVencido;

    public Entorno()
    {
        var hasher = new HasherPrueba();
        Auditoria = new ServicioAuditoria(Db);
        Usuarios = new ServicioUsuarios(Db, hasher, Auditoria);
        Plantillas = new RepositorioPlantillas(Db, Auditoria);
        Parametros = new RepositorioParametros(Db);
        Fichas = new ServicioFichas(Db, Auditoria);
        Solicitudes = new ServicioSolicitudes(Db, Plantillas, Parametros, Fichas, Auditoria);
        new Semilla(Db, hasher).Ejecutar(new OpcionesSemilla { ClaveAdministrador = Clave, UsuariosDemostracion = true });

        int Id(string correo) => Db.Tabla<Usuario>().Single(u => u.Correo == correo).Id;
        IdAdmin = Id("admin@enami.cl");
        IdIngeniero = Id("ingeniero.copiapo@enami.cl");
        IdJefatura = Id("jefatura@enami.cl");
        IdJefeCopiapo = Id("jefe.copiapo@enami.cl");
        IdCopiapo = Db.Tabla<Oficina>().Single(o => o.Nombre == "Oficina Minera Copiapó").Id;

        var comuna = new Comuna { Nombre = "Copiapó", Region = "Atacama" };
        Db.Agregar(comuna);
        var p = new Productor { Rut = "12345678-5", TipoProductor = TiposProductor.Natural, Nombres = "Juan", Apellidos = "Pérez Soto", IdComuna = comuna.Id };
        Db.Agregar(p);
        var vigente = DateTime.Today.AddYears(1);
        IdFaenaElegible = AgregarFaena("12396", "San José", p.Id, EstadosFaena.Activa, vigente, 60);
        IdFaenaInactiva = AgregarFaena("20781", "Anamaría", p.Id, EstadosFaena.Inactiva, vigente, null);
        IdFaenaPadronVencido = AgregarFaena("30000", "La Vieja", p.Id, EstadosFaena.Activa, DateTime.Today.AddDays(-1), null);

        int AgregarFaena(string codigo, string nombre, int idProductor, string estado, DateTime venc, decimal? km)
        {
            var f = new Faena { Codigo = codigo, Nombre = nombre, IdProductor = idProductor, IdComuna = comuna.Id, IdOficina = IdCopiapo,
                                Estado = estado, NroPadron = "P-" + codigo, VencimientoPadron = venc, DistanciaPoderCompraKm = km };
            Db.Agregar(f);
            return f.Id;
        }
    }

    public UsuarioSesion Sesion(int idUsuario) => Usuarios.Sesion(idUsuario)!;

    /// <summary>Datos completos de una solicitud con los valores de referencia de la plantilla.</summary>
    public DatosSolicitud DatosCompletos(int idFaena, decimal monto = 65_000, short plazo = 24)
    {
        var plantilla = Solicitudes.PlantillaPara(1)!;
        var d = new DatosSolicitud { IdFaena = idFaena, IdInstrumento = 1, MontoSolicitado = monto, PlazoMeses = plazo };
        foreach (var v in ServicioSolicitudes.VariablesProyecto(plantilla)) d.DatosProyecto[v.Codigo] = v.ValorPorDefecto;
        return d;
    }
}
