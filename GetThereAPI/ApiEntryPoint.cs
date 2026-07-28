namespace GetThereAPI;

/// <summary>
/// Names this assembly for <c>WebApplicationFactory&lt;TEntryPoint&gt;</c>.
/// <para>
/// The obvious argument would be <c>Program</c>, but both APIs use top-level statements and the
/// test project references both, so the two compiler-generated <c>Program</c> classes collide
/// (CS0433). Only the assembly matters to the factory, so any public type in it will do — this one
/// exists to say so out loud rather than leaving a test pointing at an unrelated class.
/// </para>
/// </summary>
public sealed class ApiEntryPoint;
