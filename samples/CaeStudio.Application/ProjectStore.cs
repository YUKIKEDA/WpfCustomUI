using CaeStudio.Domain.Models;
using R3;

namespace CaeStudio.Application;

/// <summary>
/// 現在のプロジェクト(入力一式)の単一情報源。
/// 入力は不変レコードなので、編集は Update による差し替え+ダーティフラグで表現する。
/// </summary>
public sealed class ProjectStore : IDisposable
{
    private readonly ReactiveProperty<CaeProjectData> _current;
    private readonly ReactiveProperty<string?> _filePath = new(null);
    private readonly ReactiveProperty<bool> _isDirty = new(false);

    public ProjectStore(CaeProjectData initial)
    {
        _current = new ReactiveProperty<CaeProjectData>(initial);
    }

    /// <summary>現在のプロジェクト入力。</summary>
    public ReadOnlyReactiveProperty<CaeProjectData> Current => _current;

    /// <summary>保存先パス(未保存なら null)。</summary>
    public ReadOnlyReactiveProperty<string?> FilePath => _filePath;

    /// <summary>未保存の変更があるか。</summary>
    public ReadOnlyReactiveProperty<bool> IsDirty => _isDirty;

    /// <summary>入力を編集する(不変レコードの差し替え)。ダーティになる。</summary>
    public void Update(Func<CaeProjectData, CaeProjectData> mutate)
    {
        var updated = mutate(_current.Value);
        if (!ReferenceEquals(updated, _current.Value))
        {
            _current.Value = updated;
            _isDirty.Value = true;
        }
    }

    /// <summary>新規プロジェクトに置き換える(テンプレート適用/ファイルを開く)。</summary>
    public void Replace(CaeProjectData project, string? filePath)
    {
        _current.Value = project;
        _filePath.Value = filePath;
        _isDirty.Value = false;
    }

    /// <summary>保存完了を記録する。</summary>
    public void MarkSaved(string filePath)
    {
        _filePath.Value = filePath;
        _isDirty.Value = false;
    }

    public void Dispose()
    {
        _current.Dispose();
        _filePath.Dispose();
        _isDirty.Dispose();
    }
}
