using System;
using System.IO;
using UnityEngine;

namespace ValeMesozoico
{
    // A small heightfield keeps the imported environment mesh non-readable on Quest.
    internal sealed class ForestFloorSurface
    {
        private const string ResourcePath = "Models/Environment/ForestFloorSurface";
        private const uint Magic = 0x53464D56; // VMFS, little endian.
        private const int DataVersion = 1;
        private const int HeaderBytes = 52;
        private readonly float[] heights;
        private readonly float stepX;
        private readonly float stepZ;

        public int Columns { get; }
        public int Rows { get; }
        public Bounds SurfaceBounds { get; }

        private ForestFloorSurface(int columns, int rows, Bounds bounds, float[] samples)
        {
            Columns = columns;
            Rows = rows;
            SurfaceBounds = bounds;
            heights = samples;
            stepX = bounds.size.x / (columns - 1);
            stepZ = bounds.size.z / (rows - 1);
        }

        public static ForestFloorSurface Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[Forest Floor Surface] Bake ausente; execute Tools/Vale Mesozoico/Bake Forest Floor Surface.");
                return null;
            }
            try
            {
                byte[] bytes = asset.bytes;
                using BinaryReader reader = new(new MemoryStream(bytes, false));
                if (bytes.Length < HeaderBytes || reader.ReadUInt32() != Magic || reader.ReadInt32() != DataVersion)
                {
                    throw new InvalidDataException("Formato ou versão inválida.");
                }
                int mode = reader.ReadInt32();
                int columns = reader.ReadInt32();
                int rows = reader.ReadInt32();
                int sourceVertices = reader.ReadInt32();
                int sourceTriangles = reader.ReadInt32();
                float minX = reader.ReadSingle();
                float minZ = reader.ReadSingle();
                float maxX = reader.ReadSingle();
                float maxZ = reader.ReadSingle();
                float minY = reader.ReadSingle();
                float maxY = reader.ReadSingle();
                if ((mode != 1 && mode != 2) || columns < 2 || columns > 1025 || rows < 2 || rows > 1025
                    || sourceVertices < 4 || sourceTriangles < 2
                    || !Finite(minX) || !Finite(minZ) || !Finite(maxX) || !Finite(maxZ)
                    || !Finite(minY) || !Finite(maxY) || maxX <= minX || maxZ <= minZ || maxY < minY
                    || !Finite(maxX - minX) || !Finite(maxZ - minZ) || !Finite(maxY - minY)
                    || maxX - minX <= 0.01f || maxZ - minZ <= 0.01f
                    || bytes.Length != HeaderBytes + (long)columns * rows * sizeof(float))
                {
                    throw new InvalidDataException("Dimensões, bounds ou tamanho do bake inválidos.");
                }
                float[] samples = new float[columns * rows];
                for (int index = 0; index < samples.Length; index++)
                {
                    float height = reader.ReadSingle();
                    if (!Finite(height) || height < minY - 0.001f || height > maxY + 0.001f)
                    {
                        throw new InvalidDataException("Altura fora dos bounds da fonte.");
                    }
                    samples[index] = height;
                }
                Bounds bounds = new();
                bounds.SetMinMax(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
                return new ForestFloorSurface(columns, rows, bounds, samples);
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException)
            {
                Debug.LogWarning("[Forest Floor Surface] Bake rejeitado: " + exception.Message);
                return null;
            }
        }

        public bool Sample(float x, float z, out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.up;
            Vector3 min = SurfaceBounds.min;
            Vector3 max = SurfaceBounds.max;
            if (!Finite(x) || !Finite(z) || x < min.x || x > max.x || z < min.z || z > max.z)
            {
                return false;
            }
            float gridX = Mathf.Clamp((x - min.x) / stepX, 0f, Columns - 1);
            float gridZ = Mathf.Clamp((z - min.z) / stepZ, 0f, Rows - 1);
            int column = Mathf.Min(Mathf.FloorToInt(gridX), Columns - 2);
            int row = Mathf.Min(Mathf.FloorToInt(gridZ), Rows - 2);
            float tx = gridX - column;
            float tz = gridZ - row;
            float bottom = Mathf.Lerp(Height(column, row), Height(column + 1, row), tx);
            float top = Mathf.Lerp(Height(column, row + 1), Height(column + 1, row + 1), tx);
            point = new Vector3(x, Mathf.Lerp(bottom, top, tz), z);
            Vector3 bottomNormal = Vector3.Lerp(Normal(column, row), Normal(column + 1, row), tx);
            Vector3 topNormal = Vector3.Lerp(Normal(column, row + 1), Normal(column + 1, row + 1), tx);
            normal = Vector3.Lerp(bottomNormal, topNormal, tz).normalized;
            return true;
        }

        private float Height(int column, int row) => heights[row * Columns + column];

        private Vector3 Normal(int column, int row)
        {
            int left = Mathf.Max(0, column - 1);
            int right = Mathf.Min(Columns - 1, column + 1);
            int back = Mathf.Max(0, row - 1);
            int front = Mathf.Min(Rows - 1, row + 1);
            float dx = (Height(right, row) - Height(left, row)) / ((right - left) * stepX);
            float dz = (Height(column, front) - Height(column, back)) / ((front - back) * stepZ);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
