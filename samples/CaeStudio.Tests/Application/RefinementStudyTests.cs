using CaeStudio.Application;
using CaeStudio.Domain.Models;

namespace CaeStudio.Tests.Application;

public sealed class RefinementStudyTests
{
    [Fact]
    public void ScaleDivisions_RespectsMinimums()
    {
        var plate = new PlateWithHoleGeometry { RadialDivisions = 4, AngularDivisions = 8 };
        var scaled = Assert.IsType<PlateWithHoleGeometry>(
            RefinementStudy.ScaleDivisions(plate, 0.1));
        Assert.Equal(4, scaled.RadialDivisions);
        Assert.Equal(8, scaled.AngularDivisions);

        var beam = new CantileverPlateGeometry { DivisionsX = 2, DivisionsY = 2 };
        var scaledBeam = Assert.IsType<CantileverPlateGeometry>(
            RefinementStudy.ScaleDivisions(beam, 0.1));
        Assert.Equal(2, scaledBeam.DivisionsX);
        Assert.Equal(2, scaledBeam.DivisionsY);
    }

    [Fact]
    public void Run_ProducesMonotonicDofGrowthForPlate()
    {
        var project = ProjectTemplates.CreatePlateWithHole() with
        {
            Geometry = new PlateWithHoleGeometry
            {
                RadialDivisions = 6,
                AngularDivisions = 16,
            },
            Solver = new SolverSettings { Tolerance = 1e-8, MaxIterations = 20_000 },
        };

        var points = new List<StudyPoint>();
        RefinementStudy.Run(project, [0.5, 1.0, 1.5], points.Add);

        Assert.Equal(3, points.Count);
        Assert.True(points[0].Dofs < points[1].Dofs);
        Assert.True(points[1].Dofs < points[2].Dofs);
        Assert.All(points, p => Assert.True(p.Metric > 0));
    }

    [Fact]
    public void MetricLabel_DependsOnAnalysisType()
    {
        Assert.Contains("Mises", RefinementStudy.MetricLabel(AnalysisType.Static));
        Assert.Contains("Hz", RefinementStudy.MetricLabel(AnalysisType.Modal));
    }
}
