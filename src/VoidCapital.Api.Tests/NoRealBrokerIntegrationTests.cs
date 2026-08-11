using System.Reflection;

namespace VoidCapital.Api.Tests;

/// <summary>
/// Permanent safety guard (project rule): Void Capital must never contain any
/// code capable of placing real orders with a live broker. The system is
/// research + paper trading only. Any new code that talks to a brokerage
/// order API (order-placement endpoints, broker SDK clients, order service
/// keys) fails this test at build time.
/// </summary>
public class NoRealBrokerIntegrationTests
{
    private static readonly string[] ForbiddenMarkers =
    {
        // Assembled at runtime so the literals never appear in the repo and
        // cannot trip the scan itself.
        string.Concat("place", "_order"),
        string.Concat("Place", "Order"),
        string.Concat("place", "Order"),
        string.Concat("order", "_placement"),
        string.Concat("Order", "Params"),
        string.Concat("order", "Placing"),
        string.Concat("Order", "Placing"),
        string.Concat("Smart", "API"),
        string.Concat("angelbroking", "/order"),
        string.Concat("order", "Placer")
    };

    [Fact]
    public void NoSourceFileReferencesRealOrderPlacementApis()
    {
        var root = FindRepoRoot();
        var testFile = Path.GetFileName(GetType().Name) + ".cs";
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Equals(testFile, StringComparison.OrdinalIgnoreCase))
                continue;

            var content = File.ReadAllText(file);
            foreach (var marker in ForbiddenMarkers)
            {
                if (content.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    violations.Add($"{Path.GetRelativePath(root, file)}: [{marker}]");
            }
        }

        Assert.True(violations.Count == 0,
            "Real-broker order-placement code is forbidden (paper trading only). Found:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "VoidCapital.Api")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root while scanning for broker code.");
    }
}
