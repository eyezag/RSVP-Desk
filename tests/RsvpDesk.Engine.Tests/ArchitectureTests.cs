namespace RsvpDesk.Engine.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Engine_references_only_the_base_class_library()
    {
        var references = typeof(ConversationEngine).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        var foreign = references.Where(name => !(name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard")).ToList();

        Assert.True(foreign.Count == 0, $"Engine must not depend on: {string.Join(", ", foreign)}");
    }
}
