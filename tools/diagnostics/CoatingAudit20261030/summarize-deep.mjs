// Evidence reduction only: all optical quantities come from saved Core/reference outputs.
import { readFileSync, writeFileSync } from 'node:fs';
const root = process.argv[2];
const read = path => JSON.parse(readFileSync(`${root}/${path}`, 'utf8').replace(/^\uFEFF/, ''));
const inputs = read('scans/reference-input.json');
const scans = read('scans/sweeps.json');
const reference = read('reference/reference-results.json');
const validated = read('revalidated/reference-candidates-native.json');
const brief = m => ({ peakNm: m.PeakWavelengthNanometers, peakT: m.PeakTransmittance,
  fwhmNm: m.FwhmNanometers, minimumR: m.MinimumReflectance });
const numericTargets = (name, m, ods) => {
  const t = inputs.find(s => s.Name === name).Target;
  return m.FwhmNanometers !== null && Math.abs(m.FwhmNanometers - t.FwhmNm) <= t.FwhmToleranceNm
    && Math.abs(m.PeakWavelengthNanometers - t.CenterNm) <= t.CenterToleranceNm
    && m.PeakTransmittance >= t.PeakTransmittance && ods.every(od => od >= t.BlockingOd);
};
const scanBrief = s => ({ name: s.Label, loss: s.Loss, embedded: s.Embedded, layers: s.LayerCount,
  withinLayerBudget: s.WithinOriginalLayerBudget, withinThicknessBounds: s.WithinOriginalThicknessBounds,
  counterfactual: s.Counterfactual, screenOnly: s.ScreenOnly, metrics: brief(s.Metrics), center: s.CenterPower,
  worstReflectanceWavelength: s.WorstReflectanceWavelength, worstReflectancePower: s.WorstReflectancePower,
  minimumOds: s.BlockingOd.map(b => b.MinimumOd),
  numericTargetsOnly: s.Name !== 'HR' && numericTargets(s.Name, s.Metrics, s.BlockingOd.map(b => b.MinimumOd)) });
const refinements = ['HR', 'NBF1', 'NBF2'].map(name => {
  const s = read(`reoptimized/${name}-summary.json`);
  return { name, scope: s.Scope, seconds: s.Seconds, before: brief(s.Before.Metrics), after: brief(s.After.Metrics),
    beforeMerit: s.Before.Merit, afterMerit: s.After.Merit, passed: s.After.Passed, samplingConverged: s.After.SamplingConverged,
    run: s.After.Run, checks: s.After.Checks };
});
const matched = read('reoptimized/matched-native-HR-summary.json');
const comparison = read('scans/tmmcore-comparison.json');
const geometryComparison = read('revalidated/reference-candidates-rta-check.json');
const selectedRef = validated.filter(v => v.Source === 'HR' || /m[67]-order1|integer0/.test(v.Name)).map(v => ({
  name: v.Name, counterfactual: v.Counterfactual, layers: v.LayerCount, withinLayerBudget: v.WithinOriginalLayerBudget,
  withinThicknessBounds: v.WithinOriginalThicknessBounds, intended: brief(v.IntendedScreen.Metrics),
  intendedOds: v.IntendedScreen.MinimumOd, originalRealAir: brief(v.OriginalAirAndRealMaterialsScreen.Metrics),
  originalRealAirOds: v.OriginalAirAndRealMaterialsScreen.MinimumOd,
  actualPassed: v.OriginalAssessment?.Passed ?? null,
}));
const result = {
  scope: 'Diagnostic measurements and counterfactual controls; numericTargetsOnly does not assert dense sampling convergence or product acceptance.',
  conditions: inputs.map(s => ({ name: s.Name, target: s.Target, settings: s.Settings,
    materials: s.Materials.map(m => ({ id: m.Id, n550: m.CenterN, k550: m.CenterK })) })),
  independentRta: { scans: { cases: comparison.cases, maximumErrors: comparison.maximumErrors },
    referenceGeometries: { cases: geometryComparison.Cases, R: geometryComparison.MaximumR, T: geometryComparison.MaximumT, A: geometryComparison.MaximumA } },
  scanCount: scans.length, referenceGeometryCount: validated.length,
  endpointControls: scans.filter(s => s.Label.endsWith(':endpoint') || s.Label.endsWith(':endpoint-embedded')).map(scanBrief),
  hrLayerSweep: scans.filter(s => s.Name === 'HR' && s.Label.startsWith('HR:quarter:') && s.Loss === 'real').map(scanBrief),
  hrBestWithinBounds: scans.filter(s => s.Name === 'HR' && !s.Counterfactual && s.WithinOriginalLayerBudget && s.WithinOriginalThicknessBounds)
    .sort((a,b) => b.Metrics.MinimumReflectance - a.Metrics.MinimumReflectance).slice(0,4).map(scanBrief),
  nbfSweepCounts: ['NBF1','NBF2'].map(name => {
    const rows = scans.filter(s => s.Name === name);
    return { name, screens: rows.length, originalRealAirScreens: rows.filter(s => !s.Counterfactual).length,
      numericPassesReal: rows.filter(s => !s.Counterfactual && numericTargets(name,s.Metrics,s.BlockingOd.map(b=>b.MinimumOd))).map(scanBrief),
      numericPassesCounterfactual: rows.filter(s => s.Counterfactual && numericTargets(name,s.Metrics,s.BlockingOd.map(b=>b.MinimumOd))).map(scanBrief) };
  }),
  residuals: ['NBF1','NBF2'].map(name => ({ name, ...read(`scans/${name}-objective-groups.json`) })),
  additionalNativeOptimization: refinements,
  hrMatchedObjective: { native: { seconds: matched.Seconds, report: {
    initialMeanSquare: matched.Report.InitialMerit, finalMeanSquare: matched.Report.FinalMerit,
    initialRms: Math.sqrt(matched.Report.InitialMerit), finalRms: Math.sqrt(matched.Report.FinalMerit),
    iterations: matched.Report.Iterations, evaluations: matched.Report.FunctionEvaluations, stopReason: matched.Report.StopReason },
    originalAssessment: brief(matched.OriginalTargetAssessment.Metrics), actualPassed: matched.OriginalTargetAssessment.Passed },
    reference: reference.summaries.filter(s => s.name.startsWith('HR-')) },
  selectedReferenceGeometries: selectedRef,
  referenceGeometryNumericCounts: ['NBF1','NBF2'].map(name => {
    const rows = validated.filter(v => v.Source === name && v.WithinOriginalLayerBudget && v.WithinOriginalThicknessBounds);
    return { name, rowsWithinOriginalBounds: rows.length,
      intendedNumericPasses: rows.filter(v => numericTargets(name,v.IntendedScreen.Metrics,v.IntendedScreen.MinimumOd)).map(v=>v.Name),
      originalRealAirNumericPasses: rows.filter(v => numericTargets(name,v.OriginalAirAndRealMaterialsScreen.Metrics,v.OriginalAirAndRealMaterialsScreen.MinimumOd)).map(v=>v.Name) };
  }),
};
writeFileSync(`${root}/analysis-summary.json`, JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify({scanCount:result.scanCount, independentRta:result.independentRta,
  hrMatched: result.hrMatchedObjective.native, referenceGeometryNumericCounts:result.referenceGeometryNumericCounts},null,2));
