#if UNITY_EDITOR
using UnityEditor;

namespace SynapticPro
{
    /// <summary>
    /// ESC-0234: manual escape hatch to drop the cached Mono.CSharp
    /// evaluator. Normally the AssemblyReload hook in NexusCSharpEval
    /// handles this automatically, but if run_csharp starts returning
    /// unexpected results (parse errors that used to work, stale type
    /// bindings after a package add, etc.) this menu lets the user force
    /// a re-initialization without restarting the Editor.
    /// </summary>
    public static class NexusResetMenu
    {
        [MenuItem("Tools/Synaptic Pro/Reset Internal State", priority = 100)]
        public static void ResetInternalState()
        {
            NexusCSharpEval.Reset();
            EditorUtility.DisplayDialog(
                "Synaptic AI Pro",
                "Internal state has been reset.\n\n・run_csharp evaluator が再生成されます\n・次回 run_csharp 呼び出し時に自動的に再初期化されます\n\n動作が異常な時はこのボタンをまず試してください。",
                "OK");
        }
    }
}
#endif
