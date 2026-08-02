using MishuAgents.Demo.Contracts;

namespace MishuAgents.Demo.Output;

/// <summary>
/// Render del expediente final: caja fija de 76 columnas interiores, bordes
/// color cian, sin ANSI dentro del contenido (el padding tiene que cuadrar).
/// </summary>
public static class ReportFormatter
{
    const int Inner = 76;

    enum RowKind { Title, Sep, Header, Text }

    public static void Print(SynthesisReport r, string? anexo)
    {
        var rows = new List<(RowKind Kind, string Text)>
        {
            (RowKind.Title, "EXPEDIENTE DESCLASIFICADO · MAYO 2026"),
            (RowKind.Text, $"CLASIFICACIÓN: {r.Classification}"),
            (RowKind.Text, $"OPERACIÓN: {OperationBoard.OperationId}"),
        };

        rows.Add((RowKind.Sep, ""));
        rows.Add((RowKind.Header, "FUENTES"));
        foreach (var s in r.Sources)
            rows.AddRange(Wrap($"· {s}"));

        rows.Add((RowKind.Sep, ""));
        rows.Add((RowKind.Header, "HALLAZGOS"));
        foreach (var h in r.Hallazgos)
            rows.AddRange(Wrap($"· {h}"));

        rows.Add((RowKind.Sep, ""));
        rows.Add((RowKind.Header, "TRIANGULACIONES"));
        foreach (var t in r.Triangulaciones)
            rows.AddRange(Wrap($"· {t.IncidentId} · {t.When} · {t.Where} · {t.Lights} luces · {t.Quadrant} · confianza {t.Confidence:F2}"));

        rows.Add((RowKind.Sep, ""));
        rows.Add((RowKind.Header, "INFILTRACIÓN"));
        var exonerated = r.Infiltracion.Where(v => v.Reason.Contains("falso positivo")).ToArray();
        var flagged = r.Infiltracion.Where(v => v.Flagged).ToArray();
        rows.Add((RowKind.Text, $"· {r.Infiltracion.Length - 1} perfiles de «personas normales» relevados · {exonerated.Length} falso positivo (exonerado)"));
        foreach (var v in exonerated)
            rows.AddRange(Wrap($"· {v.ProfileId} {v.Person} → exonerada: la «perfección burocrática» engañó al modelo"));
        foreach (var v in flagged)
            rows.AddRange(Wrap($"· {v.ProfileId} {v.Person} · score {v.AndroidScore:F2} · ⚠ FLAG — {v.Reason}"));

        if (anexo is not null)
        {
            rows.Add((RowKind.Sep, ""));
            rows.Add((RowKind.Header, "ANEXO"));
            rows.AddRange(Wrap(anexo));
        }

        rows.Add((RowKind.Sep, ""));
        rows.Add((RowKind.Header, "CONCLUSIÓN"));
        rows.AddRange(Wrap(r.Conclusion));
        rows.Add((RowKind.Text, "· Coordinación a cargo de: [CENSURADO]"));
        rows.Add((RowKind.Text, $"· Firma: {r.Firma}"));

        Render(rows, "╔", "╠", "╚", "║");
    }

    /// <summary>Caja chica para la corrección de firma post-revelación.</summary>
    public static void PrintFirmaCorrection(string firma)
    {
        var rows = new List<(RowKind Kind, string Text)>
        {
            (RowKind.Text, "Coordinación a cargo de: [CENSURADO]  →  MISHU"),
            (RowKind.Text, $"Firma: {firma}"),
        };
        Render(rows, "┌", "├", "└", "│", title: "CORRECCIÓN DE FIRMA");
    }

    static void Render(
        List<(RowKind Kind, string Text)> rows,
        string topLeft, string sepLeft, string botLeft, string side,
        string? title = null)
    {
        var top = topLeft + new string('═', Inner + 2) + (topLeft == "╔" ? "╗" : "┐");
        var sep = sepLeft + new string('═', Inner + 2) + (sepLeft == "╠" ? "╣" : "┤");
        var bot = botLeft + new string('═', Inner + 2) + (botLeft == "╚" ? "╝" : "┘");

        ConsoleWriter.Line();
        ConsoleWriter.Raw(ConsoleWriter.Col(ConsoleWriter.Cyan, top));
        if (title is not null)
            ConsoleWriter.Raw(Row(title, RowKind.Title, side, titleColor: ConsoleWriter.Bold + ConsoleWriter.White));

        foreach (var (kind, text) in rows)
        {
            var line = kind switch
            {
                RowKind.Sep => ConsoleWriter.Col(ConsoleWriter.Cyan, sep),
                RowKind.Header => Row(text, RowKind.Header, side),
                RowKind.Title => Row(text, RowKind.Title, side),
                _ => Row(text, RowKind.Text, side),
            };
            ConsoleWriter.Raw(line);
            ConsoleWriter.Beat(18);
        }

        ConsoleWriter.Raw(ConsoleWriter.Col(ConsoleWriter.Cyan, bot));
    }

    static string Row(string text, RowKind kind, string side, string? titleColor = null)
    {
        var padded = kind switch
        {
            RowKind.Title => Center(text, Inner),
            _ => text.PadRight(Inner),
        };
        var colored = kind switch
        {
            RowKind.Header => ConsoleWriter.Col(ConsoleWriter.White, padded),
            RowKind.Title => ConsoleWriter.Col(titleColor ?? ConsoleWriter.Bold, padded),
            _ => padded,
        };
        return $"{side} {colored} {side}";
    }

    static string Center(string text, int width)
    {
        var left = Math.Max(0, (width - text.Length) / 2);
        return text.PadLeft(left + text.Length).PadRight(width);
    }

    static IEnumerable<(RowKind, string)> Wrap(string text)
    {
        if (text.Length <= Inner)
        {
            yield return (RowKind.Text, text);
            yield break;
        }

        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > Inner)
            {
                yield return (RowKind.Text, line);
                line = word;
            }
            else
            {
                line = line.Length == 0 ? word : line + " " + word;
            }
        }
        if (line.Length > 0) yield return (RowKind.Text, line);
    }
}
