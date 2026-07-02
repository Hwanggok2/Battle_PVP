# Build Troubleshooting Notes

This project has a few recurring build-log issues that can look similar but have different causes.

## 1. Encoding Breakage In C# Files

### Symptom

`dotnet build Battle_PVP.sln` fails with syntax errors around string literals or comments, for example:

- `CS1003: Syntax error, ':' expected`
- `CS1010: Newline in constant`
- Unexpected non-code characters inside string literals

### Cause

Some Unity C# files contain non-ASCII text. Rewriting a whole file with PowerShell commands such as `Set-Content` can change the file encoding or corrupt existing non-ASCII strings. This can turn a valid string literal into broken text, which then becomes a C# syntax error.

### Prevention

- Prefer `apply_patch` for manual edits.
- Avoid whole-file rewrites with PowerShell for C# files containing non-ASCII text.
- Do not use `Set-Content` for broad mechanical replacements unless encoding is explicitly preserved and the result is verified.
- If a broad replacement is unavoidable, verify the affected area immediately and run:

```powershell
dotnet build Battle_PVP.sln -m:1 -nodeReuse:false -v:q
```

### Fix

Inspect the reported line numbers and restore corrupted strings/comments manually. The issue is usually localized to the lines shown in the compiler errors.

## 2. Sandbox Git Warning

### Symptom

Git commands may print:

```text
warning: unable to access 'C:\Users\JW/.config/git/ignore': Permission denied
```

### Cause

The Codex sandbox can read and write the project workspace, but it may not have permission to read user-home Git config files outside the workspace.

### Impact

This is not a Unity or C# build failure. It usually appears during `git status` or `git diff` and can be ignored if the project command itself succeeds.

### Prevention

- Do not treat this warning as a code problem.
- If a command needs access outside the workspace, request explicit escalation instead of trying to work around the sandbox.

## 3. System.Net.Http Version Conflict Warning

### Symptom

Build succeeds but logs warnings like:

```text
MSB3277: Found conflicts between different versions of "System.Net.Http"
System.Net.Http, Version=4.1.2.0
System.Net.Http, Version=4.2.0.0
```

### Cause

Unity NetStandard shim references and plugin DLL references, such as `Edgegap.dll`, can depend on different `System.Net.Http` versions.

### Impact

This is currently a warning, not an error. If the build ends with `error 0`, this warning does not block gameplay code changes.

### Possible Future Cleanup

- Review plugin DLL references.
- Check whether `Edgegap.dll` can be updated or replaced.
- Avoid changing package/DLL references during unrelated gameplay work.

## Safe Build Workflow

1. Make small scoped edits.
2. Prefer `apply_patch`.
3. Avoid whole-file rewrites on Unity C# scripts.
4. Run:

```powershell
dotnet build Battle_PVP.sln -m:1 -nodeReuse:false -v:q
```

5. Treat compiler errors as blockers.
6. Treat the Git sandbox warning and current `System.Net.Http` conflict as non-blocking unless they become actual errors.
