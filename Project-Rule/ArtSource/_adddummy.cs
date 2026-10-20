System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    t = a.GetType("RuleGame.Development.P3.AttackTestDummy");
    if (t != null) break;
}
if (t == null) return "找不到 AttackTestDummy 类型";
var go = GameObject.Find("Dummy");
if (go == null) return "场景里没有 Dummy";
if (go.GetComponent(t) != null) return "Dummy 上已经有训练靶组件了";
go.AddComponent(t);
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
return "已挂到 Dummy 并存盘（scene=" + scene.name + "）";
