using UnityEngine;
using Zenject;

namespace Modules.SceneModule
{
    [CreateAssetMenu(fileName = "SceneModuleInstaller", menuName = "Installers/SceneModuleInstaller")]
    public class SceneModuleInstaller : ScriptableObjectInstaller<SceneModuleInstaller>
    {
        public SceneDatabase sceneDatabase;
        public GameObject sceneModulePrefab;
        public GameObject titleScreenPrefab;
        
        public override void InstallBindings()
        {
            Container.Bind<SceneDatabase>().FromScriptableObject(sceneDatabase).AsSingle().NonLazy();
            Container.Bind<SceneModule>().FromComponentInNewPrefab(sceneModulePrefab).NetcodeFriendlyParenting().AsSingle().WithArguments(titleScreenPrefab).NonLazy();
        }
    }
}