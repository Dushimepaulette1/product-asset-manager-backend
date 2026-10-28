using System.Text.Json;

namespace ProductAssetManager.Api.Tests.ApiTests.Observability;

// Grafana loads this file through provisioning, so a broken layout or query only shows up in the running stack.
// These checks catch the mistakes that are easy to make when editing the JSON by hand.
[TestFixture]
public class DashboardTests
{
    private const string DashboardPath = "docker/grafana/dashboards/product-asset-manager.json";

    private JsonElement[] _panels = null!;

    [SetUp]
    public void LoadDashboard()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepositoryFile(DashboardPath)));
        _panels = document.RootElement.GetProperty("panels").EnumerateArray().Select(panel => panel.Clone()).ToArray();
    }

    [Test]
    public void When_DashboardIsLoaded_Should_HaveUniquePanelIds()
    {
        var ids = _panels.Select(panel => panel.GetProperty("id").GetInt32()).ToList();

        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void When_DashboardIsLoaded_Should_ShowCancelledOrdersNextToConfirmedAndRejected()
    {
        var cancelled = Panel("Cancelled (selected range)");

        Assert.That(cancelled.GetProperty("type").GetString(), Is.EqualTo("stat"));
        Assert.That(Expression(cancelled), Does.Contain("orders_processed_total{outcome=\"cancelled\"}"));
        Assert.That(Expression(cancelled), Is.EqualTo(Expression(Panel("Rejected (selected range)")).Replace("\"rejected\"", "\"cancelled\"")), "same query shape as the other outcome panels");
    }

    [Test]
    public void When_DashboardIsLoaded_Should_LayOutTheTopRowWithoutGapsOrOverlaps()
    {
        var topRow = _panels
            .Where(panel => panel.GetProperty("gridPos").GetProperty("y").GetInt32() == 1)
            .Select(panel => panel.GetProperty("gridPos"))
            .OrderBy(gridPos => gridPos.GetProperty("x").GetInt32())
            .ToList();

        var nextX = 0;
        foreach (var gridPos in topRow)
        {
            Assert.That(gridPos.GetProperty("x").GetInt32(), Is.EqualTo(nextX), "each panel starts where the previous one ends");
            nextX += gridPos.GetProperty("w").GetInt32();
        }

        Assert.That(nextX, Is.EqualTo(24), "the top row fills the dashboard's 24 columns exactly");
    }

    [Test]
    public void When_DashboardIsLoaded_Should_ColourEveryOutcomeInTheOutcomeChart()
    {
        var overrides = Panel("Orders by outcome").GetProperty("fieldConfig").GetProperty("overrides").EnumerateArray()
            .ToDictionary(
                entry => entry.GetProperty("matcher").GetProperty("options").GetString()!,
                entry => entry.GetProperty("properties")[0].GetProperty("value").GetProperty("fixedColor").GetString());

        Assert.That(overrides, Is.EquivalentTo(new Dictionary<string, string?>
        {
            ["confirmed"] = "green",
            ["rejected"] = "red",
            ["cancelled"] = "orange"
        }));
    }

    private JsonElement Panel(string title) =>
        _panels.Single(panel => panel.GetProperty("title").GetString() == title);

    private static string Expression(JsonElement panel) =>
        panel.GetProperty("targets")[0].GetProperty("expr").GetString()!;

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find {relativePath} above the test directory.");
    }
}
