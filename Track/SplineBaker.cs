using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(SplineContainer))]
[ExecuteInEditMode]
public class SplineBaker : MonoBehaviour
{
    [Header("Terrain Baking Settings")]
    public LayerMask terrainLayer = ~0;
    public float raycastHeight = 1000f;
    public float heightOffset = 0.5f;

    [Header("Smoothing Settings")]
    [Range(1, 20)]
    [Tooltip("Сколько раз применять сглаживание. Больше = более гладкая трасса.")]
    public int smoothingIterations = 5;

    [Tooltip("Радиус окна сглаживания (кол-во соседей в каждую сторону). " +
             "1 = 3 точки (слабо), 3 = 7 точек (сильно), 5 = 11 точек (агрессивно).")]
    [Range(1, 5)]
    public int smoothingRadius = 3;

    [Header("Seam Auto-Fix")]
    [Tooltip("Порог (метры). Если разница высот между двумя соседними точками больше этого — считается швом.")]
    public float seamThreshold = 0.3f;
    [Tooltip("Сколько точек в каждую сторону от шва обрабатывать.")]
    [Range(1, 10)]
    public int seamFixRadius = 5;
    [Tooltip("Итерации сглаживания зоны шва.")]
    [Range(1, 20)]
    public int seamFixIterations = 10;

    [Header("Zone Smoothing (Local Fixes)")]
    public float zoneRadius = 20f;
    public List<Transform> smoothingZones = new List<Transform>();

    [Header("Optimization Settings")]
    public float simplifyAngleThreshold = 2.0f;

    [Header("Macro Smoothing (Highway Mode)")]
    public float macroAnchorDistance = 150f;

    private SplineContainer splineContainer;

    private void OnEnable()
    {
        splineContainer = GetComponent<SplineContainer>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  1. BAKE TO TERRAIN
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("1. Bake To Terrain")]
    public void BakeToTerrain()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0) return;

#if UNITY_EDITOR
        Undo.RecordObject(splineContainer.gameObject, "Bake Spline To Terrain");
#endif

        Spline spline = splineContainer.Spline;
        int knotCount = spline.Count;
        int bakedCount = 0;

        for (int i = 0; i < knotCount; i++)
        {
            BezierKnot knot = spline[i];
            Vector3 worldPos = transform.TransformPoint((Vector3)knot.Position);

            Vector3 rayStart = new Vector3(worldPos.x, 10000f, worldPos.z);
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, Mathf.Infinity, terrainLayer))
            {
                worldPos.y = hit.point.y + heightOffset;
                knot.Position = (float3)transform.InverseTransformPoint(worldPos);
                spline.SetKnot(i, knot);
                bakedCount++;
            }
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(splineContainer.gameObject);
#endif

        Debug.Log($"[SplineBaker] Baked {bakedCount} out of {knotCount} knots to the terrain!");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  2. AUTO-FIX SEAMS (НОВЫЙ)
    //  Находит резкие скачки высот между соседними точками
    //  и усиленно сглаживает только зону вокруг шва.
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("2. Auto-Fix Seams")]
    public void AutoFixSeams()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0) return;

#if UNITY_EDITOR
        Undo.RecordObject(splineContainer.gameObject, "Auto-Fix Seams");
#endif

        Spline spline = splineContainer.Spline;
        int knotCount = spline.Count;
        if (knotCount < 3) return;

        // Шаг 1: Находим индексы швов (резкие скачки по Y)
        List<int> seamIndices = new List<int>();

        for (int i = 1; i < knotCount; i++)
        {
            float prevY = ((Vector3)spline[i - 1].Position).y;
            float currY = ((Vector3)spline[i].Position).y;
            float deltaY = Mathf.Abs(currY - prevY);

            if (deltaY > seamThreshold)
            {
                seamIndices.Add(i);
            }
        }

        if (seamIndices.Count == 0)
        {
            Debug.Log("[SplineBaker] No seams detected! All joints are smooth.");
            return;
        }

        Debug.Log($"[SplineBaker] Detected {seamIndices.Count} seams. Fixing...");

        // Шаг 2: Для каждого шва — сгладить зону вокруг него
        // Собираем множество индексов, которые нужно обработать
        HashSet<int> affectedIndices = new HashSet<int>();
        foreach (int seamIdx in seamIndices)
        {
            for (int offset = -seamFixRadius; offset <= seamFixRadius; offset++)
            {
                int idx = seamIdx + offset;
                if (spline.Closed)
                {
                    idx = (idx + knotCount) % knotCount;
                }
                else if (idx < 0 || idx >= knotCount)
                {
                    continue;
                }
                affectedIndices.Add(idx);
            }
        }

        // Шаг 3: Итеративное сглаживание только затронутых точек
        for (int iteration = 0; iteration < seamFixIterations; iteration++)
        {
            Vector3[] newPositions = new Vector3[knotCount];
            for (int i = 0; i < knotCount; i++)
                newPositions[i] = (Vector3)spline[i].Position;

            foreach (int i in affectedIndices)
            {
                if (!spline.Closed && (i == 0 || i == knotCount - 1)) continue;

                int prevIdx = (i - 1 + knotCount) % knotCount;
                int nextIdx = (i + 1) % knotCount;

                Vector3 prev = (Vector3)spline[prevIdx].Position;
                Vector3 curr = (Vector3)spline[i].Position;
                Vector3 next = (Vector3)spline[nextIdx].Position;

                // Усреднение только Y — не двигаем точку по горизонтали
                float smoothedY = (prev.y + curr.y + next.y) / 3f;
                newPositions[i] = new Vector3(curr.x, smoothedY, curr.z);
            }

            for (int i = 0; i < knotCount; i++)
            {
                BezierKnot knot = spline[i];
                knot.Position = (float3)newPositions[i];
                spline.SetKnot(i, knot);
            }
        }

        // Пересчитываем тангенсы для затронутых точек
        foreach (int i in affectedIndices)
        {
            spline.SetTangentMode(i, TangentMode.AutoSmooth);
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(splineContainer.gameObject);
#endif

        Debug.Log($"[SplineBaker] Fixed {seamIndices.Count} seams " +
                  $"({affectedIndices.Count} knots × {seamFixIterations} iterations).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  3. SMOOTH SPLINE (УСИЛЕННЫЙ)
    //  Gaussian-like: окно 2*smoothingRadius+1 соседей с весами.
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("3. Smooth Spline")]
    public void SmoothSpline()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0) return;

#if UNITY_EDITOR
        Undo.RecordObject(splineContainer.gameObject, "Smooth Spline");
#endif

        Spline spline = splineContainer.Spline;
        int knotCount = spline.Count;

        if (knotCount < 3)
        {
            Debug.LogWarning("[SplineBaker] Spline is too short for smoothing!");
            return;
        }

        // Вычисляем веса один раз: [1, 2, 3, ..., radius+1, ..., 3, 2, 1]
        int windowSize = smoothingRadius * 2 + 1;
        float[] weights = new float[windowSize];
        float weightSum = 0f;
        for (int w = 0; w < windowSize; w++)
        {
            // Треугольное ядро: максимум в центре, убывает к краям
            weights[w] = smoothingRadius + 1 - Mathf.Abs(w - smoothingRadius);
            weightSum += weights[w];
        }

        for (int iteration = 0; iteration < smoothingIterations; iteration++)
        {
            Vector3[] newPositions = new Vector3[knotCount];

            for (int i = 0; i < knotCount; i++)
            {
                if (!spline.Closed && (i == 0 || i == knotCount - 1))
                {
                    newPositions[i] = (Vector3)spline[i].Position;
                    continue;
                }

                Vector3 currentPos = (Vector3)spline[i].Position;
                float smoothedY = 0f;

                // Взвешенное усреднение по окну [i-radius .. i+radius]
                for (int w = 0; w < windowSize; w++)
                {
                    int neighborOffset = w - smoothingRadius;
                    int neighborIdx;

                    if (spline.Closed)
                    {
                        neighborIdx = (i + neighborOffset + knotCount) % knotCount;
                    }
                    else
                    {
                        neighborIdx = Mathf.Clamp(i + neighborOffset, 0, knotCount - 1);
                    }

                    smoothedY += ((Vector3)spline[neighborIdx].Position).y * weights[w];
                }

                smoothedY /= weightSum;
                newPositions[i] = new Vector3(currentPos.x, smoothedY, currentPos.z);
            }

            // Применяем
            for (int i = 0; i < knotCount; i++)
            {
                BezierKnot knot = spline[i];
                knot.Position = (float3)newPositions[i];
                spline.SetKnot(i, knot);
            }
        }

        // Пересчитываем тангенсы
        for (int i = 0; i < knotCount; i++)
        {
            spline.SetTangentMode(i, TangentMode.AutoSmooth);
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(splineContainer.gameObject);
#endif

        Debug.Log($"[SplineBaker] Smoothing: {smoothingIterations} iterations, " +
                  $"window {windowSize} knots (radius={smoothingRadius}).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  4. SMOOTH INSIDE ZONES ONLY
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("4. Smooth Inside Zones Only")]
    public void SmoothZonesOnly()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0) return;
        if (smoothingZones.Count == 0)
        {
            Debug.LogWarning("[SplineBaker] Add at least one zone (empty GameObject) to smoothingZones array!");
            return;
        }

#if UNITY_EDITOR
        Undo.RecordObject(splineContainer.gameObject, "Smooth Spline Zones");
#endif

        Spline spline = splineContainer.Spline;
        int knotCount = spline.Count;
        if (knotCount < 3) return;

        HashSet<int> modifiedKnots = new HashSet<int>();

        for (int iteration = 0; iteration < smoothingIterations; iteration++)
        {
            Vector3[] newPositions = new Vector3[knotCount];

            for (int i = 0; i < knotCount; i++)
            {
                BezierKnot currentKnot = spline[i];
                Vector3 currentPos = (Vector3)currentKnot.Position;
                Vector3 worldPos = transform.TransformPoint(currentPos);

                bool isInsideZone = false;
                foreach (Transform zone in smoothingZones)
                {
                    if (zone != null && Vector3.Distance(worldPos, zone.position) <= zoneRadius)
                    {
                        isInsideZone = true;
                        break;
                    }
                }

                if (!isInsideZone || (!spline.Closed && (i == 0 || i == knotCount - 1)))
                {
                    newPositions[i] = currentPos;
                    continue;
                }

                int prevIndex = (i - 1 + knotCount) % knotCount;
                int nextIndex = (i + 1) % knotCount;

                Vector3 prevPos = (Vector3)spline[prevIndex].Position;
                Vector3 nextPos = (Vector3)spline[nextIndex].Position;

                float smoothedY = (prevPos.y + currentPos.y + nextPos.y) / 3f;
                newPositions[i] = new Vector3(currentPos.x, smoothedY, currentPos.z);

                modifiedKnots.Add(i);
            }

            for (int i = 0; i < knotCount; i++)
            {
                BezierKnot knot = spline[i];
                knot.Position = (float3)newPositions[i];
                spline.SetKnot(i, knot);
            }
        }

        foreach (int i in modifiedKnots)
        {
            spline.SetTangentMode(i, TangentMode.AutoSmooth);
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(splineContainer.gameObject);
#endif

        Debug.Log($"[SplineBaker] Zonal smoothing: {modifiedKnots.Count} knots modified.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  5. MACRO SMOOTH (HIGHWAY MODE)
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("5. Macro Smooth (Highway Mode)")]
    public void MacroSmoothHighway()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0) return;

#if UNITY_EDITOR
        Undo.RecordObject(splineContainer.gameObject, "Macro Smooth Spline");
#endif

        Spline spline = splineContainer.Spline;
        float splineLength = spline.GetLength();

        if (splineLength <= macroAnchorDistance)
        {
            Debug.LogWarning("[SplineBaker] Track is too short for Macro Smoothing!");
            return;
        }

        List<float3> anchorPositions = new List<float3>();
        int anchorCount = Mathf.CeilToInt(splineLength / macroAnchorDistance);

        for (int i = 0; i < anchorCount; i++)
        {
            float t = (float)i / (float)anchorCount;
            float3 sampledPos = splineContainer.EvaluatePosition(t);
            anchorPositions.Add(sampledPos);
        }

        if (!spline.Closed)
        {
            anchorPositions.Add(splineContainer.EvaluatePosition(1f));
        }

        spline.Clear();

        for (int i = 0; i < anchorPositions.Count; i++)
        {
            BezierKnot anchorKnot = new BezierKnot(anchorPositions[i]);
            spline.Add(anchorKnot);
        }

        for (int i = 0; i < spline.Count; i++)
        {
            spline.SetTangentMode(i, TangentMode.AutoSmooth);
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(splineContainer.gameObject);
#endif

        Debug.Log($"[SplineBaker] Macro Smoothing: {spline.Count} anchor knots with step ~{macroAnchorDistance}m.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  6. BAKE TRANSFORM SCALE
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("6. Bake Transform Scale to Spline (FIX SCALE)")]
    public void BakeTransformScale()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0) return;

#if UNITY_EDITOR
        Undo.RecordObject(splineContainer.gameObject, "Bake Spline Scale");
        Undo.RecordObject(transform, "Bake Spline Transform");
#endif

        Vector3 scale = transform.localScale;
        if (scale == Vector3.one)
        {
            Debug.Log("[SplineBaker] Scale is already (1, 1, 1). Nothing to bake.");
            return;
        }

        foreach (var spline in splineContainer.Splines)
        {
            for (int i = 0; i < spline.Count; i++)
            {
                BezierKnot knot = spline[i];
                knot.Position = new float3(knot.Position.x * scale.x, knot.Position.y * scale.y, knot.Position.z * scale.z);
                knot.TangentIn = new float3(knot.TangentIn.x * scale.x, knot.TangentIn.y * scale.y, knot.TangentIn.z * scale.z);
                knot.TangentOut = new float3(knot.TangentOut.x * scale.x, knot.TangentOut.y * scale.y, knot.TangentOut.z * scale.z);
                spline.SetKnot(i, knot);
            }

            for (int i = 0; i < spline.Count; i++)
            {
                if (spline.GetTangentMode(i) == TangentMode.AutoSmooth)
                {
                    spline.SetTangentMode(i, TangentMode.AutoSmooth);
                }
            }
        }

        transform.localScale = Vector3.one;

        RoadGenerator roadGen = GetComponent<RoadGenerator>();
        if (roadGen != null)
        {
            roadGen.GenerateRoad();
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(splineContainer.gameObject);
#endif

        Debug.Log("[SplineBaker] Scale baked into spline knots, Transform Scale reset to (1, 1, 1).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  7. SIMPLIFY SPLINE
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("7. Simplify Spline (Optimize)")]
    public void SimplifySpline()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0) return;

#if UNITY_EDITOR
        Undo.RecordObject(splineContainer.gameObject, "Simplify Spline");
#endif

        Spline spline = splineContainer.Spline;
        int originalCount = spline.Count;
        if (originalCount < 3) return;

        int removedCount = 0;
        float minDistanceThreshold = 100f;

        for (int i = originalCount - 2; i >= 1; i--)
        {
            Vector3 prevPos = (Vector3)spline[i - 1].Position;
            Vector3 currentPos = (Vector3)spline[i].Position;
            Vector3 nextPos = (Vector3)spline[i + 1].Position;

            Vector3 dir1 = (currentPos - prevPos).normalized;
            Vector3 dir2 = (nextPos - currentPos).normalized;

            float angle = Vector3.Angle(dir1, dir2);
            float distToNext = Vector3.Distance(currentPos, nextPos);

            if (angle <= simplifyAngleThreshold && distToNext < minDistanceThreshold)
            {
                spline.RemoveAt(i);
                removedCount++;
            }
        }

        for (int i = 0; i < spline.Count; i++)
        {
            spline.SetTangentMode(i, TangentMode.AutoSmooth);
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(splineContainer.gameObject);
#endif

        Debug.Log($"[SplineBaker] Removed {removedCount} knots. Remaining: {spline.Count}.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  8. SUBDIVIDE SPLINE
    // ═══════════════════════════════════════════════════════════════════════

    [ContextMenu("8. Optional: Subdivide Spline (Add More Knots)")]
    public void SubdivideSpline()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0) return;

#if UNITY_EDITOR
        Undo.RecordObject(splineContainer.gameObject, "Subdivide Spline");
#endif

        Spline spline = splineContainer.Spline;
        int originalCount = spline.Count;

        if (originalCount > 1000)
        {
            Debug.LogWarning($"[SplineBaker] WARNING: {originalCount} knots. " +
                             $"Subdividing will create {originalCount * 2} knots!");
        }

        int limit = spline.Closed ? originalCount : originalCount - 1;

        for (int i = limit - 1; i >= 0; i--)
        {
            BezierCurve curve = spline.GetCurve(i);
            float3 midPosition = CurveUtility.EvaluatePosition(curve, 0.5f);
            BezierKnot newKnot = new BezierKnot(midPosition);
            spline.Insert(i + 1, newKnot);
        }

        for (int i = 0; i < spline.Count; i++)
        {
            spline.SetTangentMode(i, TangentMode.AutoSmooth);
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(splineContainer.gameObject);
#endif

        Debug.Log($"[SplineBaker] Subdivided: {spline.Count} knots.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GIZMOS
    // ═══════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        if (smoothingZones == null || smoothingZones.Count == 0) return;

        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        foreach (Transform zone in smoothingZones)
        {
            if (zone != null)
            {
                Gizmos.DrawWireSphere(zone.position, zoneRadius);
            }
        }
    }
}