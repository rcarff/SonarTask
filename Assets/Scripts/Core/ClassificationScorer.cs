using System.Collections.Generic;
using System.Linq;

namespace SonarTask.Core {
public enum ClassCorrectness { NO, YES, PARTIAL }

public static class ClassificationScorer {
    public static ClassCorrectness Score(string selected, IEnumerable<ResolvedSignal> signals) {
        var types = signals.Select(s => s.Classification).ToList();
        if (types.Count == 0) return ClassCorrectness.NO;
        var matches = types.Count(x => x == selected);
        return matches == types.Count ? ClassCorrectness.YES : matches > 0 ? ClassCorrectness.PARTIAL : ClassCorrectness.NO;
    }
}
}
