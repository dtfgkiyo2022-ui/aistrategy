using System.Globalization;
using System.Text;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Headless.Cli;

/// <summary>
/// Draws a generated map as text and reports the fairness numbers V3-1 only measures (technical-design-v3 2.2):
/// resources near each core and walking distances. One character per 2 m cell, north at the top.
/// </summary>
internal static class GenMapCommand
{
    internal static int Run(Dictionary<string, string> options)
    {
        ulong seed = ulong.Parse(options.TryGetValue("--seed", out var value) ? value : throw new InvalidDataException("Missing --seed."), CultureInfo.InvariantCulture);
        var scenario = MapGenerator.Generate(seed);
        string text = Describe(scenario);
        if (options.TryGetValue("--out", out var path)) File.WriteAllText(path, text, new UTF8Encoding(false));
        else Console.Write(text);
        return 0;
    }

    internal static string Describe(ScenarioDefinition s)
    {
        var map = s.Map;
        int columns = map.WidthCells, rows = map.HeightCells, size = map.CellSizeMeters;
        var blocked = new bool[columns * rows];
        foreach (int id in map.BlockedCellIds) blocked[id] = true;
        var marks = new char[columns * rows];
        for (int i = 0; i < marks.Length; i++) marks[i] = blocked[i] ? '#' : '.';
        int Cell(SimPoint p) => (int)(p.Z.Raw / 65536 / size) * columns + (int)(p.X.Raw / 65536 / size);
        foreach (var n in s.ResourceNodes) marks[Cell(n.Position)] = n.Kind == ResourceKind.Wood ? 'w' : 'f';
        foreach (var d in s.Soldiers) marks[Cell(d.Position)] = d.FactionId == 1 ? '1' : '2';
        marks[Cell(s.Outposts[0].Position)] = 'N';
        marks[Cell(s.Outposts[1].Position)] = 'S';
        marks[Cell(s.Cores[0].Position)] = 'W';
        marks[Cell(s.Cores[1].Position)] = 'E';

        var b = new StringBuilder();
        b.AppendLine(s.ScenarioId + "  (" + MapGenerator.Version + ", " + columns + "x" + rows + " cells of " + size + " m)");
        b.AppendLine("W/E cores, N/S outposts, w wood, f food, 1/2 soldiers, # blocked");
        for (int z = rows - 1; z >= 0; z--)
        {
            for (int x = 0; x < columns; x++) b.Append(marks[z * columns + x]);
            b.AppendLine();
        }
        b.AppendLine("blocked cells: " + map.BlockedCellIds.Length + " / " + blocked.Length
            + " (" + (map.BlockedCellIds.Length * 1000 / blocked.Length) + " permille)");
        var fromWest = Distances(blocked, columns, rows, Cell(s.Cores[0].Position));
        var fromEast = Distances(blocked, columns, rows, Cell(s.Cores[1].Position));
        b.AppendLine("walk (cells): core to core " + fromWest[Cell(s.Cores[1].Position)]
            + " | west to N " + fromWest[Cell(s.Outposts[0].Position)] + ", S " + fromWest[Cell(s.Outposts[1].Position)]
            + " | east to N " + fromEast[Cell(s.Outposts[0].Position)] + ", S " + fromEast[Cell(s.Outposts[1].Position)]);
        for (int f = 0; f < 2; f++)
        {
            var core = s.Cores[f].Position;
            int wood = 0, food = 0, woodAmount = 0, foodAmount = 0;
            foreach (var n in s.ResourceNodes)
            {
                long dx = (n.Position.X.Raw - core.X.Raw) / 65536, dz = (n.Position.Z.Raw - core.Z.Raw) / 65536;
                if (dx * dx + dz * dz > 30 * 30) continue;
                if (n.Kind == ResourceKind.Wood) { wood++; woodAmount += n.Amount; } else { food++; foodAmount += n.Amount; }
            }
            b.AppendLine((f == 0 ? "west" : "east") + " core within 30 m: wood " + wood + " (" + woodAmount + "), food " + food + " (" + foodAmount + ")");
        }
        return b.ToString();
    }

    /// <summary>Breadth-first walking distance in cells; -1 where unreachable.</summary>
    private static int[] Distances(bool[] blocked, int columns, int rows, int start)
    {
        var distance = new int[blocked.Length];
        Array.Fill(distance, -1);
        var queue = new Queue<int>();
        distance[start] = 0; queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue(), x = cell % columns, z = cell / columns;
            foreach (int next in new[] { z + 1 < rows ? cell + columns : -1, x + 1 < columns ? cell + 1 : -1, z > 0 ? cell - columns : -1, x > 0 ? cell - 1 : -1 })
            {
                if (next < 0 || blocked[next] || distance[next] >= 0) continue;
                distance[next] = distance[cell] + 1;
                queue.Enqueue(next);
            }
        }
        return distance;
    }
}
