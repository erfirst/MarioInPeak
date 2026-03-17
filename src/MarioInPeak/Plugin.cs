using BepInEx;
using BepInEx.Logging;
using LibSM64;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MarioInPeak
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static new ManualLogSource Logger { get; private set; }

        static List<SM64Mario> _marios = new List<SM64Mario>();
        static List<SM64DynamicTerrain> _surfaceObjects = new List<SM64DynamicTerrain>();

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
            Logger.LogDebug("Subscribing to scene events");

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            InitializeSM64();
        }

        public void InitializeSM64()
        {
            byte[] rom;
            try
            {
                rom = File.ReadAllBytes("sm64.z64");
            }
            catch (FileNotFoundException)
            {
                Logger.LogError("Super Mario 64 US ROM 'sm64.z64' not found next to game .exe!");
                return;
            }

            using (var cryptoProvider = new SHA1CryptoServiceProvider())
            {
                byte[] hash = cryptoProvider.ComputeHash(rom);
                StringBuilder result = new StringBuilder(4 * 2);
                for (int i = 0; i < 4; i++)
                    result.Append(hash[i].ToString("x2"));

                string hashStr = result.ToString();
                if (hashStr != "9bef1128")
                {
                    Logger.LogError($"ROM SHA-1 mismatch. Expected: 9bef1128, Got: {hashStr}");
                    return;
                }
            }

            Interop.GlobalInit(rom);
            Logger.LogInfo("libsm64 initialized successfully!");
        }

        public void OnSceneUnloaded(Scene scene)
        {
            _surfaceObjects.Clear();
            _marios.Clear();
        }

        public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded: [{scene.buildIndex}] {scene.name}");

            // TODO: Replace -1 with PEAK's actual gameplay scene index
            // Use dnSpy to find it, or log scene.buildIndex on every load to identify it
            if (scene.buildIndex == -1)
            {
                RegisterTerrain();
                SpawnMario();
            }
        }

        private void RegisterTerrain()
        {
            MeshCollider[] meshCols = GameObject.FindObjectsOfType<MeshCollider>();
            BoxCollider[] boxCols = GameObject.FindObjectsOfType<BoxCollider>();

            for (int i = 0; i < meshCols.Length; i++)
            {
                MeshCollider c = meshCols[i];
                if (c.isTrigger) continue;

                GameObject surfaceObj = new GameObject($"SM64_SURFACE_MESH ({c.name})");
                MeshCollider surfaceMesh = surfaceObj.AddComponent<MeshCollider>();
                surfaceObj.AddComponent<SM64StaticTerrain>();
                surfaceObj.transform.rotation = c.transform.rotation;
                surfaceObj.transform.position = c.transform.position;

                List<int> tris = new List<int>();
                for (int j = 0; j < c.sharedMesh.subMeshCount; j++)
                {
                    int[] sub = c.sharedMesh.GetTriangles(j);
                    for (int k = 0; k < sub.Length; k++)
                        tris.Add(sub[k]);
                }

                Mesh mesh = new Mesh();
                mesh.name = $"SM64_MESH {i}";
                mesh.SetVertices(c.sharedMesh.vertices);
                mesh.SetTriangles(tris, 0);
                surfaceMesh.sharedMesh = mesh;
            }

            RefreshStaticTerrain();
        }

        private void SpawnMario()
        {
            // TODO: Replace this with finding PEAK's actual player object
            // e.g. GameObject p = GameObject.Find("Player");
            // or   GameObject p = GameObject.FindObjectOfType<PlayerController>()?.gameObject;
            GameObject p = null;

            if (p == null)
            {
                Logger.LogWarning("Player object not found — Mario will not spawn.");
                return;
            }

            Renderer[] renderers = p.GetComponentsInChildren<Renderer>();
            Material material = null;

            foreach (var r in renderers)
            {
                r.forceRenderingOff = true;
                // TODO: adjust shader name match to whatever PEAK's player uses
                if (material == null && r.material.shader.name.Contains("Standard"))
                    material = Material.Instantiate(r.material);
            }

            if (material != null)
            {
                material.SetTexture("_BaseMap", Interop.marioTexture);
                material.SetColor("_BaseColor", Color.white);
            }

            GameObject marioObj = new GameObject("SM64_MARIO");
            marioObj.transform.position = p.transform.position;

            marioObj.AddComponent<SM64InputGame>(); // your SM64InputProvider subclass
            SM64Mario mario = marioObj.AddComponent<SM64Mario>();

            if (mario.spawned)
            {
                mario.SetMaterial(material);
                RegisterMario(mario);
                p.SetActive(false);
                Logger.LogInfo("Mario spawned successfully!");
            }
            else
            {
                Logger.LogError("Mario failed to spawn.");
            }
        }

        private void Update()
        {
            foreach (var o in _surfaceObjects) o.contextUpdate();
            foreach (var o in _marios) o.contextUpdate();
        }

        private void FixedUpdate()
        {
            foreach (var o in _surfaceObjects) o.contextFixedUpdate();
            foreach (var o in _marios) o.contextFixedUpdate();
        }

        private void OnDestroy()
        {
            Interop.GlobalTerminate();
        }

        public void RefreshStaticTerrain()
        {
            Interop.StaticSurfacesLoad(Utils.GetAllStaticSurfaces());
        }

        public void RegisterMario(SM64Mario mario)
        {
            if (!_marios.Contains(mario)) _marios.Add(mario);
        }

        public void UnregisterMario(SM64Mario mario)
        {
            if (_marios.Contains(mario)) _marios.Remove(mario);
        }

        public void RegisterSurfaceObject(SM64DynamicTerrain obj)
        {
            if (!_surfaceObjects.Contains(obj)) _surfaceObjects.Add(obj);
        }

        public void UnregisterSurfaceObject(SM64DynamicTerrain obj)
        {
            if (_surfaceObjects.Contains(obj)) _surfaceObjects.Remove(obj);
        }
    }
}