namespace Oloraculo.Web.Tests;

public class SourceRegressionTests
{
    [Fact]
    public void PredictionDeletionFeatureIsNotExposed()
    {
        var root = RepositoryRoot();
        var matchesPage = File.ReadAllText(Path.Combine(root.FullName, "Oloraculo.Web", "Components", "Pages", "Matches.razor"));
        var snapshotService = File.ReadAllText(Path.Combine(root.FullName, "Oloraculo.Web", "Services", "SnapshotService.cs"));

        Assert.DoesNotContain("Borrar predicción", matchesPage);
        Assert.DoesNotContain("DeletePrediction", matchesPage);
        Assert.DoesNotContain("DeleteMatchSnapshotsAsync", snapshotService);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Oloraculo.Web", "Oloraculo.Web.csproj")))
                return directory;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
