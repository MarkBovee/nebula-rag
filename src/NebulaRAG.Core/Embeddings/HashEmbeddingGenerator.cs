namespace NebulaRAG.Core.Embeddings;

/// <summary>
/// Generates deterministic vector embeddings using stable hash functions.
/// Suitable for development and testing; not recommended for production use.
/// </summary>
public sealed class HashEmbeddingGenerator : IEmbeddingGenerator
{
    /// <summary>
    /// Generates a stable hash-based embedding for the provided text.
    /// Uses FNV-1a hashing and normalization to produce consistent vectors.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="dimensions">The target vector dimension count.</param>
    /// <returns>A normalized float array representing the text embedding.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if dimensions is not greater than 0.</exception>
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

    /// <summary>
    /// Tokenizes text into lowercase alphanumeric tokens.
    /// </summary>
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

    /// <summary>
    /// Computes a deterministic FNV-1a hash for the token.
    /// </summary>
    private static uint GetStableHash(string token)
    {
        unchecked
        {
            // FNV-1a algorithm for stable, reproducible hashing
            var hash = 2166136261u;
            foreach (var c in token)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return hash;
        }
    }

    /// <summary>
    /// Normalizes the vector to unit length (L2 normalization).
    /// </summary>
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

        // Compute L2 norm and divide all elements to normalize
        var norm = (float)Math.Sqrt(sumSquares);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }
    }
}
