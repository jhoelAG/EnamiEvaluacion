namespace Enami.Web.Servicios;

/// <summary>Mensajes que deben mostrarse después de navegar a otra página del mismo circuito (por ejemplo, al guardar una solicitud nueva).</summary>
public class AvisoPendiente
{
    private (List<string> Avisos, string Clase)? _pendiente;

    public void Dejar(List<string> avisos, string clase) => _pendiente = (avisos, clase);

    public bool Tomar(out List<string> avisos, out string clase)
    {
        avisos = _pendiente?.Avisos ?? [];
        clase = _pendiente?.Clase ?? "";
        bool hay = _pendiente is not null;
        _pendiente = null;
        return hay;
    }
}
