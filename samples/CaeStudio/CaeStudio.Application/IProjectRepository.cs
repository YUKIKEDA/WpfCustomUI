using CaeStudio.Domain.Models;

namespace CaeStudio.Application;

/// <summary>
/// プロジェクトファイルの永続化(Infrastructure が JSON で実装する。spec 6.26.6)。
/// 入力のみを保存し、結果は保存しない(入力から決定的に再現できるため)。
/// </summary>
public interface IProjectRepository
{
    /// <summary>プロジェクトをファイルへ保存する。</summary>
    Task SaveAsync(CaeProjectData project, string filePath, CancellationToken cancellationToken = default);

    /// <summary>プロジェクトをファイルから読み込む。</summary>
    Task<CaeProjectData> LoadAsync(string filePath, CancellationToken cancellationToken = default);
}
