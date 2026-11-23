using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Enami.Datos;

/// <summary>
/// Permite usar «dotnet ef migrations add» y «dotnet ef database update» desde este proyecto.
/// La cadena se puede cambiar con la variable de entorno ENAMI_SQL.
/// </summary>
public class FabricaDisenoContexto : IDesignTimeDbContextFactory<EnamiDbContext>
{
    public EnamiDbContext CreateDbContext(string[] args)
    {
        var cadena = Environment.GetEnvironmentVariable("ENAMI_SQL")
                     ?? @"Server=(localdb)\MSSQLLocalDB;Database=EnamiEvaluacion;Trusted_Connection=True;TrustServerCertificate=True";
        var opciones = new DbContextOptionsBuilder<EnamiDbContext>().UseSqlServer(cadena).Options;
        return new EnamiDbContext(opciones);
    }
}
