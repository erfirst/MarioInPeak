using BepInEx;
using BepInEx.Logging;
using LibSM64;
using UnityEngine;
using System.Security.Cryptography;
using System.Text;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.SceneManagement;
using UnityEngine.SocialPlatforms;

namespace LibSM64
{
    [BepInPlugin("com.erfirst.sm64mario", "MarioInPeak", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        static List<SM64Mario> _marios = new List<SM64Mario>();
        static List<SM64DynamicTerrain> _surfaceObjects = new List<SM64DynamicTerrain>();
        public static Plugin Instance { get; private set; }
        public static new ManualLogSource Logger { get; private set; }

        private Vector3 _lastTerrainUpdatePos = Vector3.zero;
        private const float TERRAIN_UPDATE_DISTANCE = 50f;
        private bool _loggedNoMarioInUpdate;
        private bool _loggedNoMarioInFixedUpdate;

        public GameObject Player { get; private set; }
        public void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            Logger.LogMessage("Initializing SM64 Mario Mod");
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
                Logger.LogMessage("Super Mario 64 US ROM 'sm64.z64' not found");
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
                    Logger.LogMessage($"Super Mario 64 US ROM 'sm64.z64' SHA-1 mismatch\nExpected: 9bef1128\nYour copy: {hashStr}\n\nPlease supply the correct ROM.");
                    return;
                }
            }
            Logger.LogMessage("ROM hash verified, initializing SM64 library");
            Interop.GlobalInit(rom);
            Logger.LogMessage("SM64 library initialized successfully");
        }
        public void OnSceneUnloaded(Scene scene)
        {
            _surfaceObjects.Clear();
            _marios.Clear();
        }

        public void registerTerrain()
        {
            MeshCollider[] meshCols = GameObject.FindObjectsOfType<MeshCollider>();
            BoxCollider[] boxCols = GameObject.FindObjectsOfType<BoxCollider>();
            Logger.LogMessage($"Found {meshCols.Length} mesh colliders and {boxCols.Length} box colliders in the scene");
            int loadedMeshColliders = 0;
            int loadedBoxColliders = 0;
            int skippedTriggerColliders = 0;
            int skippedMissingMeshColliders = 0;
            for (int i = 0; i < meshCols.Length; i++)
            {
                MeshCollider c = meshCols[i];
                if (c.isTrigger)
                {
                    skippedTriggerColliders++;
                    continue;
                }

                if (c.sharedMesh == null)
                {
                    skippedMissingMeshColliders++;
                    Logger.LogWarning($"Skipping mesh collider '{c.name}' because sharedMesh is null");
                    continue;
                }

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
                loadedMeshColliders++;

                if (loadedMeshColliders <= 3)
                {
                    Logger.LogMessage($"Prepared mesh surface '{c.name}' with {c.sharedMesh.vertexCount} vertices and {tris.Count / 3} triangles");
                }
            }
            for (var i = 0; i < boxCols.Length; i++)
            {
                BoxCollider c = boxCols[i];
                if (c.isTrigger)
                {
                    skippedTriggerColliders++;
                    continue;
                }

                GameObject surfaceObj = new GameObject($"SM64_SURFACE_BOX ({c.name})");
                MeshCollider surfaceMesh = surfaceObj.AddComponent<MeshCollider>();
                surfaceObj.AddComponent<SM64StaticTerrain>();

                Mesh mesh = new Mesh();
                mesh.name = $"SM64_MESH {i}";
                mesh.SetVertices(GetColliderVertexPositions(c));
                mesh.SetTriangles(new int[] {
                        // min Y
                        0, 1, 4,
                        5, 4, 1,

                        // max Y
                        2, 3, 6,
                        7, 6, 3,

                        /*
                        // min X
                        2, 1, 0,
                        1, 2, 3,

                        // max X
                        4, 5, 6,
                        7, 6, 5,

                        // min Z
                        4, 2, 0,
                        2, 4, 6,
                        */
                    }, 0);
                surfaceMesh.sharedMesh = mesh;
                loadedBoxColliders++;
            }

            Logger.LogMessage($"Terrain prep summary: loadedMesh={loadedMeshColliders}, loadedBox={loadedBoxColliders}, skippedTriggers={skippedTriggerColliders}, skippedMissingMesh={skippedMissingMeshColliders}");
            RefreshStaticTerrain();
            Logger.LogMessage($"Loaded {loadedMeshColliders} mesh colliders and {loadedBoxColliders} box colliders as SM64 surfaces");
        }

        public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogMessage($"Scene loaded: index={scene.buildIndex}, name='{scene.name}', mode={mode}");
            if (scene.name.StartsWith("Level_"))
            {
                Logger.LogMessage("Loading SM64 surfaces and spawning Mario");
                registerTerrain();
                StartCoroutine(SpawnMarioWithRetry(10, 2f));

            }
            else
            {
                Logger.LogMessage("Not spawning Mario in this scene");
            }
        }
        private System.Collections.IEnumerator SpawnMarioWithRetry(int maxAttempts, float delaySeconds)
        {
            Logger.LogMessage($"Attempting to spawn Mario (maxAttempts={maxAttempts}, delay={delaySeconds:0.##}s)");

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (TrySpawnMario())
                {
                    Logger.LogMessage($"Mario spawn succeeded on attempt {attempt}/{maxAttempts}");
                    yield break;
                }

                if (attempt < maxAttempts)
                {
                    Logger.LogMessage($"Mario spawn failed on attempt {attempt}/{maxAttempts}; retrying in {delaySeconds:0.##}s");
                    yield return new WaitForSeconds(delaySeconds);
                }
            }

            Logger.LogError($"Mario failed to spawn after {maxAttempts} attempts.");
        }

        private bool TrySpawnMario()
        {
            Logger.LogMessage("Gunna spawn mario");
            // "p" is the player object/component in this case.
            //  GameObject p = null;
            //p = GameObject.Find("Player.localPlayer");
            Character p = Character.localCharacter;
            if (p != null)
            {
                Player = p.gameObject;
                Logger.LogMessage("Found player object at position " + p.transform.position);
                Renderer[] r = p.GetComponentsInChildren<Renderer>();
                Logger.LogMessage($"Player has {r.Length} renderers in hierarchy");
                Material material = null;
                for (int i = 0; i < r.Length; i++)
                {
                    Logger.LogMessage($"MAT NAME {i} '{r[i].material.name}' '{r[i].material.shader.name}'");

                    // Hide the original character renderer by hiding that material
                    r[i].forceRenderingOff = true;


                    // Change this with the shader that you want. You'll have to play around a bit
                    if (material == null && r[i].material.name.StartsWith("Default"))
                        material = Material.Instantiate<Material>(r[i].material);
                }

                if (material != null)
                {
                    material.SetTexture("_BaseMap", Interop.marioTexture);
                    material.SetColor("_BaseColor", Color.white);
                    Logger.LogMessage($"Selected material '{material.name}' with shader '{material.shader.name}' for Mario renderer");
                }
                else
                {
                    Logger.LogWarning("No player material matched shader prefix 'W/Character'; Mario may render with no material");
                }


                // Uncomment this to create a test SM64 surface at the player's spawn position
                
                Vector3 P = p.transform.position;
                P.y -= 2;
                GameObject surfaceObj = new GameObject("SM64_SURFACE");
                MeshCollider surfaceMesh = surfaceObj.AddComponent<MeshCollider>();
                surfaceObj.AddComponent<SM64StaticTerrain>();
                Mesh mesh = new Mesh();
                mesh.name = "TEST_MESH";
                mesh.SetVertices(
                    new Vector3[]
                    {
                        new Vector3(P.x-128,P.y,P.z-128), new Vector3(P.x+128,P.y,P.z+128), new Vector3(P.x+128,P.y,P.z-128),
                        new Vector3(P.x+128,P.y,P.z+128), new Vector3(P.x-128,P.y,P.z-128), new Vector3(P.x-128,P.y,P.z+128),
                    }
                );
                mesh.SetTriangles(new int[] { 0, 1, 2, 3, 4, 5 }, 0);
                surfaceMesh.sharedMesh = mesh;
                Logger.LogMessage($"Created test surface at {surfaceObj.transform.position}");
                RefreshStaticTerrain();
          

                GameObject marioObj = new GameObject("SM64_MARIO");
                Vector3 spawnPos = p.transform.position;
                marioObj.transform.position = spawnPos;
                Logger.LogMessage($"Setting Mario spawn to {marioObj.transform.position}"); 
                SM64InputGame input = marioObj.AddComponent<SM64InputGame>();
                SM64Mario mario = marioObj.AddComponent<SM64Mario>();
                Logger.LogMessage($"Mario object created. hasInput={input != null}, hasMarioComponent={mario != null}, activeInHierarchy={marioObj.activeInHierarchy}");
                if (mario.spawned)
                {
                    RegisterMario(mario);

                    // p.enabled = false;
                    Logger.LogMessage("Mario spawned successfully");
                    return true;
                }
                else
                {
                    Destroy(marioObj);
                    Logger.LogMessage("Failed to spawn Mario");
                    return false;
                }
            }
            else
            {
                Logger.LogMessage("Failed to find player object, Mario not spawned this attempt");
                return false;
            }
        }
        private float _debugTimer = 0f;

        public void Update()
        {
            foreach (var o in _surfaceObjects) o.contextUpdate();
            foreach (var o in _marios) o.contextUpdate();

            _debugTimer += Time.deltaTime;
            if (_debugTimer >= 1f)
            {
                _debugTimer = 0f;
                if (_marios.Count > 0)
                {
                    var mario = _marios[0];
                    Vector3? playerPos = Character.localCharacter != null ? Character.localCharacter.transform.position : (Vector3?)null;
                    Logger.LogMessage($"Coords compare | playerUnity={(playerPos.HasValue ? playerPos.Value.ToString() : "<null>")} marioUnity={mario.transform.position} marioStateUnity={mario.marioState.unityPosition} marioM64Raw={mario.rawM64Position}");
                    Logger.LogMessage($"Mario GameObject active: {_marios[0].gameObject.activeSelf}");
                    Logger.LogMessage($"Camera position: {Camera.main?.transform.position}");
                }
                else
                {
                    Logger.LogMessage("No marios in list");
                }
            }
        }
        public void FixedUpdate()
        {
            foreach (var o in _surfaceObjects)
                o.contextFixedUpdate();

            foreach (var o in _marios)
                o.contextFixedUpdate();

            if (_marios.Count == 0 && !_loggedNoMarioInFixedUpdate)
            {
                _loggedNoMarioInFixedUpdate = true;
                Logger.LogWarning("FixedUpdate loop has zero registered Mario instances");
            }
            else if (_marios.Count > 0)
            {
                _loggedNoMarioInFixedUpdate = false;
            }
        }
        public void OnDestroy()
        {
            Interop.GlobalTerminate();
        }


        public void RefreshStaticTerrain()
        {
            var surfaces = Utils.GetAllStaticSurfaces();
            Logger.LogMessage($"Refreshing static terrain with {surfaces.Length} SM64 surfaces");
            if (surfaces.Length == 0)
            {
                Logger.LogWarning("No static terrain surfaces were generated; Mario may have nothing to collide with");
            }

            Interop.StaticSurfacesLoad(surfaces);
        }

        public void RegisterMario(SM64Mario mario)
        {
            if (!_marios.Contains(mario))
                _marios.Add(mario);
        }

        public void UnregisterMario(SM64Mario mario)
        {
            Logger.LogMessage("Unregistering mario");
            if (_marios.Contains(mario))
                _marios.Remove(mario);
        }

        public void RegisterSurfaceObject(SM64DynamicTerrain surfaceObject)
        {
            if (!_surfaceObjects.Contains(surfaceObject))
                _surfaceObjects.Add(surfaceObject);
        }

        public void UnregisterSurfaceObject(SM64DynamicTerrain surfaceObject)
        {
            if (_surfaceObjects.Contains(surfaceObject))
                _surfaceObjects.Remove(surfaceObject);
        }

        Vector3[] GetColliderVertexPositions(BoxCollider col)
        {
            var trans = col.transform;
            var min = (col.center - col.size * 0.5f);
            var max = (col.center + col.size * 0.5f);

            Vector3 savedPos = trans.position;

            var P000 = trans.TransformPoint(new Vector3(min.x, min.y, min.z));
            var P001 = trans.TransformPoint(new Vector3(min.x, min.y, max.z));
            var P010 = trans.TransformPoint(new Vector3(min.x, max.y, min.z));
            var P011 = trans.TransformPoint(new Vector3(min.x, max.y, max.z));
            var P100 = trans.TransformPoint(new Vector3(max.x, min.y, min.z));
            var P101 = trans.TransformPoint(new Vector3(max.x, min.y, max.z));
            var P110 = trans.TransformPoint(new Vector3(max.x, max.y, min.z));
            var P111 = trans.TransformPoint(new Vector3(max.x, max.y, max.z));

            return new Vector3[] { P000, P001, P010, P011, P100, P101, P110, P111 };
            /*
            var vertices = new Vector3[8];
            var thisMatrix = col.transform.localToWorldMatrix;
            var storedRotation = col.transform.rotation;
            col.transform.rotation = Quaternion.identity;

            var extents = col.bounds.extents;
            vertices[0] = thisMatrix.MultiplyPoint3x4(-extents);
            vertices[1] = thisMatrix.MultiplyPoint3x4(new Vector3(-extents.x, -extents.y, extents.z));
            vertices[2] = thisMatrix.MultiplyPoint3x4(new Vector3(-extents.x, extents.y, -extents.z));
            vertices[3] = thisMatrix.MultiplyPoint3x4(new Vector3(-extents.x, extents.y, extents.z));
            vertices[4] = thisMatrix.MultiplyPoint3x4(new Vector3(extents.x, -extents.y, -extents.z));
            vertices[5] = thisMatrix.MultiplyPoint3x4(new Vector3(extents.x, -extents.y, extents.z));
            vertices[6] = thisMatrix.MultiplyPoint3x4(new Vector3(extents.x, extents.y, -extents.z));
            vertices[7] = thisMatrix.MultiplyPoint3x4(extents);

            col.transform.rotation = storedRotation;
            return vertices;
            */
        }
    }
}