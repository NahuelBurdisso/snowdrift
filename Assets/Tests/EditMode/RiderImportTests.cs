using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class RiderImportTests
{
    [Test]
    public void RiderPrefab_RootMotionDisabled()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Rider.prefab");
        Assert.IsNotNull(prefab, "Rider prefab missing — run Snowdrift/Build Rider Prefab");
        Assert.IsFalse(prefab.GetComponentInChildren<Animator>(true).applyRootMotion);
    }

    [Test]
    public void AnimationImports_BakeRootIntoPose()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Art/Animations" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            foreach (var clip in imp.clipAnimations)
                Assert.IsTrue(clip.lockRootRotation && clip.lockRootHeightY && clip.lockRootPositionXZ, path);
        }
    }
}
