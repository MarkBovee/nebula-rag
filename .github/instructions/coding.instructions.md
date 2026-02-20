---
applyTo: '**'
---

# Coding Standards

- DRY and SOLID
- Prefer small functions and clear naming
- For C#: avoid fully-qualified type names and avoid `dynamic`
- Build with zero errors/warnings and keep tests passing
---
applyTo: '**'
---

# Coding Standards

## Core Principles

- **DRY**: Before adding code, check if similar functionality exists. Refactor 3+ duplications into shared components.
- **SOLID**: Single responsibility, open/closed, Liskov substitution, interface segregation, dependency inversion.
- **Small functions**: Under 20 lines, single level of abstraction, 0-2 parameters preferred.
- **Pure functions**: Prefer functions without side effects when possible.
- **Meaningful names**: Use descriptive, intention-revealing names for all identifiers.

## C# Specific Rules

### No Fully Qualified Type Names
Always add `using` directives and use short type names. Only exception: disambiguation of same-named types.
```csharp
// Bad: System.Collections.Generic.List<string>
// Good: List<string> (with using System.Collections.Generic;)
```

### No Long Parameter Lists
- 3+ parameters → use a model/DTO/request object.
```csharp
// Bad: CreateDealer(string name, string email, string phone, string address)
// Good: CreateDealer(CreateDealerRequest request)
```

### Parameter formatting
- Keep the full parameter list for methods and functions on a single line when it fits within the project's line-length policy. Do not place each parameter on its own line.
- If the parameter list is long, prefer creating a request/DTO object instead of breaking parameters across multiple lines.

### Method invocation formatting
- Keep method invocations on a single line when they fit within the project's line-length policy.
- Prefer this especially for fluent/assertion/test calls to keep intent readable at a glance.
- If an invocation is too long, break at logical argument boundaries.

### Variable naming
- Use informative, intention-revealing variable names (e.g. `companyGroupCode`, `updatedMarketplaceSettings`) instead of generic names (e.g. `code`, `updated`, `result`).
- Avoid ambiguous short names unless they are conventional loop variables in a very small local scope.

### No `dynamic`
Use strongly-typed classes, `object` with safe casting, or `JsonElement`/`JObject` for JSON processing. `dynamic` is forbidden.

### Constructor Optimization
When adding properties that touch many files, prefer optional parameters with defaults, factory methods, or builder patterns over requiring changes everywhere.

### XML Documentation Comments
- Add XML documentation comments (`///`) to all methods and classes.
- This applies to `public`, `internal`, and `private` members.
- Keep comments concise and useful: summarize intent, key parameters, and return behavior where relevant.

## EF Core Best Practices

- **Centralize timestamps** in `DbContext.SaveChanges()` override — set `SysAdd` on insert, `SysMod` on insert+update. Never manage timestamps in business logic.
- **Fluent API**: Use `HasDefaultValueSql("GETUTCDATE()")` for database-level defaults.
- **Bulk operations**: Manually set timestamps before `BulkInsertAsync()`/`BulkUpdateAsync()` since they bypass change tracking.

## Error Handling & Performance

- **Fail fast**: Validate inputs early with clear error messages.
- **Resource management**: Use `using` statements for disposable resources.
- **Lazy loading**: Don't compute values until needed.
- **Caching**: Cache expensive computations and frequently accessed data.

## Quality Checklist

After implementing changes, verify:
- [ ] No code duplication introduced
- [ ] Performance impact acceptable
- [ ] Error handling comprehensive
- [ ] Build: 0 errors, 0 warnings
- [ ] All tests passing
- [ ] Code is self-documenting or has "why" comments where needed
