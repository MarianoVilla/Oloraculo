namespace Oloraculo.Web.Tests.ComboLab;

public class SportsScalpStoragePolicyTests : TestFixtures
{
    [Fact]
    public void Gitignore_BlocksLocalRawMarketDataDirectoriesAndSecretFiles()
    {
        var root = RepoRoot();
        var gitignore = File.ReadAllText(Path.Combine(root, ".gitignore"));

        Assert.DoesNotContain("*.ndjson.zst", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*.jsonl.zst", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*.parquet", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*.duckdb", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/data/", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Oloraculo.Web/Data/hot/", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Oloraculo.Web/Data/raw/", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Oloraculo.Web/Data/bronze/", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Oloraculo.Web/Data/silver/", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Oloraculo.Web/Data/gold/", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pmkey.txt", gitignore, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceOfTruth_DocumentsHotCacheOnlyAndObjectStorageArchive()
    {
        var root = RepoRoot();
        var sourceOfTruth = File.ReadAllText(Path.Combine(root, "docs", "source-of-truth", "POLYMARKET_SPORTS_SCALP_COCKPIT.md"));
        var dataPolicy = File.ReadAllText(Path.Combine(root, "docs", "source-of-truth", "DATA_AND_SECRETS.md"));

        Assert.Contains("hot cache only", sourceOfTruth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cloudflare R2", sourceOfTruth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Parquet", sourceOfTruth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DuckDB", sourceOfTruth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not persist raw full-depth books locally", sourceOfTruth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Local disk is hot cache only", dataPolicy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Permanent market-data history belongs in Oloraculo-owned Cloudflare", dataPolicy, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Oloraculo.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find Oloraculo.sln from test base directory.");
    }
}
