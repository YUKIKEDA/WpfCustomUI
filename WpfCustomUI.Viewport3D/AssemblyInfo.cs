using System.Windows;
using System.Windows.Markup;

[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

// コアと同じ URI に載せることで、利用側は xmlns:ui 1 本でビューポートも参照できる
[assembly: XmlnsDefinition("https://schemas.wpfcustomui.dev/xaml", "WpfCustomUI.Viewport3D")]
