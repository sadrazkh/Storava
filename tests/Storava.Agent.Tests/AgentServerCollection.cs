namespace Storava.Agent.Tests;

/// <summary>
/// Test classes that start a real Agent share this collection so they never run at the same time.
/// <para>
/// There are only four agreed ports, and every one of these classes binds one for every test in
/// it. Left to run in parallel they compete for the same handful of sockets, and a client can end
/// up talking to another test's server — which fails in ways that look like product bugs.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AgentServerCollection
{
    public const string Name = "agent-server";
}
