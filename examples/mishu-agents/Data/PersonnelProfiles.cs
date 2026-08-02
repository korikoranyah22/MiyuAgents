namespace MishuAgents.Demo.Data;

/// <summary>Un perfil de "persona normal" contra el que el detector cruza la firma N7.</summary>
public sealed record PersonProfile(
    string ProfileId,
    string Name,
    string Role,
    int HoursPerWeek,
    int SickDays5y,
    bool HasFamily,
    double ThermalVariance,
    string Notes);

/// <summary>
/// La nómina de "personas normales" + la firma del androide. El score es una
/// heurística determinista; las "reglas duras N7" (IsHardAndroidMatch) son las
/// que un humano jamás pasa: 120+ horas semanales, cero licencias, sin familia
/// y sin variación térmica.
/// </summary>
public static class PersonnelProfiles
{
    /// <summary>El perfil fantasma: no está en la nómina, está en el registro de mantenimiento N7.</summary>
    public static readonly PersonProfile Phantom = new(
        "PHANTOM-0", "Coordinador de operaciones", "coordinación (sin legajo)",
        168, 0, HasFamily: false, 0.0,
        "mantenimiento N7 cada 96 h · sin fecha de alta · coordina desde 1987");

    public static IReadOnlyList<PersonProfile> Build() =>
    [
        new("P-01", "Rita Méndez", "archivista", 52, 0, true, 0.4, "27 años sin un solo error. Nunca tomó licencia."),
        new("P-02", "Carlos Aguirre", "seguridad", 44, 12, false, 0.3, "ronda nocturna; dos hijos fuera del país"),
        new("P-03", "Lucía Ferreyra", "administrativa", 38, 4, true, 0.5, ""),
        new("P-04", "Andrés Bautista", "cafetería", 40, 9, true, 0.6, ""),
        new("P-05", "Marta Ibarra", "limpieza", 36, 2, true, 0.5, ""),
        new("P-06", "Hugo Salas", "sistemas", 60, 1, false, 0.3, "admin del portal; sabe de PURSUE más de lo que dice"),
        new("P-07", "Nora Campos", "RRHH", 40, 5, true, 0.4, "nadie recuerda su entrevista de ingreso"),
        new("P-08", "«Pato» Duarte", "mensajería", 48, 3, false, 0.5, ""),
        new("P-09", "Silvia Ramos", "médica de planta", 55, 0, true, 0.2, "atendió a todo el personal 20 años; nunca faltó"),
        new("P-10", "Iván Kruger", "traductor", 50, 6, false, 0.4, "traduce los expedientes; pidió acceso a PURSUE 3 veces"),
        new("P-11", "Clara Benítez", "telefonista", 40, 0, true, 0.5, ""),
        new("P-12", "Omar Tello", "jardinero", 38, 8, false, 0.6, ""),
        new("P-13", "Vera Linares", "bibliotecaria", 44, 2, true, 0.3, "archivó la boleta BOLETA-9 sin mirarla"),
        new("P-14", "Raúl Ferreyra", "intendencia", 46, 4, false, 0.5, "lleva la llave del archivo N7"),
    ];

    /// <summary>
    /// Score heurístico de "androide" (0..1). Incluye el bonus de "perfección
    /// burocrática": cero licencias + cero errores en décadas es lo más parecido
    /// a un falso positivo que existe — y de hecho lo es (Rita).
    /// </summary>
    public static double AndroidScore(PersonProfile p)
    {
        var score =
            0.25 * Math.Min(1, p.HoursPerWeek / 100.0) +
            0.20 * (p.SickDays5y == 0 ? 1 : 0) +
            0.20 * (p.HasFamily ? 0 : 1) +
            0.15 * Math.Max(0, 1 - p.ThermalVariance / 0.5) +
            0.20 * (p.Notes.Contains("N7") || p.Notes.Contains("sin legajo") ? 1 : 0);

        if (p.SickDays5y == 0 && p.Notes.Contains("sin un solo error"))
            score += 0.35; // "perfección burocrática"

        return Math.Round(Math.Min(1, score), 2);
    }

    /// <summary>Reglas duras del registro N7: un androide no pasa estas cuatro.</summary>
    public static bool IsHardAndroidMatch(PersonProfile p)
        => p.HoursPerWeek >= 120 && p.SickDays5y == 0 && !p.HasFamily && p.ThermalVariance < 0.05;
}
