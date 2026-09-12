namespace Inceptus.DocumentEngine.UnitTests.Architecture;

/// <summary>
/// Identifies the exact native-serialization source and test files approved by Phase N10.2.
/// Legacy phase guards continue to reject unrelated serialization-path changes.
/// </summary>
internal static class ApprovedN102Changes
{
    internal static bool IsApprovedSerializationPath(string path) => path is
        "src/Inceptus.DocumentEngine.Runtime/Documents/NativeDocumentJsonCodec.cs" or
        "src/Inceptus.DocumentEngine.Runtime/Documents/NativeDocumentSerializer.cs" or
        "tests/Inceptus.DocumentEngine.UnitTests/Architecture/PhaseN102NativeDocumentSerializationArchitectureTests.cs" or
        "tests/Inceptus.DocumentEngine.UnitTests/Runtime/Documents/NativeDocumentSerializationTests.cs";
}
