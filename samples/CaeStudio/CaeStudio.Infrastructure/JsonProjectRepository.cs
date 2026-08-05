using CaeStudio.Application;
using CaeStudio.Domain.Models;
using System.Text.Json;

namespace CaeStudio.Infrastructure;

/// <summary>
/// <see cref="IProjectRepository"/> の JSON 実装(.wcuproj)。
/// 入力のみを保存し、結果は保存しない(入力から決定的に再現できるため。spec 6.26.6)。
/// </summary>
public sealed class JsonProjectRepository : IProjectRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task SaveAsync(
        CaeProjectData project, string filePath, CancellationToken cancellationToken = default)
    {
        var model = ProjectFileModel.FromDomain(project);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, model, Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CaeProjectData> LoadAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var model = await JsonSerializer.DeserializeAsync<ProjectFileModel>(
            stream, Options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("プロジェクトファイルが空です。");
        return model.ToDomain();
    }
}
