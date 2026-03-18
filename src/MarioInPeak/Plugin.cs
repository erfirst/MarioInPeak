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

namespace LibSM64
{
    [BepInPlugin("com.github.erfirst.MarioInPeak", "MarioInPeak", "1.0.0")]
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

            Logger.LogInfo($"Mario64 is loaded!");
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
            if (scene.name.StartsWith("Level_"))
            {
                RegisterTerrain();
                StartCoroutine(SpawnMarioWhenReady());
            }
        }

        private System.Collections.IEnumerator SpawnMarioWhenReady()
        {
            Logger.LogInfo("Waiting for local character...");
            float timeout = 30f;
            while (Character.localCharacter == null && timeout > 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (Character.localCharacter == null)
            {
                Logger.LogError("Timed out waiting for local character.");
                yield break;
            }

            Logger.LogInfo("Character found, spawning Mario...");
            SpawnMario();
        }

        private void RegisterTerrain()
        {
            Logger.LogInfo("Finding surfaces...");
            MeshCollider[] meshCols = GameObject.FindObjectsOfType<MeshCollider>();
            BoxCollider[] boxCols = GameObject.FindObjectsOfType<BoxCollider>();
            Logger.LogInfo($"Found {meshCols.Length} mesh colliders, {boxCols.Length} box colliders");

            int registered = 0;
            for (int i = 0; i < meshCols.Length; i++)
            {
                MeshCollider c = meshCols[i];
                try
                {
                    if (c == null || c.isTrigger) continue;
                    if (c.sharedMesh == null) continue;

                    List<int> tris = new List<int>();
                    for (int j = 0; j < c.sharedMesh.subMeshCount; j++)
                    {
                        int[] sub = c.sharedMesh.GetTriangles(j);
                        for (int k = 0; k < sub.Length; k++)
                            tris.Add(sub[k]);
                    }

                    if (tris.Count == 0) continue;

                    Mesh mesh = new Mesh();
                    mesh.name = $"SM64_MESH {i}";
                    mesh.SetVertices(c.sharedMesh.vertices);
                    mesh.SetTriangles(tris, 0);

                    GameObject surfaceObj = new GameObject($"SM64_SURFACE_MESH ({c.name})");
                    MeshCollider surfaceMesh = surfaceObj.AddComponent<MeshCollider>();
                    surfaceMesh.sharedMesh = mesh;
                    surfaceObj.AddComponent<SM64StaticTerrain>();
                    surfaceObj.transform.rotation = c.transform.rotation;
                    surfaceObj.transform.position = c.transform.position;

                    registered++;
                }
                catch (Exception e)
                {
                    Logger.LogError($"Error registering MeshCollider [{i}] {c?.name}: {e.Message}");
                }
            }

            Logger.LogInfo($"Registered {registered} terrain surfaces. Calling RefreshStaticTerrain...");
            RefreshStaticTerrain();
            Logger.LogInfo("RegisterTerrain complete.");
        }

        private void SpawnMario()
        {
            Logger.LogInfo($"Gonna spawn Mario");
            Character c = Character.localCharacter;
            if (c == null)
            {
                Logger.LogWarning("Local character not found — Mario will not spawn.");
                return;
            }

            // Hide the original character renderer
            SkinnedMeshRenderer smr = c.refs.mainRenderer;
            Material material = null;

            if (smr != null)
            {
                smr.forceRenderingOff = true;
                material = Material.Instantiate(smr.material);
                material.SetTexture("_BaseMap", Interop.marioTexture);
                material.SetColor("_BaseColor", Color.white);
            }

            GameObject marioObj = new GameObject("SM64_MARIO");
            marioObj.transform.position = c.transform.position;

            marioObj.AddComponent<SM64InputGame>();
            SM64Mario mario = marioObj.AddComponent<SM64Mario>();

            if (mario.spawned)
            {
                mario.SetMaterial(material);
                RegisterMario(mario);
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
            try
            {
                Logger.LogInfo("Getting all static surfaces...");
                var surfaces = Utils.GetAllStaticSurfaces();
                Logger.LogInfo($"Got {surfaces.Length} surfaces, calling StaticSurfacesLoad...");
                Interop.StaticSurfacesLoad(surfaces);
                Logger.LogInfo("StaticSurfacesLoad complete.");
            }
            catch (Exception e)
            {
                Logger.LogError($"RefreshStaticTerrain crashed: {e.Message}\n{e.StackTrace}");
            }
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