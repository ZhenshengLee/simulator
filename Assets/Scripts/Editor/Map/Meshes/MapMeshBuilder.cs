/**
 * Copyright (c) 2020 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

namespace Simulator.Editor.MapMeshes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Simulator.Map;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.Rendering;

    public class MapMeshBuilder
    {
        private class LaneBoundEnumerator : IEnumerable<LineVert>, IEnumerator<LineVert>
        {
            private Dictionary<MapLine, LineData> linesData;
            private int cIndex;
            private MapLane cLane;
            private LineVert current;

            public bool CurrentIsLeft => cIndex == 0 || cIndex == 1;

            public LaneBoundEnumerator(Dictionary<MapLine, LineData> linesData)
            {
                this.linesData = linesData;
            }

            public IEnumerable<LineVert> Enumerate(MapLane lane)
            {
                cIndex = -1;
                cLane = lane;
                return this;
            }

            public IEnumerator<LineVert> GetEnumerator()
            {
                return this;
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public bool MoveNext()
            {
                if (cIndex == 3)
                    return false;

                cIndex++;
                return true;
            }

            public void Reset()
            {
                cIndex = -1;
            }

            public LineVert Current
            {
                get
                {
                    switch (cIndex)
                    {
                        case 0:
                            return linesData[cLane.leftLineBoundry].worldPoints[0];
                        case 1:
                        {
                            var points = linesData[cLane.leftLineBoundry].worldPoints;
                            return points[points.Count - 1];
                        }
                        case 2:
                            return linesData[cLane.rightLineBoundry].worldPoints[0];
                        case 3:
                        {
                            var points = linesData[cLane.rightLineBoundry].worldPoints;
                            return points[points.Count - 1];
                        }
                        default:
                            return null;
                    }
                }
            }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }
        }

        private class LineVert
        {
            public Vector3 position;
            public Vector3 outVector;

            private List<LineVert> linked = new List<LineVert>();

            public IReadOnlyList<LineVert> Linked => linked;

            public LineVert(Vector3 position)
            {
                this.position = position;
            }

            public void AddLink(LineVert linkedVec)
            {
                if (linkedVec == this)
                    return;

                if (!linked.Contains(linkedVec))
                    linked.Add(linkedVec);
            }

            public void AddLinks(List<LineVert> linkedVecs)
            {
                foreach (var linkedVec in linkedVecs)
                    AddLink(linkedVec);
            }
        }

        private class LineData
        {
            public enum LineShape
            {
                None,
                Solid,
                Dotted,
                Double
            }

            public int usageCount;
            public Color color;
            public LineShape shape;
            public List<LineVert> worldPoints = new List<LineVert>();

            public LineData(MapLine line)
            {
                foreach (var pos in line.mapLocalPositions)
                    worldPoints.Add(new LineVert(line.transform.TransformPoint(pos)));

                switch (line.lineType)
                {
                    case MapData.LineType.UNKNOWN:
                    case MapData.LineType.VIRTUAL:
                        shape = LineShape.None;
                        break;
                    case MapData.LineType.CURB:
                    case MapData.LineType.STOP:
                        color = Color.white;
                        shape = LineShape.Solid;
                        break;
                    case MapData.LineType.SOLID_WHITE:
                        color = Color.white;
                        shape = LineShape.Solid;
                        break;
                    case MapData.LineType.SOLID_YELLOW:
                        color = Color.yellow;
                        shape = LineShape.Solid;
                        break;
                    case MapData.LineType.DOTTED_WHITE:
                        color = Color.white;
                        shape = LineShape.Dotted;
                        break;
                    case MapData.LineType.DOTTED_YELLOW:
                        color = Color.yellow;
                        shape = LineShape.Dotted;
                        break;
                    case MapData.LineType.DOUBLE_WHITE:
                        color = Color.white;
                        shape = LineShape.Double;
                        break;
                    case MapData.LineType.DOUBLE_YELLOW:
                        color = Color.yellow;
                        shape = LineShape.Double;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private readonly MapMeshSettings settings;
        private readonly Dictionary<MapLine, LineData> linesData = new Dictionary<MapLine, LineData>();

        public MapMeshBuilder(MapMeshSettings settings)
        {
            this.settings = settings;
        }

        public void BuildMesh(GameObject parentObject, MapMeshMaterials materials)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Building mesh", "Preprocessing HD map data...", 0f);

                var mapManagerData = new MapManagerData();
                var lanes = mapManagerData.GetTrafficLanes();
                var allLanes = new List<MapLane>();
                allLanes.AddRange(lanes);
                var intersections = mapManagerData.GetIntersections();

                foreach (var intersection in intersections)
                {
                    var intLanes = intersection.GetComponentsInChildren<MapLane>();
                    foreach (var intLane in intLanes)
                    {
                        allLanes.Add(intLane);
                        if (!linesData.ContainsKey(intLane.leftLineBoundry))
                            linesData[intLane.leftLineBoundry] = new LineData(intLane.leftLineBoundry);
                        if (!linesData.ContainsKey(intLane.rightLineBoundry))
                            linesData[intLane.rightLineBoundry] = new LineData(intLane.rightLineBoundry);
                    }

                    var intLines = intersection.GetComponentsInChildren<MapLine>();
                    foreach (var intLine in intLines)
                    {
                        if (!linesData.ContainsKey(intLine))
                            linesData[intLine] = new LineData(intLine) {usageCount = 1};
                    }
                }

                foreach (var lane in allLanes)
                {
                    // Count boundary usage
                    if (linesData.ContainsKey(lane.leftLineBoundry))
                        linesData[lane.leftLineBoundry].usageCount++;
                    else
                        linesData[lane.leftLineBoundry] = new LineData(lane.leftLineBoundry) {usageCount = 1};

                    if (linesData.ContainsKey(lane.rightLineBoundry))
                        linesData[lane.rightLineBoundry].usageCount++;
                    else
                        linesData[lane.rightLineBoundry] = new LineData(lane.rightLineBoundry) {usageCount = 1};
                }

                if (settings.snapLaneEnds)
                    SnapLanes(allLanes);

                if (settings.pushOuterVerts)
                    CalculateOutVectors(allLanes);

                for (var i = 0; i < lanes.Count; ++i)
                {
                    EditorUtility.DisplayProgressBar("Building mesh", $"Creating lanes ({i}/{lanes.Count})", (float) i / lanes.Count);
                    CreateLaneMesh(lanes[i], parentObject, materials);
                }

                for (var i = 0; i < intersections.Count; ++i)
                {
                    EditorUtility.DisplayProgressBar("Building mesh", $"Creating intersections ({i}/{intersections.Count})", (float) i / intersections.Count);
                    CreateIntersectionMesh(intersections[i], parentObject, materials);
                }

                if (settings.createRenderers)
                {
                    var doneLineCount = 0;
                    foreach (var lineData in linesData)
                    {
                        EditorUtility.DisplayProgressBar("Building mesh", $"Creating lane lines ({doneLineCount}/{linesData.Count})", (float) doneLineCount++ / linesData.Count);
                        CreateLineMesh(lineData, parentObject, materials);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorSceneManager.MarkAllScenesDirty();
            }
        }

        private void CreateLaneMesh(MapLane lane, GameObject parentObject, MapMeshMaterials materials)
        {
            var name = lane.gameObject.name;
            var poly = BuildLanePoly(lane, true);
            var optimized = MeshUtils.OptimizePoly(poly, name);
            var mesh = Triangulation.TiangulateMultiPolygon(optimized, name);
            AddUv(mesh);
            var laneTransform = lane.transform;
            MoveMeshVericesToLocalSpace(mesh, laneTransform);

            if (mesh == null)
                return;
            var go = new GameObject(name + "_mesh");
            go.transform.SetParent(parentObject.transform);
            go.transform.rotation = laneTransform.rotation;
            go.transform.position = laneTransform.position;

            if (settings.createRenderers)
            {
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = mesh;
                mr.sharedMaterial = materials.road;
            }

            if (settings.createCollider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }
        }

        private void CreateIntersectionMesh(MapIntersection intersection, GameObject parentObject, MapMeshMaterials materials)
        {
            var name = intersection.gameObject.name;
            var intersectionLanes = intersection.GetComponentsInChildren<MapLane>();
            var merged = MeshUtils.ClipVatti(intersectionLanes.Select(x => BuildLanePoly(x, true)).ToList());
            var optimized = MeshUtils.OptimizePoly(merged, name);
            var mesh = Triangulation.TiangulateMultiPolygon(optimized, name);
            AddUv(mesh);
            var intersectionTransform = intersection.transform;
            MoveMeshVericesToLocalSpace(mesh, intersectionTransform);

            var go = new GameObject(name + "_mesh");
            go.transform.SetParent(parentObject.transform);
            go.transform.rotation = intersectionTransform.rotation;
            go.transform.position = intersectionTransform.position;

            if (settings.createRenderers)
            {
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = mesh;
                mr.sharedMaterial = materials.road;
            }

            if (settings.createCollider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }
        }

        private void CreateLineMesh(KeyValuePair<MapLine, LineData> lineData, GameObject parentObject, MapMeshMaterials materials)
        {
            if (lineData.Value.shape == LineData.LineShape.None)
                return;

            var mesh = BuildLineMesh(lineData.Key, lineData.Value.shape == LineData.LineShape.Double ? settings.lineWidth * 3 : settings.lineWidth);

            var go = new GameObject(lineData.Key.gameObject.name + "_mesh");
            go.transform.SetParent(parentObject.transform);
            var lineTransform = lineData.Key.transform;
            go.transform.rotation = lineTransform.rotation;
            go.transform.position = lineTransform.position;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            switch (lineData.Value.shape)
            {
                case LineData.LineShape.Solid:
                    mr.sharedMaterial = materials.GetSolidLineMaterial(lineData.Value.color);
                    break;
                case LineData.LineShape.Dotted:
                    mr.sharedMaterial = materials.GetDottedLineMaterial(lineData.Value.color);
                    break;
                case LineData.LineShape.Double:
                    mr.sharedMaterial = materials.GetDoubleLineMaterial(lineData.Value.color);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private List<Vertex> BuildLanePoly(MapLane lane, bool worldSpace = false)
        {
            var leftPoints = ListPool<LineVert>.Get();
            var rightPoints = ListPool<LineVert>.Get();

            var leftBound = lane.leftLineBoundry;
            var rightBound = lane.rightLineBoundry;

            leftPoints.AddRange(linesData[leftBound].worldPoints);
            rightPoints.AddRange(linesData[rightBound].worldPoints);

            GetLinesOrientation(lane, out var leftReversed, out var rightReversed);

            if (rightReversed)
                rightPoints.Reverse();

            if (!leftReversed)
                leftPoints.Reverse();

            var polygon = rightPoints.Select(point => new Vertex(point.position + point.outVector * settings.pushDistance)).ToList();
            polygon.AddRange(leftPoints.Select(point => new Vertex(point.position + point.outVector * settings.pushDistance)));

            MeshUtils.RemoveDuplicates(polygon);

            if (!worldSpace)
            {
                for (var i = 0; i < polygon.Count; ++i)
                    polygon[i].Position = lane.transform.InverseTransformPoint(polygon[i].Position);
            }

            ListPool<LineVert>.Release(leftPoints);
            ListPool<LineVert>.Release(rightPoints);

            return polygon;
        }

        private void GetLinesOrientation(MapLane lane, out bool leftReversed, out bool rightReversed)
        {
            var leftPoints = linesData[lane.leftLineBoundry].worldPoints;
            var rightPoints = linesData[lane.rightLineBoundry].worldPoints;

            var laneStart = lane.transform.TransformPoint(lane.mapLocalPositions[0]);
            var leftStart = leftPoints[0].position;
            var leftEnd = leftPoints[leftPoints.Count - 1].position;
            var rightStart = rightPoints[0].position;
            var rightEnd = rightPoints[rightPoints.Count - 1].position;

            leftReversed = Vector3.Distance(laneStart, leftEnd) < Vector3.Distance(laneStart, leftStart);
            rightReversed = Vector3.Distance(laneStart, rightEnd) < Vector3.Distance(laneStart, rightStart);
        }

        private void CalculateOutVectors(List<MapLane> lanes)
        {
            foreach (var lane in lanes)
            {
                var lb = lane.leftLineBoundry;
                var rb = lane.rightLineBoundry;

                if (linesData[lb].usageCount != 1 && linesData[rb].usageCount != 1)
                    continue;

                GetLinesOrientation(lane, out var leftReversed, out var rightReversed);

                if (linesData[lb].usageCount == 1)
                    CalculateOutVectorsForLine(linesData[lb].worldPoints, !leftReversed);

                if (linesData[rb].usageCount == 1)
                    CalculateOutVectorsForLine(linesData[rb].worldPoints, rightReversed);
            }

            var e = new LaneBoundEnumerator(linesData);

            foreach (var lane in lanes)
            {
                foreach (var vert in e.Enumerate(lane))
                {
                    var vertHasOutVec = vert.outVector.sqrMagnitude > 0.5f;
                    var sum = vertHasOutVec ? vert.outVector : Vector3.zero;
                    var count = vertHasOutVec ? 1 : 0;
                    foreach (var linkedVert in vert.Linked)
                    {
                        if (linkedVert.outVector.sqrMagnitude < 0.5f)
                            continue;

                        sum += linkedVert.outVector;
                        count++;
                    }

                    if (count == 0)
                        continue;

                    var outVec = (sum / count).normalized;

                    vert.outVector = outVec;
                    foreach (var linkedVert in vert.Linked)
                        linkedVert.outVector = outVec;
                }
            }
        }

        private void CalculateOutVectorsForLine(List<LineVert> line, bool reversed)
        {
            int GetIndex(int rawIndex)
            {
                return reversed ? line.Count - rawIndex - 1 : rawIndex;
            }

            for (var i = 0; i < line.Count; ++i)
            {
                var c = GetIndex(i);
                var p = GetIndex(i - 1);
                var n = GetIndex(i + 1);

                var fwd = Vector3.zero;
                if (i > 0)
                    fwd += line[c].position - line[p].position;
                if (i < line.Count - 1)
                    fwd += line[n].position - line[c].position;

                line[c].outVector = new Vector3(fwd.z, 0f, -fwd.x).normalized;
            }
        }

        private Mesh BuildLineMesh(MapLine line, float width)
        {
            width *= 0.5f;
            var verts = new List<Vector3>();
            var indices = new List<int>();
            var uvs = new List<Vector2>();

            var points = ListPool<Vector3>.Get();
            points.AddRange(linesData[line].worldPoints.Select(x => x.position));

            var fVec = (points[1] - points[0]).normalized * width;
            verts.Add(points[0] + new Vector3(-fVec.z, fVec.y, fVec.x));
            verts.Add(points[0] + new Vector3(fVec.z, fVec.y, -fVec.x));
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));

            var lastUv = 0f;

            for (var i = 1; i < points.Count; ++i)
            {
                if (i == points.Count - 1)
                {
                    var vec = (points[i] - points[i - 1]).normalized * width;
                    verts.Add(points[i] + new Vector3(-vec.z, vec.y, vec.x));
                    verts.Add(points[i] + new Vector3(vec.z, vec.y, -vec.x));
                }
                else
                {
                    var a = points[i - 1];
                    var b = points[i];
                    var c = points[i + 1];
                    var ab = (b - a).normalized * width;
                    var bc = (c - b).normalized * width;
                    var abL = new Vector3(-ab.z, ab.y, ab.x);
                    var bcL = new Vector3(-bc.z, bc.y, bc.x);
                    var abR = new Vector3(ab.z, ab.y, -ab.x);
                    var bcR = new Vector3(bc.z, bc.y, -bc.x);

                    var pointLeft = FindIntersection(a + abL, b + abL, b + bcL, c + bcL);
                    var pointRight = FindIntersection(a + abR, b + abR, b + bcR, c + bcR);

                    verts.Add(pointLeft);
                    verts.Add(pointRight);
                }

                var uvIncr = Vector3.Distance(points[i], points[i - 1]) / settings.lineUvUnit;
                uvs.Add(new Vector2(0, lastUv + uvIncr));
                uvs.Add(new Vector2(1, lastUv + uvIncr));
                lastUv += uvIncr;

                indices.Add(2 * i - 2);
                indices.Add(2 * i);
                indices.Add(2 * i + 1);
                indices.Add(2 * i - 2);
                indices.Add(2 * i + 1);
                indices.Add(2 * i - 1);
            }

            for (var i = 0; i < verts.Count; ++i)
            {
                var vert = line.transform.InverseTransformPoint(verts[i]);
                vert.y += settings.lineBump;
                verts[i] = vert;
            }

            ListPool<Vector3>.Release(points);

            var mesh = new Mesh {name = $"{line.gameObject.name}_mesh"};
            mesh.SetVertices(verts);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.Optimize();
            return mesh;
        }

        private void SnapLanes(List<MapLane> lanes)
        {
            var validPoints = new List<LineVert>();
            var linkedPoints = new List<LineVert>();
            var connectedLanes = new List<MapLane>();
            var pointsToProcess = new List<LineVert>();
            var ptpIsLeft = new List<bool>();
            var e = new LaneBoundEnumerator(linesData);

            foreach (var lane in lanes)
            {
                pointsToProcess.Clear();
                ptpIsLeft.Clear();

                foreach (var vert in e.Enumerate(lane))
                {
                    pointsToProcess.Add(vert);
                    ptpIsLeft.Add(e.CurrentIsLeft);
                }

                connectedLanes.Clear();
                connectedLanes.Add(lane);
                foreach (var prevLane in lane.prevConnectedLanes)
                {
                    connectedLanes.Add(prevLane);
                    foreach (var nestedLane in prevLane.nextConnectedLanes)
                    {
                        if (!connectedLanes.Contains(nestedLane))
                            connectedLanes.Add(nestedLane);
                    }
                }

                foreach (var nextLane in lane.nextConnectedLanes)
                {
                    connectedLanes.Add(nextLane);
                    foreach (var nestedLane in nextLane.prevConnectedLanes)
                    {
                        if (!connectedLanes.Contains(nestedLane))
                            connectedLanes.Add(nestedLane);
                    }
                }

                for (var i = 0; i < pointsToProcess.Count; ++i)
                {
                    var point = pointsToProcess[i];

                    linkedPoints.Clear();
                    validPoints.Clear();
                    validPoints.Add(point);

                    foreach (var cLane in connectedLanes)
                    {
                        var bestVert = (LineVert) null;
                        var bestDistance = float.MaxValue;

                        foreach (var cPoint in e.Enumerate(cLane))
                        {
                            if (e.CurrentIsLeft ^ ptpIsLeft[i])
                                continue;

                            var dist = Vector3.Distance(point.position, cPoint.position);
                            if (dist < settings.snapThreshold && dist < bestDistance)
                            {
                                bestVert = cPoint;
                                bestDistance = dist;
                            }
                        }

                        if (bestVert != null)
                            validPoints.Add(bestVert);
                    }

                    if (validPoints.Count > 1)
                    {
                        linkedPoints.AddRange(validPoints);

                        foreach (var validPoint in validPoints)
                        {
                            foreach (var linked in validPoint.Linked)
                            {
                                if (!linkedPoints.Contains(linked))
                                    linkedPoints.Add(linked);
                            }
                        }

                        var avg = linkedPoints.Aggregate(Vector3.zero, (current, validPoint) => current + validPoint.position);
                        avg /= linkedPoints.Count;

                        foreach (var linkedPoint in linkedPoints)
                        {
                            linkedPoint.position = avg;
                            linkedPoint.AddLinks(linkedPoints);
                        }
                    }
                }
            }
        }

        private void AddUv(Mesh worldSpaceMesh)
        {
            var verts = worldSpaceMesh.vertices;
            var uv = new Vector2[verts.Length];

            for (var i = 0; i < verts.Length; ++i)
                uv[i] = MapWorldUv(verts[i]);

            worldSpaceMesh.SetUVs(0, uv);
            worldSpaceMesh.RecalculateTangents();
        }

        private void MoveMeshVericesToLocalSpace(Mesh worldSpaceMesh, Transform transform)
        {
            var verts = worldSpaceMesh.vertices;

            for (var i = 0; i < verts.Length; ++i)
                verts[i] = transform.InverseTransformPoint(verts[i]);

            worldSpaceMesh.SetVertices(verts);
            worldSpaceMesh.RecalculateBounds();
            worldSpaceMesh.RecalculateNormals();
            worldSpaceMesh.RecalculateTangents();
            worldSpaceMesh.Optimize();
        }

        private Vector2 MapWorldUv(Vector3 worldPosition)
        {
            return new Vector2(worldPosition.x / settings.roadUvUnit, worldPosition.z / settings.roadUvUnit);
        }

        private Vector3 FindIntersection(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            return MeshUtils.AreLinesIntersecting(a0, a1, b0, b1)
                ? MeshUtils.GetLineLineIntersectionPoint(a0, a1, b0, b1)
                : a1;
        }
    }
}