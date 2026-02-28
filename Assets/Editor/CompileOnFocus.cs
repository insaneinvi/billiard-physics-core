using UnityEditor;
using UnityEngine;

[InitializeOnLoad] // 确保在编辑器启动时运行此类
public static class CompileOnFocus
{
    static CompileOnFocus()
    {
        // 订阅编辑器焦点变化事件
        EditorApplication.focusChanged += OnEditorFocusChanged;
    }

    private static void OnEditorFocusChanged(bool isFocused)
    {
        // 当编辑器从非聚焦状态变为聚焦状态时
        if (isFocused)
        {
            // 避免在已经处于编译状态时再次请求编译
            if (!EditorApplication.isCompiling)
            {
                Debug.Log($"[CompileOnFocus] Editor gained focus. Requesting script compilation...");
                // 请求编译所有脚本
                AssetDatabase.Refresh(); // 确保资产数据库是最新的
                EditorApplication.QueuePlayerLoopUpdate(); // 请求脚本编译
            }
        }
    }
}
