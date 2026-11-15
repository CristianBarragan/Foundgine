using Foundgine.Providers.AgentFramework.Hosting;
using Foundgine.Providers.AgentFramework.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Foundgine.Providers.AgentFramework.Agents;

/// <summary>Builds Microsoft Agent Framework agents over the Foundgine tool surface.</summary>
public static class FoundgineAgentFactory
{
    /// <summary>
    /// Creates a MAF <see cref="ChatClientAgent"/> whose only data access is the governed Foundgine tools.
    /// MAF wraps the chat client with function invocation, so tools run inside the agent loop; tools marked for
    /// approval pause the run and surface an approval request to the caller.
    /// </summary>
    public static AIAgent Create(
        IChatClient chatClient,
        FoundgineAgentTools tools,
        FoundgineAgentFrameworkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(tools);
        options ??= new FoundgineAgentFrameworkOptions();
        options.Validate();

        return new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = options.AgentName,
            Description = options.AgentDescription,
            ChatOptions = new ChatOptions
            {
                Instructions = options.Instructions ?? FoundgineAgentInstructions.Default,
                Tools = tools.CreateTools(options.EnableMutations).ToList()
            }
        });
    }

    /// <inheritdoc cref="Create(IChatClient, FoundgineAgentTools, FoundgineAgentFrameworkOptions?)"/>
    public static AIAgent AsFoundgineAgent(
        this IChatClient chatClient,
        FoundgineAgentTools tools,
        FoundgineAgentFrameworkOptions? options = null) => Create(chatClient, tools, options);
}
