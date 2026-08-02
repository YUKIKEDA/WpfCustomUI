using System.Windows.Media;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Controls.Tests.Media;

/// <summary>アイコン辞書の整合性検証(spec 6.27.5)。</summary>
public class WcuIconsTests
{
    [Fact]
    public void Names_ContainsAtLeast60Icons()
    {
        Assert.True(WcuIcons.Names.Count >= 60,
            $"アイコン数が想定より少ない: {WcuIcons.Names.Count}");
    }

    [Fact]
    public void AllIcons_ParseToNonEmptyFrozenGeometry()
    {
        foreach (var name in WcuIcons.Names)
        {
            var geometry = WcuIcons.Get(name);
            Assert.True(geometry.IsFrozen, $"{name}: Frozen でない");
            Assert.False(geometry.Bounds.IsEmpty, $"{name}: 空の Geometry");
        }
    }

    [Fact]
    public void AllIcons_FitWithin16x16Canvas()
    {
        foreach (var name in WcuIcons.Names)
        {
            var bounds = WcuIcons.Get(name).Bounds;
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0
                && bounds.Right <= WcuIcons.CanvasSize && bounds.Bottom <= WcuIcons.CanvasSize,
                $"{name}: 16x16 キャンバス外にはみ出している ({bounds})");
        }
    }

    [Fact]
    public void AllIcons_HaveReasonableSize()
    {
        // 小さすぎるアイコン(データ誤り)を検出する。純粋な区切り線などは幅か高さの片方が大きい
        foreach (var name in WcuIcons.Names)
        {
            var bounds = WcuIcons.Get(name).Bounds;
            var maxExtent = Math.Max(bounds.Width, bounds.Height);
            Assert.True(maxExtent >= 8, $"{name}: 小さすぎる ({bounds})");
        }
    }

    [Fact]
    public void Get_SameInstanceIsCached()
    {
        var first = WcuIcons.Get("Save");
        var second = WcuIcons.Get("Save");
        Assert.Same(first, second);
        Assert.Same(first, WcuIcons.Save);
    }

    [Fact]
    public void TryGet_UnknownName_ReturnsFalse()
    {
        Assert.False(WcuIcons.TryGet("NoSuchIcon", out _));
        Assert.Throws<KeyNotFoundException>(() => WcuIcons.Get("NoSuchIcon"));
    }

    [Fact]
    public void CreateResourceDictionary_ContainsAllIconsWithPrefixedKeys()
    {
        var dictionary = WcuIcons.CreateResourceDictionary();
        Assert.Equal(WcuIcons.Names.Count, dictionary.Count);
        foreach (var name in WcuIcons.Names)
        {
            Assert.True(dictionary.Contains($"Wcu.Icon.{name}"), $"Wcu.Icon.{name} が見つからない");
            Assert.IsAssignableFrom<Geometry>(dictionary[$"Wcu.Icon.{name}"]);
        }
    }
}
