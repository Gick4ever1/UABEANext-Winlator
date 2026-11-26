using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UABEANext4.Logic;

/// <summary>
/// Lowers every quality preset in a QualitySettings JSON dump to its minimum values.
/// Only ever makes a value lower quality, never higher.
/// </summary>
public static class LowQualityPreset
{
    private enum RuleMode
    {
        Min, // never go above the target
        Max, // never go below the target
        Set  // force the target
    }

    // Texture resolution limit: 0 = full, 1 = half, 2 = quarter, 3 = eighth (lowest)
    private const int TextureMipmapLimit = 3;

    private static readonly Dictionary<string, (RuleMode Kind, double Value)> Rules = new()
    {
        ["pixelLightCount"] = (RuleMode.Min, 0),
        ["shadows"] = (RuleMode.Min, 0),
        ["shadowResolution"] = (RuleMode.Min, 0),
        ["shadowCascades"] = (RuleMode.Min, 1),
        ["shadowDistance"] = (RuleMode.Min, 15),
        ["shadowmaskMode"] = (RuleMode.Min, 0),
        ["skinWeights"] = (RuleMode.Min, 1),
        ["blendWeights"] = (RuleMode.Min, 1),
        ["globalTextureMipmapLimit"] = (RuleMode.Max, TextureMipmapLimit),
        ["textureQuality"] = (RuleMode.Max, TextureMipmapLimit),
        ["anisotropicTextures"] = (RuleMode.Min, 0),
        ["antiAliasing"] = (RuleMode.Min, 0),
        ["softParticles"] = (RuleMode.Set, 0),
        ["softVegetation"] = (RuleMode.Set, 0),
        ["realtimeReflectionProbes"] = (RuleMode.Set, 0),
        ["billboardsFaceCameraPosition"] = (RuleMode.Set, 0),
        ["vSyncCount"] = (RuleMode.Min, 0),
        ["realtimeGICPUUsage"] = (RuleMode.Min, 25),
        ["lodBias"] = (RuleMode.Min, 0.1),
        ["maximumLODLevel"] = (RuleMode.Max, 1),
        ["enableLODCrossFade"] = (RuleMode.Set, 0),
        ["particleRaycastBudget"] = (RuleMode.Min, 4),
        ["asyncUploadBufferSize"] = (RuleMode.Min, 8),
        ["asyncUploadPersistentBuffer"] = (RuleMode.Set, 0),
        ["terrainQualityOverrides"] = (RuleMode.Set, 255),
        ["terrainPixelError"] = (RuleMode.Max, 200),
        ["terrainDetailDensityScale"] = (RuleMode.Min, 0.1),
        ["terrainBasemapDistance"] = (RuleMode.Min, 150),
        ["terrainDetailDistance"] = (RuleMode.Min, 20),
        ["terrainTreeDistance"] = (RuleMode.Min, 300),
        ["terrainBillboardStart"] = (RuleMode.Min, 30),
        ["terrainMaxTrees"] = (RuleMode.Min, 10),
        // default (current) quality level = first preset
        ["m_CurrentQuality"] = (RuleMode.Set, 0),
        // texture streaming with a 256 MB budget
        ["streamingMipmapsActive"] = (RuleMode.Set, 1),
        ["streamingMipmapsMemoryBudget"] = (RuleMode.Min, 256),
    };

    /// <summary>
    /// Applies the preset in place. Returns how many values changed;
    /// <paramref name="presets"/> is how many quality presets were found.
    /// </summary>
    public static int Apply(JToken root, out int presets)
    {
        presets = 0;
        int changed = 0;
        Walk(root, ref presets, ref changed);
        return changed;
    }

    private static void Walk(JToken token, ref int presets, ref int changed)
    {
        if (token is JObject obj)
        {
            if (obj.ContainsKey("pixelLightCount"))
                presets++;

            foreach (var prop in obj.Properties().ToList())
            {
                if (prop.Value is JValue value && Rules.TryGetValue(prop.Name, out var rule))
                {
                    if (ApplyRule(value, rule.Kind, rule.Value))
                        changed++;
                }
                else
                {
                    Walk(prop.Value, ref presets, ref changed);
                }
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
                Walk(item, ref presets, ref changed);
        }
    }

    private static bool ApplyRule(JValue value, RuleMode kind, double target)
    {
        switch (value.Type)
        {
            case JTokenType.Boolean:
            {
                bool newBool = target != 0;
                if (value.Value is bool oldBool && oldBool == newBool)
                    return false;

                value.Value = newBool;
                return true;
            }
            case JTokenType.Integer:
            case JTokenType.Float:
            {
                double oldNum = value.ToObject<double>();
                double result = kind switch
                {
                    RuleMode.Min => Math.Min(oldNum, target),
                    RuleMode.Max => Math.Max(oldNum, target),
                    _ => target
                };

                if (Math.Abs(result - oldNum) < 1e-9)
                    return false;

                if (value.Type == JTokenType.Integer)
                    value.Value = (long)Math.Round(result);
                else
                    value.Value = result;
                return true;
            }
            default:
                return false;
        }
    }
}
