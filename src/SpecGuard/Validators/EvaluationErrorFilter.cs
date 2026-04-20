using Json.Schema;

namespace SpecGuard.Validators;

internal static class EvaluationErrorFilter
{
    internal readonly record struct RawError(string Key, string Message, string Path);

    /// <summary>
    /// Walks the evaluation tree and collects errors with their keyword keys and instance paths.
    /// </summary>
    internal static List<RawError> Collect(EvaluationResults evaluation)
    {
        var sink = new List<RawError>();
        Walk(evaluation, sink);
        return sink;

        static void Walk(EvaluationResults node, List<RawError> sink)
        {
            if (node.Errors is { Count: > 0 } errors)
            {
                var path = node.InstanceLocation.ToString();
                foreach (var error in errors)
                {
                    sink.Add(new RawError(error.Key, error.Value, path));
                }
            }

            if (node.Details is { Count: > 0 } details)
            {
                foreach (var detail in details)
                {
                    Walk(detail, sink);
                }
            }
        }
    }
}
