using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRSNC.Anatomy;

public static class BrainAnatomyHierarchyBuilder
{
    private const string BrainRootName = "Brain";
    private const string AnatomicalRootName = "Brain_AnatomicalOrganization";
    private const string TechnicalScopePath = "_apoio-local/ESCOPO_TECNICO_LOCAL.md";
    private const int ExpectedCatalogRows = 240;

    [MenuItem("Tools/VR SNC/Build Anatomical Organization")]
    public static void BuildFromMenu()
    {
        BuildAnatomicalOrganization(allowDialogs: true);
    }

    public static void BuildFromCommandLine()
    {
        BuildAnatomicalOrganization(allowDialogs: false);
    }

    public static void BuildAndSaveFromCommandLine()
    {
        if (!BuildAnatomicalOrganization(allowDialogs: false))
        {
            return;
        }

        var activeScene = SceneManager.GetActiveScene();
        if (EditorSceneManager.SaveScene(activeScene))
        {
            Debug.Log($"[VR SNC] Saved active scene after anatomical organization build: {activeScene.path}");
        }
        else
        {
            Debug.LogError($"[VR SNC] Failed to save active scene after anatomical organization build: {activeScene.path}");
        }
    }

    private static bool BuildAnatomicalOrganization(bool allowDialogs)
    {
        var report = new BuildReport();
        var catalog = ReadCatalog(report);
        ValidateCatalog(catalog, report);

        var brainRoot = GameObject.Find(BrainRootName);
        if (brainRoot == null)
        {
            Debug.LogError("[VR SNC] Build canceled: object 'Brain' was not found in the active scene.");
            return false;
        }

        var originalSceneSnapshot = CaptureBrainSnapshot(brainRoot);
        var sceneObjects = FindRenderableObjects(brainRoot.transform, report);
        report.SceneAnatomicalObjectCount = sceneObjects.Count;
        report.ObjectsFoundInScene = catalog.Count(row => sceneObjects.ContainsKey(row.ObjectName));
        report.TableObjectsMissingInScene.AddRange(catalog
            .Where(row => !sceneObjects.ContainsKey(row.ObjectName))
            .Select(row => row.ObjectName));

        var catalogNames = new HashSet<string>(catalog.Select(row => row.ObjectName), StringComparer.OrdinalIgnoreCase);
        report.SceneObjectsNotInTable.AddRange(sceneObjects.Keys
            .Where(name => !catalogNames.Contains(name))
            .OrderBy(name => name));

        LogValidation(report);

        if (report.ValidCatalogRows != ExpectedCatalogRows || report.InvalidRows.Count > 0)
        {
            Debug.LogError("[VR SNC] Build canceled: catalog validation failed before scene changes.");
            return false;
        }

        var existingRoot = GameObject.Find(AnatomicalRootName);
        if (existingRoot != null)
        {
            if (!allowDialogs)
            {
                Debug.LogError("[VR SNC] Build canceled: Brain_AnatomicalOrganization already exists. Use the Editor menu to choose rebuild or cancel.");
                return false;
            }

            var rebuild = EditorUtility.DisplayDialog(
                "VR SNC Anatomical Organization",
                "Brain_AnatomicalOrganization already exists in the active scene. Delete it and rebuild?",
                "Delete and rebuild",
                "Cancel");

            if (!rebuild)
            {
                Debug.Log("[VR SNC] Build canceled by user. Existing Brain_AnatomicalOrganization was preserved.");
                return false;
            }

            UnityEngine.Object.DestroyImmediate(existingRoot);
        }

        var anatomicalRoot = new GameObject(AnatomicalRootName);
        anatomicalRoot.SetActive(false);
        Undo.RegisterCreatedObjectUndo(anatomicalRoot, "Create anatomical organization root");

        var groupMap = CreateGroupHierarchy(anatomicalRoot.transform);

        foreach (var row in catalog)
        {
            if (!sceneObjects.TryGetValue(row.ObjectName, out var original))
            {
                continue;
            }

            var classification = Classify(row);
            var parent = GetOrFallbackGroup(groupMap, classification.GroupPath);
            var duplicate = DuplicateRenderableObject(original, parent);
            duplicate.name = original.name;

            var info = duplicate.AddComponent<BrainStructureInfo>();
            info.SetMetadata(
                anatomicalName: original.name,
                portugueseName: string.Empty,
                originalScenePath: GetScenePath(original.transform),
                originalParentGroup: original.transform.parent != null ? original.transform.parent.name : string.Empty,
                majorDivision: classification.MajorDivision,
                subdivision: classification.Subdivision,
                anatomicalGroup: classification.AnatomicalSystem,
                corticalLobe: classification.CorticalLobe,
                hemisphereOrSpatialDivision: DetermineSide(original.name),
                layerIndex: classification.LayerIndex,
                shortDescription: row.SubdivisionDescription,
                functionalDescription: string.Empty,
                reviewStatus: classification.ReviewStatus,
                sourceObjectName: row.ObjectFileName,
                sourceCatalogDivision: row.MajorDivision,
                sourceCatalogSubdivision: row.SubdivisionDescription);

            if (classification.ReviewStatus == BrainStructureReviewStatus.Classified)
            {
                report.ClassifiedObjects++;
            }
            else
            {
                report.UnclassifiedObjects.Add(original.name);
            }

            report.DuplicatedObjects++;
        }

        var unchanged = CompareBrainSnapshot(originalSceneSnapshot, CaptureBrainSnapshot(brainRoot));
        report.BrainOriginalUnchanged = unchanged;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        LogFinalReport(report);

        if (!unchanged)
        {
            Debug.LogError("[VR SNC] WARNING: Brain snapshot changed during the build. Review the scene before saving.");
            return false;
        }

        return true;
    }

    private static List<CatalogRow> ReadCatalog(BuildReport report)
    {
        var absolutePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, TechnicalScopePath);
        var rows = new List<CatalogRow>();

        if (!File.Exists(absolutePath))
        {
            report.InvalidRows.Add($"Catalog file not found: {absolutePath}");
            return rows;
        }

        var lines = File.ReadAllLines(absolutePath);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal) ||
                !line.Contains(".obj", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(":---", StringComparison.Ordinal))
            {
                continue;
            }

            var columns = line.Split('|')
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();

            if (columns.Length > 0 && string.Equals(columns[0], "Object (.obj)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (columns.Length < 3 || !columns[0].EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
            {
                report.InvalidRows.Add($"Line {index + 1}: {line}");
                continue;
            }

            rows.Add(new CatalogRow(columns[0], columns[1], columns[2], index + 1));
        }

        report.TableRowsRead = rows.Count;
        return rows;
    }

    private static void ValidateCatalog(IReadOnlyCollection<CatalogRow> catalog, BuildReport report)
    {
        report.ValidCatalogRows = catalog.Count;
        var duplicateGroups = catalog
            .GroupBy(row => row.ObjectName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key);

        foreach (var group in duplicateGroups)
        {
            report.DuplicateTableNames.Add($"{group.Key} ({group.Count()} rows)");
        }
    }

    private static Dictionary<string, GameObject> FindRenderableObjects(Transform root, BuildReport report)
    {
        var result = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = meshFilter.GetComponent<MeshRenderer>();
            if (renderer == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            var objectKey = GetCatalogLookupName(meshFilter.gameObject) ?? meshFilter.gameObject.name;
            if (!result.TryAdd(objectKey, meshFilter.gameObject))
            {
                duplicates.Add(objectKey);
            }
        }

        if (duplicates.Count > 0)
        {
            report.SceneNameConflicts.AddRange(duplicates.OrderBy(name => name));
            Debug.LogWarning("[VR SNC] Scene name conflicts found under Brain: " + string.Join(", ", report.SceneNameConflicts));
        }

        return result;
    }

    private static string GetCatalogLookupName(GameObject sceneObject)
    {
        var sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(sceneObject);
        var sourcePath = sourceObject != null ? AssetDatabase.GetAssetPath(sourceObject) : string.Empty;

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            var meshFilter = sceneObject.GetComponent<MeshFilter>();
            sourcePath = meshFilter != null && meshFilter.sharedMesh != null
                ? AssetDatabase.GetAssetPath(meshFilter.sharedMesh)
                : string.Empty;
        }

        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !string.Equals(Path.GetExtension(sourcePath), ".obj", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetFileNameWithoutExtension(sourcePath);
    }

    private static Dictionary<string, Transform> CreateGroupHierarchy(Transform root)
    {
        var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);

        AddGroup(map, root, "Cerebro", BrainMajorDivision.Cerebro, BrainAnatomicalSubdivision.Unknown, "Root brain division");
        AddGroup(map, root, "Cerebro/Telencefalo", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.Unknown, "Catalog division: Telencephalon");
        AddGroup(map, root, "Cerebro/Telencefalo/Cortex_Cerebral", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, "Cortical grouping");
        AddGroup(map, root, "Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Frontal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, "Frontal lobe");
        AddGroup(map, root, "Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Parietal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, "Parietal lobe");
        AddGroup(map, root, "Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Temporal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, "Temporal lobe");
        AddGroup(map, root, "Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Occipital", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, "Occipital lobe");
        AddGroup(map, root, "Cerebro/Telencefalo/Cortex_Cerebral/Insula", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, "Insular lobe");
        AddGroup(map, root, "Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Limbico", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, "Limbic lobe");
        AddGroup(map, root, "Cerebro/Telencefalo/Formacao_Hipocampal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.FormacaoHipocampal, "Hippocampal formation");
        AddGroup(map, root, "Cerebro/Telencefalo/Sistema_Limbico", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.SistemaLimbico, "Limbic system");
        AddGroup(map, root, "Cerebro/Telencefalo/Ganglios_da_Base", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.GangliosDaBase, "Basal ganglia");
        AddGroup(map, root, "Cerebro/Telencefalo/Sistema_Olfatorio", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.SistemaOlfatorio, "Olfactory system");
        AddGroup(map, root, "Cerebro/Telencefalo/Substancia_Branca_Comissuras_e_Tratos", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.SubstanciaBrancaComissurasETratos, "White matter, commissures and tracts");
        AddGroup(map, root, "Cerebro/Telencefalo/Area_Septal_e_Prosencefalo_Basal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.AreaSeptalEProsencefaloBasal, "Septal area and basal forebrain");

        AddGroup(map, root, "Cerebro/Diencefalo", BrainMajorDivision.Diencefalo, BrainAnatomicalSubdivision.Unknown, "Catalog division: Diencephalon");
        AddGroup(map, root, "Cerebro/Diencefalo/Talamo", BrainMajorDivision.Diencefalo, BrainAnatomicalSubdivision.Talamo, "Thalamus");
        AddGroup(map, root, "Cerebro/Diencefalo/Hipotalamo", BrainMajorDivision.Diencefalo, BrainAnatomicalSubdivision.Hipotalamo, "Hypothalamus");
        AddGroup(map, root, "Cerebro/Diencefalo/Epitalamo", BrainMajorDivision.Diencefalo, BrainAnatomicalSubdivision.Epitalamo, "Epithalamus");
        AddGroup(map, root, "Cerebro/Diencefalo/Subtalamo", BrainMajorDivision.Diencefalo, BrainAnatomicalSubdivision.Subtalamo, "Subthalamus");

        AddGroup(map, root, "Cerebelo", BrainMajorDivision.Cerebelo, BrainAnatomicalSubdivision.Cerebelo, "Catalog division: Cerebellum");

        AddGroup(map, root, "Tronco_Encefalico", BrainMajorDivision.TroncoEncefalico, BrainAnatomicalSubdivision.Unknown, "Catalog division: Brainstem");
        AddGroup(map, root, "Tronco_Encefalico/Mesencefalo", BrainMajorDivision.TroncoEncefalico, BrainAnatomicalSubdivision.Mesencefalo, "Mesencephalon");
        AddGroup(map, root, "Tronco_Encefalico/Ponte", BrainMajorDivision.TroncoEncefalico, BrainAnatomicalSubdivision.Ponte, "Pons");
        AddGroup(map, root, "Tronco_Encefalico/Bulbo_Medula_Oblonga", BrainMajorDivision.TroncoEncefalico, BrainAnatomicalSubdivision.BulboMedulaOblonga, "Medulla oblongata");

        AddGroup(map, root, "Sistema_Ventricular", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.Unknown, "Catalog division: Ventricular system");
        AddGroup(map, root, "Sistema_Ventricular/Ventriculos_Laterais", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.VentriculosLaterais, "Lateral ventricle");
        AddGroup(map, root, "Sistema_Ventricular/Terceiro_Ventriculo", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.TerceiroVentriculo, "Third ventricle");
        AddGroup(map, root, "Sistema_Ventricular/Aqueduto_Cerebral", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.AquedutoCerebral, "Cerebral aqueduct");
        AddGroup(map, root, "Sistema_Ventricular/Quarto_Ventriculo", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.QuartoVentriculo, "Fourth ventricle");
        AddGroup(map, root, "Sistema_Ventricular/Plexo_Coroide", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.PlexoCoroide, "Choroid plexus");

        AddGroup(map, root, "Outros_Revisao", BrainMajorDivision.OutrosRevisao, BrainAnatomicalSubdivision.RevisaoAnatomicaNecessaria, "Review root");
        AddGroup(map, root, "Outros_Revisao/Nao_Classificado", BrainMajorDivision.OutrosRevisao, BrainAnatomicalSubdivision.NaoClassificado, "Not classified");
        AddGroup(map, root, "Outros_Revisao/Revisao_Anatomica_Necessaria", BrainMajorDivision.OutrosRevisao, BrainAnatomicalSubdivision.RevisaoAnatomicaNecessaria, "Anatomical review required");

        return map;
    }

    private static Transform AddGroup(
        IDictionary<string, Transform> map,
        Transform root,
        string path,
        BrainMajorDivision majorDivision,
        BrainAnatomicalSubdivision subdivision,
        string sourceRule)
    {
        if (map.TryGetValue(path, out var existing))
        {
            return existing;
        }

        var segments = path.Split('/');
        var current = root;
        var currentPath = string.Empty;

        foreach (var segment in segments)
        {
            currentPath = currentPath.Length == 0 ? segment : $"{currentPath}/{segment}";
            if (!map.TryGetValue(currentPath, out var child))
            {
                var childObject = new GameObject(segment);
                child = childObject.transform;
                child.SetParent(current, false);
                childObject.AddComponent<BrainAnatomicalGroup>()
                    .SetGroup(segment, currentPath, majorDivision, subdivision, sourceRule);
                map[currentPath] = child;
            }

            current = child;
        }

        return current;
    }

    private static Transform GetOrFallbackGroup(IReadOnlyDictionary<string, Transform> groupMap, string path)
    {
        if (groupMap.TryGetValue(path, out var group))
        {
            return group;
        }

        return groupMap["Outros_Revisao/Nao_Classificado"];
    }

    private static GameObject DuplicateRenderableObject(GameObject original, Transform parent)
    {
        var duplicate = new GameObject(original.name);
        duplicate.transform.SetParent(parent, false);
        CopyWorldTransform(original.transform, duplicate.transform);

        var originalMesh = original.GetComponent<MeshFilter>();
        var originalRenderer = original.GetComponent<MeshRenderer>();
        if (originalMesh != null && originalRenderer != null)
        {
            var mesh = duplicate.AddComponent<MeshFilter>();
            mesh.sharedMesh = originalMesh.sharedMesh;

            var renderer = duplicate.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = originalRenderer.sharedMaterials;
            renderer.shadowCastingMode = originalRenderer.shadowCastingMode;
            renderer.receiveShadows = originalRenderer.receiveShadows;
            renderer.lightProbeUsage = originalRenderer.lightProbeUsage;
            renderer.reflectionProbeUsage = originalRenderer.reflectionProbeUsage;
        }

        foreach (var childMesh in original.GetComponentsInChildren<MeshFilter>(true))
        {
            if (childMesh.gameObject == original)
            {
                continue;
            }

            var childRenderer = childMesh.GetComponent<MeshRenderer>();
            if (childRenderer == null)
            {
                continue;
            }

            var childDuplicate = new GameObject(childMesh.gameObject.name);
            childDuplicate.transform.SetParent(duplicate.transform, false);
            CopyWorldTransform(childMesh.transform, childDuplicate.transform);

            var mesh = childDuplicate.AddComponent<MeshFilter>();
            mesh.sharedMesh = childMesh.sharedMesh;

            var renderer = childDuplicate.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = childRenderer.sharedMaterials;
            renderer.shadowCastingMode = childRenderer.shadowCastingMode;
            renderer.receiveShadows = childRenderer.receiveShadows;
            renderer.lightProbeUsage = childRenderer.lightProbeUsage;
            renderer.reflectionProbeUsage = childRenderer.reflectionProbeUsage;
        }

        return duplicate;
    }

    private static void CopyWorldTransform(Transform source, Transform target)
    {
        target.position = source.position;
        target.rotation = source.rotation;
        SetWorldScale(target, source.lossyScale);
    }

    private static void SetWorldScale(Transform target, Vector3 worldScale)
    {
        var parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(
            SafeDivide(worldScale.x, parentScale.x),
            SafeDivide(worldScale.y, parentScale.y),
            SafeDivide(worldScale.z, parentScale.z));
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Approximately(divisor, 0f) ? value : value / divisor;
    }

    private static Classification Classify(CatalogRow row)
    {
        var division = Normalize(row.MajorDivision);
        var description = Normalize(row.SubdivisionDescription);

        if (division == "telencephalon")
        {
            if (description.Contains("frontal lobe") || description.Contains("orbitofrontal") || description.Contains("gyrus rectus") || description.Contains("precentral gyrus"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Frontal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, BrainAnatomicalSystem.Cortical, BrainCorticalLobe.Frontal, 1);
            }

            if (description.Contains("parietal lobe") || description.Contains("postcentral gyrus") || description.Contains("precuneus") || description.Contains("angular gyrus") || description.Contains("supramarginal"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Parietal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, BrainAnatomicalSystem.Cortical, BrainCorticalLobe.Parietal, 1);
            }

            if (description.Contains("occipitotemporal"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Temporal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, BrainAnatomicalSystem.Cortical, BrainCorticalLobe.Occipitotemporal, 1);
            }

            if (description.Contains("temporal lobe") || description.Contains("transverse temporal") || description.Contains("medial temporal"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Temporal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, BrainAnatomicalSystem.Cortical, BrainCorticalLobe.Temporal, 1);
            }

            if (description.Contains("occipital lobe") || description.Contains("lingual gyrus") || description.Contains("cuneus"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Occipital", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, BrainAnatomicalSystem.Cortical, BrainCorticalLobe.Occipital, 1);
            }

            if (description.Contains("insular lobe"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Cortex_Cerebral/Insula", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, BrainAnatomicalSystem.Cortical, BrainCorticalLobe.Insula, 1);
            }

            if (description.Contains("limbic lobe"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Cortex_Cerebral/Lobo_Limbico", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.CortexCerebral, BrainAnatomicalSystem.Limbic, BrainCorticalLobe.Limbic, 1);
            }

            if (description.Contains("olfactory"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Sistema_Olfatorio", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.SistemaOlfatorio, BrainAnatomicalSystem.Olfactory, BrainCorticalLobe.None, 2);
            }

            if (description.Contains("white matter") || description.Contains("commissural") || description.Contains("commissure") || description.Contains("projection fibers") || description.Contains("fornix") || description.Contains("capsule") || description.Contains("stria") || description.Contains("tract"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Substancia_Branca_Comissuras_e_Tratos", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.SubstanciaBrancaComissurasETratos, BrainAnatomicalSystem.WhiteMatter, BrainCorticalLobe.None, 2);
            }

            if (description.Contains("septal") || description.Contains("basal forebrain"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Area_Septal_e_Prosencefalo_Basal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.AreaSeptalEProsencefaloBasal, BrainAnatomicalSystem.Limbic, BrainCorticalLobe.None, 2);
            }

            if (description.Contains("hippocampal formation"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Formacao_Hipocampal", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.FormacaoHipocampal, BrainAnatomicalSystem.Limbic, BrainCorticalLobe.None, 2);
            }

            if (description.Contains("limbic system") || description.Contains("amygdaloid") || description.Contains("periamygdaloid") || description.Contains("limbic cortex"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Sistema_Limbico", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.SistemaLimbico, BrainAnatomicalSystem.Limbic, BrainCorticalLobe.Limbic, 2);
            }

            if (description.Contains("basal ganglia"))
            {
                return Classification.Classified("Cerebro/Telencefalo/Ganglios_da_Base", BrainMajorDivision.Telencefalo, BrainAnatomicalSubdivision.GangliosDaBase, BrainAnatomicalSystem.BasalGanglia, BrainCorticalLobe.None, 2);
            }
        }

        if (division == "diencephalon")
        {
            if (description.Contains("hypothalamus") || description.Contains("hypothalamic") || description.Contains("preoptic") || description.Contains("mammillary") || description.Contains("tuberal") || description.Contains("suprachiasmatic") || description.Contains("supraoptic"))
            {
                return Classification.Classified("Cerebro/Diencefalo/Hipotalamo", BrainMajorDivision.Diencefalo, BrainAnatomicalSubdivision.Hipotalamo, BrainAnatomicalSystem.Diencephalic, BrainCorticalLobe.None, 3);
            }

            if (description.Contains("epithalamus") || description.Contains("pineal") || description.Contains("habenula") || description.Contains("stria medullaris") || description.Contains("posterior commissure"))
            {
                return Classification.Classified("Cerebro/Diencefalo/Epitalamo", BrainMajorDivision.Diencefalo, BrainAnatomicalSubdivision.Epitalamo, BrainAnatomicalSystem.Diencephalic, BrainCorticalLobe.None, 3);
            }

            if (description.Contains("subthalamus") || description.Contains("subthalamic"))
            {
                return Classification.Classified("Cerebro/Diencefalo/Subtalamo", BrainMajorDivision.Diencefalo, BrainAnatomicalSubdivision.Subtalamo, BrainAnatomicalSystem.Diencephalic, BrainCorticalLobe.None, 3);
            }

            if (description.Contains("thalamus") || description.Contains("thalamic") || description.Contains("pulvinar") || description.Contains("geniculate") || description.Contains("mediodorsal") || description.Contains("intralaminar") || description.Contains("metathalamus") || description.Contains("anterior nuclear") || description.Contains("reticular nucleus"))
            {
                return Classification.Classified("Cerebro/Diencefalo/Talamo", BrainMajorDivision.Diencefalo, BrainAnatomicalSubdivision.Talamo, BrainAnatomicalSystem.Diencephalic, BrainCorticalLobe.None, 3);
            }
        }

        if (division == "cerebellum")
        {
            return Classification.Classified("Cerebelo", BrainMajorDivision.Cerebelo, BrainAnatomicalSubdivision.Cerebelo, BrainAnatomicalSystem.Cerebellar, BrainCorticalLobe.None, 4);
        }

        if (division == "brainstem")
        {
            if (description.Contains("pons"))
            {
                return Classification.Classified("Tronco_Encefalico/Ponte", BrainMajorDivision.TroncoEncefalico, BrainAnatomicalSubdivision.Ponte, BrainAnatomicalSystem.Brainstem, BrainCorticalLobe.None, 4);
            }

            if (description.Contains("medulla"))
            {
                return Classification.Classified("Tronco_Encefalico/Bulbo_Medula_Oblonga", BrainMajorDivision.TroncoEncefalico, BrainAnatomicalSubdivision.BulboMedulaOblonga, BrainAnatomicalSystem.Brainstem, BrainCorticalLobe.None, 4);
            }

            if (description.Contains("mesencephalon") || description.Contains("colliculus") || description.Contains("substantia nigra") || description.Contains("red nucleus") || description.Contains("cerebral crus"))
            {
                return Classification.Classified("Tronco_Encefalico/Mesencefalo", BrainMajorDivision.TroncoEncefalico, BrainAnatomicalSubdivision.Mesencefalo, BrainAnatomicalSystem.Brainstem, BrainCorticalLobe.None, 4);
            }
        }

        if (division == "ventricular system")
        {
            if (description.Contains("lateral ventricle"))
            {
                return Classification.Classified("Sistema_Ventricular/Ventriculos_Laterais", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.VentriculosLaterais, BrainAnatomicalSystem.Ventricular, BrainCorticalLobe.None, 3);
            }

            if (description.Contains("third ventricle"))
            {
                return Classification.Classified("Sistema_Ventricular/Terceiro_Ventriculo", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.TerceiroVentriculo, BrainAnatomicalSystem.Ventricular, BrainCorticalLobe.None, 3);
            }

            if (description.Contains("cerebral aqueduct"))
            {
                return Classification.Classified("Sistema_Ventricular/Aqueduto_Cerebral", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.AquedutoCerebral, BrainAnatomicalSystem.Ventricular, BrainCorticalLobe.None, 3);
            }

            if (description.Contains("fourth ventricle"))
            {
                return Classification.Classified("Sistema_Ventricular/Quarto_Ventriculo", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.QuartoVentriculo, BrainAnatomicalSystem.Ventricular, BrainCorticalLobe.None, 3);
            }

            if (description.Contains("choroid plexus"))
            {
                return Classification.Classified("Sistema_Ventricular/Plexo_Coroide", BrainMajorDivision.SistemaVentricular, BrainAnatomicalSubdivision.PlexoCoroide, BrainAnatomicalSystem.Ventricular, BrainCorticalLobe.None, 3);
            }
        }

        return new Classification(
            "Outros_Revisao/Nao_Classificado",
            BrainMajorDivision.OutrosRevisao,
            BrainAnatomicalSubdivision.NaoClassificado,
            BrainAnatomicalSystem.ReviewRequired,
            BrainCorticalLobe.ReviewRequired,
            -1,
            BrainStructureReviewStatus.NeedsReview);
    }

    private static BrainSide DetermineSide(string objectName)
    {
        var normalized = Normalize(objectName);
        if (normalized.StartsWith("left ", StringComparison.Ordinal) || normalized.Contains(" of left ", StringComparison.Ordinal))
        {
            return BrainSide.Left;
        }

        if (normalized.StartsWith("right ", StringComparison.Ordinal) || normalized.Contains(" of right ", StringComparison.Ordinal))
        {
            return BrainSide.Right;
        }

        return BrainSide.Unspecified;
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string GetScenePath(Transform transform)
    {
        var names = new Stack<string>();
        var current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static string CaptureBrainSnapshot(GameObject brainRoot)
    {
        var entries = brainRoot.GetComponentsInChildren<Transform>(true)
            .OrderBy(transform => GetScenePath(transform))
            .Select(transform => string.Join("|",
                GetScenePath(transform),
                transform.parent != null ? GetScenePath(transform.parent) : string.Empty,
                transform.localPosition.ToString("R"),
                transform.localRotation.ToString("R"),
                transform.localScale.ToString("R"),
                transform.gameObject.activeSelf));

        return string.Join("\n", entries);
    }

    private static bool CompareBrainSnapshot(string before, string after)
    {
        return string.Equals(before, after, StringComparison.Ordinal);
    }

    private static void LogValidation(BuildReport report)
    {
        Debug.Log("[VR SNC] Anatomical catalog validation\n" +
                  $"Rows read: {report.TableRowsRead}\n" +
                  $"Valid rows: {report.ValidCatalogRows}\n" +
                  $"Scene anatomical renderable objects under Brain: {report.SceneAnatomicalObjectCount}\n" +
                  $"Table objects found in scene: {report.ObjectsFoundInScene}\n" +
                  $"Duplicate names in table: {report.DuplicateTableNames.Count}\n" +
                  $"Possible scene name conflicts: {report.SceneNameConflicts.Count}\n" +
                  $"Invalid table rows: {report.InvalidRows.Count}\n" +
                  $"Table rows without scene object: {report.TableObjectsMissingInScene.Count}\n" +
                  $"Scene objects not in table: {report.SceneObjectsNotInTable.Count}");

        LogList("Duplicate table names", report.DuplicateTableNames);
        LogList("Possible scene name conflicts", report.SceneNameConflicts);
        LogList("Invalid table rows", report.InvalidRows);
        LogList("Table objects not found in scene", report.TableObjectsMissingInScene);
        LogList("Scene objects not present in table", report.SceneObjectsNotInTable);
    }

    private static void LogFinalReport(BuildReport report)
    {
        Debug.Log("[VR SNC] Anatomical organization build report\n" +
                  $"Table rows read: {report.TableRowsRead}\n" +
                  $"Objects found in scene: {report.ObjectsFoundInScene}\n" +
                  $"Objects duplicated: {report.DuplicatedObjects}\n" +
                  $"Objects classified: {report.ClassifiedObjects}\n" +
                  $"Objects not found: {report.TableObjectsMissingInScene.Count}\n" +
                  $"Objects not classified: {report.UnclassifiedObjects.Count}\n" +
                  $"Possible scene name conflicts: {report.SceneNameConflicts.Count}\n" +
                  $"Brain original unchanged: {report.BrainOriginalUnchanged}");

        LogList("Unclassified objects", report.UnclassifiedObjects);
        LogList("Possible scene name conflicts", report.SceneNameConflicts);
        LogList("Table objects not found in scene", report.TableObjectsMissingInScene);
        LogList("Scene objects not present in table", report.SceneObjectsNotInTable);
    }

    private static void LogList(string title, IReadOnlyCollection<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        Debug.Log($"[VR SNC] {title} ({items.Count}):\n- " + string.Join("\n- ", items));
    }

    private sealed class CatalogRow
    {
        public CatalogRow(string objectFileName, string majorDivision, string subdivisionDescription, int lineNumber)
        {
            ObjectFileName = objectFileName;
            ObjectName = objectFileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
                ? objectFileName.Substring(0, objectFileName.Length - 4)
                : objectFileName;
            MajorDivision = majorDivision;
            SubdivisionDescription = subdivisionDescription;
            LineNumber = lineNumber;
        }

        public string ObjectFileName { get; }
        public string ObjectName { get; }
        public string MajorDivision { get; }
        public string SubdivisionDescription { get; }
        public int LineNumber { get; }
    }

    private readonly struct Classification
    {
        public Classification(
            string groupPath,
            BrainMajorDivision majorDivision,
            BrainAnatomicalSubdivision subdivision,
            BrainAnatomicalSystem anatomicalSystem,
            BrainCorticalLobe corticalLobe,
            int layerIndex,
            BrainStructureReviewStatus reviewStatus)
        {
            GroupPath = groupPath;
            MajorDivision = majorDivision;
            Subdivision = subdivision;
            AnatomicalSystem = anatomicalSystem;
            CorticalLobe = corticalLobe;
            LayerIndex = layerIndex;
            ReviewStatus = reviewStatus;
        }

        public string GroupPath { get; }
        public BrainMajorDivision MajorDivision { get; }
        public BrainAnatomicalSubdivision Subdivision { get; }
        public BrainAnatomicalSystem AnatomicalSystem { get; }
        public BrainCorticalLobe CorticalLobe { get; }
        public int LayerIndex { get; }
        public BrainStructureReviewStatus ReviewStatus { get; }

        public static Classification Classified(
            string groupPath,
            BrainMajorDivision majorDivision,
            BrainAnatomicalSubdivision subdivision,
            BrainAnatomicalSystem anatomicalSystem,
            BrainCorticalLobe corticalLobe,
            int layerIndex)
        {
            return new Classification(groupPath, majorDivision, subdivision, anatomicalSystem, corticalLobe, layerIndex, BrainStructureReviewStatus.Classified);
        }
    }

    private sealed class BuildReport
    {
        public int TableRowsRead;
        public int ValidCatalogRows;
        public int SceneAnatomicalObjectCount;
        public int ObjectsFoundInScene;
        public int DuplicatedObjects;
        public int ClassifiedObjects;
        public bool BrainOriginalUnchanged;
        public readonly List<string> DuplicateTableNames = new();
        public readonly List<string> SceneNameConflicts = new();
        public readonly List<string> InvalidRows = new();
        public readonly List<string> TableObjectsMissingInScene = new();
        public readonly List<string> SceneObjectsNotInTable = new();
        public readonly List<string> UnclassifiedObjects = new();
    }
}
