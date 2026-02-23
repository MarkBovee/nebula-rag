namespace NebulaRAG.Core.Exceptions;

/// <summary>
/// Base exception for all NebulaRAG operations.
/// </summary>
public abstract class RagException : Exception
{
    /// <summary>
    /// Error code for categorizing the failure.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RagException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="code">The error code for categorization.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    protected RagException(string message, string code, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = code;
    }
}

/// <summary>
/// Thrown when a database operation fails.
/// </summary>
public sealed class RagDatabaseException : RagException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RagDatabaseException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public RagDatabaseException(string message, Exception? innerException = null)
        : base(message, "DB_ERROR", innerException)
    {
    }
}

/// <summary>
/// Thrown when configuration is invalid or incomplete.
/// </summary>
public sealed class RagConfigurationException : RagException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RagConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public RagConfigurationException(string message, Exception? innerException = null)
        : base(message, "CONFIG_ERROR", innerException)
    {
    }
}

/// <summary>
/// Thrown when an indexing operation fails.
/// </summary>
public sealed class RagIndexingException : RagException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RagIndexingException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public RagIndexingException(string message, Exception? innerException = null)
        : base(message, "INDEX_ERROR", innerException)
    {
    }
}

/// <summary>
/// Thrown when a query operation fails.
/// </summary>
public sealed class RagQueryException : RagException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RagQueryException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public RagQueryException(string message, Exception? innerException = null)
        : base(message, "QUERY_ERROR", innerException)
    {
    }
}
