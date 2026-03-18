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

        public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogMessage($"{scene.buildIndex} {scene.name}");
            /*
            * At this point, there's a bunch of things to do:
            * 
            * First, you need to find the scene where the actual gameplay takes place,
            * and make sure to spawn Mario in that scene only.
            *
            * After you find it, You need to find the game's collision surfaces,
            * either MeshCollider or BoxCollider objects, and convert them into SM64 static terrain.
            * You can find these objects with "GameObject.FindObjectsOfType<>();"
            * then create the surfaceObj GameObject and add the SM64StaticTerrain component to it.
            * For moving platforms or destructible terrain, you should use SM64DynamicTerrain instead.
            * 
            * You also need to make a class that inherits from SM64InputProvider.
            * This is where SM64Mario will read the game's input presses.
            * Typically you'll want to read the player object's input.
            * 
            * Once that is said and done, create a new GameObject,
            * and add the SM64Mario component and your SM64 input provider class as a component to it.
            * You also need to give Mario an Unity Material object. Usually you can just borrow this from the player object.
            * 
            * I'll leave behind an example that does all this.
            * You'll need to adapt it to the game you're modding.
            */
            if (scene.name.StartsWith("Level_"))
            {
                Logger.LogMessage("Loading SM64 surfaces and spawning Mario");
                MeshCollider[] meshCols = GameObject.FindObjectsOfType<MeshCollider>();
                BoxCollider[] boxCols = GameObject.FindObjectsOfType<BoxCollider>();
                Logger.LogMessage($"Found {meshCols.Length} mesh colliders and {boxCols.Length} box colliders in the scene");
                for (int i = 0; i < meshCols.Length; i++)
                {
                    MeshCollider c = meshCols[i];
                    if (c.isTrigger)
                        continue;

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
                Logger.LogMessage($"Loaded {meshCols.Length} mesh colliders as SM64 surfaces");
                for (var i = 0; i < boxCols.Length; i++)
                {
                    BoxCollider c = boxCols[i];
                    if (c.isTrigger)
                        continue;

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
                }
                RefreshStaticTerrain();
                Logger.LogMessage($"Loaded {meshCols.Length} mesh colliders and {boxCols.Length} box colliders as SM64 surfaces");
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
                Renderer[] r = p.GetComponentsInChildren<Renderer>();
                Material material = null;
                for (int i = 0; i < r.Length; i++)
                {


                    // Hide the original character renderer by hiding that material
                    r[i].forceRenderingOff = true;


                    // Change this with the shader that you want. You'll have to play around a bit
                    if (material == null && r[i].material.shader.name.StartsWith("Toony Colors Pro 2"))
                        material = Material.Instantiate<Material>(r[i].material);
                }

                // Possibly hides character?
                /* 
                SkinnedMeshRenderer smr = p.refs.mainRenderer;
                if (smr != null)
                {
                    smr.forceRenderingOff = true;
                    material = Material.Instantiate(smr.material);
                    material.SetTexture("_BaseMap", Interop.marioTexture);
                    material.SetColor("_BaseColor", Color.white);
                }
                */

                if (material == null)
                {
                    Logger.LogMessage("Couldn't find a material to use for Mario, using default material");
                    material = new Material(Shader.Find("Standard"));
                    material.SetTexture("_BaseMap", Interop.marioTexture);
                    material.SetColor("_BaseColor", Color.white);
                }


                // Uncomment this to create a test SM64 surface at the player's spawn position
                /*
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
                RefreshStaticTerrain();
                */

                GameObject marioObj = new GameObject("SM64_MARIO");
                marioObj.transform.position = p.transform.position;
                Logger.LogMessage($"spawn {p.transform.position.x} {p.transform.position.y} {p.transform.position.z}");
                SM64InputGame input = marioObj.AddComponent<SM64InputGame>();
                SM64Mario mario = marioObj.AddComponent<SM64Mario>();
                if (mario.spawned)
                {
                    mario.SetMaterial(material);
                    RegisterMario(mario);

                   // p.enabled = false;
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
        public void Update()
        {
            foreach (var o in _surfaceObjects)
                o.contextUpdate();

            foreach (var o in _marios)
                o.contextUpdate();
        }
        public void FixedUpdate()
        {
            foreach (var o in _surfaceObjects)
                o.contextFixedUpdate();

            foreach (var o in _marios)
                o.contextFixedUpdate();
        }
        public void OnDestroy()
        {
            Interop.GlobalTerminate();
        }


        public void RefreshStaticTerrain()
        {
            Interop.StaticSurfacesLoad(Utils.GetAllStaticSurfaces());
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