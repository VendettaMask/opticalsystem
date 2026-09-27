namespace OptilandWorkbench.Core.Optimization;

/// <summary>An unavailable objective value, never a numerical penalty or a stationary point.</summary>
public sealed class OptimizationEvaluationException : InvalidOperationException
{
    public OptimizationEvaluationException(string operandName, string reason)
        : base($"Optimization evaluation '{operandName}' failed: {reason}")
    {
        OperandName = operandName;
        Reason = reason;
    }

    public string OperandName { get; }
    public string Reason { get; }
}
