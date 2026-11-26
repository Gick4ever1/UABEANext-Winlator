using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.ImportExport;

namespace UABEANext4.Logic;

/// <summary>
/// Finds the URP / HDRP assets a game really uses (by following the PathIDs from
/// QualitySettings and GraphicsSettings in globalgamemanagers into globalgamemanagers.assets)
/// and lowers their values. Edits are applied to the loaded assets in memory; save afterwards.
/// </summary>
public static class LowPipelineAuto
{
    public const string AssetsFileName = "globalgamemanagers.assets";

    public sealed record AssetUpdate(AssetInst Asset, byte[] Data);

    public sealed class Result
    {
        public string Pipeline { get; set; } = "NONE";
        public List<string> Lines { get; } = new();
        public List<AssetUpdate> Updates { get; } = new();
    }

    private sealed class Target
    {
        public AssetInst Asset = null!;
        public AssetTypeValueField BaseField = null!;
        public string Text = "";
        public string Name = "";
        public LowRenderPipelinePreset.Handler Handler = null!;
    }

    private static readonly HashSet<string> FollowKeys = new()
    {
        "m_RendererDataList", "m_RendererData", "m_RendererFeatures", "components", "m_DefaultVolumeProfile"
    };

    private static readonly Regex VolumeProfileRegex = new("(?i)volumeprofile");

    private static bool TryPtr(JToken? token, out int fileId, out long pathId)
    {
        fileId = 0;
        pathId = 0;
        if (token is JObject obj
            && obj.TryGetValue("m_FileID", out var f)
            && obj.TryGetValue("m_PathID", out var p)
            && f.Type == JTokenType.Integer
            && p.Type == JTokenType.Integer)
        {
            fileId = (int)f;
            pathId = (long)p;
            return true;
        }
        return false;
    }

    private static void CollectPointers(JToken? v, List<(int FileId, long PathId)> output)
    {
        if (v == null)
            return;

        if (TryPtr(v, out int f, out long p))
        {
            output.Add((f, p));
        }
        else if (v is JObject obj)
        {
            foreach (var prop in obj.Properties())
                CollectPointers(prop.Value, output);
        }
        else if (v is JArray arr)
        {
            foreach (var item in arr)
                CollectPointers(item, output);
        }
    }

    private static void FollowPointers(JToken? o, List<(int FileId, long PathId)> output)
    {
        if (o is JObject obj)
        {
            foreach (var prop in obj.Properties())
            {
                if (FollowKeys.Contains(prop.Name) || VolumeProfileRegex.IsMatch(prop.Name))
                    CollectPointers(prop.Value, output);
                else
                    FollowPointers(prop.Value, output);
            }
        }
        else if (o is JArray arr)
        {
            foreach (var item in arr)
                FollowPointers(item, output);
        }
    }

    private static void CollectRoots(JToken? o, HashSet<string> keys, List<(int FileId, long PathId)> output)
    {
        if (o is JObject obj)
        {
            foreach (var prop in obj.Properties())
            {
                if (keys.Contains(prop.Name))
                {
                    if (TryPtr(prop.Value, out int f, out long p))
                        output.Add((f, p));
                }
                else
                {
                    CollectRoots(prop.Value, keys, output);
                }
            }
        }
        else if (o is JArray arr)
        {
            foreach (var item in arr)
                CollectRoots(item, keys, output);
        }
    }

    private static string DumpJson(AssetTypeValueField baseField)
    {
        using var ms = new MemoryStream();
        var exporter = new AssetExport(ms);
        exporter.DumpJsonAsset(baseField);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static JToken? TryParse(string json)
    {
        try
        {
            return JToken.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detects the pipeline and prepares the edits. Safe to run on a background thread;
    /// apply <see cref="Result.Updates"/> on the UI thread afterwards.
    /// </summary>
    public static Result Analyze(Workspace workspace, AssetsFileInstance ggm, LowRenderPipelinePreset.Options options)
    {
        var result = new Result();
        var warnings = new List<string>();
        var queue = new Queue<(AssetsFileInstance Src, int FileId, long PathId)>();

        // 1) roots: render pipeline pointers in QualitySettings (47) and GraphicsSettings (30)
        foreach (var info in ggm.file.AssetInfos)
        {
            bool isQuality = info.TypeId == 47;
            bool isGraphics = info.TypeId == 30;
            if (!isQuality && !isGraphics)
                continue;

            AssetTypeValueField? baseField;
            try
            {
                baseField = workspace.GetBaseField(ggm, info.PathId);
            }
            catch
            {
                baseField = null;
            }

            if (baseField == null)
            {
                warnings.Add($"Could not read {(isQuality ? "QualitySettings" : "GraphicsSettings")}.");
                continue;
            }

            var keys = isQuality
                ? new HashSet<string> { "customRenderPipeline" }
                : new HashSet<string> { "m_CustomRenderPipeline", "m_DefaultRenderPipeline" };

            var roots = new List<(int FileId, long PathId)>();
            CollectRoots(TryParse(DumpJson(baseField)), keys, roots);
            foreach (var (fileId, pathId) in roots)
                queue.Enqueue((ggm, fileId, pathId));
        }

        // 2) follow the pointers
        var visited = new HashSet<string>();
        var targets = new List<Target>();
        var kinds = new List<string>();
        int unresolved = 0;

        while (queue.Count > 0)
        {
            var (src, fileId, pathId) = queue.Dequeue();
            if (pathId == 0)
                continue;

            // pointers inside the assets file must stay in that file
            if (src != ggm && fileId != 0)
            {
                string w = $"A pointer (FileID {fileId}, PathID {pathId}) points into another file and was not followed.";
                if (visited.Add("x:" + w))
                    warnings.Add(w);
                continue;
            }

            AssetInst? inst;
            try
            {
                inst = workspace.GetAssetInst(src, fileId, pathId);
            }
            catch
            {
                inst = null;
            }

            if (inst == null)
            {
                if (visited.Add($"?:{src.name}:{fileId}:{pathId}"))
                {
                    unresolved++;
                    warnings.Add($"Asset {pathId} could not be found (is {AssetsFileName} loaded?).");
                }
                continue;
            }

            // from globalgamemanagers, only the assets file is of interest
            if (src == ggm && !inst.FileInstance.name.Equals(AssetsFileName, StringComparison.OrdinalIgnoreCase))
            {
                string w = $"Pipeline asset {pathId} is in {inst.FileInstance.name}, not {AssetsFileName}; skipped.";
                if (visited.Add("x:" + w))
                    warnings.Add(w);
                continue;
            }

            if (!visited.Add($"{inst.FileInstance.name}:{inst.PathId}"))
                continue;

            AssetTypeValueField? bf;
            try
            {
                bf = workspace.GetBaseField(inst);
            }
            catch
            {
                bf = null;
            }

            if (bf == null)
            {
                unresolved++;
                warnings.Add($"Asset {pathId} could not be read (a script template may be missing).");
                continue;
            }

            string text = DumpJson(bf);
            string name = LowRenderPipelinePreset.GetAssetName(text);
            if (name.Length == 0)
                name = "asset " + pathId;

            var handler = LowRenderPipelinePreset.Classify(text, options);
            if (handler != null)
            {
                kinds.Add(handler.Kind);
                targets.Add(new Target { Asset = inst, BaseField = bf, Text = text, Name = name, Handler = handler });
            }
            else
            {
                warnings.Add($"{name} was not recognised and will not be edited.");
            }

            var next = new List<(int FileId, long PathId)>();
            FollowPointers(TryParse(text), next);
            foreach (var (nf, np) in next)
                queue.Enqueue((inst.FileInstance, nf, np));
        }

        // 3) pipeline type
        bool urp = kinds.Exists(k => k.Contains("URP"));
        bool hdrp = kinds.Exists(k => k.Contains("HDRP"));
        result.Pipeline = urp && hdrp ? "MIXED"
            : urp ? "URP"
            : hdrp ? "HDRP"
            : (targets.Count > 0 || unresolved > 0 ? "UNKNOWN" : "NONE");

        if (targets.Count == 0 && unresolved == 0)
        {
            result.Lines.Add("No custom render pipeline is assigned, so this game uses the built-in pipeline.");
            result.Lines.Add("Use the Low Graphics Preset button on QualitySettings instead.");
            return result;
        }

        result.Lines.Add("Render pipeline: " + result.Pipeline);

        if (hdrp && !kinds.Exists(k => k.StartsWith("Volume effect")) && unresolved == 0)
        {
            warnings.Add("HDRP asset found but no volume effects were reached.");
        }

        // 4) apply the lowering and prepare the new asset data
        foreach (var target in targets)
        {
            var (newText, changes) = target.Handler.Apply(target.Text);
            if (changes == 0)
            {
                result.Lines.Add($"Already low: {target.Name} ({target.Handler.Kind})");
                continue;
            }

            byte[]? data;
            string? error;
            try
            {
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(newText));
                var importer = new AssetImport(ms, workspace.Manager.GetRefTypeManager(target.Asset.FileInstance));
                data = importer.ImportJsonAsset(target.BaseField.TemplateField, out error);
            }
            catch (Exception ex)
            {
                data = null;
                error = ex.Message;
            }

            if (data == null)
            {
                result.Lines.Add($"Error in {target.Name}: {error}");
                continue;
            }

            result.Updates.Add(new AssetUpdate(target.Asset, data));
            result.Lines.Add($"Lowered {changes} values: {target.Name} ({target.Handler.Kind})");
        }

        foreach (var w in warnings)
            result.Lines.Add("Note: " + w);

        return result;
    }
}
