using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;


namespace LordAshes
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(LordAshes.FileAccessPlugin.Guid)]
    [BepInDependency(LordAshes.StatMessaging.Guid)]
    public partial class BodyAuraPlugin : BaseUnityPlugin
    {
        // Plugin info
        public const string Name = "Body Aura Plug-In";              
        public const string Guid = "org.lordashes.plugins.bodyaura";
        public const string Version = "1.1.2.0";

        public string data = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

        public const float baseMagicNumber = 0.570697f;

        // Configuration
        private ConfigEntry<KeyboardShortcut> triggerKey { get; set; }
        private ConfigEntry<KeyboardShortcut> triggerReapplyKey { get; set; }
        private ConfigEntry<float> displacement { get; set; }
        private ConfigEntry<bool> displacementMultiplier { get; set; }

        private static float delayAuraProcessing { get; set; } = 3.0f;

    private static BoardState boardReady = BoardState.boardNotReady;

        private static List<StatMessaging.Change> backlog = new List<StatMessaging.Change>();

        public enum BoardState
        { 
            boardNotReady = 0,
            boardDelayForMiniBuild = 1,
            boardReady = 2
        }

        void Awake()
        {
            UnityEngine.Debug.Log("Body Aura Plugin: Active.");

            triggerKey = Config.Bind("Hotkeys", "Extract Mesh", new KeyboardShortcut(KeyCode.B, KeyCode.LeftControl));
            triggerReapplyKey = Config.Bind("Hotkeys", "Reapply Body Auras", new KeyboardShortcut(KeyCode.R, KeyCode.RightControl));
            displacement = Config.Bind("Settings", "Aura Displacement", 0.01f);
            displacementMultiplier = Config.Bind("Settings", "Use Aura Displacement Size Multiplier", true);
            delayAuraProcessing = Config.Bind("Settings", "Delay Aura Processing On Board Load", 3.0f).Value;

            StatMessaging.Subscribe(BodyAuraPlugin.Guid, RequestHandler);

            Utility.PostOnMainPage(this.GetType());
        }

        void Update()
        {
            if (Utility.isBoardLoaded())
            {
                if (boardReady == BoardState.boardNotReady)
                {
                    boardReady = BoardState.boardDelayForMiniBuild;
                    Debug.Log("Body Aura Plugin: Board Loaded. Delaying Body Aura Request Message Processing");
                    StartCoroutine("DelayMessageProcessing", new object[] { delayAuraProcessing });
                }

                if (Utility.StrictKeyCheck(triggerKey.Value))
                {
                    CreatureBoardAsset asset;
                    CreaturePresenter.TryGetAsset(LocalClient.SelectedCreatureId, out asset);
                    if (asset != null)
                    {
                        SystemMessage.AskForTextInput("Aura Texture", "Aura Texture Name", "OK", (texName) =>
                        {
                            StatMessaging.SetInfo(asset.Creature.CreatureId, BodyAuraPlugin.Guid, texName);
                        }, null, "Remove", () =>
                        {
                            StatMessaging.ClearInfo(asset.Creature.CreatureId, BodyAuraPlugin.Guid);
                        }, "");
                    }
                }

                if (Utility.StrictKeyCheck(triggerReapplyKey.Value))
                {
                    Debug.Log("Body Aura Plugin: Reapplying Body Auras");
                    foreach(CreatureBoardAsset asset in CreaturePresenter.AllCreatureAssets)
                    {
                        string value = StatMessaging.ReadInfo(asset.Creature.CreatureId, BodyAuraPlugin.Guid);
                        if(value!=null && value!="")
                        {
                            RequestHandler(new StatMessaging.Change[]
                            {
                                new StatMessaging.Change()
                                {
                                    action = StatMessaging.ChangeType.modified,
                                    cid = asset.Creature.CreatureId,
                                    key = BodyAuraPlugin.Guid,
                                    previous = "",
                                    value = value
                                }
                            });
                        }
                    }
                }
            }
            else
            {
                if (boardReady != BoardState.boardNotReady) { boardReady = BoardState.boardNotReady; }
            }
        }

        private IEnumerator DelayMessageProcessing(object[] inputs)
        {
            yield return new WaitForSeconds((float)inputs[0]);
            boardReady = BoardState.boardReady;
            Debug.Log("Body Aura Plugin: Processing Backlog");
            RequestHandler(backlog.ToArray());
            Debug.Log("Body Aura Plugin: Processing Body Aura Request Messages");
            backlog.Clear();
        }

        private void RequestHandler(StatMessaging.Change[] changes)
        {
            if (boardReady != BoardState.boardReady)
            {
                backlog.AddRange(changes);
            }
            else
            {
                foreach (StatMessaging.Change change in changes)
                {
                    StatMessaging.ChangeType action = (change.value == "" && change.action != StatMessaging.ChangeType.removed) ? StatMessaging.ChangeType.removed : change.action;
                    switch (action)
                    {
                        case StatMessaging.ChangeType.removed:
                            if (GameObject.Find(change.cid + ".BodyAura") != null) { GameObject.Destroy(GameObject.Find(change.cid + ".BodyAura")); }
                            break;
                        default:
                            CreatureBoardAsset asset = null;
                            CreaturePresenter.TryGetAsset(change.cid, out asset);
                            if (asset != null)
                            {
                                StartCoroutine("BuildBodyAura", new object[] { change.value, asset });
                            }
                            break;
                    }
                }
            }
        }

        IEnumerator BuildBodyAura(object[] inputs)
        {
            yield return new WaitForSeconds(0.1f);
            string texName = (string)inputs[0];
            CreatureBoardAsset asset = (CreatureBoardAsset)inputs[1];
            Debug.Log("Body Aura Plugin: Getting Shader");
            AssetBundle assetBundle = FileAccessPlugin.AssetBundle.Load(data + "/siStandardAura");
            Shader shader = (Shader)assetBundle.LoadAsset<Shader>("siStandardAura");
            assetBundle.Unload(false);
            Debug.Log("Body Aura Plugin: Creating Aura Object");
            if (GameObject.Find(asset.Creature.CreatureId + ".BodyAura")!=null) { GameObject.Destroy(GameObject.Find(asset.Creature.CreatureId + ".BodyAura")); }
            GameObject aura = new GameObject(); 
            aura.name = asset.Creature.CreatureId + ".BodyAura";
            aura.transform.localScale = asset.CreatureLoaders[0].transform.localScale * (asset.BaseRadius / baseMagicNumber);
            aura.transform.position = asset.CreatureLoaders[0].transform.position;
            aura.transform.eulerAngles = asset.CreatureLoaders[0].transform.eulerAngles;
            aura.transform.SetParent(asset.CreatureLoaders[0].transform);
            Debug.Log("Body Aura Plugin: Creating Aura MeshFilter");
            MeshFilter srcMeshFilter = asset.CreatureLoaders[0].LoadedAsset.GetComponent<MeshFilter>();
            MeshFilter dstMeshFilter = aura.AddComponent<MeshFilter>();
            Renderer srcRenderer = null;
            Renderer dstRenderer = null;
            if (asset.CreatureLoaders[0].LoadedAsset.GetComponent<MeshRenderer>() != null)
            {
                Debug.Log("Body Aura Plugin: Creating Aura MeshRenderer");
                srcRenderer = asset.CreatureLoaders[0].LoadedAsset.GetComponent<MeshRenderer>();
                dstRenderer = aura.AddComponent<MeshRenderer>();
            }
            else
            {
                Debug.Log("Body Aura Plugin: Creating Aura SkinnedMeshRenderer");
                srcRenderer = asset.CreatureLoaders[0].LoadedAsset.GetComponent<SkinnedMeshRenderer>();
                dstRenderer = aura.AddComponent<MeshRenderer>();
            }

            if (srcMeshFilter!=null && dstMeshFilter!=null)
            {
                if (srcMeshFilter.mesh!=null)
                {
                    Debug.Log("Body Aura Plugin: Aplying Mesh");
                    dstMeshFilter.mesh = srcMeshFilter.mesh;
                }
            }

            if (srcRenderer!=null && dstRenderer!=null)
            {
                float distance = displacement.Value;
                Debug.Log("Body Aura Plugin: Distance = "+distance+", Scale Multiplier = "+displacementMultiplier.Value+", Scale = "+ asset.CreatureLoaders[0].transform.localScale.y);
                if (displacementMultiplier.Value && asset.CreatureLoaders[0].transform.localScale.y<1)
                {
                    distance = (distance * (1/asset.CreatureLoaders[0].transform.localScale.y));
                    Debug.Log("Body Aura Plugin: Adjusted Distance = " + distance);
                }

                Debug.Log("Body Aura Plugin: Applying Material");
                dstRenderer.material = srcRenderer.material;
                Debug.Log("Body Aura Plugin: Applying Shader " + shader.name);
                dstRenderer.material.shader = shader;
                Debug.Log("Body Aura Plugin: Verifying Shader " + dstRenderer.material.shader.name);
                Debug.Log("Body Aura Plugin: Applying Dispalcement");
                dstRenderer.material.SetFloat("_Float0", distance);
                Debug.Log("Body Aura Plugin: Applying Texture " + texName);
                dstRenderer.material.mainTexture = FileAccessPlugin.Image.LoadTexture("Images/"+texName);
            }
        }
    }
}
