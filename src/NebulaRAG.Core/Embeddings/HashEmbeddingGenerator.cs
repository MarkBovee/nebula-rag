namespace NebulaRAG.Core.Embeddings;

public sealed class HashEmbeddingGenerator : IEmbeddingGenerator
{
    public float[] GenerateEmbedding(string text, int dimensions)
    {
        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Vector dimensions must be greater than 0.");
        }

        var vector = new float[dimensions];
        if (string.IsNullOrWhiteSpace(text))
        {
            return vector;
        }

        foreach (var token in Tokenize(text))
        {
            var hash = GetStableHash(token);
            var index = (int)(hash % (uint)dimensions);
            var sign = ((hash >> 31) & 1) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        Normalize(vector);
        return vector;
    }

    private static IEnumerable<string> Tokenize(string input)
    {
        var buffer = new List<char>(32);
        foreach (var c in input.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer.Add(c);
                continue;
            }

            if (buffer.Count > 0)
            {
                yield return new string([.. buffer]);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            yield return new string([.. buffer]);
        }
    }

    private static uint GetStableHash(string token)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in token)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return hash;
        }
    }

    private static void Normalize(float[] vector)
    {
        var sumSquares = 0d;
        foreach (var value in vector)
        {
            sumSquares += value * value;
        }

        if (sumSquares == 0d)
        {
            return;
        }

        var norm = (float)Math.Sqrt(sumSquares);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }
    }
}
