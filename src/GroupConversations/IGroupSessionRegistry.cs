namespace MiyuAgents.GroupConversations;

/// <summary>
/// Registro de "sesiones vivas" indexadas por id (típicamente un orquestador de
/// conversación grupal o de sueño por conversación), con <b>expiración por
/// inactividad</b>.  Reemplaza el patrón ad-hoc de un
/// <c>ConcurrentDictionary&lt;string, …&gt;.GetOrAdd</c> que crece sin techo: una
/// sesión que no se toca en <c>idleTtl</c> se puede barrer.
///
/// Distinción central:
/// <list type="bullet">
///   <item><see cref="Remove"/> — <b>cierre limpio</b> (la conversación terminó
///   normalmente). No dispara el callback de eviction.</item>
///   <item><see cref="SweepExpiredAsync"/> — <b>eviction por inactividad</b>
///   (la sesión quedó huérfana). Dispara el callback de finalización, que el
///   consumidor usa para, p.ej., persistir un sueño que nunca despertó.</item>
/// </list>
///
/// Toda lectura (<see cref="GetOrCreate"/>, <see cref="TryGet"/>) "toca" la
/// sesión: actualiza su último acceso para que no la barra el próximo sweep.
/// </summary>
/// <typeparam name="TSession">El objeto de sesión (orquestador, wrapper, etc.).</typeparam>
public interface IGroupSessionRegistry<TSession> where TSession : class
{
    /// <summary>Devuelve la sesión existente o la crea con <paramref name="factory"/>. Toca el último acceso.</summary>
    TSession GetOrCreate(string sessionId, Func<string, TSession> factory);

    /// <summary>Devuelve la sesión si existe (y la toca); si no, <c>false</c>.</summary>
    bool TryGet(string sessionId, out TSession? session);

    /// <summary>Cierre limpio: quita la sesión sin tratarla como huérfana. No dispara el callback de eviction.</summary>
    bool Remove(string sessionId);

    /// <summary>
    /// Barre las sesiones inactivas por más de <c>idleTtl</c>, disparando el
    /// callback de eviction por cada una. Devuelve cuántas barrió.
    /// </summary>
    ValueTask<int> SweepExpiredAsync(CancellationToken ct = default);

    /// <summary>Cantidad de sesiones vivas.</summary>
    int Count { get; }

    /// <summary>Ids de las sesiones vivas (snapshot).</summary>
    IReadOnlyCollection<string> ActiveSessionIds { get; }
}
