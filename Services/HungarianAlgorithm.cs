using System;
using System.Collections.Generic;
namespace ObjectDetectTrackApp.Services
{
    public class HungarianAlgorithm
    {
        public static List<(int detection, int track)> FindMatches(float[,] costs, float maxCost = float.MaxValue)
        {
            int n = costs.GetLength(0), real = costs.GetLength(1), m = real + n;
            var result = new List<(int, int)>();
            if (n == 0 || real == 0) return result;
            double limit = maxCost == float.MaxValue ? 1e12 : maxCost;
            double dummy = limit + 1e-6;
            var u = new double[n + 1]; var v = new double[m + 1];
            var p = new int[m + 1]; var way = new int[m + 1];
            for (int i = 1; i <= n; i++)
            {
                p[0] = i; int j0 = 0;
                var min = new double[m + 1]; Array.Fill(min, double.PositiveInfinity);
                var used = new bool[m + 1];
                do
                {
                    used[j0] = true; int i0 = p[j0], j1 = 0; double delta = double.PositiveInfinity;
                    for (int j = 1; j <= m; j++) if (!used[j])
                    {
                        double c = j <= real ? costs[i0 - 1, j - 1] : dummy;
                        if (double.IsNaN(c) || c > limit) c = dummy + 1e6;
                        double cur = c - u[i0] - v[j];
                        if (cur < min[j]) { min[j] = cur; way[j] = j0; }
                        if (min[j] < delta) { delta = min[j]; j1 = j; }
                    }
                    for (int j = 0; j <= m; j++)
                        if (used[j]) { u[p[j]] += delta; v[j] -= delta; } else min[j] -= delta;
                    j0 = j1;
                } while (p[j0] != 0);
                do { int j1 = way[j0]; p[j0] = p[j1]; j0 = j1; } while (j0 != 0);
            }
            for (int j = 1; j <= real; j++)
                if (p[j] != 0 && costs[p[j]-1,j-1] <= limit) result.Add((p[j]-1,j-1));
            return result;
        }
    }
}
