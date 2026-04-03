using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;

[RequireComponent(typeof(SplineContainer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
[ExecuteInEditMode]
public class RoadGenerator : MonoBehaviour
{
    [Header("Road Settings")]
    public bool autoUpdate = false;
    public float roadWidth = 8f;
    [Min(0.1f)]
    [Tooltip("Длина сегмента (м). Чем меньше — тем глаже дорога. 0.5 = очень гладко, 1 = нормально, 2+ = грубо.")]
    public float segmentLength = 0.5f;
    public float uvScale = 1f;

    [Header("Smoothness")]
    [Tooltip("Использовать fan-триангуляцию: добавляет центральную вершину в каждый квадрат. " +
             "4 треугольника вместо 2. Убирает складки на стыках.")]
    public bool useFanTriangulation = true;

    [Tooltip("Количество вершин поперёк дороги (3 = лево-центр-право, 5 = ещё два промежуточных). " +
             "Больше = глаже поверхность поперёк ширины.")]
    [Range(2, 7)]
    public int crossSectionVertices = 3;

    [Header("Track Scaling")]
    [Tooltip("Множитель масштаба сплайна.")]
    public Vector3 trackScale = Vector3.one;

    private SplineContainer splineContainer;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private Mesh generatedMesh;
    private bool hasScaledSpline = false;
    private bool isDirty = false;

    private void OnEnable()
    {
        InitializeComponents();

        if (Application.isPlaying)
        {
            ApplyRuntimeScale();
            GenerateRoadImmediate();
        }
        else if (autoUpdate)
        {
            GenerateRoadImmediate();
        }
    }

    private void InitializeComponents()
    {
        splineContainer = GetComponent<SplineContainer>();
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();

        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer.sharedMaterial == null || renderer.sharedMaterial.name == "Default-Material")
        {
            // Automatically assign a URP material that supports vertex colors
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Lit");
            if (shader == null) shader = Shader.Find("Standard"); // fallback
            
            if (shader != null)
            {
                Material mat = new Material(shader);
                mat.name = "GeneratedRoadMaterial";
                renderer.sharedMaterial = mat;
            }
        }

        Spline.Changed -= OnSplineChanged;
        Spline.Changed += OnSplineChanged;
    }

    private void OnDisable()
    {
        Spline.Changed -= OnSplineChanged;
    }

    private void ApplyRuntimeScale()
    {
        if (hasScaledSpline) return;
        if (splineContainer == null) return;

        Vector3 totalScale = new Vector3(
            trackScale.x * transform.localScale.x,
            trackScale.y * transform.localScale.y,
            trackScale.z * transform.localScale.z
        );

        if (totalScale == Vector3.one)
        {
            hasScaledSpline = true;
            return;
        }

        foreach (var spline in splineContainer.Splines)
        {
            for (int i = 0; i < spline.Count; i++)
            {
                BezierKnot knot = spline[i];
                knot.Position = new float3(
                    knot.Position.x * totalScale.x,
                    knot.Position.y * totalScale.y,
                    knot.Position.z * totalScale.z
                );
                knot.TangentIn = new float3(
                    knot.TangentIn.x * totalScale.x,
                    knot.TangentIn.y * totalScale.y,
                    knot.TangentIn.z * totalScale.z
                );
                knot.TangentOut = new float3(
                    knot.TangentOut.x * totalScale.x,
                    knot.TangentOut.y * totalScale.y,
                    knot.TangentOut.z * totalScale.z
                );
                spline.SetKnot(i, knot);
            }
        }

        transform.localScale = Vector3.one;
        trackScale = Vector3.one;
        hasScaledSpline = true;
    }

    private void OnSplineChanged(Spline spline, int knotIndex, SplineModification modificationType)
    {
        if (!autoUpdate) return;
        if (splineContainer != null && splineContainer.Splines != null)
        {
            foreach (var containerSpline in splineContainer.Splines)
            {
                if (containerSpline == spline)
                {
                    GenerateRoad();
                    break;
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (isDirty)
        {
            isDirty = false;
            GenerateRoadImmediate();
        }
    }

    [ContextMenu("Generate Road")]
    public void GenerateRoad()
    {
        if (Application.isPlaying)
            isDirty = true;
        else
            GenerateRoadImmediate();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ГЕНЕРАЦИЯ МЕША ДОРОГИ
    // ═══════════════════════════════════════════════════════════════════════

    private void GenerateRoadImmediate()
    {
        if (splineContainer == null || splineContainer.Spline == null) return;

        if (generatedMesh != null)
        {
            if (Application.isPlaying) Destroy(generatedMesh);
            else DestroyImmediate(generatedMesh);
        }

        generatedMesh = new Mesh();
        generatedMesh.name = "Generated_Road_Mesh";
        generatedMesh.hideFlags = HideFlags.HideAndDontSave;

        Spline spline = splineContainer.Spline;
        float splineLength = spline.GetLength();
        if (splineLength == 0) return;

        int lengthSegments = Mathf.CeilToInt(splineLength / segmentLength) + 1;
        if (lengthSegments < 2) lengthSegments = 2;

        int crossVerts = Mathf.Max(2, crossSectionVertices);

        generatedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // ─────────────────────────────────────────────────────────────────
        //  Шаг 1: Сэмплируем сплайн → центры + правые векторы
        // ─────────────────────────────────────────────────────────────────

        Vector3[] centers = new Vector3[lengthSegments];
        Vector3[] rights  = new Vector3[lengthSegments];

        float step = 1f / (lengthSegments - 1);
        Vector3 prevRight = Vector3.right;

        for (int i = 0; i < lengthSegments; i++)
        {
            float t = i * step;
            if (i == lengthSegments - 1 && spline.Closed) t = 1f;

            splineContainer.Evaluate(t, out float3 localPos, out float3 localTangent, out float3 localUp);

            centers[i] = transform.TransformPoint((Vector3)localPos);

            Vector3 worldForward = transform.TransformDirection((Vector3)localTangent).normalized;
            Vector3 flatForward  = Vector3.ProjectOnPlane(worldForward, Vector3.up);

            if (flatForward.sqrMagnitude > 0.0001f)
            {
                flatForward.Normalize();
                Vector3 newRight = Vector3.Cross(Vector3.up, flatForward).normalized;

                // Плавный переход: берём предыдущий right и мягко поворачиваем к новому.
                // Это убирает рывки направления на поворотах.
                if (i > 0)
                {
                    rights[i] = Vector3.Slerp(prevRight, newRight, 0.5f).normalized;
                }
                else
                {
                    rights[i] = newRight;
                }
                prevRight = rights[i];
            }
            else
            {
                rights[i] = prevRight;
            }
        }

        // Дополнительное сглаживание right vectors (3 прохода)
        for (int pass = 0; pass < 3; pass++)
        {
            Vector3[] smoothed = new Vector3[lengthSegments];
            smoothed[0] = rights[0];
            smoothed[lengthSegments - 1] = rights[lengthSegments - 1];

            for (int i = 1; i < lengthSegments - 1; i++)
            {
                Vector3 avg = rights[i - 1] + rights[i] + rights[i + 1];
                smoothed[i] = avg.sqrMagnitude > 0.001f ? avg.normalized : rights[i];
            }
            rights = smoothed;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Шаг 2: Строим сетку вершин (lengthSegments × crossVerts)
        // ─────────────────────────────────────────────────────────────────

        // Сетка вершин: каждый ряд (cross-section) имеет crossVerts вершин,
        // равномерно распределённых от -roadWidth/2 до +roadWidth/2.

        int gridVertexCount = lengthSegments * crossVerts;
        Vector3[] gridVertices = new Vector3[gridVertexCount];
        Vector2[] gridUVs      = new Vector2[gridVertexCount];
        Color32[] gridColors   = new Color32[gridVertexCount];

        Color32 colorAsphalt = new Color32(40, 40, 42, 255);
        Color32 colorLine    = new Color32(30, 140, 255, 255); // Blue

        float currentDistance = 0f;

        for (int i = 0; i < lengthSegments; i++)
        {
            if (i > 0)
                currentDistance += Vector3.Distance(centers[i], centers[i - 1]);

            float vCoord = currentDistance * uvScale;

            for (int j = 0; j < crossVerts; j++)
            {
                // u идёт от -0.5 до +0.5
                float u = (float)j / (crossVerts - 1) - 0.5f;

                Vector3 worldPos = centers[i] + rights[i] * (u * roadWidth);
                int idx = i * crossVerts + j;

                gridVertices[idx] = transform.InverseTransformPoint(worldPos);
                gridUVs[idx] = new Vector2((float)j / (crossVerts - 1), vCoord);
                gridColors[idx] = Mathf.Abs(u) <= 0.05f ? colorLine : colorAsphalt;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Шаг 3: Триангуляция
        // ─────────────────────────────────────────────────────────────────

        List<Vector3> finalVertices = new List<Vector3>();
        List<Vector2> finalUVs      = new List<Vector2>();
        List<Color32> finalColors   = new List<Color32>();
        List<int>     finalTris     = new List<int>();

        if (useFanTriangulation)
        {
            // ═══════════════════════════════════════════════════════════
            //  FAN TRIANGULATION
            //
            //  Для каждого квадрата (i,j) → (i+1,j) → (i+1,j+1) → (i,j+1)
            //  добавляем ЦЕНТРАЛЬНУЮ вершину = среднее четырёх углов.
            //  Затем строим 4 треугольника от центра к каждому ребру.
            //
            //  Почему это убирает бугры:
            //  - Обычный квадрат из 2 треугольников имеет ДИАГОНАЛЬ,
            //    на которой два треугольника встречаются под углом (складка).
            //  - Fan из 4 треугольников не имеет диагонали —
            //    все 4 плоскости сходятся в центре, переход плавный.
            // ═══════════════════════════════════════════════════════════

            // Сначала копируем все grid-вершины
            finalVertices.AddRange(gridVertices);
            finalUVs.AddRange(gridUVs);
            finalColors.AddRange(gridColors);

            for (int i = 0; i < lengthSegments - 1; i++)
            {
                for (int j = 0; j < crossVerts - 1; j++)
                {
                    // Индексы четырёх углов квадрата в grid
                    int bl = i * crossVerts + j;         // bottom-left  (текущий ряд, слева)
                    int br = i * crossVerts + j + 1;     // bottom-right (текущий ряд, справа)
                    int tl = (i + 1) * crossVerts + j;   // top-left     (след. ряд, слева)
                    int tr = (i + 1) * crossVerts + j + 1; // top-right  (след. ряд, справа)

                    // Центральная вершина = среднее четырёх углов
                    Vector3 centerPos = (gridVertices[bl] + gridVertices[br] +
                                         gridVertices[tl] + gridVertices[tr]) * 0.25f;
                    Vector2 centerUV  = (gridUVs[bl] + gridUVs[br] +
                                         gridUVs[tl] + gridUVs[tr]) * 0.25f;
                    
                    float centerU = centerUV.x - 0.5f;
                    Color32 centerColor = Mathf.Abs(centerU) <= 0.05f ? colorLine : colorAsphalt;

                    int centerIdx = finalVertices.Count;
                    finalVertices.Add(centerPos);
                    finalUVs.Add(centerUV);
                    finalColors.Add(centerColor);

                    // 4 треугольника от центра к рёбрам (видимые сверху)
                    finalTris.Add(bl); finalTris.Add(centerIdx); finalTris.Add(br);
                    finalTris.Add(br); finalTris.Add(centerIdx); finalTris.Add(tr);
                    finalTris.Add(tr); finalTris.Add(centerIdx); finalTris.Add(tl);
                    finalTris.Add(tl); finalTris.Add(centerIdx); finalTris.Add(bl);
                }
            }
        }
        else
        {
            // ═══════════════════════════════════════════════════════════
            //  ОБЫЧНАЯ ТРИАНГУЛЯЦИЯ (2 треугольника на квадрат)
            // ═══════════════════════════════════════════════════════════

            finalVertices.AddRange(gridVertices);
            finalUVs.AddRange(gridUVs);
            finalColors.AddRange(gridColors);

            for (int i = 0; i < lengthSegments - 1; i++)
            {
                for (int j = 0; j < crossVerts - 1; j++)
                {
                    int bl = i * crossVerts + j;
                    int br = i * crossVerts + j + 1;
                    int tl = (i + 1) * crossVerts + j;
                    int tr = (i + 1) * crossVerts + j + 1;

                    finalTris.Add(bl); finalTris.Add(br); finalTris.Add(tl);
                    finalTris.Add(br); finalTris.Add(tr); finalTris.Add(tl);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Шаг 4: Применяем меш
        // ─────────────────────────────────────────────────────────────────

        generatedMesh.SetVertices(finalVertices);
        generatedMesh.SetUVs(0, finalUVs);
        generatedMesh.SetColors(finalColors);
        generatedMesh.SetTriangles(finalTris, 0);

        generatedMesh.RecalculateBounds();
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateTangents();

        meshFilter.sharedMesh   = generatedMesh;
        meshCollider.sharedMesh = generatedMesh;

        int totalTris = finalTris.Count / 3;
        int totalVerts = finalVertices.Count;
        if (totalVerts > 50000)
        {
            Debug.LogWarning($"[RoadGenerator] Mesh: {totalVerts} vertices, {totalTris} triangles. " +
                             $"Too high! Increase segmentLength or decrease crossSectionVertices.");
        }
    }
}