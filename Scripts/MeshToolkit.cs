using System;
using System.Collections.Generic;
using System.Linq;
using AtlasUtilities.Editor;
using g4;
using gs;
using JetBrains.Annotations;
using MeshUtilities.Runtime;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MeshUtilities.Editor
{
    public static class MeshToolkit
    {
        private static readonly int VeinsEffectIntensity           = Shader.PropertyToID("_VeinsDisplacementIntensity");
        private static readonly int BreathingDisplacementIntensity = Shader.PropertyToID("_BreathingDisplacementIntensity");
        private static readonly int ColorMapID                            = Shader.PropertyToID("_Color_Map");

        /// <summary>
        /// Combine Meshes function handles all the filtering and merges meshes grouped by their material names.
        /// </summary>
        /// <param name="sourceCollection">Collection of filtered objects to be merged.</param>
        /// <param name="settings">Combine parameters, which control the behaviour of mesh attaching.</param>
        /// <param name="saveDirectory">Where to save assets created by combining meshes</param>
        /// <param name="atlasContainers">Optional atlas container scriptable object if we want to perform custom atlasing when combining meshes</param>
        /// <param name="explicitParent">Set this if the newly created combined game object should be parented to en explicit transform.</param>
        public static void Combine([NotNull] CombineCollection sourceCollection,
                                   CombineSettings settings,
                                   string saveDirectory,
                                   [CanBeNull] Dictionary<string,AtlasContainer> atlasContainers = null,
                                   [CanBeNull] Transform explicitParent = null)
        {
            string savePath = ProcessSavePath(saveDirectory);


            ModelData[] modelData = sourceCollection.GetModelData(out Dictionary<string, MaterialToolkit.CombinedMaterial> uniqueMaterials,
                                                                  out Dictionary<string, Dictionary<string, Material>> uniqueMaterialsByShader);

            List<string>                                         createdFiles     = new();
            Dictionary<string, MaterialToolkit.CombinedMaterial> atlasedMaterials = new();
            Dictionary<string, AtlasMapping>                   atlasMappings    = null;

            if (atlasContainers != null)
            {
                UpdateAtlasing(modelData, atlasContainers, uniqueMaterialsByShader, out atlasedMaterials, out atlasMappings);
            }

            CombineMeshes(savePath,
                          sourceCollection.TargetName,
                          sourceCollection,
                          explicitParent ? explicitParent : sourceCollection.TargetParent,
                          settings,
                          modelData,
                          createdFiles,
                          atlasedMaterials.Count > 0 ? atlasedMaterials : uniqueMaterials,
                          atlasMappings);

            OnAfterCombine(sourceCollection, settings.AfterCombineAction);
        }

        private static void UpdateAtlasing([NotNull] ModelData[] modelData,
                                           Dictionary<string, AtlasContainer> atlasContainers,
                                           Dictionary<string, Dictionary<string, Material>> uniqueMaterialsByShader,
                                           out Dictionary<string, MaterialToolkit.CombinedMaterial> atlasedMaterials,
                                           out Dictionary<string, AtlasMapping> atlasMappings)
        {
            atlasMappings    = new();
            atlasedMaterials = new();

            foreach (KeyValuePair<string,AtlasContainer> atlasContainer in atlasContainers)
            {
                if (!atlasContainer.Value || !uniqueMaterialsByShader.ContainsKey(atlasContainer.Key))
                {
                    continue;
                }

                Dictionary<string, Material> materialsForCurrentShader = uniqueMaterialsByShader[atlasContainer.Key];
                Dictionary<string, Material> nonTileableMaterials      = new();

                MaterialToolkit.CombinedMaterial combinedMaterial = new() { SourceMaterials = new() };

                FilterTileableMaterials(atlasedMaterials, materialsForCurrentShader, nonTileableMaterials);

                foreach (KeyValuePair<string,Material> nameAndMaterial in nonTileableMaterials)
                {
                    if (!combinedMaterial.SourceMaterials.Contains(nameAndMaterial.Value))
                    {
                        combinedMaterial.SourceMaterials.Add(nameAndMaterial.Value);
                    }
                    atlasContainer.Value.ProcessMaterial(nameAndMaterial.Value);
                }

                atlasContainer.Value.SaveAll(out Material resultMaterial);
                atlasContainer.Value.GetAtlasMappings(combinedMaterial.SourceMaterials, ref atlasMappings);

                combinedMaterial.Material       = resultMaterial;
                combinedMaterial.AtlasContainer = atlasContainer.Value;

                atlasContainer.Value.AddSourceModel(modelData[0].Transform.gameObject);

                if (resultMaterial)
                {
                    atlasedMaterials.Add(atlasContainer.Key, combinedMaterial);
                }

                EditorUtility.SetDirty(atlasContainer.Value);
                AssetDatabase.SaveAssetIfDirty(atlasContainer.Value);
            }
        }

        private static void FilterTileableMaterials(Dictionary<string, MaterialToolkit.CombinedMaterial> resultMaterials,
                                                    Dictionary<string, Material> materialsForShader,
                                                    Dictionary<string, Material> nonTileableMaterials)
        {
            // Filter through all current shader's materials and get only the non-tileable ones
            // as the tileable materials shouldn't be atlased
            foreach (KeyValuePair<string, Material> materialByName in materialsForShader)
            {
                if (!materialByName.Key.Contains("_tileable", StringComparison.OrdinalIgnoreCase))
                {
                    nonTileableMaterials.TryAdd(materialByName.Key, materialByName.Value);
                }
                else
                {
                    resultMaterials.TryAdd(materialByName.Key,
                                           new()
                                               {
                                                   Material        = materialByName.Value,
                                                   SourceMaterials = new() { materialByName.Value }
                                               });
                }
            }
        }

        public static void SplitCombinedObjects([NotNull] CombinedObject[] sourceObjects, bool removeCombinedFiles)
        {
            if (sourceObjects.Length < 1)
            {
                return;
            }

            foreach (CombinedObject combinedObject in sourceObjects)
            {
                EditorUtility.SetDirty(combinedObject);

                if (combinedObject.atlasContainer is AtlasContainer container)
                {
                    container.RemoveSourceModel(combinedObject.gameObject);
                }

                if (combinedObject.combinedObjects != null)
                {
                    foreach (CombinedObject nestedCombinedObject in combinedObject.combinedObjects)
                    {
                        nestedCombinedObject.gameObject.SetActive(true);
                        nestedCombinedObject.gameObject.tag = "Untagged";
                        if (nestedCombinedObject.TryGetComponent(out MeshRenderer renderer))
                        {
                            renderer.enabled = true;
                        }
                    }
                }

                foreach (CombinedSource combinedSource in combinedObject.combinedSources)
                {
                    if (!combinedSource || combinedSource.CombineObject != combinedObject)
                    {
                        continue;
                    }

                    GameObject   currentGameObject   = combinedSource.gameObject;
                    Transform    currentTransform    = combinedSource.transform;
                    MeshRenderer currentMeshRenderer = combinedSource.GetComponent<MeshRenderer>();

                    currentGameObject.tag  = combinedSource.originalTag;
                    currentGameObject.name = combinedSource.originalName;
                    currentTransform.SetParent(combinedSource.originalParent, true);

                    currentGameObject.SetActive(combinedSource.originalObjectState);
                    if (currentMeshRenderer)
                    {
                        currentMeshRenderer.enabled = combinedSource.originalRendererState;
                    }

                    Object.DestroyImmediate(combinedSource, true);
                }

                if (removeCombinedFiles)
                {
                    AssetDatabase.DeleteAssets(combinedObject.createdFiles.ToArray(), new List<string>());
                }

                Object.DestroyImmediate(combinedObject.gameObject, true);
            }
        }

        private static string ProcessSavePath(string saveDirectory)
        {
            string processedPath = saveDirectory;

            if (processedPath.Length == 0)
            {
                processedPath = "Assets/";
            }

            char lastChar = processedPath[^1];
            if (saveDirectory.Length > 0 && !(lastChar.Equals('/') || lastChar.Equals('\\')))
            {
                processedPath += "/";
            }

            return processedPath;
        }

        private static bool CheckMaterialsMatch(Material source, Material target)
        {
            if (source.name.Contains("_atlased") && !target.name.Contains("_tileable"))
            {
                return string.CompareOrdinal(MeshExtensions.GetShaderIdentifier(source.shader),
                                             MeshExtensions.GetShaderIdentifier(target.shader)) == 0;
            }

            return string.CompareOrdinal(MeshExtensions.GetMaterialIdentifier(source),
                                         MeshExtensions.GetMaterialIdentifier(target)) == 0;
        }

        [NotNull]
        private static Mesh[] CombineMeshes(
            string savePath,
            string targetName,
            CombineCollection collection,
            Transform parentTransform,
            CombineSettings combineSettings,
            [NotNull] ModelData[] modelData,
            List<string> createdFiles,
            [NotNull] Dictionary<string, MaterialToolkit.CombinedMaterial> uniqueMaterials,
            Dictionary<string, AtlasMapping> materialMapping)
        {
            List<CombineInstance> combineInstances = new();
            List<Mesh>            combinedMeshes   = new(uniqueMaterials.Count);

            int subMeshIndex = 0;

            // The target will have as many submeshes/meshes as unique materials
            foreach (KeyValuePair<string, MaterialToolkit.CombinedMaterial> material in uniqueMaterials)
            {
                int vertexCount = 0;

                GameObject combinedInstanceGameObject = new();
                StageUtility.PlaceGameObjectInCurrentStage(combinedInstanceGameObject);

                CombinedObject combinedObject = combinedInstanceGameObject.AddComponent<CombinedObject>();
                combinedObject.atlasContainer  = material.Value.AtlasContainer;
                combinedObject.combinedSources = new();

                for (int i = 0; i < modelData.Length; i++)
                {
                    for (int j = 0; j < modelData[i].Renderer.sharedMaterials.Length; j++)
                    {
                        if (modelData[i].Renderer.gameObject.name == "Plane")
                        {
                            Debug.Log("Checking plane");
                        }

                        if (!CheckMaterialsMatch(material.Value.Material, modelData[i].Renderer.sharedMaterials[j]))
                        {
                            continue;
                        }

                        Mesh    meshToCombine  = MeshExtensions.CopyMesh(modelData[i].Mesh);
                        Vector4 cachedUpVector = modelData[i].Transform.up;
                        Quaternion cachedRotation = modelData[i].Transform.rotation;

                        if (combineSettings.ChangeTransformForBillboards)
                        {
                            modelData[i].Transform.rotation = Quaternion.identity;
                        }

                        // Transforming local to world space
                        var positions = meshToCombine.vertices;
                        for (int k = 0; k < positions.Length; k++)
                        {
                            positions[k] = modelData[i].Transform.TransformPoint(positions[k]);
                        }
                        meshToCombine.vertices = positions;

                        if (combineSettings.WorldPositionAttributeEncoding != VertexChannel.Normal
                            && combineSettings.UpVectorAttributeEncoding != VertexChannel.Normal)
                        {
                            var normals = meshToCombine.normals;
                            for (int k = 0; k < normals.Length; k++)
                            {
                                normals[k] = modelData[i].Transform.TransformDirection(normals[k]);
                            }
                            meshToCombine.normals = normals;
                        }

                        if (combineSettings.WorldPositionAttributeEncoding != VertexChannel.Tangent
                            && combineSettings.UpVectorAttributeEncoding != VertexChannel.Tangent)
                        {
                            var tangents = meshToCombine.tangents;
                            for (int k = 0; k < tangents.Length; k++)
                            {
                                tangents[k] = modelData[i].Transform.TransformDirection(tangents[k]);
                            }
                            meshToCombine.tangents = tangents;
                        }

                        // Transforming UV coordinates to match atlasing (if applicable)
                        Color? tintForMaterial = AtlasContainer.GetTintColorFromMaterial(modelData[i].Renderer.sharedMaterials[j]);
                        Texture texture = modelData[i].Renderer.sharedMaterials[j].GetTexture(MeshToolkit.ColorMapID);
                        if (materialMapping != null &&
                            tintForMaterial != null && texture is Texture2D texture2D &&
                            materialMapping.TryGetValue(new Texture2DTint(texture2D, (Color)tintForMaterial).ToString(), out AtlasMapping mapping))
                        {
                            Vector2[] uvs = meshToCombine.uv;
                            for (int k = 0; k < uvs.Length; k++)
                            {
                                uvs[k] = new(
                                    uvs[k].x * mapping.XScale + mapping.XOffset,
                                    uvs[k].y * mapping.YScale + (1.0f - mapping.YScale - mapping.YOffset)
                                );
                            }
                            meshToCombine.uv = uvs;
                        }

                        if (combineSettings.WorldPositionAttributeEncoding != VertexChannel.None && combineSettings.WorldPositionAttributeEncoding != VertexChannel.Position)
                        {
                            Vector4[]     worldPositions     = new Vector4[meshToCombine.vertexCount];
                            List<Vector3> windLocalPositions = new();
                            modelData[i].Mesh.GetVertices(windLocalPositions);

                            for (int index = 0; index < worldPositions.Length; index++)
                            {
                                worldPositions[index]   = modelData[i].Transform.position;
                                worldPositions[index].w = windLocalPositions[index].y;
                            }

                            meshToCombine.SetVertexChannel(combineSettings.WorldPositionAttributeEncoding, worldPositions);
                        }

                        if (combineSettings.UpVectorAttributeEncoding != VertexChannel.None && combineSettings.UpVectorAttributeEncoding != VertexChannel.Position)
                        {
                            Vector4[] upVectors = new Vector4[meshToCombine.vertexCount];

                            for (int index = 0; index < upVectors.Length; index++)
                            {
                                upVectors[index] = cachedUpVector;
                            }

                            meshToCombine.SetVertexChannel(combineSettings.UpVectorAttributeEncoding, upVectors);
                        }

                        if (combineSettings.TreeMaterialAttributeEncoding != VertexChannel.None && combineSettings.TreeMaterialAttributeEncoding != VertexChannel.Position)
                        {
                            Vector4[] treeAttributes = meshToCombine.GetVertexChannel(combineSettings.TreeMaterialAttributeEncoding);
                            bool isTurbulenceEnabled = modelData[i]
                                                       .Renderer.sharedMaterial
                                                       .IsKeywordEnabled("_ENABLE_WIND_TURBULENCE");

                            for (int index = 0; index < treeAttributes.Length; index++)
                            {
                                treeAttributes[index].w = isTurbulenceEnabled ? 1.0f : 0.0f;
                            }

                            meshToCombine.SetVertexChannel(combineSettings.TreeMaterialAttributeEncoding, treeAttributes);
                        }

                        if (combineSettings.MyceliumParameterEncoding != VertexChannel.None && combineSettings.MyceliumParameterEncoding != VertexChannel.Position)
                        {
                            if (modelData[i].Renderer.HasPropertyBlock())
                            {
                                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                                modelData[i].Renderer.GetPropertyBlock(propertyBlock);
                                float veinsEffectIntensity     = propertyBlock.GetFloat(MeshToolkit.VeinsEffectIntensity);
                                float breathingEffectIntensity = 1.0f - propertyBlock.GetFloat(MeshToolkit.BreathingDisplacementIntensity);

                                Vector4[] myceliumParameters = meshToCombine.GetVertexChannel(combineSettings.MyceliumParameterEncoding);
                                if (myceliumParameters.Length != meshToCombine.vertexCount)
                                {
                                    myceliumParameters = new Vector4[meshToCombine.vertexCount];
                                }

                                for (int index = 0; index < myceliumParameters.Length; index++)
                                {
                                    myceliumParameters[index].z = veinsEffectIntensity;
                                    myceliumParameters[index].w = breathingEffectIntensity;
                                }

                                meshToCombine.SetVertexChannel(combineSettings.MyceliumParameterEncoding, myceliumParameters);
                            }
                            else
                            {
                                Vector4[] myceliumParameters = meshToCombine.GetVertexChannel(combineSettings.MyceliumParameterEncoding);
                                if (myceliumParameters.Length != meshToCombine.vertexCount)
                                {
                                    myceliumParameters = new Vector4[meshToCombine.vertexCount];
                                }

                                for (int index = 0; index < myceliumParameters.Length; index++)
                                {
                                    myceliumParameters[index].z = 0.0f;
                                    myceliumParameters[index].w = 1.0f;
                                }

                                meshToCombine.SetVertexChannel(combineSettings.MyceliumParameterEncoding, myceliumParameters);
                            }
                        }

                        CombineInstance instance = new()
                                                       {
                                                           // Extract each sub mesh and assign it to a sub mesh index based on the material count in mesh renderer
                                                           mesh         = meshToCombine,
                                                           transform    = Matrix4x4.identity,//modelData[i].Transform.localToWorldMatrix,
                                                           subMeshIndex = j,
                                                           lightmapScaleOffset = modelData[i].Renderer.lightmapScaleOffset,
                                                       };
                        combineInstances.Add(instance);

                        // Editor-only components for mapping and reverting the changes made by mesh combine
                        GameObject currentGameObject = modelData[i].Transform.gameObject;
                        if (!currentGameObject.TryGetComponent(out CombinedSource combinedSource))
                        {
                            combinedSource = modelData[i].Transform.gameObject.AddComponent<CombinedSource>();
                            combinedSource.originalName          = currentGameObject.name;
                            combinedSource.originalTag           = currentGameObject.tag;
                            combinedSource.originalParent        = modelData[i].Transform.parent;
                            combinedSource.originalObjectState   = currentGameObject.activeSelf;
                            combinedSource.originalRendererState = modelData[i].Renderer.enabled;

                            combinedSource.CombineObject = combinedObject;
                        }

                        combinedObject.combinedSources.Add(combinedSource);

                        if (combineSettings.ChangeTransformForBillboards)
                        {
                            modelData[i].Transform.rotation = cachedRotation;
                        }

                        //vertexCount += modelData[i].Mesh.vertexCount;
                    }
                }

                foreach (CombineInstance combineInstance in combineInstances)
                {
                    vertexCount += combineInstance.mesh.vertexCount;
                }

                Mesh combinedMesh = new()
                                        {
                                            name = $"msh_{targetName}_combined",
                                            indexFormat = combineSettings.Force16BitIndexing ? IndexFormat.UInt16 : vertexCount > UInt16.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
                                        };
                combinedMesh.CombineMeshes(combineInstances.ToArray(), true, false, false);

                if (combineSettings.RemoveOccludedTriangles)
                {
                    string cachedName = combinedMesh.name;
                    DMesh3 dMesh      = G4UnityUtils.UnityMeshToDMesh(combinedMesh);

                    RemoveOccludedTriangles removeOccludedTriangles = new(dMesh);
                    removeOccludedTriangles.OcclusionCondition = combineSettings.RemoveOccludedTrianglesSettings.OcclusionCondition;
                    removeOccludedTriangles.InsideMode      = combineSettings.RemoveOccludedTrianglesSettings.InsideMode;
                    removeOccludedTriangles.NormalOffset    = combineSettings.RemoveOccludedTrianglesSettings.NormalOffset;
                    removeOccludedTriangles.WindingIsoValue = combineSettings.RemoveOccludedTrianglesSettings.WindingIsoValue;
                    removeOccludedTriangles.Apply();

                    DMesh3 copyDMesh = dMesh;
                    if (!dMesh.IsCompact)
                    {
                        copyDMesh = new DMesh3(dMesh, true);
                    }

                    combinedMesh      = G4UnityUtils.DMeshToUnityMesh(copyDMesh);
                    combinedMesh.name = cachedName;
                }

                if (combineSettings.GenerateLightmapUVs)
                {
                    Unwrapping.GenerateSecondaryUVSet(combinedMesh, combineSettings.UnwrappingParameters);
                }

                if (combineSettings.SetMergedObjectsStatic)
                {
                    combinedInstanceGameObject.isStatic = true;
                }

                if (combinedMesh.vertexCount == 0)
                {
                    Object.DestroyImmediate(combinedInstanceGameObject);
                    continue;
                }

                combinedMeshes.Add(combinedMesh);

                Mesh createdMesh = SaveMesh(savePath, combineSettings.MeshSaveFormat, combinedMesh, subMeshIndex, ref createdFiles);
                MeshRenderer createdMeshRenderer = CreateMeshRenderer(combinedInstanceGameObject, subMeshIndex > 0 ? $"{targetName}_{subMeshIndex}_combined" : $"{targetName}_combined", createdMesh, material.Value.Material, parentTransform);

                if (combineSettings.AutoGenerateLodPivot)
                {
                    int lodStringIndex = createdMesh.name.LastIndexOf("_lod", StringComparison.OrdinalIgnoreCase);
                    if (lodStringIndex > -1)
                    {
                        if (int.TryParse(createdMesh.name[lodStringIndex + 4].ToString(), out int lodLevel))
                        {
                            Transform lodPivot = parentTransform.Find("LodPivot");
                            LODGroup  lodGroup;
                            if (!lodPivot)
                            {
                                GameObject newLodPivot = new("LodPivot");
                                newLodPivot.transform.parent = parentTransform;
                                newLodPivot.transform.localPosition = Vector3.zero;
                                newLodPivot.transform.localRotation = Quaternion.identity;
                                newLodPivot.transform.localScale = Vector3.one;
                                lodGroup                         = newLodPivot.AddComponent<LODGroup>();
                                LOD[] createdLods = lodGroup.GetLODs();
                                Array.Resize(ref createdLods, 0);
                                lodGroup.SetLODs(createdLods);
                            }
                            else
                            {
                                if (lodPivot.TryGetComponent(out lodGroup))
                                {
                                }
                                else
                                {
                                    lodGroup = lodPivot.gameObject.AddComponent<LODGroup>();
                                }
                            }

                            LOD[] lods = lodGroup.GetLODs();

                            // 3   0 true
                            // 3   1 true
                            // 3   2 true
                            // 3   3 false
                            if (!(lods.Length > lodLevel))
                            {
                                Array.Resize(ref lods, lodLevel + 1);
                            }

                            LOD currentLod = lods[lodLevel];

                            currentLod.renderers ??= new Renderer[] { createdMeshRenderer };

                            if (!currentLod.renderers.Contains(createdMeshRenderer))
                            {
                                Array.Resize(ref currentLod.renderers, currentLod.renderers.Length + 1);
                                currentLod.renderers[^1] = createdMeshRenderer;
                            }

                            lods[lodLevel] = currentLod;

                            for (int i = 0; i < lods.Length; i++)
                            {
                                LOD lod = lods[i];
                                lod.screenRelativeTransitionHeight = (float)(lods.Length - i) / (lods.Length + 1);
                                lods[i]                 = lod;
                            }

                            lodGroup.SetLODs(lods);
                        }
                    }
                }

                EditorUtility.SetDirty(combinedInstanceGameObject);

                combineInstances.Clear();
                subMeshIndex++;

                combinedObject.savePath        = savePath;
                combinedObject.createdFiles    = createdFiles;
                combinedObject.sourceMaterials = material.Value.SourceMaterials;
                combinedObject.combineSettings = combineSettings;
                combinedObject.combinedObjects = collection.NestedCombinedObjects;
            }

            return combinedMeshes.ToArray();
        }

        private static void OnAfterCombine(CombineCollection combineCollection, AfterCombineAction afterCombineAction)
        {
            ModelData[] modelData = combineCollection.GetModelData();
            if (modelData == null || modelData.Length == 0)
            {
                return;
            }

            bool isPrefabInstance = false;
            foreach (ModelData data in modelData)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(data.Transform))
                {
                    isPrefabInstance = true;
                    break;
                }
            }

            // When removing objects just destroy them and return
            if (!isPrefabInstance)
            {
                if (afterCombineAction.HasFlag(AfterCombineAction.RemoveObjects))
                {
                    foreach (ModelData data in modelData)
                    {
                        Object.DestroyImmediate(data.Transform.gameObject);
                    }

                    EditorUtility.SetDirty(combineCollection.TargetParent);
                    return;
                }

                if (afterCombineAction.HasFlag(AfterCombineAction.GroupObjects))
                {
                    GameObject sourceContainer = new($"[Combined Source] {combineCollection.TargetName}");

                    StageUtility.PlaceGameObjectInCurrentStage(sourceContainer);
                    sourceContainer.transform.SetParent(combineCollection.TargetParent);

                    foreach (ModelData data in modelData)
                    {
                        data.Transform.SetParent(sourceContainer.transform);
                    }

                    EditorUtility.SetDirty(sourceContainer);
                }
            }


            if (afterCombineAction.HasFlag(AfterCombineAction.RenameObjects))
            {
                foreach (ModelData data in modelData)
                {
                    GameObject dataObject = data.Transform.gameObject;
                    dataObject.name = $"[Combined] {dataObject.name}";

                    EditorUtility.SetDirty(dataObject);
                }
            }

            if (afterCombineAction.HasFlag(AfterCombineAction.DisableObjects))
            {
                foreach (ModelData data in modelData)
                {
                    GameObject gameObject = data.Transform.gameObject;
                    gameObject.SetActive(false);
                    EditorUtility.SetDirty(gameObject);
                }

                foreach (CombinedObject obj in combineCollection.NestedCombinedObjects)
                {
                    obj.gameObject.SetActive(false);
                    EditorUtility.SetDirty(obj);
                }
            }

            if (afterCombineAction.HasFlag(AfterCombineAction.DisableMeshRenderers))
            {
                foreach (ModelData data in modelData)
                {
                    data.Renderer.enabled = false;
                    EditorUtility.SetDirty(data.Renderer);
                }

                foreach (CombinedObject obj in combineCollection.NestedCombinedObjects)
                {
                    if (obj.TryGetComponent(out MeshRenderer renderer))
                    {
                        renderer.enabled = false;
                        EditorUtility.SetDirty(obj);
                    }
                }
            }

            if (afterCombineAction.HasFlag(AfterCombineAction.StripObjectsOnBuild))
            {
                foreach (ModelData data in modelData)
                {
                    data.Renderer.tag = "EditorOnly";
                }

                foreach (CombinedObject obj in combineCollection.NestedCombinedObjects)
                {
                    obj.tag = "EditorOnly";
                    EditorUtility.SetDirty(obj);
                }
            }
        }

        private static Mesh SaveMesh([NotNull] string folderPath, MeshSaveFormat saveFormat, Mesh mesh, int index, ref List<string> createdFiles)
        {
            int lastDotIndex = folderPath.LastIndexOf(".", StringComparison.Ordinal);
            if (lastDotIndex < 0)
            {
                lastDotIndex = folderPath.Length;
            }
            string   path      = folderPath[..lastDotIndex];
            string[] splitPath = path.Split('_');

            string fullPath = $"{path}";
            if (!int.TryParse(splitPath[^1], out _))
            {
                fullPath += $"{mesh.name}_{index}";
            }

            switch (saveFormat)
            {
                case MeshSaveFormat.Asset:
                    fullPath += ".asset";
                    AssetDatabase.DeleteAsset(fullPath);
                    AssetDatabase.CreateAsset(mesh, fullPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    break;
                case MeshSaveFormat.Fbx:
                    fullPath += ".fbx";
                    ExportModelOptions exportModelOptions = new()
                                                            {
                                                                ExportFormat           = ExportFormat.Binary,
                                                                ExportUnrendered       = true,
                                                                ModelAnimIncludeOption = Include.Model,
                                                                EmbedTextures          = false
                                                            };
                    GameObject tempObject = new GameObject(mesh.name);
                    tempObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    ModelExporter.ExportObject(fullPath, tempObject, exportModelOptions);
                    Object.DestroyImmediate(tempObject);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(saveFormat), saveFormat, null);
            }

            createdFiles.Add(fullPath);
            return AssetDatabase.LoadAssetAtPath<Mesh>(fullPath);
        }

        private static MeshRenderer CreateMeshRenderer(GameObject targetObject, string name, Mesh mesh, Material material, Transform parent = null)
        {
            targetObject.name = name;
            if (parent)
            {
                targetObject.transform.parent = parent;
            }

            targetObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = targetObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;

            return meshRenderer;
        }

        // private static void SynchronizePrefabStage(string prefabStagePath)
        // {
        //     var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
        //
        //     if (!prefabStagePath.Equals(currentStage.assetPath, StringComparison.Ordinal))
        //     {
        //         PrefabStageUtility.OpenPrefab(prefabStagePath);
        //     }
        // }
        //
        // private static void SynchronizePrefabStage(GameObject prefabObject)
        // {
        //     var    sourceStage = PrefabStageUtility.GetPrefabStage(prefabObject);
        //     string target      = !sourceStage ? AssetDatabase.GetAssetPath(prefabObject) : sourceStage.assetPath;
        //
        //     var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
        //
        //     if (!target.Equals(currentStage.assetPath, StringComparison.Ordinal))
        //     {
        //         PrefabStageUtility.OpenPrefab(target);
        //     }
        // }
    }
}
