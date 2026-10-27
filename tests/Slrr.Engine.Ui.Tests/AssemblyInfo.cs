// The shared VmClassPath (lazy corpus scan) is not thread-safe; run the corpus tests one at a time.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
