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
- Add XML documentation comments (`///`) to **all** methods, classes, records, and helper functions.
- This includes `public`, `internal`, and `private` members, including static helper functions.
- Keep comments concise and useful: summarize intent, key parameters, and return behavior where relevant.
- For each parameter, include a brief description of its purpose and any constraints.
- For return values, describe what is returned and what callers should rely on.

### Helpful Inline Comments
- Add a short inline intent comment for non-obvious logic blocks and handlers where behavior is not immediately clear from naming alone.
- Apply this especially to minimal API endpoint handlers and protocol dispatch sections
- Keep comments brief and focused on why/intent, not line-by-line narration.

### Helper Functions & Subfunctions
- All helper, utility, and static functions require comprehensive XML documentation with `<summary>`, `<param>`, and `<returns>` tags.
- Document the function's role in the larger workflow and any preconditions or side effects.
- For multi-parameter helpers, clarify relationships between parameters and their intended usage patterns.
- Include inline comments that explain non-obvious algorithms, protocols, or business logic.

## EF Core Best Practices

- **Centralize timestamps** in `DbContext.SaveChanges()` override — set `SysAdd` on insert, `SysMod` on insert+update. Never manage timestamps in business logic.
- **Fluent API**: Use `HasDefaultValueSql("GETUTCDATE()")` for database-level defaults.
- **Bulk operations**: Manually set timestamps before `BulkInsertAsync()`/`BulkUpdateAsync()` since they bypass change tracking.

## Error Handling & Performance

- **Fail fast**: Validate inputs early with clear error messages.
- **Resource management**: Use `using` statements for disposable resources.
- **Lazy loading**: Don't compute values until needed.
- **Caching**: Cache expensive computations and frequently accessed data.

## Release Versioning Rule

- For every implemented repository change, bump the Home Assistant add-on version in `nebula-rag/config.json`.
- Default bump level is patch unless the requested scope clearly requires minor/major.
- Add a matching entry in `nebula-rag/CHANGELOG.md` in the same change.

## Quality Checklist

After implementing changes, verify:
- [ ] No code duplication introduced
- [ ] Performance impact acceptable
- [ ] Error handling comprehensive
- [ ] Add-on version bumped in `nebula-rag/config.json`
- [ ] `nebula-rag/CHANGELOG.md` updated for the change
- [ ] Build: 0 errors, 0 warnings
- [ ] All tests passing
- [ ] Code is self-documenting or has "why" comments where needed
