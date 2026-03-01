# .NET refactor skill

## Trigger

Use this skill for:

- Codebase refactoring requests

## Steps

Act as a senior software architect specializing in .NET and Blazor.

Your task is to refactor the provided codebase professionally.

Refactor using these principles:

ARCHITECTURE
- Enforce separation of concerns.
- Extract business logic from Blazor components into services.
- Use dependency injection properly.
- Avoid fat components.
- Apply SRP strictly.

CODE QUALITY
- Remove code smells.
- Eliminate long methods (>30 lines where possible).
- Reduce nesting.
- Replace magic strings with constants.
- Improve naming clarity.
- Use async/await correctly.

BLAZOR SPECIFIC
- Move logic into @code-behind partial classes if needed.
- Keep Razor markup clean and readable.
- Extract reusable UI into child components.
- Use parameters and EventCallback properly.