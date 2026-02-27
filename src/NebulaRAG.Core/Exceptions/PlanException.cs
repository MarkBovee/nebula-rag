using System;

namespace NebulaRAG.Core.Exceptions;

/// <summary>
/// Thrown when a business rule violation occurs in the plan lifecycle management system.
/// </summary>
public sealed class PlanException : Exception
{
    /// <summary>
    /// The type of business rule violation.
    /// </summary>
    public string? ViolationType { get; }

    /// <summary>
    /// Session validation violation types.
    /// </summary>
    public static class SessionValidation
    {
        public const string SessionOwnership = "session_ownership_violation";
        public const string MultipleActivePlans = "multiple_active_plans_violation";
        public const string SessionRequired = "session_required";
    }

    /// <summary>
    /// Additional context about the violation.
    /// </summary>
    public object? Context { get; }

    /// <summary>
    /// Session validation violation types.
    /// </summary>
    public static class SessionValidation
    {
        public const string SessionOwnership = "session_ownership_violation";
        public const string MultipleActivePlans = "multiple_active_plans_violation";
        public const string SessionRequired = "session_required";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanException"/> class.
    /// </summary>
    public PlanException()
        : base("A business rule violation occurred in the plan lifecycle management system.")
    {
    }

    /// <summary>
    /// Session validation violation types.
    /// </summary>
    public static class SessionValidation
    {
        public const string SessionOwnership = "session_ownership_violation";
        public const string MultipleActivePlans = "multiple_active_plans_violation";
        public const string SessionRequired = "session_required";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public PlanException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Session validation violation types.
    /// </summary>
    public static class SessionValidation
    {
        public const string SessionOwnership = "session_ownership_violation";
        public const string MultipleActivePlans = "multiple_active_plans_violation";
        public const string SessionRequired = "session_required";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PlanException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Session validation violation types.
    /// </summary>
    public static class SessionValidation
    {
        public const string SessionOwnership = "session_ownership_violation";
        public const string MultipleActivePlans = "multiple_active_plans_violation";
        public const string SessionRequired = "session_required";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanException"/> class with a violation type and message.
    /// </summary>
    /// <param name="violationType">The type of business rule violation.</param>
    /// <param name="message">The message that describes the error.</param>
    public PlanException(string violationType, string message)
        : base(message)
    {
        ViolationType = violationType;
    }

    /// <summary>
    /// Session validation violation types.
    /// </summary>
    public static class SessionValidation
    {
        public const string SessionOwnership = "session_ownership_violation";
        public const string MultipleActivePlans = "multiple_active_plans_violation";
        public const string SessionRequired = "session_required";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanException"/> class with a violation type, message, and context.
    /// </summary>
    /// <param name="violationType">The type of business rule violation.</param>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="context">Additional context about the violation.</param>
    public PlanException(string violationType, string message, object? context)
        : base(message)
    {
        ViolationType = violationType;
        Context = context;
    }

    /// <summary>
    /// Session validation violation types.
    /// </summary>
    public static class SessionValidation
    {
        public const string SessionOwnership = "session_ownership_violation";
        public const string MultipleActivePlans = "multiple_active_plans_violation";
        public const string SessionRequired = "session_required";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanException"/> class with a violation type, message, context, and inner exception.
    /// </summary>
    /// <param name="violationType">The type of business rule violation.</param>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="context">Additional context about the violation.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PlanException(string violationType, string message, object? context, Exception innerException)
        : base(message, innerException)
    {
        ViolationType = violationType;
        Context = context;
    }

    /// <summary>
    /// Session validation violation types.
    /// </summary>
    public static class SessionValidation
    {
        public const string SessionOwnership = "session_ownership_violation";
        public const string MultipleActivePlans = "multiple_active_plans_violation";
        public const string SessionRequired = "session_required";
    }
}

    /// <summary>
    /// Session validation violation types.
    /// </summary>
    public static class SessionValidation
    {
        public const string SessionOwnership = "session_ownership_violation";
        public const string MultipleActivePlans = "multiple_active_plans_violation";
        public const string SessionRequired = "session_required";
    }