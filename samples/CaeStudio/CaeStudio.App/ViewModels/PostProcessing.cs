using CaeStudio.Domain.Models;
using CaeStudio.Domain.Solving;
using WpfCustomUI.Charts;

namespace CaeStudio.App.ViewModels;

/// <summary>パスプロット 1 枚分のデータ(FEM 値+厳密解曲線)。</summary>
public sealed record PathPlotData(
    double[] FemX, double[] FemY,
    double[] ExactX, double[] ExactY,
    string XLabel, string YLabel,
    string FemName, string ExactName);

/// <summary>解析結果 → チャート用データの変換(純関数、VM から分離してテスト可能に)。</summary>
public static class PostProcessing
{
    /// <summary>
    /// 円孔付き平板: y 軸(θ=90°)に沿った von Mises 分布を Kirsch 厳密解と比較する
    /// パスプロット。プレート以外のテンプレートでは null。
    /// </summary>
    public static PathPlotData? CreateKirschPath(CaeProjectData project, StaticResult result)
    {
        if (project.Geometry is not PlateWithHoleGeometry plate ||
            !result.Mesh.Groups.TryGetValue(ProjectTemplates.Groups.YAxis, out var axis))
        {
            return null;
        }

        var holeRadius = plate.HoleDiameter / 2.0;
        var tension = Math.Abs(project.BoundaryConditions
            .FirstOrDefault(bc => bc.GroupName == ProjectTemplates.Groups.RightEdge)?.TractionX ?? 0.0);

        // x=0 の正側レイ(y > 0)を孔縁→外周の順に
        var nodes = axis.Nodes
            .Where(n => result.Mesh.Positions[n * 2 + 1] > holeRadius * 0.999)
            .OrderBy(n => result.Mesh.Positions[n * 2 + 1])
            .ToArray();
        if (nodes.Length < 2)
        {
            return null;
        }

        var femX = new double[nodes.Length];
        var femY = new double[nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
        {
            femX[i] = result.Mesh.Positions[nodes[i] * 2 + 1] / holeRadius; // r/a
            femY[i] = result.NodalVonMises[nodes[i]];
        }

        const int samples = 200;
        var maxR = femX[^1];
        var exactX = new double[samples];
        var exactY = new double[samples];
        for (var i = 0; i < samples; i++)
        {
            var r = 1.0 + (maxR - 1.0) * i / (samples - 1);
            exactX[i] = r;
            exactY[i] = ExactSolutions.KirschVonMises(holeRadius, tension, r * holeRadius, Math.PI / 2.0);
        }

        return new PathPlotData(
            femX, femY, exactX, exactY,
            XLabel: "r / a  (path: hole edge -> boundary, theta = 90 deg)",
            YLabel: "von Mises [MPa]",
            FemName: "FEM",
            ExactName: "Kirsch (infinite plate)");
    }

    /// <summary>
    /// モード重ね合わせによる周波数応答(レセプタンス |H| と位相)。
    /// 応答点は 1 次モードの最大振幅 DOF、モード質量は 1(質量正規化済み)。
    /// </summary>
    public static FrequencyResponseSeries CreateFrf(ModalResult result, double dampingRatio = 0.02)
    {
        // 応答 DOF: 1 次モードで振幅最大の自由度
        var firstShape = result.Modes[0].Shape;
        var responseDof = 0;
        for (var dof = 1; dof < firstShape.Length; dof++)
        {
            if (Math.Abs(firstShape[dof]) > Math.Abs(firstShape[responseDof]))
            {
                responseDof = dof;
            }
        }

        var fMin = result.Modes[0].FrequencyHz / 4.0;
        var fMax = result.Modes[^1].FrequencyHz * 2.0;
        const int points = 400;

        var frequencies = new double[points];
        var magnitudes = new double[points];
        var phases = new double[points];

        for (var i = 0; i < points; i++)
        {
            var f = fMin * Math.Pow(fMax / fMin, (double)i / (points - 1));
            var omega = 2.0 * Math.PI * f;

            var (re, im) = (0.0, 0.0);
            foreach (var mode in result.Modes)
            {
                var omegaI = 2.0 * Math.PI * mode.FrequencyHz;
                var phi = mode.Shape[responseDof];

                // H += φ² / (ωᵢ² − ω² + 2jζωᵢω)
                var denominatorRe = omegaI * omegaI - omega * omega;
                var denominatorIm = 2.0 * dampingRatio * omegaI * omega;
                var denominatorAbs2 = denominatorRe * denominatorRe + denominatorIm * denominatorIm;
                re += phi * phi * denominatorRe / denominatorAbs2;
                im += phi * phi * -denominatorIm / denominatorAbs2;
            }

            frequencies[i] = f;
            magnitudes[i] = Math.Sqrt(re * re + im * im);
            phases[i] = Math.Atan2(im, re) * 180.0 / Math.PI;
        }

        return new FrequencyResponseSeries
        {
            Name = "Receptance (drive point)",
            Frequencies = frequencies,
            Magnitudes = magnitudes,
            Phases = phases,
        };
    }
}
