using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Broiler.Media.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<ValueTask> Body)>();
        CatalogTests.Register(tests);
        ArchitectureTests.Register(tests);

        int passed = 0;
        var failures = new List<string>();
        Console.WriteLine($"Running {tests.Count} media foundation test(s)...\n");

        foreach ((string name, Func<ValueTask> body) in tests)
        {
            try
            {
                await body().ConfigureAwait(false);
                passed++;
                Console.WriteLine($"  [PASS] {name}");
            }
            catch (Exception ex)
            {
                failures.Add(name);
                Console.WriteLine($"  [FAIL] {name}");
                Console.WriteLine($"         {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{passed}/{tests.Count} passed, {failures.Count} failed.");
        return failures.Count;
    }
}

