# C# Code Style

The owner approved these C# conventions. [../.editorconfig](../.editorconfig) contains the supported editor and analyzer settings. Apply the rules below during code review as well; not every convention has a built-in diagnostic.

## Layout and Control Flow

- Use four spaces per indentation level. Put opening braces on a new line for types, methods, and blocks that need braces.
- Omit braces when an `if`, `else`, or loop contains a single statement. Keep that statement on a separate, indented line. A nested control statement counts as one statement.
- Use braces when the body contains multiple statements or syntax requires a block. When removing braces would change which `if` owns an `else`, retain the braces needed to preserve behavior.
- Keep readable method signatures on one line. Split only when a signature becomes difficult to read. No fixed line-length limit was selected.
- Keep simple auto-properties on one line and separate members with a blank line.
- Place using directives outside the namespace, put System directives first, and remove unused directives.
- Use file-scoped namespaces.

```csharp
namespace CityPortal.Identity;

public async Task<Account?> FindAsync(Guid accountId, CancellationToken cancellationToken)
{
    if (accountId == Guid.Empty)
        throw new ArgumentException("Account ID is required.", nameof(accountId));

    Account? account = await _accounts.FindByIdAsync(accountId, cancellationToken);

    if (account is null)
        return null;

    return account;
}
```

This is a method-layout excerpt, not a complete implementation or a business specification.

## Naming and Types

- Use PascalCase for types, methods, and properties; prefix interface names with I.
- Use camelCase for parameters and local variables, and _camelCase for private fields.
- Use C# type keywords such as `string`, `bool`, and `int` instead of framework aliases.
- Use explicit built-in types, including `string role` in a foreach loop.
- Use `var` when the initializer clearly exposes the non-built-in type, such as `var summary = new AccountSummary { ... };`. Use an explicit type for method results, such as `Account? account = await ...;`.

## Members and Dependencies

- Use ordinary constructors for classes and structs. Constructor-injected dependencies are readonly private fields. Primary constructors are outside the selected style.
- Use expression-bodied methods and properties for short, single-line expressions. Use block bodies for longer logic.
- Enable nullable reference types in each project during scaffolding. Check nullable results explicitly; use the null-forgiving operator only with a concrete, documented reason.
- Give task-returning asynchronous methods an Async suffix and propagate CancellationToken to operations that support cancellation, especially I/O.
- Write code comments in English. Explain the reason for a decision instead of narrating the statement below it.
- Keep production types in their corresponding files.

## Verification and Limits

The application solution is `CityPortal.slnx`. Run `dotnet format CityPortal.slnx --verify-no-changes --no-restore` after restoring dependencies.

`Directory.Build.props` enables nullable references, build-time style analysis, and warnings as errors. XML documentation generation enables unused-using analysis; CS1591 alone is suppressed because exhaustive public XML comments are not a selected convention. CI checks formatting against the actual solution. Verify selected diagnostics during build and formatting rather than assuming every editor option is enforced.

The built-in braces option expresses the preference for omitted braces; it does not provide a complete remove-braces enforcement guarantee. Likewise, disabling IDE0290 stops suggestions to use primary constructors but does not reject existing primary constructors. Signature wrapping, Async naming, cancellation propagation, comment quality, and the ordinary-constructor convention also require review. Custom analyzers are not introduced for these conventions at this stage.

References: [brace preferences](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0011), [primary constructor diagnostic](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0290), and [dotnet format](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format).
