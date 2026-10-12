// WPF property metadata and native focus are shared across the STA windows in this process.
// Running separate window test classes concurrently can corrupt framework caches or steal focus.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
