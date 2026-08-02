using MishuAgents.Demo.Contracts;

namespace MishuAgents.Demo.Data;

/// <summary>
/// Catálogo de triangulaciones que el Trazador cruza con los hallazgos del
/// analista. El patrón se repite desde 1972: tres luces, cuadrante inferior
/// derecho, "objeto físico masivo" — la anomalía de la Apollo 17 a la cabeza.
/// </summary>
public static class TriangleCatalog
{
    public static readonly TriangleSighting[] Catalog =
    [
        new("APOLLO-17", "diciembre de 1972", "cielo lunar", 3, "cuadrante inferior derecho",
            "objeto físico masivo reportado por la tripulación; tres puntos de luz en formación", 0.98),
        new("PURSUE-7", "marzo de 1981", "valle de La Rioja", 3, "cuadrante inferior derecho",
            "tres firmas radar equidistantes, sin emisión de motor, patrón de tres tonos", 0.91),
        new("BOLETA-9", "julio de 1999", "corredor aéreo sur", 3, "cuadrante inferior derecho",
            "formación triangular seguida por un orbe que se unió a la formación", 0.87),
        new("TRK-2023", "octubre de 2023", "Patagonia norte", 3, "cuadrante inferior derecho",
            "el cuadrante reaparece en 9 de cada 10 avistamientos triangulares", 0.94),
    ];
}
