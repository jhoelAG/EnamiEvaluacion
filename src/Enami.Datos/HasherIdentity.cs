using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;
using Microsoft.AspNetCore.Identity;

namespace Enami.Datos;

/// <summary>
/// Hash de contraseñas con el PasswordHasher de ASP.NET Core Identity (PBKDF2 con HMAC-SHA512,
/// sal aleatoria y 100.000 iteraciones). El formato incluye la versión, así que se puede endurecer sin perder las claves existentes.
/// </summary>
public class HasherIdentity : IHasherClaves
{
    private readonly PasswordHasher<Usuario> _hasher = new();

    public string Calcular(string clave) => _hasher.HashPassword(null!, clave);

    public bool Verificar(string hash, string clave)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        try { return _hasher.VerifyHashedPassword(null!, hash, clave) != PasswordVerificationResult.Failed; }
        catch (FormatException) { return false; }
    }
}
