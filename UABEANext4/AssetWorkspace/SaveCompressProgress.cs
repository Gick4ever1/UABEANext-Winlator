using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace UABEANext4.AssetWorkspace;

// Passes real bundle-compression progress to the status bar.
public class SaveCompressProgress : IAssetBundleCompressProgress
{
    private readonly Workspace _workspace;
    private readonly string _text;

    public SaveCompressProgress(Workspace workspace, string text)
    {
        _workspace = workspace;
        _text = text;
    }

    public void SetProgress(float progress)
    {
        _workspace.SetProgressThreadSafe(progress, _text);
    }
}
