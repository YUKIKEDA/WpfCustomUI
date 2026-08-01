using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;
using Silk.NET.Direct3D9;
using Silk.NET.DXGI;
using Blend = Silk.NET.Direct3D11.Blend;
using Box = Silk.NET.Direct3D11.Box;
using Buffer = System.Buffer;
using D3D9Format = Silk.NET.Direct3D9.Format;
using DxgiFormat = Silk.NET.DXGI.Format;
using PresentParameters = Silk.NET.Direct3D9.PresentParameters;

namespace WpfCustomUI.Viewport3D.Rendering;

/// <summary>コンター描画のスカラー変換パラメータ(<see cref="ColorScale"/> から導出)。</summary>
internal readonly record struct ContourSettings(
    float Min,
    float InvRange,
    bool UseLog,
    Vector4 NaNColor,
    Vector4 BelowColor,
    Vector4 AboveColor);

/// <summary>HLSL の cbuffer FrameConstants と 1:1 対応(208 バイト、16 バイト境界)。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FrameConstants
{
    public Matrix4x4 ViewProj;
    public Vector4 EyeDirection;
    public Vector4 ObjectColor;  // 単色 / エッジ線色 / ピックパスでは x=パーツID
    public Vector4 ScalarParams; // x=min, y=1/range, z=コンター有効, w=対数
    public Vector4 NaNColor;
    public Vector4 BelowColor;
    public Vector4 AboveColor;
    public Vector4 ViewportInfo; // xy=ピクセルサイズ, z=ポイント直径(px)
    public Vector4 DeformParams; // x=変形スケール(振動アニメ係数込み)
    public Vector4 ClipPlane;    // xyz=正規化法線, w=定数項。無効時 (0,0,0,1)
}

/// <summary>描画 1 パーツ分の入力(メッシュ+選択ハイライト情報)。</summary>
internal readonly record struct RenderItem(
    GpuMesh Mesh,
    GpuSelectionMesh? Selection,
    bool IsPartSelected);

/// <summary>
/// D3D11 レンダリングエンジン本体(spec 6.16.1 / 6.16.2)。
/// <para>
/// - ハードウェア経路: D3D11 で MSAA 描画 → 非 MSAA 共有テクスチャへ Resolve →
///   D3D9Ex で共有ハンドルを開き、そのサーフェスを D3DImage のバックバッファにする。
///   D3DImage は WPF 合成にネイティブ統合されるためエアスペース問題が起きない。
/// - ソフトウェア経路(WARP / D3D9 不可): WARP アダプタの共有テクスチャは
///   ハードウェア D3D9 から開けないため、ステージングテクスチャへ CPU 読み戻しして
///   WriteableBitmap に転送する(オンデマンド描画なので実用上十分)。
/// </para>
/// </summary>
internal sealed unsafe class ViewportRenderer : IDisposable
{
    private const int ColorMapWidth = 256;

    /// <summary>断面インジケータの頂点 float 数(14 頂点 × 6 float、spec 6.19.4)。</summary>
    private const int SectionIndicatorFloatCount = 14 * 6;

    private readonly D3D11 _d3d11;
    private readonly D3DCompiler _compiler;
    private D3D9? _d3d9Api;

    private ComPtr<ID3D11Device> _device;
    private ComPtr<ID3D11DeviceContext> _context;
    private IDirect3D9Ex* _d3d9;
    private IDirect3DDevice9Ex* _d3d9Device;

    // パイプライン(サイズ非依存)
    private ComPtr<ID3D11VertexShader> _meshVs;
    private ComPtr<ID3D11PixelShader> _meshPs;
    private ComPtr<ID3D11VertexShader> _lineVs;
    private ComPtr<ID3D11PixelShader> _linePs;
    private ComPtr<ID3D11VertexShader> _pickVs;
    private ComPtr<ID3D11PixelShader> _pickPs;
    private ComPtr<ID3D11VertexShader> _pointVs;
    private ComPtr<ID3D11PixelShader> _pointPs;
    private ComPtr<ID3D11InputLayout> _meshLayout;
    private ComPtr<ID3D11InputLayout> _lineLayout;
    private ComPtr<ID3D11InputLayout> _pickLayout;
    private ComPtr<ID3D11InputLayout> _pointLayout;
    private ComPtr<ID3D11Buffer> _constantBuffer;
    private ComPtr<ID3D11Buffer> _sectionIndicatorBuffer;
    private ComPtr<ID3D11RasterizerState> _rasterizerState;
    private ComPtr<ID3D11DepthStencilState> _depthState;
    private ComPtr<ID3D11DepthStencilState> _highlightDepthState;
    private ComPtr<ID3D11BlendState> _alphaBlendState;
    private ComPtr<ID3D11SamplerState> _colorMapSampler;
    private ComPtr<ID3D11Texture2D> _colorMapTexture;
    private ComPtr<ID3D11ShaderResourceView> _colorMapSrv;

    // サイズ依存リソース
    private ComPtr<ID3D11Texture2D> _msaaColor;
    private ComPtr<ID3D11RenderTargetView> _msaaRtv;
    private ComPtr<ID3D11Texture2D> _depthTexture;
    private ComPtr<ID3D11DepthStencilView> _depthView;
    private ComPtr<ID3D11Texture2D> _resolveTexture;
    private ComPtr<ID3D11Texture2D> _stagingTexture;
    private IDirect3DTexture9* _d3d9Texture;
    private IDirect3DSurface9* _d3d9Surface;
    private uint _sampleCount = 1;

    // ピッキング用リソース(初回ピック時に遅延作成、リサイズで再作成)
    private ComPtr<ID3D11Texture2D> _pickTexture;
    private ComPtr<ID3D11RenderTargetView> _pickRtv;
    private ComPtr<ID3D11Texture2D> _pickDepthTexture;
    private ComPtr<ID3D11DepthStencilView> _pickDepthView;

    public ViewportRenderer()
    {
        _d3d11 = D3D11.GetApi(null);
        _compiler = D3DCompiler.GetApi();

        CreateDevice();
        TryCreateD3D9();
        CreatePipeline();
    }

    /// <summary>WARP(ソフトウェアラスタライザ)で動作しているか。</summary>
    public bool IsSoftwareRendering { get; private set; }

    /// <summary>D3DImage 経由の表示が可能か(不可なら WriteableBitmap 経路)。</summary>
    public bool CanUseD3DImage => _d3d9Surface is not null || (!IsSoftwareRendering && _d3d9Device is not null);

    /// <summary>D3DImage.SetBackBuffer に渡す IDirect3DSurface9 ポインタ(ハードウェア経路のみ)。</summary>
    public nint BackBufferSurface => (nint)_d3d9Surface;

    public int Width { get; private set; }

    public int Height { get; private set; }

    internal ComPtr<ID3D11Device> Device => _device;

    /// <summary>描画先のピクセルサイズを設定し、サイズ依存リソースを再作成する。</summary>
    public void Resize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (width == Width && height == Height && _msaaColor.Handle is not null)
        {
            return;
        }

        Width = width;
        Height = height;
        ReleaseSizedResources();

        var device = _device.Handle;

        // MSAA 4x が使えるか確認
        _sampleCount = 1;
        uint qualityLevels = 0;
        device->CheckMultisampleQualityLevels(DxgiFormat.FormatB8G8R8A8Unorm, 4, &qualityLevels);
        if (qualityLevels > 0)
        {
            _sampleCount = 4;
        }

        // MSAA カラーターゲット
        var colorDesc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormat.FormatB8G8R8A8Unorm,
            SampleDesc = new SampleDesc(_sampleCount, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.RenderTarget,
        };
        SilkMarshal.ThrowHResult(device->CreateTexture2D(&colorDesc, null, _msaaColor.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreateRenderTargetView(
            (ID3D11Resource*)_msaaColor.Handle, null, _msaaRtv.GetAddressOf()));

        // 深度バッファ(MSAA 一致)
        var depthDesc = colorDesc with
        {
            Format = DxgiFormat.FormatD24UnormS8Uint,
            BindFlags = (uint)BindFlag.DepthStencil,
        };
        SilkMarshal.ThrowHResult(device->CreateTexture2D(&depthDesc, null, _depthTexture.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreateDepthStencilView(
            (ID3D11Resource*)_depthTexture.Handle, null, _depthView.GetAddressOf()));

        // Resolve 先(非 MSAA)。ハードウェア経路では D3D9 と共有する
        var resolveDesc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormat.FormatB8G8R8A8Unorm,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.RenderTarget,
            MiscFlags = _d3d9Device is not null ? (uint)ResourceMiscFlag.Shared : 0u,
        };
        SilkMarshal.ThrowHResult(device->CreateTexture2D(&resolveDesc, null, _resolveTexture.GetAddressOf()));

        if (_d3d9Device is not null)
        {
            OpenSharedSurfaceOnD3D9(width, height);
        }

        if (_d3d9Surface is null)
        {
            // ソフトウェア経路: CPU 読み戻し用ステージング
            var stagingDesc = resolveDesc with
            {
                Usage = Usage.Staging,
                BindFlags = 0,
                MiscFlags = 0,
                CPUAccessFlags = (uint)CpuAccessFlag.Read,
            };
            SilkMarshal.ThrowHResult(device->CreateTexture2D(&stagingDesc, null, _stagingTexture.GetAddressOf()));
        }
    }

    /// <summary>ColorScale をサンプリングした RGBA(4 バイト/texel ×256)でコンター用 1D テクスチャを作り直す。</summary>
    public void SetColorMap(ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != ColorMapWidth * 4)
        {
            throw new ArgumentException($"カラーマップは {ColorMapWidth} texel の RGBA データが必要です。", nameof(rgba));
        }

        _colorMapSrv.Dispose();
        _colorMapTexture.Dispose();
        _colorMapSrv = default;
        _colorMapTexture = default;

        var desc = new Texture2DDesc
        {
            Width = ColorMapWidth,
            Height = 1,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormat.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Immutable,
            BindFlags = (uint)BindFlag.ShaderResource,
        };

        fixed (byte* pData = rgba)
        {
            var init = new SubresourceData
            {
                PSysMem = pData,
                SysMemPitch = ColorMapWidth * 4,
            };
            SilkMarshal.ThrowHResult(_device.Handle->CreateTexture2D(&desc, &init, _colorMapTexture.GetAddressOf()));
        }

        SilkMarshal.ThrowHResult(_device.Handle->CreateShaderResourceView(
            (ID3D11Resource*)_colorMapTexture.Handle, null, _colorMapSrv.GetAddressOf()));
    }

    /// <summary>シーン全体を描画して Resolve まで行う(表示側への転送は呼び出し元が行う)。</summary>
    public void Render(
        IReadOnlyList<RenderItem> items,
        in Matrix4x4 viewProj,
        Vector3 eyeDirection,
        Vector4 background,
        in ContourSettings contour,
        bool contoursEnabled,
        Vector4 edgeColor,
        Vector4 highlightColor,
        Vector4 nodeColor,
        float nodePointSizePixels,
        float deformationScale,
        bool showUndeformedWireframe,
        Vector4 undeformedColor,
        Vector4 clipPlane,
        float[]? sectionIndicatorVertices,
        Vector4 sectionFillColor,
        Vector4 sectionLineColor)
    {
        if (_msaaRtv.Handle is null)
        {
            return;
        }

        var ctx = _context.Handle;

        var bg = stackalloc float[4] { background.X, background.Y, background.Z, background.W };
        ctx->ClearRenderTargetView(_msaaRtv.Handle, bg);
        ctx->ClearDepthStencilView(_depthView.Handle, (uint)ClearFlag.Depth, 1.0f, 0);

        var rtv = _msaaRtv.Handle;
        ctx->OMSetRenderTargets(1, &rtv, _depthView.Handle);

        var viewport = new Silk.NET.Direct3D11.Viewport(0, 0, Width, Height, 0.0f, 1.0f);
        ctx->RSSetViewports(1, &viewport);
        ctx->RSSetState(_rasterizerState.Handle);
        ctx->OMSetDepthStencilState(_depthState.Handle, 0);

        var cb = _constantBuffer.Handle;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        ctx->PSSetConstantBuffers(0, 1, &cb);

        var srv = _colorMapSrv.Handle;
        var sampler = _colorMapSampler.Handle;
        ctx->PSSetShaderResources(0, 1, &srv);
        ctx->PSSetSamplers(0, 1, &sampler);

        var constants = new FrameConstants
        {
            ViewProj = viewProj,
            EyeDirection = new Vector4(eyeDirection, 0.0f),
            ScalarParams = new Vector4(contour.Min, contour.InvRange, 0.0f, contour.UseLog ? 1.0f : 0.0f),
            NaNColor = contour.NaNColor,
            BelowColor = contour.BelowColor,
            AboveColor = contour.AboveColor,
            ViewportInfo = new Vector4(Width, Height, nodePointSizePixels, 0.0f),
            DeformParams = new Vector4(deformationScale, 0.0f, 0.0f, 0.0f),
            ClipPlane = ViewportSection.DisabledClip,
        };

        // パス 1: 不透明メッシュ → 半透明メッシュの順に三角形を描画
        foreach (var transparentPass in (ReadOnlySpan<bool>)[false, true])
        {
            foreach (var item in items)
            {
                var mesh = item.Mesh;
                if (mesh.IsTransparent != transparentPass)
                {
                    continue;
                }

                ctx->OMSetBlendState(transparentPass ? _alphaBlendState.Handle : null, null, 0xFFFFFFFF);

                constants.ObjectColor = mesh.Color;
                constants.ScalarParams.Z = contoursEnabled && mesh.HasScalars && _colorMapSrv.Handle is not null ? 1.0f : 0.0f;
                constants.ClipPlane = mesh.IsClippable ? clipPlane : ViewportSection.DisabledClip;
                UploadConstants(in constants);

                BindVertexBuffer(ctx, mesh);
                ctx->IASetInputLayout(_meshLayout.Handle);
                ctx->VSSetShader(_meshVs.Handle, null, 0);
                ctx->PSSetShader(_meshPs.Handle, null, 0);
                ctx->IASetIndexBuffer(mesh.TriangleIndexBufferHandle, DxgiFormat.FormatR32Uint, 0);
                ctx->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
                ctx->DrawIndexed(mesh.TriangleIndexCount, 0, 0);
            }
        }

        // パス 2: エッジ重畳(不透明のみ、深度テストあり・シェーダ側で微小手前シフト)
        ctx->OMSetBlendState(null, null, 0xFFFFFFFF);
        foreach (var item in items)
        {
            var mesh = item.Mesh;
            if (!mesh.ShowEdges || mesh.EdgeIndexCount == 0)
            {
                continue;
            }

            constants.ObjectColor = edgeColor;
            constants.ScalarParams.Z = 0.0f;
            constants.ClipPlane = mesh.IsClippable ? clipPlane : ViewportSection.DisabledClip;
            UploadConstants(in constants);

            BindVertexBuffer(ctx, mesh);
            ctx->IASetInputLayout(_lineLayout.Handle);
            ctx->VSSetShader(_lineVs.Handle, null, 0);
            ctx->PSSetShader(_linePs.Handle, null, 0);
            ctx->IASetIndexBuffer(mesh.EdgeIndexBufferHandle, DxgiFormat.FormatR32Uint, 0);
            ctx->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyLinelist);
            ctx->DrawIndexed(mesh.EdgeIndexCount, 0, 0);
        }

        // パス 2.5: 非変形ワイヤフレーム重畳(spec 6.18.4)。DeformParams.x=0 で元形状の
        // エッジを半透明描画し、変形前後の対比を見せる。深度書き込みなしで面を汚さない
        if (showUndeformedWireframe)
        {
            ctx->OMSetBlendState(_alphaBlendState.Handle, null, 0xFFFFFFFF);
            ctx->OMSetDepthStencilState(_highlightDepthState.Handle, 0);

            constants.DeformParams.X = 0.0f;
            foreach (var item in items)
            {
                var mesh = item.Mesh;
                if (mesh.EdgeIndexCount == 0)
                {
                    continue;
                }

                constants.ObjectColor = undeformedColor;
                constants.ScalarParams.Z = 0.0f;
                // 非変形形状にも同じ平面でクリップを適用する(DeformParams.x=0 なので
                // シェーダ内の符号付き距離も非変形位置で評価される)
                constants.ClipPlane = mesh.IsClippable ? clipPlane : ViewportSection.DisabledClip;
                UploadConstants(in constants);

                BindVertexBuffer(ctx, mesh);
                ctx->IASetInputLayout(_lineLayout.Handle);
                ctx->VSSetShader(_lineVs.Handle, null, 0);
                ctx->PSSetShader(_linePs.Handle, null, 0);
                ctx->IASetIndexBuffer(mesh.EdgeIndexBufferHandle, DxgiFormat.FormatR32Uint, 0);
                ctx->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyLinelist);
                ctx->DrawIndexed(mesh.EdgeIndexCount, 0, 0);
            }

            constants.DeformParams.X = deformationScale;
            ctx->OMSetBlendState(null, null, 0xFFFFFFFF);
            ctx->OMSetDepthStencilState(_depthState.Handle, 0);
        }

        // パス 3: 選択面ハイライト(半透明オーバーレイ、深度書き込みなし+微小手前シフト)
        // パーツ選択はメッシュ全体の三角形インデックスをそのまま使う(追加バッファ不要)
        ctx->OMSetBlendState(_alphaBlendState.Handle, null, 0xFFFFFFFF);
        ctx->OMSetDepthStencilState(_highlightDepthState.Handle, 0);
        foreach (var item in items)
        {
            ID3D11Buffer* indexBuffer;
            uint indexCount;
            if (item.IsPartSelected)
            {
                indexBuffer = item.Mesh.TriangleIndexBufferHandle;
                indexCount = item.Mesh.TriangleIndexCount;
            }
            else if (item.Selection is { FaceIndexCount: > 0 } selection)
            {
                indexBuffer = selection.FaceIndexBufferHandle;
                indexCount = selection.FaceIndexCount;
            }
            else
            {
                continue;
            }

            constants.ObjectColor = highlightColor;
            constants.ScalarParams.Z = 0.0f;
            constants.ClipPlane = item.Mesh.IsClippable ? clipPlane : ViewportSection.DisabledClip;
            UploadConstants(in constants);

            BindVertexBuffer(ctx, item.Mesh);
            ctx->IASetInputLayout(_lineLayout.Handle);
            ctx->VSSetShader(_lineVs.Handle, null, 0);
            ctx->PSSetShader(_linePs.Handle, null, 0);
            ctx->IASetIndexBuffer(indexBuffer, DxgiFormat.FormatR32Uint, 0);
            ctx->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
            ctx->DrawIndexed(indexCount, 0, 0);
        }

        // パス 4: 選択節点ポイント(丸ポイント、深度テストあり・書き込みなし)
        foreach (var item in items)
        {
            if (item.Selection is not { NodeVertexCount: > 0 } selection)
            {
                continue;
            }

            constants.ObjectColor = nodeColor;
            constants.ClipPlane = item.Mesh.IsClippable ? clipPlane : ViewportSection.DisabledClip;
            UploadConstants(in constants);

            var vb = selection.NodeVertexBufferHandle;
            var stride = GpuSelectionMesh.PointVertexStride;
            uint offset = 0;
            ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
            ctx->IASetInputLayout(_pointLayout.Handle);
            ctx->VSSetShader(_pointVs.Handle, null, 0);
            ctx->PSSetShader(_pointPs.Handle, null, 0);
            ctx->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
            ctx->Draw(selection.NodeVertexCount, 0);
        }

        // パス 5: 断面平面インジケータ(spec 6.19.4)。半透明クワッド+輪郭線。
        // 深度テストあり・書き込みなしで、モデルに刺さった位置関係が分かるようにする
        if (sectionIndicatorVertices is { Length: >= SectionIndicatorFloatCount })
        {
            ctx->OMSetBlendState(_alphaBlendState.Handle, null, 0xFFFFFFFF);
            ctx->OMSetDepthStencilState(_highlightDepthState.Handle, 0);
            RenderSectionIndicator(ctx, sectionIndicatorVertices, sectionFillColor, sectionLineColor, ref constants);
        }

        ctx->OMSetBlendState(null, null, 0xFFFFFFFF);
        ctx->OMSetDepthStencilState(_depthState.Handle, 0);

        // MSAA → 非 MSAA へ解決
        if (_sampleCount > 1)
        {
            ctx->ResolveSubresource(
                (ID3D11Resource*)_resolveTexture.Handle, 0,
                (ID3D11Resource*)_msaaColor.Handle, 0,
                DxgiFormat.FormatB8G8R8A8Unorm);
        }
        else
        {
            ctx->CopyResource((ID3D11Resource*)_resolveTexture.Handle, (ID3D11Resource*)_msaaColor.Handle);
        }

        ctx->Flush();
    }

    // ================= GPU ID ピッキング(spec 6.17.1) =================

    /// <summary>
    /// 1 ピクセルの ID ピック。ヒットがあれば (メッシュインデックス, 三角形インデックス) を返す。
    /// メッシュインデックスは渡した <paramref name="meshes"/> リスト内の位置。
    /// </summary>
    public (int MeshIndex, int TriangleIndex)? PickPixel(
        IReadOnlyList<GpuMesh> meshes, in Matrix4x4 viewProj, int x, int y, float deformationScale,
        Vector4 clipPlane)
    {
        var region = PickRegion(meshes, in viewProj, x, y, 1, 1, deformationScale, clipPlane);
        foreach (var (meshIndex, triangles) in region)
        {
            foreach (var triangle in triangles)
            {
                return (meshIndex, triangle);
            }
        }

        return null;
    }

    /// <summary>
    /// 矩形領域の ID ピック。領域に出現する (メッシュインデックス → 三角形インデックス集合) を返す。
    /// GPU が描画したピクセルの列挙なので「見えているものだけ」が返る(spec 6.17.4)。
    /// </summary>
    public Dictionary<int, HashSet<int>> PickRegion(
        IReadOnlyList<GpuMesh> meshes, in Matrix4x4 viewProj, int x, int y, int width, int height,
        float deformationScale, Vector4 clipPlane)
    {
        var result = new Dictionary<int, HashSet<int>>();

        // ビューポート内へクランプ
        var x0 = Math.Max(x, 0);
        var y0 = Math.Max(y, 0);
        var x1 = Math.Min(x + width, Width);
        var y1 = Math.Min(y + height, Height);
        if (x0 >= x1 || y0 >= y1 || meshes.Count == 0 || _msaaRtv.Handle is null)
        {
            return result;
        }

        RenderIdPass(meshes, in viewProj, deformationScale, clipPlane);

        var regionWidth = x1 - x0;
        var regionHeight = y1 - y0;
        var device = _device.Handle;
        var ctx = _context.Handle;

        // 領域サイズのステージングを都度作る(ピックはユーザー操作ペースなのでコストは無視できる)
        var stagingDesc = new Texture2DDesc
        {
            Width = (uint)regionWidth,
            Height = (uint)regionHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormat.FormatR32G32Uint,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Staging,
            CPUAccessFlags = (uint)CpuAccessFlag.Read,
        };

        ComPtr<ID3D11Texture2D> staging = default;
        SilkMarshal.ThrowHResult(device->CreateTexture2D(&stagingDesc, null, staging.GetAddressOf()));
        try
        {
            var box = new Box((uint)x0, (uint)y0, 0, (uint)x1, (uint)y1, 1);
            ctx->CopySubresourceRegion(
                (ID3D11Resource*)staging.Handle, 0, 0, 0, 0,
                (ID3D11Resource*)_pickTexture.Handle, 0, &box);

            var mapped = default(MappedSubresource);
            SilkMarshal.ThrowHResult(ctx->Map((ID3D11Resource*)staging.Handle, 0, Map.Read, 0, &mapped));
            try
            {
                for (var row = 0; row < regionHeight; row++)
                {
                    var pRow = (uint*)((byte*)mapped.PData + row * (int)mapped.RowPitch);
                    for (var col = 0; col < regionWidth; col++)
                    {
                        var part = pRow[col * 2];
                        if (part == 0)
                        {
                            continue; // 背景
                        }

                        var meshIndex = (int)part - 1;
                        if (meshIndex >= meshes.Count)
                        {
                            continue;
                        }

                        var triangle = (int)pRow[col * 2 + 1];
                        if (!result.TryGetValue(meshIndex, out var set))
                        {
                            set = [];
                            result[meshIndex] = set;
                        }

                        set.Add(triangle);
                    }
                }
            }
            finally
            {
                ctx->Unmap((ID3D11Resource*)staging.Handle, 0);
            }
        }
        finally
        {
            staging.Dispose();
        }

        return result;
    }

    /// <summary>ID パスを描画する(R=パーツID+1 / G=三角形インデックス、非 MSAA、専用深度)。</summary>
    private void RenderIdPass(
        IReadOnlyList<GpuMesh> meshes, in Matrix4x4 viewProj, float deformationScale, Vector4 clipPlane)
    {
        EnsurePickTargets();

        var ctx = _context.Handle;

        // 整数 RTV だが 0 クリアは float→uint 変換でも 0 になるため安全
        var zero = stackalloc float[4];
        ctx->ClearRenderTargetView(_pickRtv.Handle, zero);
        ctx->ClearDepthStencilView(_pickDepthView.Handle, (uint)ClearFlag.Depth, 1.0f, 0);

        var rtv = _pickRtv.Handle;
        ctx->OMSetRenderTargets(1, &rtv, _pickDepthView.Handle);

        var viewport = new Silk.NET.Direct3D11.Viewport(0, 0, Width, Height, 0.0f, 1.0f);
        ctx->RSSetViewports(1, &viewport);
        ctx->RSSetState(_rasterizerState.Handle);
        ctx->OMSetDepthStencilState(_depthState.Handle, 0);
        ctx->OMSetBlendState(null, null, 0xFFFFFFFF);

        var cb = _constantBuffer.Handle;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        ctx->PSSetConstantBuffers(0, 1, &cb);

        var constants = new FrameConstants
        {
            ViewProj = viewProj,
            DeformParams = new Vector4(deformationScale, 0.0f, 0.0f, 0.0f),
            ClipPlane = ViewportSection.DisabledClip,
        };

        for (var i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            constants.ObjectColor = new Vector4(i + 1, 0.0f, 0.0f, 0.0f);
            // 表示と同じクリップを適用 → 断面で隠れた要素はピックにも掛からない(spec 6.19.2)
            constants.ClipPlane = mesh.IsClippable ? clipPlane : ViewportSection.DisabledClip;
            UploadConstants(in constants);

            BindVertexBuffer(ctx, mesh);
            ctx->IASetInputLayout(_pickLayout.Handle);
            ctx->VSSetShader(_pickVs.Handle, null, 0);
            ctx->PSSetShader(_pickPs.Handle, null, 0);
            ctx->IASetIndexBuffer(mesh.TriangleIndexBufferHandle, DxgiFormat.FormatR32Uint, 0);
            ctx->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
            ctx->DrawIndexed(mesh.TriangleIndexCount, 0, 0);
        }
    }

    private void EnsurePickTargets()
    {
        if (_pickTexture.Handle is not null)
        {
            return;
        }

        var device = _device.Handle;

        var pickDesc = new Texture2DDesc
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormat.FormatR32G32Uint,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.RenderTarget,
        };
        SilkMarshal.ThrowHResult(device->CreateTexture2D(&pickDesc, null, _pickTexture.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreateRenderTargetView(
            (ID3D11Resource*)_pickTexture.Handle, null, _pickRtv.GetAddressOf()));

        var depthDesc = pickDesc with
        {
            Format = DxgiFormat.FormatD24UnormS8Uint,
            BindFlags = (uint)BindFlag.DepthStencil,
        };
        SilkMarshal.ThrowHResult(device->CreateTexture2D(&depthDesc, null, _pickDepthTexture.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreateDepthStencilView(
            (ID3D11Resource*)_pickDepthTexture.Handle, null, _pickDepthView.GetAddressOf()));
    }

    /// <summary>
    /// ソフトウェア経路: Resolve 済みの絵をステージング経由で CPU バッファ(BGRA、stride 指定)へ転送する。
    /// </summary>
    public void ReadPixels(nint destination, int destStride)
    {
        if (_stagingTexture.Handle is null)
        {
            return;
        }

        var ctx = _context.Handle;
        ctx->CopyResource((ID3D11Resource*)_stagingTexture.Handle, (ID3D11Resource*)_resolveTexture.Handle);

        var mapped = default(MappedSubresource);
        SilkMarshal.ThrowHResult(ctx->Map((ID3D11Resource*)_stagingTexture.Handle, 0, Map.Read, 0, &mapped));
        try
        {
            var rowBytes = Math.Min((int)mapped.RowPitch, Math.Min(destStride, Width * 4));
            for (var y = 0; y < Height; y++)
            {
                Buffer.MemoryCopy(
                    (byte*)mapped.PData + y * (int)mapped.RowPitch,
                    (byte*)destination + y * destStride,
                    destStride,
                    rowBytes);
            }
        }
        finally
        {
            ctx->Unmap((ID3D11Resource*)_stagingTexture.Handle, 0);
        }
    }

    public GpuMesh? CreateMesh(ViewportMesh mesh, double originX, double originY, double originZ) =>
        GpuMesh.Create(_device, mesh, originX, originY, originZ);

    /// <summary>変位バッファのみ差し替える(過渡再生のフレーム更新用、spec 6.18.3)。</summary>
    public void UpdateMeshDisplacements(GpuMesh mesh, double[]? displacements) =>
        mesh.UpdateDisplacements(_context, displacements);

    public GpuSelectionMesh? CreateSelectionMesh(
        ViewportMesh mesh,
        IReadOnlyCollection<int> selectedFaces,
        IReadOnlyCollection<int> selectedNodes,
        double originX, double originY, double originZ) =>
        GpuSelectionMesh.Create(_device, mesh, selectedFaces, selectedNodes, originX, originY, originZ);

    // ================= 初期化 =================

    private void CreateDevice()
    {
        // BGRA サポートは D3DImage / D3D9 共有に必須
        const uint flags = (uint)CreateDeviceFlag.BgraSupport;

        var hr = _d3d11.CreateDevice(
            default(ComPtr<IDXGIAdapter>),
            D3DDriverType.Hardware,
            Software: 0,
            flags,
            null,
            0,
            D3D11.SdkVersion,
            ref _device,
            null,
            ref _context);

        if (HResult.IndicatesFailure(hr))
        {
            // GPU なし / RDP 環境向け WARP フォールバック(spec 6.16.2)
            SilkMarshal.ThrowHResult(_d3d11.CreateDevice(
                default(ComPtr<IDXGIAdapter>),
                D3DDriverType.Warp,
                Software: 0,
                flags,
                null,
                0,
                D3D11.SdkVersion,
                ref _device,
                null,
                ref _context));
            IsSoftwareRendering = true;
        }
    }

    private void TryCreateD3D9()
    {
        if (IsSoftwareRendering)
        {
            // WARP アダプタの共有テクスチャはハードウェア D3D9 から開けないため
            // D3D9 は作らず CPU 読み戻し経路を使う
            return;
        }

        try
        {
            _d3d9Api = D3D9.GetApi(null);

            IDirect3D9Ex* d3d9 = null;
            SilkMarshal.ThrowHResult(_d3d9Api.Direct3DCreate9Ex(D3D9.SdkVersion, &d3d9));
            _d3d9 = d3d9;

            var hwnd = GetDesktopWindow();
            var pp = new PresentParameters
            {
                Windowed = 1,
                SwapEffect = Swapeffect.Discard,
                BackBufferFormat = D3D9Format.Unknown,
                BackBufferWidth = 1,
                BackBufferHeight = 1,
                BackBufferCount = 1,
                HDeviceWindow = hwnd,
            };

            // 0x46 = HARDWARE_VERTEXPROCESSING | MULTITHREADED | FPU_PRESERVE
            const uint behaviorFlags = 0x40 | 0x04 | 0x02;

            IDirect3DDevice9Ex* device = null;
            SilkMarshal.ThrowHResult(_d3d9->CreateDeviceEx(
                0, Devtype.Hal, hwnd, behaviorFlags, &pp, null, &device));
            _d3d9Device = device;
        }
        catch
        {
            // D3D9 が使えない環境では CPU 読み戻し経路にフォールバック
            ReleaseD3D9();
        }
    }

    private void OpenSharedSurfaceOnD3D9(int width, int height)
    {
        try
        {
            // D3D11 共有テクスチャのハンドルを取得
            IDXGIResource* dxgiResource = null;
            var iid = IDXGIResource.Guid;
            SilkMarshal.ThrowHResult(
                ((IUnknown*)_resolveTexture.Handle)->QueryInterface(&iid, (void**)&dxgiResource));

            void* sharedHandle = null;
            var hr = dxgiResource->GetSharedHandle(&sharedHandle);
            dxgiResource->Release();
            SilkMarshal.ThrowHResult(hr);

            // D3D9Ex 側で同じテクスチャを開く(A8R8G8B8 = B8G8R8A8Unorm)
            const uint usageRenderTarget = 0x00000001;
            IDirect3DTexture9* texture = null;
            SilkMarshal.ThrowHResult(_d3d9Device->CreateTexture(
                (uint)width, (uint)height, 1, usageRenderTarget,
                D3D9Format.A8R8G8B8, Pool.Default, &texture, &sharedHandle));
            _d3d9Texture = texture;

            IDirect3DSurface9* surface = null;
            SilkMarshal.ThrowHResult(_d3d9Texture->GetSurfaceLevel(0, &surface));
            _d3d9Surface = surface;
        }
        catch
        {
            // 共有に失敗した場合は CPU 読み戻し経路へ
            if (_d3d9Texture is not null)
            {
                _d3d9Texture->Release();
                _d3d9Texture = null;
            }

            _d3d9Surface = null;
        }
    }

    private void CreatePipeline()
    {
        var device = _device.Handle;

        // シェーダ(vs_4_0 / ps_4_0: FL10 GPU でも動作。SV_PrimitiveID も SM4.0 で使用可)
        using var meshVsCode = CompileShader(HlslSource.Mesh, "VSMain", "vs_4_0");
        using var meshPsCode = CompileShader(HlslSource.Mesh, "PSMain", "ps_4_0");
        using var lineVsCode = CompileShader(HlslSource.Line, "VSMain", "vs_4_0");
        using var linePsCode = CompileShader(HlslSource.Line, "PSMain", "ps_4_0");
        using var pickVsCode = CompileShader(HlslSource.Pick, "VSMain", "vs_4_0");
        using var pickPsCode = CompileShader(HlslSource.Pick, "PSMain", "ps_4_0");
        using var pointVsCode = CompileShader(HlslSource.Point, "VSMain", "vs_4_0");
        using var pointPsCode = CompileShader(HlslSource.Point, "PSMain", "ps_4_0");

        SilkMarshal.ThrowHResult(device->CreateVertexShader(
            meshVsCode.GetBufferPointer(), meshVsCode.GetBufferSize(), null, _meshVs.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreatePixelShader(
            meshPsCode.GetBufferPointer(), meshPsCode.GetBufferSize(), null, _meshPs.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreateVertexShader(
            lineVsCode.GetBufferPointer(), lineVsCode.GetBufferSize(), null, _lineVs.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreatePixelShader(
            linePsCode.GetBufferPointer(), linePsCode.GetBufferSize(), null, _linePs.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreateVertexShader(
            pickVsCode.GetBufferPointer(), pickVsCode.GetBufferSize(), null, _pickVs.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreatePixelShader(
            pickPsCode.GetBufferPointer(), pickPsCode.GetBufferSize(), null, _pickPs.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreateVertexShader(
            pointVsCode.GetBufferPointer(), pointVsCode.GetBufferSize(), null, _pointVs.GetAddressOf()));
        SilkMarshal.ThrowHResult(device->CreatePixelShader(
            pointPsCode.GetBufferPointer(), pointPsCode.GetBufferSize(), null, _pointPs.GetAddressOf()));

        // 入力レイアウト
        var semanticPosition = SilkMarshal.StringToMemory("POSITION");
        var semanticNormal = SilkMarshal.StringToMemory("NORMAL");
        var semanticTexCoord = SilkMarshal.StringToMemory("TEXCOORD");
        try
        {
            fixed (byte* pPosition = semanticPosition)
            fixed (byte* pNormal = semanticNormal)
            fixed (byte* pTexCoord = semanticTexCoord)
            {
                // 変位(TEXCOORD1)は slot 1 の独立バッファから読む(spec 6.18.2)
                var displacementElement = new InputElementDesc
                {
                    SemanticName = pTexCoord,
                    SemanticIndex = 1,
                    Format = DxgiFormat.FormatR32G32B32Float,
                    InputSlot = 1,
                    AlignedByteOffset = 0,
                    InputSlotClass = InputClassification.PerVertexData,
                };

                var meshElements = stackalloc InputElementDesc[4]
                {
                    new InputElementDesc
                    {
                        SemanticName = pPosition,
                        Format = DxgiFormat.FormatR32G32B32Float,
                        AlignedByteOffset = 0,
                        InputSlotClass = InputClassification.PerVertexData,
                    },
                    new InputElementDesc
                    {
                        SemanticName = pNormal,
                        Format = DxgiFormat.FormatR32G32B32Float,
                        AlignedByteOffset = 12,
                        InputSlotClass = InputClassification.PerVertexData,
                    },
                    new InputElementDesc
                    {
                        SemanticName = pTexCoord,
                        Format = DxgiFormat.FormatR32Float,
                        AlignedByteOffset = 24,
                        InputSlotClass = InputClassification.PerVertexData,
                    },
                    displacementElement,
                };
                SilkMarshal.ThrowHResult(device->CreateInputLayout(
                    meshElements, 4,
                    meshVsCode.GetBufferPointer(), meshVsCode.GetBufferSize(),
                    _meshLayout.GetAddressOf()));

                var lineElements = stackalloc InputElementDesc[2]
                {
                    new InputElementDesc
                    {
                        SemanticName = pPosition,
                        Format = DxgiFormat.FormatR32G32B32Float,
                        AlignedByteOffset = 0,
                        InputSlotClass = InputClassification.PerVertexData,
                    },
                    displacementElement,
                };
                SilkMarshal.ThrowHResult(device->CreateInputLayout(
                    lineElements, 2,
                    lineVsCode.GetBufferPointer(), lineVsCode.GetBufferSize(),
                    _lineLayout.GetAddressOf()));

                // ピックパス: メッシュ頂点バッファ(28B ストライド)の POSITION + 変位を読む
                SilkMarshal.ThrowHResult(device->CreateInputLayout(
                    lineElements, 2,
                    pickVsCode.GetBufferPointer(), pickVsCode.GetBufferSize(),
                    _pickLayout.GetAddressOf()));

                // ポイントパス: position + corner + displacement(32B ストライド、slot 0 のみ)
                var pointElements = stackalloc InputElementDesc[3]
                {
                    new InputElementDesc
                    {
                        SemanticName = pPosition,
                        Format = DxgiFormat.FormatR32G32B32Float,
                        AlignedByteOffset = 0,
                        InputSlotClass = InputClassification.PerVertexData,
                    },
                    new InputElementDesc
                    {
                        SemanticName = pTexCoord,
                        Format = DxgiFormat.FormatR32G32Float,
                        AlignedByteOffset = 12,
                        InputSlotClass = InputClassification.PerVertexData,
                    },
                    new InputElementDesc
                    {
                        SemanticName = pTexCoord,
                        SemanticIndex = 1,
                        Format = DxgiFormat.FormatR32G32B32Float,
                        AlignedByteOffset = 20,
                        InputSlotClass = InputClassification.PerVertexData,
                    },
                };
                SilkMarshal.ThrowHResult(device->CreateInputLayout(
                    pointElements, 3,
                    pointVsCode.GetBufferPointer(), pointVsCode.GetBufferSize(),
                    _pointLayout.GetAddressOf()));
            }
        }
        finally
        {
            semanticPosition.Dispose();
            semanticNormal.Dispose();
            semanticTexCoord.Dispose();
        }

        // 定数バッファ(動的、フレーム毎 Map/WriteDiscard)
        var cbDesc = new BufferDesc
        {
            ByteWidth = (uint)sizeof(FrameConstants),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };
        SilkMarshal.ThrowHResult(device->CreateBuffer(&cbDesc, null, _constantBuffer.GetAddressOf()));

        // ラスタライザ: CAE シェルは裏面も見えるためカリングなし
        var rsDesc = new RasterizerDesc
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            DepthClipEnable = 1,
            MultisampleEnable = 1,
        };
        SilkMarshal.ThrowHResult(device->CreateRasterizerState(&rsDesc, _rasterizerState.GetAddressOf()));

        // 深度: LessEqual(エッジ重畳のため)
        var dsDesc = new DepthStencilDesc
        {
            DepthEnable = 1,
            DepthWriteMask = DepthWriteMask.All,
            DepthFunc = ComparisonFunc.LessEqual,
        };
        SilkMarshal.ThrowHResult(device->CreateDepthStencilState(&dsDesc, _depthState.GetAddressOf()));

        // ハイライト用深度: テストあり・書き込みなし(半透明オーバーレイが深度を汚さない)
        var highlightDsDesc = dsDesc with { DepthWriteMask = DepthWriteMask.Zero };
        SilkMarshal.ThrowHResult(device->CreateDepthStencilState(
            &highlightDsDesc, _highlightDepthState.GetAddressOf()));

        // 半透明パーツ用アルファブレンド
        var blendDesc = new BlendDesc();
        blendDesc.RenderTarget[0] = new RenderTargetBlendDesc
        {
            BlendEnable = 1,
            SrcBlend = Blend.SrcAlpha,
            DestBlend = Blend.InvSrcAlpha,
            BlendOp = BlendOp.Add,
            SrcBlendAlpha = Blend.One,
            DestBlendAlpha = Blend.InvSrcAlpha,
            BlendOpAlpha = BlendOp.Add,
            RenderTargetWriteMask = (byte)ColorWriteEnable.All,
        };
        SilkMarshal.ThrowHResult(device->CreateBlendState(&blendDesc, _alphaBlendState.GetAddressOf()));

        // カラーマップサンプラ: 離散レベルの境界を鈍らせないポイントサンプリング
        var samplerDesc = new SamplerDesc
        {
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
        };
        SilkMarshal.ThrowHResult(device->CreateSamplerState(&samplerDesc, _colorMapSampler.GetAddressOf()));
    }

    private ComPtr<ID3D10Blob> CompileShader(string source, string entryPoint, string profile)
    {
        var bytes = Encoding.ASCII.GetBytes(source);

        ComPtr<ID3D10Blob> code = default;
        ComPtr<ID3D10Blob> errors = default;
        HResult hr = _compiler.Compile(
            in bytes[0],
            (nuint)bytes.Length,
            "WcuViewport",
            null,
            ref Unsafe.NullRef<ID3DInclude>(),
            entryPoint,
            profile,
            0,
            0,
            ref code,
            ref errors);

        if (hr.IsFailure)
        {
            var message = errors.Handle is not null
                ? SilkMarshal.PtrToString((nint)errors.GetBufferPointer())
                : $"HRESULT 0x{hr.Value:X8}";
            errors.Dispose();
            throw new InvalidOperationException($"シェーダのコンパイルに失敗しました ({entryPoint}): {message}");
        }

        errors.Dispose();
        return code;
    }

    // ================= 描画ヘルパー =================

    private void UploadConstants(in FrameConstants constants)
    {
        var ctx = _context.Handle;
        var mapped = default(MappedSubresource);
        SilkMarshal.ThrowHResult(ctx->Map(
            (ID3D11Resource*)_constantBuffer.Handle, 0, Map.WriteDiscard, 0, &mapped));
        *(FrameConstants*)mapped.PData = constants;
        ctx->Unmap((ID3D11Resource*)_constantBuffer.Handle, 0);
    }

    private static void BindVertexBuffer(ID3D11DeviceContext* ctx, GpuMesh mesh)
    {
        var buffers = stackalloc ID3D11Buffer*[2] { mesh.VertexBufferHandle, mesh.DisplacementBufferHandle };
        var strides = stackalloc uint[2] { GpuMesh.VertexStride, GpuMesh.DisplacementStride };
        var offsets = stackalloc uint[2] { 0, 0 };
        ctx->IASetVertexBuffers(0, 2, buffers, strides, offsets);
    }

    /// <summary>
    /// 断面平面インジケータを描く。頂点は position+displacement(ゼロ) の 24B インターリーブで、
    /// 同じバッファを slot 0(offset 0)と slot 1(offset 12)にストライド 24 で二重バインドして
    /// ライン系シェーダの 2 スロットレイアウトを満たす(専用シェーダ不要)。
    /// 先頭 6 頂点がクワッド(三角形リスト)、続く 8 頂点が輪郭(ラインリスト)。
    /// </summary>
    private void RenderSectionIndicator(
        ID3D11DeviceContext* ctx, float[] vertices, Vector4 fillColor, Vector4 lineColor,
        ref FrameConstants constants)
    {
        if (_sectionIndicatorBuffer.Handle is null)
        {
            var desc = new BufferDesc
            {
                ByteWidth = SectionIndicatorFloatCount * sizeof(float),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };
            SilkMarshal.ThrowHResult(_device.Handle->CreateBuffer(&desc, null, _sectionIndicatorBuffer.GetAddressOf()));
        }

        var mapped = default(MappedSubresource);
        SilkMarshal.ThrowHResult(ctx->Map(
            (ID3D11Resource*)_sectionIndicatorBuffer.Handle, 0, Map.WriteDiscard, 0, &mapped));
        fixed (float* pVertices = vertices)
        {
            Buffer.MemoryCopy(pVertices, mapped.PData,
                SectionIndicatorFloatCount * sizeof(float), SectionIndicatorFloatCount * sizeof(float));
        }

        ctx->Unmap((ID3D11Resource*)_sectionIndicatorBuffer.Handle, 0);

        var buffers = stackalloc ID3D11Buffer*[2] { _sectionIndicatorBuffer.Handle, _sectionIndicatorBuffer.Handle };
        var strides = stackalloc uint[2] { 24, 24 };
        var offsets = stackalloc uint[2] { 0, 12 };
        ctx->IASetVertexBuffers(0, 2, buffers, strides, offsets);
        ctx->IASetInputLayout(_lineLayout.Handle);
        ctx->VSSetShader(_lineVs.Handle, null, 0);
        ctx->PSSetShader(_linePs.Handle, null, 0);

        constants.ScalarParams.Z = 0.0f;
        constants.DeformParams.X = 0.0f;
        constants.ClipPlane = ViewportSection.DisabledClip;

        constants.ObjectColor = fillColor;
        UploadConstants(in constants);
        ctx->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
        ctx->Draw(6, 0);

        constants.ObjectColor = lineColor;
        UploadConstants(in constants);
        ctx->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyLinelist);
        ctx->Draw(8, 6);
    }

    // ================= 解放 =================

    private void ReleaseSizedResources()
    {
        if (_d3d9Surface is not null)
        {
            _d3d9Surface->Release();
            _d3d9Surface = null;
        }

        if (_d3d9Texture is not null)
        {
            _d3d9Texture->Release();
            _d3d9Texture = null;
        }

        _stagingTexture.Dispose();
        _resolveTexture.Dispose();
        _depthView.Dispose();
        _depthTexture.Dispose();
        _msaaRtv.Dispose();
        _msaaColor.Dispose();
        _pickDepthView.Dispose();
        _pickDepthTexture.Dispose();
        _pickRtv.Dispose();
        _pickTexture.Dispose();
        _stagingTexture = default;
        _resolveTexture = default;
        _depthView = default;
        _depthTexture = default;
        _msaaRtv = default;
        _msaaColor = default;
        _pickDepthView = default;
        _pickDepthTexture = default;
        _pickRtv = default;
        _pickTexture = default;
    }

    private void ReleaseD3D9()
    {
        if (_d3d9Device is not null)
        {
            _d3d9Device->Release();
            _d3d9Device = null;
        }

        if (_d3d9 is not null)
        {
            _d3d9->Release();
            _d3d9 = null;
        }
    }

    public void Dispose()
    {
        ReleaseSizedResources();
        ReleaseD3D9();

        _colorMapSrv.Dispose();
        _colorMapTexture.Dispose();
        _colorMapSampler.Dispose();
        _alphaBlendState.Dispose();
        _highlightDepthState.Dispose();
        _depthState.Dispose();
        _rasterizerState.Dispose();
        _sectionIndicatorBuffer.Dispose();
        _constantBuffer.Dispose();
        _pointLayout.Dispose();
        _pickLayout.Dispose();
        _lineLayout.Dispose();
        _meshLayout.Dispose();
        _pointPs.Dispose();
        _pointVs.Dispose();
        _pickPs.Dispose();
        _pickVs.Dispose();
        _linePs.Dispose();
        _lineVs.Dispose();
        _meshPs.Dispose();
        _meshVs.Dispose();
        _context.Dispose();
        _device.Dispose();

        _d3d9Api?.Dispose();
        _compiler.Dispose();
        _d3d11.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
}
