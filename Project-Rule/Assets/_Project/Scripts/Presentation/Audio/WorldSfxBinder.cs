using RuleGame.Gameplay.Enemy;
using RuleGame.UI.Launch;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleGame.Presentation.Audio
{
    /// <summary>流式分区到场时补齐内嵌对象；运行时召唤物从已接线的敌人 Prefab 继承。</summary>
    [DisallowMultipleComponent]
    public sealed class WorldSfxBinder : MonoBehaviour
    {
        private void OnEnable() => SceneManager.sceneLoaded += SceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= SceneLoaded;
        private void Start()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) Install(SceneManager.GetSceneAt(i));
        }
        private void SceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode) => Install(scene);
        public static void Install(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MainMenuPresenter menu in root.GetComponentsInChildren<MainMenuPresenter>(true))
                    if (menu.GetComponent<MainMenuSfxPresenter>() == null)
                        menu.gameObject.AddComponent<MainMenuSfxPresenter>();
                foreach (EnemyActor actor in root.GetComponentsInChildren<EnemyActor>(true))
                    if (actor.GetComponent<EnemySfxPresenter>() == null) actor.gameObject.AddComponent<EnemySfxPresenter>();
                foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component != null && MechanismSfxPresenter.Supports(component)
                        && component.GetComponent<MechanismSfxPresenter>() == null)
                        component.gameObject.AddComponent<MechanismSfxPresenter>();
            }
        }
    }
}
