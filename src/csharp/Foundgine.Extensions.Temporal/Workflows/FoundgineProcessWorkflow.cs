using Foundgine.Extensions.Temporal.Activities;
using Foundgine.Extensions.Temporal.Contracts;
using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Foundgine.Extensions.Temporal.Workflows;

/// <summary>
/// Generic saga-style process: ordered steps, optional human-approval gates, reverse-order compensation on
/// failure or cancellation. ERP/CRM processes (order, onboarding, invoicing, ...) are
/// <see cref="FoundgineProcessDefinition"/>s. Workflow code is deterministic: no I/O, no direct service calls.
/// </summary>
[Workflow("foundgine.process")]
public sealed class FoundgineProcessWorkflow
{
    private readonly Dictionary<string, FoundgineApprovalDecision> _decisions = new(StringComparer.Ordinal);
    private readonly List<string> _completed = [];
    private readonly List<string> _compensated = [];
    private string _state = FoundgineProcessState.Running;
    private string _process = "";
    private string? _currentStep;
    private string? _pendingApproval;
    private string? _failure;

    [WorkflowQuery("status")]
    public FoundgineProcessStatus GetStatus() =>
        new(_process, _state, _currentStep, _completed.ToArray(), _compensated.ToArray(), _pendingApproval, _failure);

    [WorkflowSignal("decision")]
    public Task DecideAsync(FoundgineApprovalDecision decision)
    {
        // First decision per step wins; duplicate signals are ignored so signal retries are harmless.
        _decisions.TryAdd(decision.Step, decision);
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task<FoundgineProcessResult> RunAsync(FoundgineProcessInput input)
    {
        _process = input.Definition.Name;
        var outputs = new Dictionary<string, string?>(StringComparer.Ordinal);
        var executed = new List<FoundgineProcessStep>();

        try
        {
            foreach (var step in input.Definition.Steps)
            {
                _currentStep = step.Name;

                if (step.RequiresApproval)
                {
                    _state = FoundgineProcessState.AwaitingApproval;
                    _pendingApproval = step.Name;
                    bool decided;
                    if (step.ApprovalTimeout is { } timeout)
                    {
                        decided = await Workflow.WaitConditionAsync(() => _decisions.ContainsKey(step.Name), timeout);
                    }
                    else
                    {
                        await Workflow.WaitConditionAsync(() => _decisions.ContainsKey(step.Name));
                        decided = true;
                    }
                    _pendingApproval = null;

                    if (!decided || !_decisions[step.Name].Approved)
                    {
                        _failure = decided ? _decisions[step.Name].Reason ?? "Rejected." : "Approval timed out.";
                        await CompensateAsync(input, executed, outputs);
                        _state = FoundgineProcessState.Rejected;
                        return new(FoundgineProcessState.Rejected, outputs, _failure);
                    }
                    _state = FoundgineProcessState.Running;
                }

                var result = await Workflow.ExecuteActivityAsync<FoundgineOperationResult>(
                    FoundgineOperationActivities.ActivityName,
                    [Request(input, step.Operation, outputs, compensation: false)],
                    Options(input.ActivityPolicy, Workflow.CancellationToken));
                outputs[step.Name] = result.OutputJson;
                executed.Add(step);
                _completed.Add(step.Name);
            }

            _currentStep = null;
            _state = FoundgineProcessState.Completed;
            return new(FoundgineProcessState.Completed, outputs);
        }
        catch (Exception ex) when (TemporalException.IsCanceledException(ex))
        {
            _failure = "Cancelled.";
            await CompensateAsync(input, executed, outputs);
            _state = FoundgineProcessState.Cancelled;
            throw; // Preserve Temporal's Cancelled status.
        }
        catch (ActivityFailureException ex)
        {
            _failure = ex.InnerException?.Message ?? ex.Message;
            await CompensateAsync(input, executed, outputs);
            _state = FoundgineProcessState.Failed;
            throw new ApplicationFailureException(_failure, ex, errorType: "ProcessFailed", nonRetryable: true);
        }
    }

    private async Task CompensateAsync(
        FoundgineProcessInput input, List<FoundgineProcessStep> executed, Dictionary<string, string?> outputs)
    {
        if (executed.Count == 0) return;
        _state = FoundgineProcessState.Compensating;
        for (var i = executed.Count - 1; i >= 0; i--)
        {
            var step = executed[i];
            if (step.CompensationOperation is null) continue;
            try
            {
                // CancellationToken.None: compensation must still run when the workflow itself was cancelled.
                await Workflow.ExecuteActivityAsync<FoundgineOperationResult>(
                    FoundgineOperationActivities.ActivityName,
                    [Request(input, step.CompensationOperation, outputs, compensation: true)],
                    Options(input.ActivityPolicy, CancellationToken.None));
                _compensated.Add(step.Name);
            }
            catch (ActivityFailureException)
            {
                // Keep unwinding remaining steps; the process still ends Failed/Cancelled with the cause recorded.
            }
        }
    }

    private static FoundgineOperationRequest Request(
        FoundgineProcessInput input, string operation, IReadOnlyDictionary<string, string?> outputs, bool compensation) =>
        new(operation, input.TenantId, input.Initiator, input.CorrelationId, input.PayloadJson,
            new Dictionary<string, string?>(outputs, StringComparer.Ordinal), compensation);

    private static ActivityOptions Options(FoundgineActivityPolicy policy, CancellationToken cancellationToken) => new()
    {
        StartToCloseTimeout = policy.StartToCloseTimeout,
        CancellationToken = cancellationToken,
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = policy.InitialInterval,
            MaximumInterval = policy.MaximumInterval,
            MaximumAttempts = policy.MaximumAttempts
        }
    };
}
