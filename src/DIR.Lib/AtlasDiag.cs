namespace DIR.Lib;

/// <summary>
/// DEBUG-only diagnostics for the backend-neutral SDF atlas core, using the same
/// "[rdiag] category detail" line format as SdlVulkan.Renderer's RenderDiag so a combined
/// debug log from atlas core + GPU backend reads consistently. Use this for anything in a
/// backend-neutral DIR.Lib type; backend-specific logs (VkResult codes, GL errors) stay with
/// the backend's own diagnostics. <c>[Conditional("DEBUG")]</c>: stripped entirely in Release.
/// </summary>
public static class AtlasDiag
{
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Log(string category, string detail) =>
        Console.Error.WriteLine($"[rdiag] {category} {detail}");
}
