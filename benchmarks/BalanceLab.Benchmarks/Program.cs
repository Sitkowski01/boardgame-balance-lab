using BenchmarkDotNet.Running;

// BenchmarkDotNet konczy sie kodem 0 nawet wtedy, gdy benchmark sie nie zbudowal albo rzucil wyjatek —
// CI ("benchmarki uruchamiaja sie") musi to zobaczyc.
var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)) ? 1 : 0;
