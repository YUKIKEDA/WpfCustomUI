using CaeStudio.Application;
using CaeStudio.Domain.Models;
using CaeStudio.Infrastructure;
using System.IO;

namespace CaeStudio.Tests.Infrastructure;

public sealed class ProjectPersistenceTests
{
    [Fact]
    public async Task RoundTrip_PreservesPlateWithHoleProject()
    {
        var original = ProjectTemplates.CreatePlateWithHole(tension: 120.0) with
        {
            Name = "永続化テスト",
            Solver = new SolverSettings { Tolerance = 1e-10, MaxIterations = 5000, ModeCount = 4 },
        };

        var path = Path.Combine(Path.GetTempPath(), $"caestudio-{Guid.NewGuid():N}.wcuproj");
        try
        {
            var repository = new JsonProjectRepository();
            await repository.SaveAsync(original, path);
            var loaded = await repository.LoadAsync(path);

            Assert.Equal(original.Name, loaded.Name);
            Assert.Equal(original.AnalysisType, loaded.AnalysisType);
            Assert.Equal(original.Material.Name, loaded.Material.Name);
            Assert.Equal(original.Solver.Tolerance, loaded.Solver.Tolerance);
            Assert.Equal(original.Solver.MaxIterations, loaded.Solver.MaxIterations);
            Assert.Equal(original.Solver.ModeCount, loaded.Solver.ModeCount);

            var plate = Assert.IsType<PlateWithHoleGeometry>(loaded.Geometry);
            var source = Assert.IsType<PlateWithHoleGeometry>(original.Geometry);
            Assert.Equal(source.Width, plate.Width);
            Assert.Equal(source.HoleDiameter, plate.HoleDiameter);
            Assert.Equal(source.RadialDivisions, plate.RadialDivisions);

            Assert.Equal(original.BoundaryConditions.Count, loaded.BoundaryConditions.Count);
            Assert.Contains(loaded.BoundaryConditions, bc => bc.IsLoadEditable && bc.TractionX == 120.0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RoundTrip_PreservesCantileverModalProject()
    {
        var original = ProjectTemplates.CreateCantileverPlate(AnalysisType.Modal);

        var path = Path.Combine(Path.GetTempPath(), $"caestudio-{Guid.NewGuid():N}.wcuproj");
        try
        {
            var repository = new JsonProjectRepository();
            await repository.SaveAsync(original, path);
            var loaded = await repository.LoadAsync(path);

            Assert.Equal(AnalysisType.Modal, loaded.AnalysisType);
            Assert.IsType<CantileverPlateGeometry>(loaded.Geometry);
            Assert.Contains(loaded.BoundaryConditions, bc => bc.Constraint != ConstraintKind.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_RoundTrip_PersistsThemeAndRecentFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"caestudio-settings-{Guid.NewGuid():N}.json");
        try
        {
            var service = new JsonSettingsService(path);
            service.Update(s => (s with
            {
                Theme = "Light",
                RunGesture = "Ctrl+R",
                DefaultProjectDirectory = @"D:\projects",
            }).WithRecentFile(@"D:\a.wcuproj").WithRecentFile(@"D:\b.wcuproj"));

            var reloaded = new JsonSettingsService(path);
            Assert.Equal("Light", reloaded.Current.Theme);
            Assert.Equal("Ctrl+R", reloaded.Current.RunGesture);
            Assert.Equal(@"D:\projects", reloaded.Current.DefaultProjectDirectory);
            Assert.Equal([@"D:\b.wcuproj", @"D:\a.wcuproj"], reloaded.Current.RecentFiles);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
