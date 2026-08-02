using MiyuAgents.Memory;

namespace MishuAgents.Demo.Data;

/// <summary>Un fragmento desclasificado del portal WAR.GOV/UFO / sistema PURSUE.</summary>
public sealed record ExpedienteFragment(string Id, string Source, string Classification, string Body);

/// <summary>
/// Chunk de memoria declarativa: un fragmento ya sedimentado en el archivo
/// (el tipo que almacena el InMemoryStore del framework).
/// </summary>
public sealed record FragmentChunk(string Id, string Source, string Classification, string Text, string[] Entities)
{
    public static readonly FragmentChunk None = new("", "", "", "", []);
    public bool IsEmpty => Id.Length == 0;
}

/// <summary>
/// Query del archivo declarativo: cumple el contrato IInMemoryQuery de
/// InMemoryStore (búsqueda por entidad, primer hit).
/// </summary>
public sealed class FragmentQuery(string keyword) : IInMemoryQuery<FragmentChunk>
{
    public FragmentChunk Search(IReadOnlyList<FragmentChunk> entries)
        => entries.FirstOrDefault(e => e.Entities.Contains(keyword, StringComparer.OrdinalIgnoreCase))
           ?? FragmentChunk.None;
}

/// <summary>
/// Los 162 expedientes del operativo: fragmentos deterministas (sin RNG) con
/// tachaduras [CENSURADO] y vocabulario del dominio. EX-0042 es la "tachadura
/// crítica": ilegible sin autorización → dispara el replan del PlanExecuteStrategy.
/// </summary>
public static class ExpedienteArchive
{
    public const int Total = 162;
    public const string CriticalFragment = "EX-0042";

    static readonly string[] Sources =
    [
        "portal WAR.GOV/UFO",
        "sistema PURSUE",
        "apéndice Apollo 17 · dic 1972",
        "archivo BOLETA-9",
        "memorando interno N7",
    ];

    static readonly string[] Classifications = ["CONFIDENCIAL", "RESERVADO", "ULTRA", "RESTRINGIDO"];

    static readonly string[] Bodies =
    [
        "El sistema PURSUE registró una firma anómala a las 03:12. La señal duró 41 segundos y desapareció detrás de la [CENSURADO]. No se reportó ruido de motor.",
        "Tres luces en formación triangular sobrevolando el valle. El informe de la tripulación habla de un «objeto físico masivo». Cuadrante de ingreso: inferior derecho.",
        "Personal de enlace: perfiles sin legajo aparecen en el registro N7. Se les programó mantenimiento cada 96 horas. No figura fecha de alta.",
        "La transcripción de la Apollo 17, diciembre de 1972, menciona tres puntos de luz en el cuadrante inferior derecho del cielo lunar. El comandante los describió como [CENSURADO].",
        "Boleta-9: interferencia de radio en 1420 MHz coincidente con la formación triangular. El audio contiene un patrón repetitivo de tres tonos.",
        "El portal WAR.GOV/UFO liberó este fragmento con 11 tachaduras. La versión completa permanece en [CENSURADO] bajo custodia del sistema PURSUE.",
        "Un orbe acompañó a la formación triangular durante 12 minutos y luego se unió a ella. Los tres puntos de luz pasaron a ser cuatro.",
        "Registro de mantenimiento N7: todo en orden. El operador no duerme, no transpira y no figura en la nómina. Años de servicio: 38.",
        "El sistema PURSUE clasificó la señal como «objeto físico masivo» y ordenó [CENSURADO]. La orden lleva la firma del coordinador de operaciones.",
        "El cuadrante inferior derecho reaparece en 9 de cada 10 avistamientos triangulares. La geometría es idéntica a la de la Apollo 17.",
        "La persona del archivo se reportó como «normal»: 168 turnos semanales, cero licencias, sin familia registrada. El registro biométrico no muestra variación térmica.",
        "Fragmento recuperado del portal WAR.GOV/UFO tras la desclasificación de mayo 2026: la operación fue coordinada por un perfil sin legajo. El resto es [CENSURADO].",
        "La formación triangular fue reportada por 27 testigos independientes. Ninguno pudo describir el sonido. Todos describieron el mismo cuadrante.",
        "El sistema PURSUE mantiene 41 expedientes con tachaduras críticas. El acceso requiere autorización del coordinador de operaciones, perfil sin legajo N7.",
    ];

    public static IReadOnlyList<ExpedienteFragment> Build()
    {
        var list = new List<ExpedienteFragment>(Total);
        for (var i = 1; i <= Total; i++)
        {
            var id = $"EX-{i:D4}";
            var body = id == CriticalFragment
                ? "Tachadura crítica: el contenido de este expediente fue [CENSURADO] en su totalidad por orden del sistema PURSUE. Sin autorización, el fragmento es ilegible."
                : Bodies[i % Bodies.Length];
            list.Add(new ExpedienteFragment(id, Sources[i % Sources.Length], Classifications[i % Classifications.Length], body));
        }
        return list;
    }
}
