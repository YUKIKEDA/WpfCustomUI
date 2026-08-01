namespace WpfCustomUI.Viewport3D.Rendering;

/// <summary>
/// 埋め込み HLSL ソース。d3dcompiler_47(Windows 標準搭載)で実行時コンパイルする。
/// 行列は System.Numerics の行優先・行ベクトル規約に合わせて row_major + mul(v, M) を使う
/// (転置アップロード不要)。
/// </summary>
internal static class HlslSource
{
    /// <summary>
    /// メッシュ・エッジ共用の定数バッファレイアウト(<see cref="FrameConstants"/> と一致必須)。
    /// </summary>
    private const string Constants = """
        cbuffer FrameConstants : register(b0)
        {
            row_major float4x4 ViewProj;
            float4 EyeDirection;   // xyz = 注視点->視点の単位ベクトル(ヘッドライト光源)
            float4 ObjectColor;    // 単色時のパーツ色 / エッジパスでは線色 / ピックパスでは x=パーツID
            float4 ScalarParams;   // x=min, y=1/range, z=コンター有効, w=対数スケール
            float4 NaNColor;
            float4 BelowColor;
            float4 AboveColor;
            float4 ViewportInfo;   // xy=ビューポートのピクセルサイズ, z=ポイント直径(px)
        };
        """;

    /// <summary>三角形メッシュ(単色 or コンター、両面ヘッドライトシェーディング)。</summary>
    public const string Mesh = Constants + """

        Texture2D ColorMapTex : register(t0);
        SamplerState ColorMapSampler : register(s0);

        struct VSIn
        {
            float3 pos    : POSITION;
            float3 normal : NORMAL;
            float  scalar : TEXCOORD0;
        };

        struct PSIn
        {
            float4 pos    : SV_Position;
            float3 normal : NORMAL;
            float  scalar : TEXCOORD0;
        };

        PSIn VSMain(VSIn v)
        {
            PSIn o;
            o.pos = mul(float4(v.pos, 1.0), ViewProj);
            o.normal = v.normal;
            o.scalar = v.scalar;
            return o;
        }

        float4 PSMain(PSIn i) : SV_Target
        {
            float3 baseColor = ObjectColor.rgb;

            if (ScalarParams.z > 0.5)
            {
                float s = i.scalar;
                if (isnan(s))
                {
                    if (NaNColor.a < 0.01)
                    {
                        discard;
                    }
                    baseColor = NaNColor.rgb;
                }
                else
                {
                    if (ScalarParams.w > 0.5)
                    {
                        s = log10(max(s, 1e-30));
                    }
                    float u = (s - ScalarParams.x) * ScalarParams.y;
                    if (u < 0.0)
                    {
                        baseColor = BelowColor.rgb;
                    }
                    else if (u > 1.0)
                    {
                        baseColor = AboveColor.rgb;
                    }
                    else
                    {
                        baseColor = ColorMapTex.Sample(ColorMapSampler, float2(u, 0.5)).rgb;
                    }
                }
            }

            // 両面ヘッドライト: シェル表裏どちらでも同じ明るさになるよう abs を取る
            float3 n = normalize(i.normal);
            float ndl = abs(dot(n, EyeDirection.xyz));
            float3 lit = baseColor * (0.35 + 0.65 * ndl);
            return float4(lit, ObjectColor.a);
        }
        """;

    /// <summary>
    /// エッジ(ワイヤフレーム)重畳用ラインシェーダ。
    /// Z ファイティング回避のため NDC 深度をわずかに手前へずらす。
    /// </summary>
    public const string Line = Constants + """

        struct VSIn
        {
            float3 pos : POSITION;
        };

        struct PSIn
        {
            float4 pos : SV_Position;
        };

        PSIn VSMain(VSIn v)
        {
            PSIn o;
            o.pos = mul(float4(v.pos, 1.0), ViewProj);
            o.pos.z -= 0.0005 * o.pos.w;
            return o;
        }

        float4 PSMain(PSIn i) : SV_Target
        {
            return ObjectColor;
        }
        """;

    /// <summary>
    /// GPU ID ピッキングパス(spec 6.17.1)。R32G32_UInt ターゲットに
    /// R=パーツID+1(0=背景)、G=三角形インデックス(SV_PrimitiveID)を書き込む。
    /// </summary>
    public const string Pick = Constants + """

        struct VSIn
        {
            float3 pos : POSITION;
        };

        struct PSIn
        {
            float4 pos : SV_Position;
        };

        PSIn VSMain(VSIn v)
        {
            PSIn o;
            o.pos = mul(float4(v.pos, 1.0), ViewProj);
            return o;
        }

        uint2 PSMain(PSIn i, uint primId : SV_PrimitiveID) : SV_Target
        {
            return uint2((uint)(ObjectColor.x + 0.5), primId);
        }
        """;

    /// <summary>
    /// 選択節点のポイント描画(spec 6.17.3)。節点位置+コーナーオフセット([-0.5, 0.5]²)の
    /// 6 頂点クワッドをクリップ空間でピクセルサイズに拡張し、円形に discard する。
    /// </summary>
    public const string Point = Constants + """

        struct VSIn
        {
            float3 pos    : POSITION;
            float2 corner : TEXCOORD0;
        };

        struct PSIn
        {
            float4 pos    : SV_Position;
            float2 corner : TEXCOORD0;
        };

        PSIn VSMain(VSIn v)
        {
            PSIn o;
            float4 clip = mul(float4(v.pos, 1.0), ViewProj);
            clip.z -= 0.001 * clip.w; // 面の上に確実に見えるよう手前へ
            clip.xy += v.corner * ViewportInfo.z * (2.0 / ViewportInfo.xy) * clip.w;
            o.pos = clip;
            o.corner = v.corner;
            return o;
        }

        float4 PSMain(PSIn i) : SV_Target
        {
            if (dot(i.corner, i.corner) > 0.25)
            {
                discard; // 丸ポイント化
            }
            return ObjectColor;
        }
        """;
}
