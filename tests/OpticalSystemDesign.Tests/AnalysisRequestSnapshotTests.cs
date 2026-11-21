using System.Collections;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class AnalysisRequestSnapshotTests
{
    [Theory]
    [InlineData("Spot Diagram")]
    [InlineData("Ray Fan")]
    public async Task SubmittedSettingsAreCopiedOnTheCallerAndCannotDriftWithLaterEdits(string analysis)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Documents.GetRecoveryState();
        var revision = app.Events.Revision;
        var values = new Dictionary<string, string> { ["FieldNumber"] = "1", ["WavelengthNumber"] = "1", ["RayDensity"] = "2", ["NumRays"] = "7", ["Reference"] = "质心" };
        var expected = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, analysis, new Dictionary<string, string>(values)));
        var tracked = new CallerOwnedSettings(values);
        var pending = app.Analyses.RunAsync(new(Guid.NewGuid(), 2, analysis, tracked));
        var copiedBeforeReturn = tracked.CopiedOnCaller;
        values["FieldNumber"] = "999";
        values["RayDensity"] = "64";
        values["Reference"] = "主光线";
        var actual = await pending;
        Assert.True(copiedBeforeReturn, "Settings must be snapshotted before returning the pending request.");
        Assert.Equal(0, tracked.LookupReads);
        Assert.Equal(expected.Provenance.RequestFingerprint, actual.Provenance.RequestFingerprint);
        Assert.Equal(expected.View.Series.SelectMany(s => s.Points), actual.View.Series.SelectMany(s => s.Points));
        Assert.Equal(expected.View.PlotPanes.SelectMany(p => p.Series).SelectMany(s => s.Points), actual.View.PlotPanes.SelectMany(p => p.Series).SelectMany(s => s.Points));
        Assert.Equal(before, app.Documents.GetRecoveryState());
        Assert.Equal(revision, app.Events.Revision);
    }

    private sealed class CallerOwnedSettings(Dictionary<string, string> values) : IReadOnlyDictionary<string, string>
    {
        private readonly int _caller = Environment.CurrentManagedThreadId;
        public bool CopiedOnCaller { get; private set; }
        public int LookupReads;
        public int Count => values.Count;
        public IEnumerable<string> Keys => values.Keys;
        public IEnumerable<string> Values => values.Values;
        public string this[string key] { get { Interlocked.Increment(ref LookupReads); return values[key]; } }
        public bool ContainsKey(string key) { Interlocked.Increment(ref LookupReads); return values.ContainsKey(key); }
        public bool TryGetValue(string key, out string value) { Interlocked.Increment(ref LookupReads); return values.TryGetValue(key, out value!); }
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            CopiedOnCaller |= Environment.CurrentManagedThreadId == _caller;
            return values.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
