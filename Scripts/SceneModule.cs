using System;
using System.Collections;
using Gui.SectionTitle;
using System.Collections.Generic;
using Modules.SceneModule.Data;
using Modules.ScriptedSequenceModule.IntroCutscene;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace Modules.SceneModule
{
    public class SceneModule : NetworkBehaviour
    {
        [Inject] private readonly SaveModule.SaveModule _saveModule;
        [Inject] private readonly AudioModule.AudioModule _audioModule;
        [Inject] private readonly NetworkModule.NetworkModule _networkModule;
        
        [HideInInspector] public SceneDatabase sceneDatabase;
        [Tooltip("Loading scene artificial delay in seconds")]
        public float loadingSceneDelay = 1.0f;

        public GameScene CurrentScene => sceneDatabase.Find(SceneManager.GetActiveScene().name, out int _, out int _);
        
        // Current section number, from 0 to inf; -1 represents a menu scene
        public int CurrentSection => SceneDatabase.GetSectionNumberFromSceneName(SceneManager.GetActiveScene().name);
        
        // Current level number, from 0 to inf; -1 represents a menu scene
        public int CurrentLevel => SceneDatabase.GetLevelNumberFromSceneName(SceneManager.GetActiveScene().name);
        
        public bool IsLoading { get; private set; } = false;
        public float LoadingProgress { get; private set; } = 0.0f;
        
        public event Action<GameScene, int, int> SceneLoadingStarted;
        public event Action<GameScene, int, int> SceneLoadingFinished;
        public event Action ClientsLoadingFinished;

        private GameObject _chapterTitleScreen;

        private void Start()
        {
            _audioModule.ChangeMusic(CurrentScene);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            NetworkManager.SceneManager.OnLoadEventCompleted += OnSceneLoadingFinished;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            
            NetworkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoadingFinished;
        }

        [Inject]
        public void Construct(SceneDatabase database, GameObject chapterTitleScreen)
        {
            sceneDatabase = database;
            _chapterTitleScreen = chapterTitleScreen;
        }

        public bool IsInGameplay()
        {
            return CurrentSection > -1 && CurrentLevel > -1;
        }
        
        public bool IsInAnyMenu()
        {
            return CurrentSection == -1 && CurrentLevel == -1;
        }

        public bool IsInMainMenu()
        {
            return SceneManager.GetActiveScene().name.Contains("MainMenu", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsInCredits()
        {
            return SceneManager.GetActiveScene().name.Contains("Credits", StringComparison.OrdinalIgnoreCase);
        }
        
        public bool IsInEnding()
        {
            return SceneManager.GetActiveScene().name.Contains("Ending", StringComparison.OrdinalIgnoreCase);
        }
        
        public void Continue()
        {
            _saveModule.GetProgress(out int level, out int section);
            
            if (level == -1)
            {
                level = 0;
            }

            if (section == -1)
            {
                section = 0;
            }
            
            LoadLevel(section, level);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void LoadLevelServerRpc(int section, int level)
        {
            LoadLevelClientRpc(section, level);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void LoadLevelClientRpc(int section, int level)
        {
            Time.timeScale = 1.0f;
            LoadLevel(section, level);
        }
        
        private void LoadLevel(int section, int level)
        {
            StartCoroutine(LoadSceneCoroutine(section, level, true));
        }

        public void GetNextLevel(out int level, out int section)
        {
            section = CurrentSection;
            level = CurrentLevel;
            
            if (level < sceneDatabase.LevelCount(section) - 1)
            {
                level++;
            }
            else if (section < sceneDatabase.SectionCount() - 1)
            {
                section++;
                level = 0;
            }
        }
        
        [ContextMenu("Load Next Level")]
        public void LoadNextLevel()
        {
            if (CurrentSection == -1 || CurrentLevel == -1)
            {
                LoadLevel(0, 0);
                return;
            }
            
            int section = CurrentSection;
            int level = CurrentLevel;
            
            if (level < sceneDatabase.LevelCount(section) - 1)
            {
                level++;
            }
            else if (section < sceneDatabase.SectionCount() - 1)
            {
                section++;
                level = 0;
            }
            else
            {
                LoadEndingScene();
                return;
            }

            LoadLevel(section, level);
        }

        [ContextMenu("Load Previous Level")]
        public void LoadPreviousLevel()
        {
            if (CurrentSection == -1 || CurrentLevel == -1)
            {
                Debug.LogWarning("Can't determine previous level, because we are in menu scene");
                return;
            }
            
            int section = CurrentSection;
            int level = CurrentLevel;
            
            if (level > 0)
            {
                level--;
            }
            else if (section > 0)
            {
                section--;
                level = sceneDatabase.LevelCount(section) - 1;
            }
            else
            {
                LoadMainMenu();
                return;
            }
            
            LoadLevel(section, level);
        }
        
        
        [ContextMenu("Reload Level")]
        public void ReloadLevel()
        {
            if (CurrentSection == -1 || CurrentLevel == -1)
            {
                Debug.LogWarning("Shouldn't reload non-level scenes");
                return;
            }

            LoadLevel(CurrentSection, CurrentLevel);
        }

        [ContextMenu("Load Ending Scene")]
        public void LoadEndingScene()
        {
            _networkModule.Disconnect(false);
            StartCoroutine(LoadSceneCoroutine(SceneDatabase.EndingSceneIndex, -1, false));
        }
        
        [ContextMenu("Load Main Menu")]
        public void LoadMainMenu()
        {
            StartCoroutine(LoadSceneCoroutine(SceneDatabase.MainMenuIndex, -1, true));
        }
        
        public void LoadCredits()
        {
            StartCoroutine(LoadSceneCoroutine(SceneDatabase.CreditsSceneIndex, -1, true));
        }

        public float CalculatePercentProgress(int level, int section)
        {
            int allLevels = 0;
            int currentLevel = 0;
            for (var index = 0; index < sceneDatabase.SectionCount(); index++)
            {
                var currentSection = sceneDatabase.GetSection(index);
                
                allLevels += currentSection.sectionLevels.Length - 1;

                if (index < section)
                {
                    currentLevel += currentSection.sectionLevels.Length - 1;
                }
                else if (index == section)
                {
                    currentLevel += level;
                }
            }

            return ((float)(currentLevel) / allLevels) * 100.0f;
        }

        public void ExitGame()
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        }

        private IEnumerator LoadSceneCoroutine(int sectionNumber, int levelNumber, bool showLoadingScreen)
        {
            if (CurrentSection == -1 || CurrentLevel == -1)
            {
                IntroCutsceneController cc = FindFirstObjectByType<IntroCutsceneController>();
                if (cc)
                {
                    if (sectionNumber == 0 && levelNumber == 0)
                    {
                        cc.PlayGlitchOnly = false;
                    }
                    else
                    {
                        cc.PlayGlitchOnly = true;
                    }
                    
                    cc.PlayIntroCutscene();
                    
                    yield return new WaitUntil(() => cc.IsFinished);
                }
            }
            
            GameScene scene = sceneDatabase.GetScene(sectionNumber, levelNumber);
            
            SceneLoadingStarted?.Invoke(scene, sectionNumber, levelNumber);
            IsLoading = true;

            if (!IsServer && !IsHost && !IsClient)
            {
                SceneManager.LoadScene(scene.sceneName);
                OnSceneLoadingFinishedLocal(scene, sectionNumber, levelNumber);
                yield break;
            }
            
            _saveModule.SaveProgress(CurrentLevel, CurrentSection);

            if (showLoadingScreen)
            {
                // Load scene with loading screen synchronously
                if (!NetworkManager.Singleton.ShutdownInProgress && NetworkManager.IsServer)
                {
                    SceneEventProgressStatus loadingSceneStatus = NetworkManager.Singleton.SceneManager.LoadScene(sceneDatabase.GetScene(SceneDatabase.LoadingSceneIndex).sceneName, LoadSceneMode.Single);
                }
                else if (!NetworkManager.IsServer && !NetworkManager.IsClient)
                {
                    SceneManager.LoadScene(sceneDatabase.GetScene(SceneDatabase.LoadingSceneIndex).sceneName);
                }
                
                yield return new WaitForSeconds(loadingSceneDelay);
            }

            if (!NetworkManager.Singleton.ShutdownInProgress && NetworkManager.IsServer)
            {
                SceneEventProgressStatus status = NetworkManager.SceneManager.LoadScene(scene.sceneName, LoadSceneMode.Single);
                
                if (status != SceneEventProgressStatus.Started)
                {
                    Debug.Log($"Failed to load scene {scene.sceneName}");
                }
                
                LoadingProgress = 1.0f;
            }
            else if (!NetworkManager.IsServer && !NetworkManager.IsClient)
            {
                // Load target scene asynchronously
                AsyncOperation loadSceneAsync = SceneManager.LoadSceneAsync(scene.sceneName);
                // Prevent scene from activating as long as the progress is less than 90%
                loadSceneAsync.allowSceneActivation = false;
                
                while (!loadSceneAsync.isDone)
                {
                    LoadingProgress = loadSceneAsync.progress;
                
                    if (LoadingProgress >= 0.9f)
                    {
                        loadSceneAsync.allowSceneActivation = true;
                    }
                    
                    yield return new WaitForEndOfFrame();
                }
                
                OnSceneLoadingFinishedLocal(scene, sectionNumber, levelNumber);
            }
        }

        public void CreateSectionTitle()
        {
            Instantiate(_chapterTitleScreen).GetComponent<SectionTitleController>().SetSectionText(CurrentSection, sceneDatabase.GetSection(CurrentSection).sectionName);
        }
        
        private void OnSceneLoadingFinished(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (clientsTimedOut.Count > 0)
            {
                Debug.LogWarning($"Clients Timed out count {clientsTimedOut.Count}, please add timeout handling");
            }
            
            
            GameScene scene = sceneDatabase.Find(sceneName, out int sectionNumber, out int levelNumber);
            
            if (NetworkManager.Singleton.ShutdownInProgress || (!NetworkManager.IsClient && !NetworkManager.IsServer))
            {
                ClientsLoadingFinished?.Invoke();
                OnSceneLoadingFinishedLocal(scene, sectionNumber, levelNumber);
                
                return;
            }

            if (IsServer && clientsCompleted.Count == NetworkManager.ConnectedClients.Count)
            {
                if (sectionNumber > -1 && levelNumber > -1)
                {
                    OnSceneLoadingFinishedClientRpc(sectionNumber, levelNumber);
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost, RequireOwnership = false)]
        private void OnSceneLoadingFinishedClientRpc(int sectionNumber, int levelNumber)
        {
            ClientsLoadingFinished?.Invoke();
            GameScene scene = sceneDatabase.GetScene(sectionNumber, levelNumber);
            OnSceneLoadingFinishedLocal(scene, sectionNumber, levelNumber);
        }

        private void OnSceneLoadingFinishedLocal(GameScene scene, int sectionNumber, int levelNumber)
        {
            _audioModule.ChangeMusic(scene);
            SceneLoadingFinished?.Invoke(scene, sectionNumber, levelNumber);
            IsLoading = false;
        }

        public bool IsInEscapeLevel()
        {
            return CurrentScene.sceneType == SceneType.Escape;
        }
    }
}
