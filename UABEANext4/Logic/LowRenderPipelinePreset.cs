using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UABEANext4.Logic;

/// <summary>
/// Lowers graphics values in JSON dumps of URP / HDRP assets (port of lowrp.sh).
/// Only ever lowers values, never raises them.
/// </summary>
public static class LowRenderPipelinePreset
{
    public sealed class Options
    {
        /// <summary>URP render scale (lower = faster, blurrier).</summary>
        public double RenderScale { get; set; } = 0.8;

        /// <summary>URP: keep post-processing (default: switch it off).</summary>
        public bool KeepPostProcessing { get; set; } = false;
    }

    public sealed record Handler(string Kind, Func<string, (string Text, int Changes)> Apply);

    private enum Mode { Set, Min, Max }

    private static readonly Regex NumRegex = new(@"-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?");
    private static readonly Regex NameRegex = new("\"m_Name\"\\s*:\\s*\"([^\"]*)\"");

    private static double ParseD(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static string Fmt(double x, string like)
    {
        if (like.Contains('.'))
        {
            string s = x.ToString("F6", CultureInfo.InvariantCulture).TrimEnd('0');
            return s.EndsWith(".") ? s + "0" : s;
        }
        return ((long)Math.Round(x)).ToString(CultureInfo.InvariantCulture);
    }

    private static double Pick(Mode mode, double old, double val)
    {
        return mode switch
        {
            Mode.Min => Math.Min(old, val),
            Mode.Max => Math.Max(old, val),
            _ => val
        };
    }

    // "key": <number>  -> lowered everywhere it appears
    private static (string Text, int Changes) Scalar(string text, string key, Mode mode, double val)
    {
        int n = 0;
        var pat = new Regex("(\"" + Regex.Escape(key) + "\"\\s*:\\s*)(-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)");
        string result = pat.Replace(text, m =>
        {
            double old = ParseD(m.Groups[2].Value);
            string neu = Fmt(Pick(mode, old, val), m.Groups[2].Value);
            if (ParseD(neu) != old)
            {
                n++;
                return m.Groups[1].Value + neu;
            }
            return m.Value;
        });
        return (result, n);
    }

    // edit a number inside a flat {...} block such as "key": { "m_Value": 1 }
    private static (string Text, int Changes) BlockValue(string text, string key, string field, double newVal, Mode mode = Mode.Set)
    {
        var pat = new Regex("(\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{)([^{}]*)(\\})");
        var m = pat.Match(text);
        if (!m.Success)
            return (text, 0);

        string body = m.Groups[2].Value;
        var fm = new Regex("(\"" + Regex.Escape(field) + "\"\\s*:\\s*)(-?\\d+(?:\\.\\d+)?)").Match(body);
        if (!fm.Success)
            return (text, 0);

        double old = ParseD(fm.Groups[2].Value);
        string neu = Fmt(Pick(mode, old, newVal), fm.Groups[2].Value);
        if (ParseD(neu) == old)
            return (text, 0);

        string newBody = body.Substring(0, fm.Groups[2].Index) + neu + body.Substring(fm.Groups[2].Index + fm.Groups[2].Length);
        string newText = text.Substring(0, m.Groups[2].Index) + newBody + text.Substring(m.Groups[2].Index + m.Groups[2].Length);
        return (newText, 1);
    }

    // "active": 1 -> 0 (first occurrence)
    private static (string Text, int Changes) TopActiveOff(string text, string field = "active")
    {
        var m = new Regex("(\"" + Regex.Escape(field) + "\"\\s*:\\s*)1\\b").Match(text);
        if (!m.Success)
            return (text, 0);

        string newText = text.Substring(0, m.Index) + m.Groups[1].Value + "0" + text.Substring(m.Index + m.Length);
        return (newText, 1);
    }

    // set every entry of an Array to its first entry (or to a fixed value)
    private static (string Text, int Changes) ArrayFlat(string text, string key, string? value = null, bool valuesWrapper = false)
    {
        int n = 0;
        string head = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{\\s*";
        if (valuesWrapper)
            head += "\"m_Values\"\\s*:\\s*\\{\\s*";

        var pat = new Regex("(" + head + "\"Array\"\\s*:\\s*\\[)([^\\]]*)(\\])");
        string result = pat.Replace(text, m =>
        {
            string body = m.Groups[2].Value;
            var vals = NumRegex.Matches(body);
            if (vals.Count == 0)
                return m.Value;

            string target = value ?? vals[0].Value;
            string newBody = NumRegex.Replace(body, _ => target);
            if (newBody != body)
                n++;
            return m.Groups[1].Value + newBody + m.Groups[3].Value;
        });
        return (result, n);
    }

    // ---------------- URP ----------------
    private static (string Text, int Changes) DoUrpAsset(string t, Options o)
    {
        var rules = new (string Key, Mode Mode, double Value)[]
        {
            ("m_RenderScale", Mode.Min, o.RenderScale), ("m_SupportsHDR", Mode.Min, 0), ("m_MSAA", Mode.Min, 1),
            ("m_EnableLODCrossFade", Mode.Min, 0), ("m_OpaqueDownsampling", Mode.Max, 2),
            ("m_AdditionalLightsCookieResolution", Mode.Min, 512), ("m_ColorGradingLutSize", Mode.Min, 16),
            ("m_MainLightShadowsSupported", Mode.Min, 0), ("m_AdditionalLightShadowsSupported", Mode.Min, 0),
            ("m_SoftShadowsSupported", Mode.Min, 0), ("m_AdditionalLightsPerObjectLimit", Mode.Min, 4),
            ("m_ReflectionProbeBlending", Mode.Min, 0), ("m_ReflectionProbeBoxProjection", Mode.Min, 0),
        };

        int n = 0;
        foreach (var (key, mode, val) in rules)
        {
            var (nt, c) = Scalar(t, key, mode, val);
            t = nt;
            n += c;
        }
        return (t, n);
    }

    private static (string Text, int Changes) DoUrpRenderer(string t, Options o)
    {
        int n = 0;
        if (!o.KeepPostProcessing)
        {
            // the 2D renderer uses m_PostProcessData
            var pat = new Regex("(\"(?:m_)?[pP]ostProcessData\"\\s*:\\s*\\{)([^{}]*)(\\})");
            var m = pat.Match(t);
            if (m.Success && Regex.IsMatch(m.Groups[2].Value, "\"m_PathID\"\\s*:\\s*[1-9]"))
            {
                string body = Regex.Replace(m.Groups[2].Value, "(\"m_PathID\"\\s*:\\s*)\\d+", "${1}0");
                body = Regex.Replace(body, "(\"m_FileID\"\\s*:\\s*)\\d+", "${1}0");
                t = t.Substring(0, m.Groups[2].Index) + body + t.Substring(m.Groups[2].Index + m.Groups[2].Length);
                n++;
            }
        }

        var (nt, c) = Scalar(t, "m_ShadowTransparentReceive", Mode.Min, 0);
        return (nt, n + c);
    }

    // ---------------- HDRP ----------------
    private static readonly (string Key, double Value)[] HdrpScalars =
    {
        ("m_DefaultMaterialQualityLevel", 1), ("supportSSAO", 0), ("supportSSR", 0), ("supportVolumetrics", 0),
        ("supportSubsurfaceScattering", 0), ("increaseSssSampleCount", 0), ("increaseResolutionOfVolumetrics", 0),
        ("supportDecals", 0), ("supportDistortion", 0), ("supportRuntimeDebugDisplay", 0), ("supportDitheringCrossFade", 0),
        ("maxShadowRequests", 32), ("shadowFilteringQuality", 0), ("shadowAtlasResolution", 2048),
        ("maxDirectionalShadowMapResolution", 1024), ("maxPunctualShadowMapResolution", 1024),
        ("maxAreaShadowMapResolution", 1024), ("skyReflectionSize", 128), ("reflectionCubemapSize", 128),
        ("planarReflectionAtlasSize", 1024), ("m_LutSize", 16),
    };

    private static readonly string[] HdrpArrayFirst =
    {
        "NearBlurSampleCount", "NearBlurMaxRadius", "FarBlurSampleCount", "FarBlurMaxRadius",
        "DoFResolution", "DoFHighQualityFiltering", "MotionBlurSampleCount", "BloomRes", "BloomHighQualityFiltering",
        "ChromaticAberrationMaxSamples", "AOStepCount", "AOFullRes", "AOMaximumRadiusPixels", "AOBilateralUpsample",
        "AODirectionCount", "ContactShadowSampleCount", "SSRMaxRaySteps",
    };

    private static (string Text, int Changes) DoHdrpAsset(string t)
    {
        int n = 0;
        foreach (var (key, val) in HdrpScalars)
        {
            var (nt, c) = Scalar(t, key, Mode.Min, val);
            t = nt;
            n += c;
        }
        foreach (var key in HdrpArrayFirst)
        {
            var (nt, c) = ArrayFlat(t, key);
            t = nt;
            n += c;
        }
        {
            var (nt, c) = ArrayFlat(t, "useContactShadow", "0", true);
            t = nt;
            n += c;
        }
        {
            var (nt, c) = ArrayFlat(t, "lodBias", "0.5", true);
            t = nt;
            n += c;
        }
        {
            var (nt, c) = ArrayFlat(t, "maximumLODLevel", "1", true);
            t = nt;
            n += c;
        }
        return (t, n);
    }

    // ---------------- Volume effects ----------------
    private static readonly HashSet<string> ActiveOff = new()
    {
        "MotionBlur", "AmbientOcclusion", "DepthOfField", "ScreenSpaceReflection",
        "GlobalIllumination", "VolumetricClouds"
    };

    private static bool IsVolumeName(string name) =>
        ActiveOff.Contains(name) || name == "ContactShadows" || name == "Bloom" || name == "HDShadowSettings";

    private static (string Text, int Changes) DoVolume(string name, string t)
    {
        int n = 0;
        if (ActiveOff.Contains(name))
        {
            var (nt, c) = TopActiveOff(t);
            t = nt;
            n += c;
        }
        else if (name == "ContactShadows")
        {
            var (nt, c) = BlockValue(t, "enable", "m_Value", 0);
            t = nt;
            n += c;
            (nt, c) = BlockValue(t, "m_SampleCount", "m_Value", 6, Mode.Min);
            t = nt;
            n += c;
        }
        else if (name == "Bloom")
        {
            // HDRP only; URP Bloom has no 'quality'
            var (nt, c) = BlockValue(t, "quality", "m_Value", 0, Mode.Min);
            t = nt;
            n += c;
        }
        else if (name == "HDShadowSettings")
        {
            var (nt, c) = BlockValue(t, "maxShadowDistance", "m_Value", 50, Mode.Min);
            t = nt;
            n += c;
            (nt, c) = BlockValue(t, "cascadeShadowSplitCount", "m_Value", 2, Mode.Min);
            t = nt;
            n += c;
            if (c > 0)
            {
                (nt, c) = BlockValue(t, "cascadeShadowSplitCount", "m_OverrideState", 1);
                t = nt;
                n += c;
                (nt, c) = BlockValue(t, "cascadeShadowSplit0", "m_OverrideState", 1);
                t = nt;
                n += c;
                (nt, c) = BlockValue(t, "cascadeShadowSplit0", "m_Value", 0.25, Mode.Max);
                t = nt;
                n += c;
            }
        }
        return (t, n);
    }

    public static string GetAssetName(string json)
    {
        var m = NameRegex.Match(json);
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>Recognises what a JSON dump is. Returns null if it isn't a URP/HDRP asset we handle.</summary>
    public static Handler? Classify(string t, Options options)
    {
        string name = GetAssetName(t);

        if (t.Contains("\"m_RendererDataList\"") && t.Contains("\"m_RenderScale\""))
            return new Handler("URP pipeline asset", x => DoUrpAsset(x, options));

        if (t.Contains("\"m_RendererFeatures\"") && (t.Contains("\"postProcessData\"") || t.Contains("\"m_PostProcessData\"")))
            return new Handler("URP renderer data", x => DoUrpRenderer(x, options));

        if (t.Contains("\"m_RenderPipelineSettings\""))
            return new Handler("HDRP pipeline asset", DoHdrpAsset);

        if (Regex.IsMatch(name, "(?i)ssao|ambientocclusion") && t.Contains("\"m_Active\"") && !t.Contains("\"m_AdvancedMode\""))
            return new Handler("URP SSAO feature", x => TopActiveOff(x, "m_Active"));

        if (IsVolumeName(name) && t.Contains("\"m_AdvancedMode\""))
            return new Handler("Volume effect: " + name, x => DoVolume(name, x));

        return null;
    }
}
