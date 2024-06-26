using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class Manager : MonoBehaviour
{
    public Dictionary<(int x, int y, int z), GameObject> GamobjectMap = new Dictionary<(int x, int y, int z), GameObject>();

    [SerializeField] private GameObject tilePrefab; // Prefab for individual tiles, used in rendering.

    public int seed;
    public (int x, int y, int z) max;
    public (int x, int y, int z) regionSize;


    protected Dictionary<(int x, int y, int z), NestedRegion> mRegion;
    public Dictionary<(int x, int y, int z), TileProps> mGameObject;
    public List<(int x, int y, int z)> mNotCollapsesed;
    [SerializeField] protected GameObject RegionPrefab;

    public TextMeshProUGUI RunningTotal;

    public Dictionary<int, byte[]> entropy = new Dictionary<int, byte[]>();
    public bool collapsed = false;
    public bool rendered = false;

    public int complexity = 0;

    protected (int x, int y, int z)[] Dirs = new (int x, int y, int z)[] { (1, 0, 0), (0, 1, 0), (0, 0, 1), (-1, 0, 0), (0, -1, 0), (0, 0, -1) };

    public Stack<(int x, int y, int z)> mStack;

    protected (int x, int y, int z) maxRegion;

    protected float StartTime;
    public float FinishCollapseTime;

    public int mCollapseCount = 0;

    public bool realTimeRender = false;
    public bool renderOnFinish = false;

    public bool Running = true;

    private void Start()
    {
        mGameObject = new Dictionary<(int x, int y, int z), TileProps>();
        mNotCollapsesed = new List<(int x, int y, int z)>();
        mStack = new Stack<(int x, int y, int z)>();
        mRegion = new Dictionary<(int x, int y, int z), NestedRegion>();


        StartTime = Time.time;
        time = Time.time;


        InitRegions();
    }

    public int collapseCount = 0;
    public int renderedCount = 0;
    protected (int x, int y, int z) targetRegion = (0, 0, 0);

    public float time;

    public int failCount = 0;

    private void Update()
    {
        if (!Running) return;
        // Update logic runs only if the game is in a "running" state.
        mCollapseCount = mNotCollapsesed.Count;
        time = Time.time;

        // Check if all regions have been rendered.
        if (renderedCount >= mRegion.Count) rendered = true;

        if (!collapsed)
        {
            var r = mRegion[targetRegion];
            r.running = true;
            r.RunUpdate();
            if (realTimeRender) r.RunRenderer();

        }
        else if (!rendered)
        {
            // Handle rendering completed regions.
            var r = mRegion[mRegion.ElementAt(renderedCount).Key];
            if (renderOnFinish && !r.rendered) r.RunRenderer();

            if (r.rendered)
            {
                renderedCount++;
                RunningTotal.text = renderedCount.ToString();
            }
        }
    }

    public void SpawnTiles(Dictionary<(int x, int y, int z), Tile> mTile)
    {
        foreach (var tile in mTile)
        {
            var TempTile = tile.Value;
            TileProps temp = mGameObject[tile.Key];
            var e = mTile[tile.Key].GetExits();
            byte[] ent;
            if (e == -1)
            {
                ent = new byte[6] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            }
            else
            {
                ent = entropy[e];
            }
            temp.SetExits(ent, tile.Key);
            //CombineMeshes();
        }
    }

    protected virtual void InitRegions()
    {
        var regions = new Dictionary<(int x, int y, int z), (int minX, int minY, int minZ, int maxX, int maxY, int maxZ)>();

        int posX = 0, posY = 0, posZ = 0; // Trackers for the chunk's position

        for (int x = 0; x < max.x; x += regionSize.x, posX++)
        {
            posY = 0; // Reset Y position for each new X level
            for (int y = 0; y < max.y; y += regionSize.y, posY++)
            {
                posZ = 0; // Reset Z position for each new Y level in the curresnt X
                for (int z = 0; z < max.z; z += regionSize.z, posZ++)
                {
                    int endX = Math.Min(x + regionSize.x, max.x);
                    int endY = Math.Min(y + regionSize.y, max.y);
                    int endZ = Math.Min(z + regionSize.z, max.z);

                    // Use the position as the key and the chunk boundaries as the value
                    //Debug.Log($"({posX}, {posY}, {posZ}), ({x}, {y}, {z}, {endX}, {endY}, {endZ})");

                    regions.Add((posX, posY, posZ), (x, y, z, endX, endY, endZ));

                }
            }
        }
        maxRegion = (posX, posY, posZ);

        for (int x = 0; x < max.x; x++)
        {
            for (int y = 0; y < max.y; y++)
            {
                for (int z = 0; z < max.z; z++)
                {
                    Vector3 _targetPos = new Vector3((float)x * 3, (float)y * 3, (float)z * 3);
                    GameObject TempTile = Instantiate(tilePrefab, _targetPos, Quaternion.identity, transform);
                    //TileProps tileProps = new TileProps();
                    mGameObject.Add((x, y, z), TempTile.GetComponent<TileProps>());
                }
            }
        }

        foreach (var region in regions)
        {
            Vector3 position = new Vector3(0, 0, 0);//new Vector3(region.Value.minX * 3, region.Value.minY * 3, region.Value.minZ * 3);

            NestedRegion RegionScript = new NestedRegion();
            mRegion.Add(region.Key, RegionScript);
            var min = (region.Value.minX, region.Value.minY, region.Value.minZ);
            var max = (region.Value.maxX, region.Value.maxY, region.Value.maxZ);
            RegionScript.Initialize(region.Key, max, min, entropy, this);
            mNotCollapsesed.Add(region.Key);
        }

        GamobjectMap = new Dictionary<(int x, int y, int z), GameObject>();
    }

    public HashSet<int> GetTileEntropy((int x, int y, int z) _tP)
    {
        _tP.x = (_tP.x + max.x) % max.x;
        _tP.y = (_tP.y + max.y) % max.y;
        _tP.z = (_tP.z + max.z) % max.z;

        (int x, int y, int z) _tR = (_tP.x / regionSize.x, _tP.y / regionSize.y, _tP.z / regionSize.z);

        return mRegion[_tR].GetTile(_tP);
    }

    public void ResetRegion((int x, int y, int z) _startRegion, int counter)
    {
        ((int x, int y, int z) pos, (int x, int y, int z) min, (int x, int y, int z) max) region = (mRegion[targetRegion].pos, mRegion[targetRegion].min, mRegion[targetRegion].max);

        mRegion.Remove(targetRegion);

        Vector3 position = new Vector3(0, 0, 0);
        NestedRegion RegionScript = new NestedRegion();
        mRegion.Add(targetRegion, RegionScript);
        RegionScript.Initialize(region.pos, region.max, region.min, entropy, this);
    }

    public void ResetAllRegion()
    {
        mNotCollapsesed = new List<(int x, int y, int z)>();
        mStack = new Stack<(int x, int y, int z)>();
        mRegion = new Dictionary<(int x, int y, int z), NestedRegion>();
        entropy = new Dictionary<int, byte[]>();

        collapseCount = 0;

        Start();
    }

    public void UpdateRegionEntropyList(bool change)
    {
        if (mNotCollapsesed.Count > 0)
        {
            // Update all entropy values first.
            for (int i = 0; i < mNotCollapsesed.Count; i++)
            {
                mRegion[mNotCollapsesed[i]].ResetRegionState();
                mRegion[mNotCollapsesed[i]].UpdateAllEntropy();
                //mRegion[tile].UpdateAllEntropy();
            }

            ConcurrentDictionary<(int x, int y, int z), int> mRegionEntropy = new ConcurrentDictionary<(int x, int y, int z), int>();

            for (int i = 0; i < mNotCollapsesed.Count; i++)
            {
                mRegionEntropy.TryAdd(mNotCollapsesed[i], mRegion[mNotCollapsesed[i]].GetEntropy());
            }

            //var tempDictionary = mNotCollapsesed.ToDictionary(key => key, key => mRegion[key].GetEntropy());


            // Find the minimum entropy value.
            int minEntropy = mRegionEntropy.Values.Min();

            // Select all regions that have this minimum entropy value.
            var lowEntropyRegions = mRegionEntropy.Where(pair => pair.Value == minEntropy).Select(pair => pair.Key).ToList();

            // Safely update targetRegion with the first region of the lowest entropy, if any.
            if (lowEntropyRegions.Any())
            {
                if (change) targetRegion = lowEntropyRegions[0];
            }
        }
        else
        {
            collapsed = true;
        }
    }

    public void CheckDone()
    {
        var count = 0;
        foreach (var region in mRegion)
        {
            if (region.Value.collapsed)
            {
                count++;
            }
        }
        if (count > max.x * max.y * max.z) collapsed = true;
    }

    void CombineMeshes()
    {
        //// Ensure this GameObject has a MeshFilter component
        //MeshFilter meshFilter = GetComponent<MeshFilter>();
        //if (meshFilter == null)
        //{
        //    meshFilter = gameObject.AddComponent<MeshFilter>();
        //}

        //// Ensure this GameObject has a MeshRenderer component
        //MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        //if (meshRenderer == null)
        //{
        //    meshRenderer = gameObject.AddComponent<MeshRenderer>();
        //}

        //// Get all MeshFilter components from child objects
        //MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(false);

        //// Group meshes by material
        //Dictionary<Material, List<MeshFilter>> materialGroups = new Dictionary<Material, List<MeshFilter>>();

        //foreach (MeshFilter mf in meshFilters)
        //{
        //    if (mf.sharedMesh == null || mf == meshFilter) continue; // Skip if the sharedMesh is null or it's the parent MeshFilter

        //    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
        //    if (mr == null || mr.sharedMaterial == null) continue; // Skip if there's no MeshRenderer or sharedMaterial

        //    if (!materialGroups.ContainsKey(mr.sharedMaterial))
        //    {
        //        materialGroups[mr.sharedMaterial] = new List<MeshFilter>();
        //    }
        //    materialGroups[mr.sharedMaterial].Add(mf);
        //}

        //// Combine meshes for each material group
        //CombineInstance[] combine = new CombineInstance[materialGroups.Count];
        //Material[] materials = new Material[materialGroups.Count];
        //int materialIndex = 0;
        //List<GameObject> objectsToDestroy = new List<GameObject>();
        //foreach (KeyValuePair<Material, List<MeshFilter>> kvp in materialGroups)
        //{
        //    CombineInstance[] materialCombines = new CombineInstance[kvp.Value.Count];
        //    for (int i = 0; i < kvp.Value.Count; i++)
        //    {
        //        materialCombines[i].mesh = kvp.Value[i].sharedMesh;
        //        materialCombines[i].transform = kvp.Value[i].transform.localToWorldMatrix;
        //        objectsToDestroy.Add(kvp.Value[i].gameObject); // Mark the child object for deletion
        //    }

        //    // Combine meshes for the current material
        //    Mesh combinedMesh = new Mesh();
        //    combinedMesh.CombineMeshes(materialCombines, true, true);

        //    combine[materialIndex].mesh = combinedMesh;
        //    combine[materialIndex].transform = Matrix4x4.identity;
        //    materials[materialIndex] = kvp.Key;
        //    materialIndex++;
        //}

        //// Create a new mesh and combine all the grouped meshes into it
        //Mesh finalMesh = new Mesh();
        //finalMesh.CombineMeshes(combine, false, false);
        //meshFilter.mesh = finalMesh;
        //meshRenderer.materials = materials; // Set the materials array

        //// Optionally, reset the position if needed
        //meshRenderer.transform.position = Vector3.zero;

        //// Destroy the child GameObjects
        //foreach (GameObject go in objectsToDestroy)
        //{
        //    Destroy(go);
        //}
    }
}
